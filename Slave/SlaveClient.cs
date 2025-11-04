using System.Net.WebSockets;
using System.Reflection;
using MemoryPack;
using Shared;

namespace Slave;

public class SlaveClient
{
    public const int ChunkSize = 1024 * 64;
    public const int BufferSize = 1024 * 64;

    private readonly ClientWebSocket _clientSocket;
    private readonly AlgorithmProvider _provider;

    public SlaveClient(AlgorithmProvider provider)
    {
        _provider = provider;
        _clientSocket = new ClientWebSocket();
    }

    public async Task Connect(Uri uri, CancellationToken cancellation)
    {
        try
        {
            Console.WriteLine("Trying To Connect To {0}", uri);
            await _clientSocket.ConnectAsync(uri, cancellation);
            Console.WriteLine("Success");
            await ListenLoopAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine("Fail");
            Console.WriteLine(e);
        }
        finally
        {
            _clientSocket.Dispose();
        }
    }

    private async Task ListenLoopAsync()
    {
        byte[] buffer = new byte[BufferSize];
        while (_clientSocket.State == WebSocketState.Open)
        {
            ClientMessage request = new ClientMessage(ClientMessageType.AssignmentRequest, Array.Empty<byte>());
            await SendMessageAsync(request);

            ServerMessage? message = await ReceiveMessageAsync(buffer);

            if (message is not null)
            {
                await HandleMessage(message);
            }
        }
    }

    private async Task<ServerMessage?> ReceiveMessageAsync(byte[] buffer)
    {
        using MemoryStream ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result =
                await _clientSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await _clientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "NormalClosure",
                    CancellationToken.None);
                Console.WriteLine("Closed Normal Closure");
                return null;
            }

            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return MemoryPackSerializer.Deserialize<ServerMessage>(ms.ToArray());
    }

    private async Task HandleMessage(ServerMessage message)
    {
        Console.WriteLine("Message Received {0} {1}", message.Id, message.MessageType);
        if (message.MessageType == ServerMessageType.Assignment)
        {
            Assignment? ass = MemoryPackSerializer.Deserialize<Assignment>(message.Payload);
            if (ass is null)
            {
                Console.WriteLine("Couldn't Deserialize Assignment");
                return;
            }

            Console.WriteLine("Handling Assignment {0}", ass.Id);

            if (_provider.TryGetExecutor(ass.AlgorithmName, out IAlgorithmExecutor? executor))
            {
                byte[] v = executor.Execute(ass.Parameters);
                AssignmentResult result = new AssignmentResult(ass.Id, MemoryPackSerializer.Serialize(v));
                byte[] payload = MemoryPackSerializer.Serialize(result);
                ClientMessage response = new ClientMessage(ClientMessageType.Result, payload);
                await SendMessageAsync(response);
            }
            else
            {
                Console.WriteLine("No Algorithm With Name {0}. Trying To Request", ass.AlgorithmName);
                byte[] payload = MemoryPackSerializer.Serialize(ass.AlgorithmName);
                ClientMessage request = new ClientMessage(ClientMessageType.AlgorithmRequest, payload);
                await SendMessageAsync(request);
            }

            return;
        }

        if (message.MessageType == ServerMessageType.Algorithm)
        {
            _provider.AddExecutor("count-substrings", message.Payload);
        }
    }

    private async Task SendMessageAsync(ClientMessage message)
    {
        byte[] data = MemoryPackSerializer.Serialize(message);

        int offset = 0;

        while (offset < data.Length)
        {
            int size = Math.Min(ChunkSize, data.Length - offset);
            bool endOfMessage = (offset + size) >= data.Length;

            ArraySegment<byte> segment = new ArraySegment<byte>(data, offset, size);
            await _clientSocket.SendAsync(segment, WebSocketMessageType.Binary, endOfMessage, CancellationToken.None);

            offset += size;
        }
    }
}
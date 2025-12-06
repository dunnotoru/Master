using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading.Channels;
using MemoryPack;
using Shared;

namespace Slave;

public class SlaveClient
{
    public const int ChunkSize = 1024 * 64;
    public const int BufferSize = 1024 * 64;

    private readonly ClientWebSocket _clientSocket;
    private readonly AlgorithmProvider _provider;

    private readonly Channel<ClientMessage> _messages = Channel.CreateUnbounded<ClientMessage>();

    public SlaveClient(AlgorithmProvider provider)
    {
        _provider = provider;
        _clientSocket = new ClientWebSocket();
    }

    public async Task Connect(Uri uri, CancellationToken cancellation)
    {
        Console.WriteLine("Подключение... {0}", uri);

        Task connect = _clientSocket.ConnectAsync(uri, cancellation);
        Task timeout = Task.Delay(TimeSpan.FromSeconds(5), cancellation);
        if (await Task.WhenAny(connect, timeout) != connect)
        {
            if (cancellation.IsCancellationRequested)
            {
                Console.WriteLine("Запрос на подключение был отменен пользователем");
            }
            else
            {
                Console.WriteLine("Удаленный хост недоступен (превышено время ожидания)");
            }

            return;
        }
        
        try
        {
            await connect;
            Console.WriteLine("Успешно");
        }
        catch (WebSocketException e)
        {
            Console.WriteLine("Конечный хост отверг запрос на подключение");
            Debug.WriteLine(e);
            return;
        }


        Task first = await Task.WhenAny(
            SendLoopAsync(cancellation),
            ReceiveLoopAsync(cancellation)
        );

        try
        {
            await first;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Ввод с клавиатуры, остановка");
        }
        catch (Exception e)
        {
            Console.WriteLine("Произошла Ошибка");
            Console.WriteLine(e.Message);
        }
        finally
        {
            if (_clientSocket.State == WebSocketState.Open)
            {
                await _clientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed",
                    CancellationToken.None);
            }

            _clientSocket.Dispose();
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellation)
    {
        while (await _messages.Reader.WaitToReadAsync(cancellation))
        {
            ClientMessage msg = await _messages.Reader.ReadAsync(cancellation);
            await SendMessageAsync(msg);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellation)
    {
        byte[] buffer = new byte[BufferSize];
        ClientMessage request = new ClientMessage(ClientMessageType.AssignmentRequest, Array.Empty<byte>());
        await _messages.Writer.WriteAsync(request, cancellation);
        while (_clientSocket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
        {
            ServerMessage? message = await ReceiveMessageAsync(buffer);

            if (message is not null)
            {
                await HandleMessage(message);
            }
        }
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
                // await Task.Delay(TimeSpan.FromSeconds(0.2));
                byte[] v = executor.Execute(ass.Parameters);
                AssignmentResult result =
                    new AssignmentResult(ass.Id, executor.ResultType, v);
                byte[] payload = MemoryPackSerializer.Serialize(result);
                ClientMessage response = new ClientMessage(ClientMessageType.Result, payload);
                await _messages.Writer.WriteAsync(response);
                await _messages.Writer.WriteAsync(new ClientMessage(ClientMessageType.AssignmentRequest,
                    Array.Empty<byte>()));
            }
            else
            {
                Console.WriteLine("No Algorithm With Name {0}. Trying To Request", ass.AlgorithmName);
                byte[] payload = MemoryPackSerializer.Serialize(ass.AlgorithmName);
                ClientMessage request = new ClientMessage(ClientMessageType.AlgorithmRequest, payload);
                await _messages.Writer.WriteAsync(request);
            }

            return;
        }

        if (message.MessageType == ServerMessageType.Algorithm)
        {
            AlgorithmData? data = MemoryPackSerializer.Deserialize<AlgorithmData>(message.Payload);
            if (data is null) return;

            if (string.Equals(data.Name, "not-found", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new InvalidOperationException("Requested algorithm wasn't found");
            }

            _provider.AddExecutor(data.Name, data.RawFileData);
            await _messages.Writer.WriteAsync(new ClientMessage(ClientMessageType.AssignmentRequest,
                Array.Empty<byte>()));
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
}
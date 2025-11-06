using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Threading.Channels;
using MemoryPack;
using Shared;

namespace Master;

public sealed class ClientHandler : IDisposable
{
    private readonly Channel<Assignment> _asses;
    private readonly ChannelWriter<AssignmentResult> _results;
    private readonly ModuleProvider _provider;
    private readonly Channel<ServerMessage> _messages;

    private readonly WebSocket _clientSocket;
    public int Id { get; }

    public const int ChunkSize = 1024 * 64; //64 KB
    public const int BufferSize = 1024 * 64;

    public event Action<ClientHandler>? ConnectionClosed;

    private readonly ConcurrentDictionary<AssignmentIdentifier, TaskCompletionSource<AssignmentResult>>
        _pendingAssignments = new ConcurrentDictionary<AssignmentIdentifier, TaskCompletionSource<AssignmentResult>>();

    public ClientHandler(WebSocket clientSocket, int id, Channel<Assignment> asses,
        ChannelWriter<AssignmentResult> results, ModuleProvider provider)
    {
        _clientSocket = clientSocket;
        Id = id;
        _asses = asses;
        _results = results;
        _provider = provider;
        _messages = Channel.CreateUnbounded<ServerMessage>();
    }

    public async Task WorkLoopAsync(CancellationToken cancellation)
    {
        Task send = SendLoopAsync(cancellation);
        Task listen = ReceiveLoopAsync(cancellation);
        Task first = await Task.WhenAny(send, listen);
        
        try
        {
            await first;
        }
        catch (WebSocketException ex)
        {
            foreach (TaskCompletionSource<AssignmentResult> pending in _pendingAssignments.Values)
            {
                pending.TrySetCanceled(CancellationToken.None);
            }

            if (_clientSocket.State == WebSocketState.Open)
            {
                await _clientSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Internal Server Error",
                    CancellationToken.None);
            }

            _pendingAssignments.Clear();
            ConnectionClosed?.Invoke(this);
            Debug.Print("{0}", ex);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            if (_clientSocket.State == WebSocketState.Open)
            {
                await _clientSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Internal Server Error",
                    CancellationToken.None);
            }
        }
        finally
        {
            foreach (TaskCompletionSource<AssignmentResult> pending in _pendingAssignments.Values)
            {
                pending.TrySetCanceled(CancellationToken.None);
            }

            _pendingAssignments.Clear();
            ConnectionClosed?.Invoke(this);
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellation)
    {
        while (await _messages.Reader.WaitToReadAsync(cancellation))
        {
            ServerMessage msg = await _messages.Reader.ReadAsync(cancellation);
            try
            {
                await SendMessageAsync(msg);
            }
            catch (Exception ex)
            {
                Debug.Print("Error while sending {0}", ex);
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellation)
    {
        byte[] buffer = new byte[BufferSize];
        while (_clientSocket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
        {
            ClientMessage? message = await ReceiveMessage(buffer, cancellation);
            if (message is not null)
            {
                await HandleMessageAsync(message);
            }
        }
    }


    private async Task SendMessageAsync(ServerMessage message)
    {
        byte[] data = MemoryPackSerializer.Serialize(message);

        int offset = 0;

        while (offset < data.Length)
        {
            int size = Math.Min(ChunkSize, data.Length - offset);
            bool endOfMessage = offset + size >= data.Length;

            ArraySegment<byte> segment = new ArraySegment<byte>(data, offset, size);
            await _clientSocket.SendAsync(segment, WebSocketMessageType.Binary, endOfMessage, CancellationToken.None);

            offset += size;
        }
    }

    private async Task<ClientMessage?> ReceiveMessage(byte[] buffer, CancellationToken cancellation)
    {
        using MemoryStream ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result =
                await _clientSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await _clientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                Debug.WriteLine("Closed Normal Closure");
                return null;
            }

            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        ClientMessage? message = MemoryPackSerializer.Deserialize<ClientMessage>(ms.ToArray());

        return message;
    }

    private async Task HandleMessageAsync(ClientMessage message)
    {
        switch (message.MessageType)
        {
            case ClientMessageType.AssignmentRequest:
                _ = SendNextAssignmentAsync();
                return;
            case ClientMessageType.Result:
                ReceiveResult(message);
                return;
            case ClientMessageType.AlgorithmRequest:
                await SendAlgorithmAsync(message);
                return;
            case ClientMessageType.Error:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task SendNextAssignmentAsync()
    {
        Debug.Print("Client Is Requesting Assignment");

        Assignment assignment = await _asses.Reader.ReadAsync();

        try
        {
            TaskCompletionSource<AssignmentResult> tcs =
                new TaskCompletionSource<AssignmentResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

            _pendingAssignments[assignment.Id] = tcs;

            ServerMessage message =
                new ServerMessage(ServerMessageType.Assignment, MemoryPackSerializer.Serialize(assignment));

            await _messages.Writer.WriteAsync(message);

            AssignmentResult result = await tcs.Task;

            await _results.WriteAsync(result);
        }
        catch (TaskCanceledException ex)
        {
            await _asses.Writer.WriteAsync(assignment);
            Debug.WriteLine("Assignment wasn't processed, {0}", ex);
        }
        catch (Exception ex)
        {
            Debug.Print("TRALALALA EXCEPTION WHILE WAITING ASSIGNMENT, {0}", ex);
        }
    }

    private void ReceiveResult(ClientMessage message)
    {
        AssignmentResult? response = MemoryPackSerializer.Deserialize<AssignmentResult>(message.Payload);
        if (response is null) return;
    
        AssignmentIdentifier id = response.Id;
        if (_pendingAssignments.TryGetValue(id, out TaskCompletionSource<AssignmentResult>? tcs))
        {
            tcs.SetResult(response);
            _pendingAssignments.Remove(id, out _);
        }
    }

    private async Task SendAlgorithmAsync(ClientMessage message)
    {
        Debug.WriteLine("Algorithm Request");

        string? name = MemoryPackSerializer.Deserialize<string>(message.Payload);
        if (name is null) return;

        Debug.WriteLine("Client Is Requesting Algorithm {0}", name);

        //TODO: this is dummy

        if (_provider.TryGetExecutor(name, out Module? module))
        {
            byte[] file = await File.ReadAllBytesAsync(module.ModuleFile.FullName);
            ServerMessage found = new ServerMessage(ServerMessageType.Algorithm,
                MemoryPackSerializer.Serialize(new AlgorithmData(name, file)));
            await _messages.Writer.WriteAsync(found);
        }
        else
        {
            ServerMessage notFound =
                new ServerMessage(ServerMessageType.Algorithm,
                    MemoryPackSerializer.Serialize(new AlgorithmData("not-found", Array.Empty<byte>())));
            await _messages.Writer.WriteAsync(notFound);
        }

        foreach (TaskCompletionSource<AssignmentResult> pending in _pendingAssignments.Values)
        {
            pending.TrySetCanceled(CancellationToken.None);
        }

        _pendingAssignments.Clear();
    }

    public void Dispose()
    {
        _clientSocket.Dispose();
    }
}
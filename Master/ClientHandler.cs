using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Threading.Channels;
using MemoryPack;
using Shared;

namespace Master;

public class ClientHandler : IDisposable
{
    private readonly Channel<Assignment> _asses;
    private readonly ChannelWriter<AssignmentResult> _results;

    private readonly WebSocket _clientSocket;
    public int Id { get; }

    public const int ChunkSize = 1024 * 64; //64 KB
    public const int BufferSize = 1024 * 64;

    public event Action<ClientHandler>? ConnectionClosed;

    private readonly ConcurrentDictionary<AssignmentIdentifier, TaskCompletionSource<AssignmentResult>>
        _pendingAssignments = new ConcurrentDictionary<AssignmentIdentifier, TaskCompletionSource<AssignmentResult>>();

    public ClientHandler(WebSocket clientSocket, int id, Channel<Assignment> asses,
        ChannelWriter<AssignmentResult> results)
    {
        _clientSocket = clientSocket;
        Id = id;
        _asses = asses;
        _results = results;
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

    private async Task<AssignmentResult> SendAssignmentAsync(Assignment assignment)
    {
        TaskCompletionSource<AssignmentResult> tcs =
            new TaskCompletionSource<AssignmentResult>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

        _pendingAssignments[assignment.Id] = tcs;

        ServerMessage message =
            new ServerMessage(ServerMessageType.Assignment, MemoryPackSerializer.Serialize(assignment));

        await SendMessageAsync(message);

        return await tcs.Task;
    }

    private async Task ScheduleLoopAsync(CancellationToken cancellation)
    {
        while (await _asses.Reader.WaitToReadAsync(cancellation))
        {
            Debug.WriteLine("Client {0} Is Ready", [Id]);
            Debug.WriteLine("Sending Assignment");
            Assignment ass = await _asses.Reader.ReadAsync(cancellation);

            try
            {
                AssignmentResult result = await SendAssignmentAsync(ass);
                await _results.WriteAsync(result, cancellation);
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine(ex);
                await _asses.Writer.WriteAsync(ass, cancellation);
            }
        }
    }

    public async Task WorkLoopAsync(CancellationToken cancellation)
    {
        try
        {
            Task schedule = ScheduleLoopAsync(cancellation);
            Task listen = ListenLoopAsync(cancellation);

            await Task.WhenAll(schedule, listen);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            await _clientSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Internal Server Error",
                CancellationToken.None);
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

    private async Task ListenLoopAsync(CancellationToken cancellation)
    {
        byte[] buffer = new byte[BufferSize];
        while (_clientSocket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
        {
            ClientMessage? message = await ReceiveMessage(buffer, cancellation);

            if (message is not null)
            {
                HandleMessage(message);
            }
        }
    }

    private void HandleMessage(ClientMessage message)
    {
        if (message.MessageType == ClientMessageType.Result)
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

    public void Dispose()
    {
        _clientSocket.Dispose();
    }
}
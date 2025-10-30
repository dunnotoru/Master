using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Threading.Channels;
using MemoryPack;
using Shared;

namespace Master;

class ClientHandler : IDisposable
{
    private readonly Channel<Assignment> _asses;
    private readonly ChannelWriter<AssignmentResponse> _results;
    private WebSocket ClientSocket { get; }
    public int Id { get; }

    public const int ChunkSize = 1024 * 64; //64 KB
    public const int BufferSize = 1024 * 64;

    public event Action<ClientHandler>? ConnectionClosed;

    public ClientHandler(WebSocket clientSocket, int id, Channel<Assignment> asses,
        ChannelWriter<AssignmentResponse> results)
    {
        _asses = asses;
        _results = results;
        ClientSocket = clientSocket;
        Id = id;
    }

    private Assignment? _lastAssignment;
    private TaskCompletionSource<AssignmentResponse>? _pendingResponse;

    private async Task<AssignmentResponse> SendAssignmentAsync(Assignment assignment)
    {
        _lastAssignment = assignment;
        _pendingResponse = new TaskCompletionSource<AssignmentResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        byte[] data = MemoryPackSerializer.Serialize(assignment);

        int offset = 0;

        while (offset < data.Length)
        {
            int size = Math.Min(ChunkSize, data.Length - offset);
            bool endOfMessage = offset + size >= data.Length;

            ArraySegment<byte> segment = new ArraySegment<byte>(data, offset, size);
            await ClientSocket.SendAsync(segment, WebSocketMessageType.Binary, endOfMessage, CancellationToken.None);

            offset += size;
        }

        return await _pendingResponse.Task;
    }

    private async Task ScheduleLoop(CancellationToken cancellation)
    {
        while (await _asses.Reader.WaitToReadAsync(cancellation))
        {
            Debug.WriteLine("CLIENT {0} IS READY", [Id]);
            Debug.WriteLine("SENDING ASS");
            Assignment ass = await _asses.Reader.ReadAsync(cancellation);
            AssignmentResponse result = await SendAssignmentAsync(ass);
            await _results.WriteAsync(result, cancellation);
        }
    }

    public async Task ListenAsync(CancellationToken cancellation)
    {
        try
        {
            _ = ScheduleLoop(cancellation);

            byte[] buffer = new byte[BufferSize];
            using MemoryStream ms = new MemoryStream();
            while (ClientSocket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                do
                {
                    result =
                        await ClientSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ClientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        Debug.WriteLine("Closed Normal Closure");
                        ConnectionClosed?.Invoke(this);
                        if (_lastAssignment is not null)
                        {
                            _pendingResponse?.TrySetCanceled(cancellation);
                            await _asses.Writer.WriteAsync(_lastAssignment, cancellation);
                        }

                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                AssignmentResponse? response = MemoryPackSerializer.Deserialize<AssignmentResponse>(ms.ToArray());
                ms.SetLength(0);
                
                if (response is not null
                    && _lastAssignment is not null
                    && response.JobId == _lastAssignment.JobId
                    && response.ChunkId == _lastAssignment.ChunkId)
                {
                    _pendingResponse?.TrySetResult(response);
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
            await ClientSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Internal Server Error",
                CancellationToken.None);

            if (_lastAssignment is not null)
            {
                await _asses.Writer.WriteAsync(_lastAssignment, cancellation);
            }

            _pendingResponse?.TrySetCanceled(CancellationToken.None);
            ConnectionClosed?.Invoke(this);
        }
    }

    public void Dispose()
    {
        ClientSocket.Dispose();
    }
}
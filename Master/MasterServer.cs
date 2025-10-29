using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using MemoryPack;
using Shared;

namespace Master;

public class MasterServer : IDisposable
{
    private int _count = 0;
    private ConcurrentDictionary<int, WebSocket> _clients = new ConcurrentDictionary<int, WebSocket>();
    private ConcurrentQueue<Assignment> _pendingAssignments = new ConcurrentQueue<Assignment>();

    private ConcurrentDictionary<WebSocket, Assignment?> _assignmentsInWork =
        new ConcurrentDictionary<WebSocket, Assignment?>();

    private ConcurrentDictionary<Guid, Job> _jobs =
        new ConcurrentDictionary<Guid, Job>();

    public event Action<int>? SlaveConnected;
    public event Action<int>? SlaveDisconnected;
    public event Action? JobDone;

    private Stopwatch _stopwatch = new Stopwatch();


    private enum ServerState
    {
        Accepting = 0,
        Ignoring
    }

    private ServerState _state;

    public async Task Start(string listenerPrefix, CancellationToken cancellationToken)
    {
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        listener.Start();

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext listenerContext = await listener.GetContextAsync();

            if (_state == ServerState.Ignoring)
            {
                listenerContext.Response.StatusCode = 404;
                listenerContext.Response.Close();
                continue;
            }

            if (listenerContext.Request.IsWebSocketRequest)
            {
                Debug.WriteLine("It Is WebSocket Request");
                await ProcessConnection(listenerContext);
            }
            else
            {
                Debug.WriteLine("Regular Request - Fail");
                listenerContext.Response.StatusCode = 404;
                listenerContext.Response.Close();
            }
        }
    }

    private async Task ProcessConnection(HttpListenerContext listenerContext)
    {
        WebSocketContext? webSocketContext;
        try
        {
            Debug.WriteLine("Trying To Accept WebSocket");
            webSocketContext = await listenerContext.AcceptWebSocketAsync(subProtocol: null);
            Interlocked.Increment(ref _count);
        }
        catch (Exception e)
        {
            Debug.WriteLine("Failed");
            Console.WriteLine(e.Message);
            listenerContext.Response.StatusCode = 500;
            listenerContext.Response.Close();
            return;
        }

        WebSocket clientSocket = webSocketContext.WebSocket;

        Task task = ListClientAsync(_count, clientSocket);

        _clients[_count] = clientSocket;

        SlaveConnected?.Invoke(_count);
    }

    private async Task ListClientAsync(int id, WebSocket clientSocket)
    {
        try
        {
            byte[] buffer = new byte[1024];

            while (clientSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result =
                    await clientSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await clientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    _clients.Remove(id, out _);
                    if (_assignmentsInWork.TryRemove(clientSocket, out Assignment? ass))
                    {
                        _pendingAssignments.Enqueue(ass);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    byte[] data = buffer[..result.Count];
                    ClientOnMessageReceived(clientSocket, data);
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
            await clientSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "", CancellationToken.None);
            _clients.Remove(id, out _);
            if (_assignmentsInWork.TryRemove(clientSocket, out Assignment? ass))
            {
                _pendingAssignments.Enqueue(ass);
            }
        }
    }

    private void ClientOnMessageReceived(WebSocket socket, byte[] arg2)
    {
        AssignmentResponse? response = null;
        try
        {
            response = MemoryPackSerializer.Deserialize<AssignmentResponse>(arg2);
            if (response is null)
            {
                return;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        if (response is null)
        {
            Debug.WriteLine("COULDN't READ RESPONSE");
            return;
        }

        Debug.WriteLine("{0} {1} {2} ", response.JobId, response.ChunkIndex, response.Count);

        if (_jobs.TryGetValue(response.JobId, out Job? job))
        {
            _assignmentsInWork.Remove(socket, out _);
            job.ReceivedResults.Add(response);
            if (job.ReceivedResults.Count >= job.Total)
            {
                JobDone?.Invoke();
                Debug.WriteLine("JOB DONE {0}", job.ReceivedResults.Count);
                _jobs.Remove(response.JobId, out _);
                _state = ServerState.Accepting;
                _stopwatch.Stop();
                Debug.WriteLine(_stopwatch.Elapsed.Seconds);
            }
        }
    }

    public async Task SendJob(string text, string substring)
    {
        Guid jobId = Guid.NewGuid();

        List<char[]> chunks = text.Chunk(int.Min(1024, text.Length)).ToList();

        for (int i = 0; i < chunks.Count; i++)
        {
            Assignment ass = new Assignment(jobId, i, chunks.Count, new string(chunks[i]), substring);
            _pendingAssignments.Enqueue(ass);
        }

        _jobs[jobId] = new Job(chunks.Count);

        _stopwatch.Start();
        _state = ServerState.Ignoring;

        await SendingLoop();
    }

    private async Task SendingLoop()
    {
        while (!_pendingAssignments.IsEmpty && !_jobs.IsEmpty)
        {
            foreach (KeyValuePair<int, WebSocket> client in _clients)
            {
                if (_assignmentsInWork.TryGetValue(client.Value, out _))
                {
                    continue;
                }

                if (_pendingAssignments.IsEmpty)
                {
                    break;
                }

                if (_pendingAssignments.TryDequeue(out Assignment? ass))
                {
                    _assignmentsInWork[client.Value] = ass;

                    byte[] buffer = MemoryPackSerializer.Serialize(ass);
                    int chunkSize = 1024;
                    int offset = 0;

                    while (offset < buffer.Length)
                    {
                        int size = Math.Min(chunkSize, buffer.Length - offset);
                        var segment = new ArraySegment<byte>(buffer, offset, size);

                        bool endOfMessage = (offset + size) >= buffer.Length;

                        await client.Value.SendAsync(segment, WebSocketMessageType.Binary, endOfMessage,
                            CancellationToken.None);

                        offset += size;
                    }
                }
            }

            await Task.Delay(50);
        }
    }

    private class Job(int total)
    {
        public int Total { get; } = total;
        public List<AssignmentResponse> ReceivedResults { get; } = new List<AssignmentResponse>();
    }

    public void Dispose()
    {
        foreach (KeyValuePair<int, WebSocket> client in _clients)
        {
            client.Value.Dispose();
        }
    }
}
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using Shared;

namespace Master;

public class MasterServer : IDisposable
{
    private class Job(int total)
    {
        public int Total { get; } = total;
        public List<AssignmentResponse> ReceivedResults { get; } = new List<AssignmentResponse>();
    }

    private int _count = 0;
    private ConcurrentQueue<Assignment> _pendingAssignments = new ConcurrentQueue<Assignment>();

    private ConcurrentDictionary<ClientHandler, Assignment?> _clients =
        new ConcurrentDictionary<ClientHandler, Assignment?>();

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

        ClientHandler client = new ClientHandler(clientSocket, _count);
        client.ConnectionClosed += ClientOnConnectionClosed;
        client.MessageReceived += ClientOnMessageReceived;

        Task task = client.ListenAsync(CancellationToken.None);

        _clients[client] = null;

        SlaveConnected?.Invoke(_count);
    }

    private void ClientOnConnectionClosed(ClientHandler obj)
    {
        if (_clients.TryRemove(obj, out Assignment? ass))
        {
            obj.ConnectionClosed -= ClientOnConnectionClosed;
            obj.MessageReceived -= ClientOnMessageReceived;
            SlaveDisconnected?.Invoke(obj.Id);
            if (ass is not null)
            {
                _pendingAssignments.Enqueue(ass);
            }
        }
    }

    private void ClientOnMessageReceived(ClientHandler clientHandler, AssignmentResponse? response)
    {
        if (response is null)
        {
            Debug.WriteLine("COULDN't READ RESPONSE");
            return;
        }

        Debug.WriteLine("{0} {1} {2} ", response.JobId, response.ChunkIndex, response.Count);

        if (_jobs.TryGetValue(response.JobId, out Job? job))
        {
            _clients[clientHandler] = null;

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
            foreach (KeyValuePair<ClientHandler, Assignment?> client in _clients.Where(c => c.Value is null))
            {
                if (_pendingAssignments.IsEmpty)
                {
                    break;
                }

                if (_pendingAssignments.TryDequeue(out Assignment? ass))
                {
                    _clients[client.Key] = ass;

                    await client.Key.SendAssignment(ass);
                }
            }

            await Task.Delay(50);
        }
    }

    public void Dispose()
    {
        foreach (KeyValuePair<ClientHandler, Assignment?> client in _clients)
        {
            client.Key.Dispose();
        }
    }
}
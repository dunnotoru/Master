using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;
using Shared;

namespace Master;

public sealed class MasterServer : IDisposable
{
    private int _count = 0;
    private readonly Channel<Assignment> _asses = Channel.CreateUnbounded<Assignment>();
    private readonly Channel<AssignmentResponse> _results = Channel.CreateUnbounded<AssignmentResponse>();

    private readonly ConcurrentDictionary<ClientHandler, int> _clients =
        new ConcurrentDictionary<ClientHandler, int>();

    private readonly ConcurrentDictionary<Guid, Job> _jobs =
        new ConcurrentDictionary<Guid, Job>();

    public event Action<int>? SlaveConnected;
    public event Action<int>? SlaveDisconnected;
    public event Action<JobResult>? JobDone;

    public async Task Start(string listenerPrefix, CancellationToken cancellation)
    {
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        listener.Start();

        Task result = ResultAssemblingLoop(cancellation);
        Task listen = ListenConnectionsLoop(listener, cancellation);

        await Task.WhenAll(result, listen);
    }

    private async Task ListenConnectionsLoop(HttpListener listener, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            HttpListenerContext listenerContext = await listener.GetContextAsync();

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

        ClientHandler client = new ClientHandler(clientSocket, _count, _asses, _results.Writer);
        client.ConnectionClosed += ClientOnConnectionClosed;

        _ = client.ListenAsync(CancellationToken.None);

        _clients[client] = _count;

        SlaveConnected?.Invoke(_count);
    }

    private async Task ResultAssemblingLoop(CancellationToken cancellation)
    {
        while (await _results.Reader.WaitToReadAsync(cancellation))
        {
            AssignmentResponse response = await _results.Reader.ReadAsync(cancellation);
            Debug.WriteLine("RESPONSE RECEIVED: {0} {1} {2} ", response.JobId, response.ChunkId, response.Count);

            if (_jobs.TryGetValue(response.JobId, out Job? job))
            {
                job.ReceivedResults.Add(response);
                if (job.ReceivedResults.Count >= job.Total)
                {
                    Debug.WriteLine("JOB DONE {0}", job.ReceivedResults.Count);
                    _jobs.Remove(response.JobId, out _);
                    job.Timer.Stop();
                    JobDone?.Invoke(new JobResult(response.JobId, job.Timer.Elapsed,
                        job.ReceivedResults.Sum(x => x.Count)));
                    Debug.WriteLine(job.Timer.Elapsed.Seconds);
                }
            }

            Debug.WriteLine("JOBS REMAINING {0}", [string.Join(' ', _jobs.Keys)]);
        }
    }

    private void ClientOnConnectionClosed(ClientHandler obj)
    {
        if (_clients.TryRemove(obj, out _))
        {
            obj.ConnectionClosed -= ClientOnConnectionClosed;

            SlaveDisconnected?.Invoke(obj.Id);
        }
    }

    public async Task AddJob(string text, string substring)
    {
        Guid jobId = Guid.NewGuid();

        List<char[]> chunks = text.Chunk(int.Min(1024 * 64, text.Length)).ToList();

        Job job = new Job(chunks.Count);
        job.Timer.Start();
        _jobs[jobId] = job;

        for (int i = 0; i < chunks.Count; i++)
        {
            Assignment ass = new Assignment(jobId, i, chunks.Count, new string(chunks[i]), substring);
            await _asses.Writer.WriteAsync(ass);
        }
    }

    public void Dispose()
    {
        foreach (KeyValuePair<ClientHandler, int> client in _clients)
        {
            client.Key.Dispose();
        }
    }
}
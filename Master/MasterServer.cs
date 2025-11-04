using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;
using MemoryPack;
using Shared;

namespace Master;

public sealed class MasterServer : IDisposable
{
    private int _count = 0;
    private readonly Channel<Assignment> _asses = Channel.CreateUnbounded<Assignment>();
    private readonly Channel<AssignmentResult> _results = Channel.CreateUnbounded<AssignmentResult>();

    private readonly ConcurrentDictionary<ClientHandler, int> _connectedClients =
        new ConcurrentDictionary<ClientHandler, int>();

    private readonly ConcurrentDictionary<Guid, Job> _jobsInProgress =
        new ConcurrentDictionary<Guid, Job>();

    public event Action<int>? SlaveConnected;
    public event Action<int>? SlaveDisconnected;
    public event Action<JobResult>? JobDone;

    public async Task Start(string listenerPrefix, CancellationToken cancellation)
    {
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        listener.Start();

        Task result = ResultAssemblingLoopAsync(cancellation);
        Task listen = ListenConnectionsLoopAsync(listener, cancellation);

        await Task.WhenAll(result, listen);
    }

    private async Task ListenConnectionsLoopAsync(HttpListener listener, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            HttpListenerContext listenerContext = await listener.GetContextAsync();

            if (listenerContext.Request.IsWebSocketRequest)
            {
                Debug.WriteLine("WebSocket Request from {0}", listenerContext.Request.Url);
                await HandleConnectionAsync(listenerContext, cancellation);
            }
            else
            {
                Debug.WriteLine("Regular Request. Ignoring");
                listenerContext.Response.StatusCode = 404;
                listenerContext.Response.Close();
            }
        }
    }

    private async Task HandleConnectionAsync(HttpListenerContext listenerContext, CancellationToken cancellation)
    {
        WebSocketContext? webSocketContext;

        try
        {
            Debug.WriteLine("Accepting WebSocket Connection");
            webSocketContext = await listenerContext.AcceptWebSocketAsync(subProtocol: null);
            Debug.WriteLine("Success");
            Interlocked.Increment(ref _count);
        }
        catch (Exception e)
        {
            Debug.WriteLine("Fail");
            Console.WriteLine(e.Message);
            listenerContext.Response.StatusCode = 500;
            listenerContext.Response.Close();
            return;
        }

        WebSocket clientSocket = webSocketContext.WebSocket;
        ClientHandler client = new ClientHandler(clientSocket, _count, _asses, _results.Writer);
        client.ConnectionClosed += ClientOnConnectionClosed;
        _ = client.WorkLoopAsync(cancellation);

        _connectedClients[client] = _count;

        SlaveConnected?.Invoke(_count);
    }

    private async Task ResultAssemblingLoopAsync(CancellationToken cancellation)
    {
        while (await _results.Reader.WaitToReadAsync(cancellation))
        {
            AssignmentResult result = await _results.Reader.ReadAsync(cancellation);
            Debug.WriteLine("Received Result For Job {0}", result.Id);

            if (_jobsInProgress.TryGetValue(result.Id.JobId, out Job? job))
            {
                job.ReceivedResults.Add(result);
                if (job.ReceivedResults.Count >= job.Total)
                {
                    Debug.WriteLine("JOB DONE {0}", job.ReceivedResults.Count);
                    _jobsInProgress.Remove(result.Id.JobId, out _);
                    job.Timer.Stop();
                    JobDone?.Invoke(new JobResult(result.Id.JobId, job.Timer.Elapsed, 1));
                    Debug.WriteLine(job.Timer.Elapsed.Seconds);
                }
            }
            else
            {
                Debug.WriteLine("There's No Job With {0} in Progress", [result.Id.JobId]);
            }

            Debug.WriteLine("JOBS REMAINING {0}", [string.Join('\n', _jobsInProgress.Keys)]);
        }
    }

    private void ClientOnConnectionClosed(ClientHandler clientHandler)
    {
        if (_connectedClients.TryRemove(clientHandler, out _))
        {
            clientHandler.ConnectionClosed -= ClientOnConnectionClosed;
            SlaveDisconnected?.Invoke(clientHandler.Id);
        }
    }

    public async Task AddJobAsync(string text, string substring) //TODO: change to generic job
    {
        Guid jobId = Guid.NewGuid();

        List<char[]> chunks = text.Chunk(int.Min(1024 * 64, text.Length)).ToList();

        Job job = new Job(chunks.Count);
        job.Timer.Start();
        _jobsInProgress[jobId] = job;

        for (int i = 0; i < chunks.Count; i++)
        {
            AssignmentIdentifier id = new AssignmentIdentifier(jobId, i);
            Assignment ass = new Assignment(id, chunks.Count, "count-substrings",
                new Dictionary<string, byte[]>
                {
                    ["text"] = MemoryPackSerializer.Serialize(new string(chunks[i])),
                    ["substring"] = MemoryPackSerializer.Serialize(substring)
                }
            );

            await _asses.Writer.WriteAsync(ass);
        }
    }

    public void Dispose()
    {
        foreach (KeyValuePair<ClientHandler, int> client in _connectedClients)
        {
            client.Key.Dispose();
        }
    }
}
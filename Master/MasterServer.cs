using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;
using Shared;

namespace Master;

public class MasterServer : IDisposable
{
    private int _count = 0;
    private readonly Channel<Assignment> _asses = Channel.CreateUnbounded<Assignment>();

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

        await ListenConnectionsLoop(listener, cancellation);
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

        ClientHandler client = new ClientHandler(clientSocket, _count, _asses);
        client.ConnectionClosed += ClientOnConnectionClosed;
        client.MessageReceived += ClientOnMessageReceived;

        _ = client.ListenAsync(CancellationToken.None);

        _clients[client] = _count;

        SlaveConnected?.Invoke(_count);
    }

    private void ClientOnConnectionClosed(ClientHandler obj)
    {
        if (_clients.TryRemove(obj, out _))
        {
            obj.ConnectionClosed -= ClientOnConnectionClosed;
            obj.MessageReceived -= ClientOnMessageReceived;

            if (obj.AssInWork is not null)
            {
                _asses.Writer.TryWrite(obj.AssInWork);
            }

            SlaveDisconnected?.Invoke(obj.Id);
        }
    }

    private void ClientOnMessageReceived(ClientHandler client, AssignmentResponse? response)
    {
        if (response is null)
        {
            Debug.WriteLine("COULDN't READ RESPONSE");
            return;
        }
        
        Debug.WriteLine("RESPONSE RECEIVED {0}", [response]);
        
        Debug.WriteLine("{0} {1} {2} ", response.JobId, response.ChunkIndex, response.Count);

        if (_jobs.TryGetValue(response.JobId, out Job? job))
        {
            client.SetAssignmentComplete();
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

    public async Task SendJob(string text, string substring)
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
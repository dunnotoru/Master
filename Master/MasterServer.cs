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
    public event Action<Job>? JobDone;

    private readonly ModuleProvider _provider;

    public MasterServer(ModuleProvider provider)
    {
        _provider = provider;
    }

    public async Task RunAsync(string listenerPrefix, CancellationToken cancellation)
    {
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        listener.Start();

        Task first = await Task.WhenAny(
            ResultAssemblingLoopAsync(cancellation),
            ListenConnectionsLoopAsync(listener, cancellation)
        );

        try
        {
            await first;
            listener.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
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
        ClientHandler client = new ClientHandler(clientSocket, _count, _asses, _results.Writer, _provider);

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
                job.AddResult(result);
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
            clientHandler.Dispose();
            SlaveDisconnected?.Invoke(clientHandler.Id);
        }
    }

    public async Task EnqueueJobAsync(Job job, List<Assignment> asses)
    {
        foreach (Assignment ass in asses)
        {
            await _asses.Writer.WriteAsync(ass);
        }

        job.JobDone += OnJobDone;
        _jobsInProgress[job.Id] = job;
    }

    private void OnJobDone(Job result)
    {
        if (_jobsInProgress.TryRemove(result.Id, out Job? job))
        {
            job.JobDone -= OnJobDone;
        }

        JobDone?.Invoke(result);
    }

    public void Dispose()
    {
        foreach (KeyValuePair<ClientHandler, int> client in _connectedClients)
        {
            client.Key.Dispose();
        }
    }
}
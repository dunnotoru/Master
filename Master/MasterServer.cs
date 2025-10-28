using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Shared;

namespace Master;

public class MasterServer : IDisposable
{
    private int _count = 0;
    private List<ClientHandler> _clients = new List<ClientHandler>();
    private ConcurrentQueue<Assignment> _pendingAssignments = new ConcurrentQueue<Assignment>();
    private object _clientsLock = new object();

    public async Task Start(string listenerPrefix, CancellationToken cancellationToken)
    {
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        listener.Start();

        while (!cancellationToken.IsCancellationRequested)
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
        WebSocketContext? webSocketContext = null;
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

        WebSocket webSocket = webSocketContext.WebSocket;

        ClientHandler client = new ClientHandler(webSocket, _count);
        client.Closed += ClientOnClosed;
        client.MessageReceived += ClientOnMessageReceived;
        Task task = client.ListenAsync(CancellationToken.None);

        lock (_clientsLock)
        {
            _clients.Add(client);
            Debug.WriteLine(_clients.Count);
        }
    }

    public void EnqueueJob(string text, string substring)
    {
        Guid jobId = Guid.NewGuid();

        List<char[]> chunks = text.Chunk(text.Length).ToList();
        for (int i = 0; i < chunks.Count; i++)
        {
            Assignment ass = new Assignment(jobId, i, chunks.Count, Encoding.UTF8.GetBytes("1klas"));
            _pendingAssignments.Enqueue(ass);
        }

        _jobResults[jobId] = new Job(chunks.Count);
    }

    public async Task RunJobAssignment()
    {
        List<Task> tasks = new List<Task>();
        lock (_clientsLock)
        {
            foreach (ClientHandler client in _clients)
            {
                if (_pendingAssignments.Count == 0)
                {
                    break;
                }

                if (_pendingAssignments.TryDequeue(out Assignment? ass))
                {
                    Task t = client.SendAssignment(ass);
                    tasks.Add(t);
                }
            }
        }

        await Task.WhenAll(tasks);
    }

    private class Job(int total)
    {
        public int Total { get; } = total;
        public List<AssignmentResponse> ReceivedResults { get; } = new List<AssignmentResponse>();
    }

    private ConcurrentDictionary<Guid, Job> _jobResults =
        new ConcurrentDictionary<Guid, Job>();

    private void ClientOnMessageReceived(ClientHandler clientHandler, byte[] arg2)
    {
        AssignmentResponse? response = null;
        try
        {
            response = JsonSerializer.Deserialize<AssignmentResponse>(arg2);
            if (response is null)
            {
                return;
            }
        }
        catch (JsonException e)
        {
            Debug.WriteLine(e);
        }

        if (response is null)
        {
            return;
        }

        Debug.WriteLine("{0} {1} {2} ", response.JobId, response.ChunkIndex, response.payload);

        if (_jobResults.TryGetValue(response.JobId, out Job? job))
        {
            job.ReceivedResults.Add(response);
            if (job.ReceivedResults.Count > job.Total)
            {
                //invoke job done
                Debug.WriteLine("JOB DONE {0}", job.ReceivedResults.Count);
                _jobResults.Remove(response.JobId, out _);
            }
        }

        clientHandler.LastAssignment = null;
    }

    private void ClientOnClosed(ClientHandler clientHandler)
    {
        if (clientHandler.LastAssignment is not null)
        {
            _pendingAssignments.Enqueue(clientHandler.LastAssignment);
        }

        lock (_clientsLock)
        {
            _clients.Remove(clientHandler);
        }
    }

    public void Dispose()
    {
        foreach (ClientHandler client in _clients)
        {
            client.Dispose();
        }
    }
}
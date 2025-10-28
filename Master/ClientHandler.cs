using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Shared;

namespace Master;

class ClientHandler : IDisposable
{
    public WebSocket Socket { get; }
    public int Id { get; }

    public event Action<ClientHandler, byte[]>? MessageReceived;
    public event Action<ClientHandler>? Closed;

    public Assignment? LastAssignment { get; set; }

    public ClientHandler(WebSocket socket, int id)
    {
        Socket = socket;
        Id = id;
    }

    public async Task SendAssignment(Assignment assignment)
    {
        LastAssignment = assignment;
        byte[] buffer = JsonSerializer.SerializeToUtf8Bytes(assignment);
        await Socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true,
            CancellationToken.None);
    }

    public async Task ListenAsync(CancellationToken cancellation)
    {
        // CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            byte[] buffer = new byte[1024];

            while (!cancellation.IsCancellationRequested && Socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result =
                    await Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    Closed?.Invoke(this);
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    byte[] data = buffer[..result.Count];
                    MessageReceived?.Invoke(this, data);
                }
            }
        }
        catch (OperationCanceledException e)
        {
            Debug.WriteLine(e.Message);
            await Socket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Timeout Exception",
                CancellationToken.None);
            Closed?.Invoke(this);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
            await Socket.CloseAsync(WebSocketCloseStatus.InternalServerError, "", CancellationToken.None);
            Closed?.Invoke(this);
        }
    }

    public void Dispose()
    {
        Socket.Dispose();
    }
}
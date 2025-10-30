using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MemoryPack;
using Shared;

namespace Master;

class ClientHandler : IDisposable
{
    private WebSocket ClientSocket { get; }
    public int Id { get; }
    
    public const int ChunkSize = 1024 * 64; //64 KB

    public event Action<ClientHandler, AssignmentResponse?>? MessageReceived;
    public event Action<ClientHandler>? ConnectionClosed;

    public ClientHandler(WebSocket clientSocket, int id)
    {
        ClientSocket = clientSocket;
        Id = id;
    }

    public async Task SendAssignment(Assignment assignment)
    {
        byte[] buffer = MemoryPackSerializer.Serialize(assignment);
        int offset = 0;

        while (offset < buffer.Length)
        {
            int size = Math.Min(ChunkSize, buffer.Length - offset);
            ArraySegment<byte> segment = new ArraySegment<byte>(buffer, offset, size);

            bool endOfMessage = (offset + size) >= buffer.Length;

            await ClientSocket.SendAsync(segment, WebSocketMessageType.Binary, endOfMessage,
                CancellationToken.None);

            offset += size;
        }
    }

    public async Task ListenAsync(CancellationToken cancellation)
    {
        try
        {
            byte[] buffer = new byte[1024];

            while (ClientSocket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
            {
                WebSocketReceiveResult result =
                    await ClientSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ClientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    ConnectionClosed?.Invoke(this);
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    List<byte> receivedBytes = new List<byte>();

                    CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    
                    while (!result.EndOfMessage)
                    {
                        result = await ClientSocket.ReceiveAsync(new ArraySegment<byte>(buffer),
                            cts.Token);
                    }

                    receivedBytes.AddRange(buffer.Take(result.Count));
                    byte[] fullMessage = receivedBytes.ToArray();

                    AssignmentResponse? response = MemoryPackSerializer.Deserialize<AssignmentResponse>(fullMessage);

                    MessageReceived?.Invoke(this, response);
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
            await ClientSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "", CancellationToken.None);
            ConnectionClosed?.Invoke(this);
        }
    }

    public void Dispose()
    {
        ClientSocket.Dispose();
    }
}
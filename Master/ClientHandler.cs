using System.Diagnostics;
using System.IO;
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
    public const int BufferSize = 1024 * 64;

    public event Action<ClientHandler, AssignmentResponse?>? MessageReceived;
    public event Action<ClientHandler>? ConnectionClosed;

    public ClientHandler(WebSocket clientSocket, int id)
    {
        ClientSocket = clientSocket;
        Id = id;
    }

    public async Task SendAssignment(Assignment assignment)
    {
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
    }

    public async Task ListenAsync(CancellationToken cancellation)
    {
        try
        {
            while (ClientSocket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
            {
                byte[] buffer = new byte[BufferSize];
                using MemoryStream ms = new MemoryStream();

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
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                AssignmentResponse? response = MemoryPackSerializer.Deserialize<AssignmentResponse>(ms.ToArray());
                
                MessageReceived?.Invoke(this, response);
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
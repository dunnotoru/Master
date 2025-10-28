using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Shared;

namespace Slave;

class Program
{
    static void Main(string[] args)
    {
        Connect(new Uri("ws://localhost:5000/master"));
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    public static async Task Connect(Uri uri)
    {
        ClientWebSocket? socket = null;
        try
        {
            Console.WriteLine("Trying To Connect");
            socket = new ClientWebSocket();
            await socket.ConnectAsync(uri, CancellationToken.None);
            await ListenForAssignments(socket);
        }
        catch (Exception e)
        {
            Console.WriteLine("Fail");
            Console.WriteLine(e.Message);
        }
        finally
        {
            socket?.Dispose();
        }
    }

    public static async Task ListenForAssignments(ClientWebSocket socket)
    {
        byte[] buffer = new byte[1024];
        while (socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result =
                await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            }
            else
            {
                Console.WriteLine("Handle Response");
                byte[] arr = JsonSerializer.SerializeToUtf8Bytes(new AssignmentResponse(Guid.NewGuid(), 1, "penis"u8.ToArray()));
                await Task.Delay(TimeSpan.FromSeconds(2)); // Hardworking
                await socket.SendAsync(new ArraySegment<byte>(arr), WebSocketMessageType.Binary,
                    false, CancellationToken.None);
            }
        }
    }
}
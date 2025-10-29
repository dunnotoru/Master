using System.Net.WebSockets;
using MemoryPack;
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
        while (socket.State == WebSocketState.Open)
        {
            List<byte> receivedBytes = new List<byte>();
            byte[] buffer = new byte[1024];
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                receivedBytes.AddRange(buffer.Take(result.Count));
            } while (!result.EndOfMessage);

            byte[] fullMessage = receivedBytes.ToArray();
            
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            }
            else
            {
                Assignment? ass = MemoryPackSerializer.Deserialize<Assignment>(fullMessage);
                //handle errors

                int count = FindSubstrings(ass.Text, ass.Substring);

                AssignmentResponse response = new AssignmentResponse(ass.JobId, ass.ChunkId, count);

                Console.WriteLine("Handle Response");

                byte[] arr = MemoryPackSerializer.Serialize(response);
                await socket.SendAsync(new ArraySegment<byte>(arr), WebSocketMessageType.Binary,
                    false, CancellationToken.None);
            }
        }
    }

    private static int FindSubstrings(string text, string substring)
    {
        int result = 0;
        int n = text.Length;
        int m = substring.Length;
        if (m == 0) return result;

        for (int i = 0; i <= n - m; i++)
        {
            int j = 0;
            for (; j < m; j++)
            {
                if (text[i + j] != substring[j]) break;
            }

            if (j == m)
            {
                result += 1;
            }
        }

        return result;
    }
}
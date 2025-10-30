using System.Net.WebSockets;
using MemoryPack;
using Shared;

namespace Slave;

internal static class Program
{
    private static void Main(string[] args)
    {
        Connect(new Uri("ws://localhost:5000/master"));
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    private static async Task Connect(Uri uri)
    {
        ClientWebSocket? socket = null;
        try
        {
            Console.WriteLine("Trying To Connect");
            socket = new ClientWebSocket();
            await socket.ConnectAsync(uri, CancellationToken.None);
            Console.WriteLine("Success");
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

    private static async Task ListenForAssignments(ClientWebSocket socket)
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

                await Task.Delay(TimeSpan.FromSeconds(1));
                int count = FindSubstrings(ass.Text, ass.Substring);

                AssignmentResponse response = new AssignmentResponse(ass.JobId, ass.ChunkId, count);

                Console.WriteLine("Handle Response {0} {1}", response.JobId, response.ChunkIndex);

                int chunkSize = 1024;
                int offset = 0;

                buffer = MemoryPackSerializer.Serialize(response);
                
                while (offset < buffer.Length)
                {
                    int size = Math.Min(chunkSize, buffer.Length - offset);
                    ArraySegment<byte> segment = new ArraySegment<byte>(buffer, offset, size);

                    bool endOfMessage = (offset + size) >= buffer.Length;

                    await socket.SendAsync(segment, WebSocketMessageType.Binary, endOfMessage,
                        CancellationToken.None);

                    offset += size;
                }
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
using System.Net.WebSockets;
using MemoryPack;
using Shared;

namespace Slave;

internal static class Program
{
    private const int ChunkSize = 1024 * 64;
    private const int BufferSize = 1024 * 64;
    
    
    private static void Main(string[] args)
    {
        CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Connect(new Uri("ws://localhost:5000/master"), cts.Token);
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    private static async Task Connect(Uri uri, CancellationToken cancellation)
    {
        ClientWebSocket? socket = null;

        try
        {
            Console.WriteLine("Trying To Connect");
            socket = new ClientWebSocket();
            await socket.ConnectAsync(uri, cancellation);
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
            byte[] buffer = new byte[BufferSize];
            using MemoryStream ms = new MemoryStream();

            WebSocketReceiveResult result;
            do
            {
                result =
                    await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    Console.WriteLine("Closed Normal Closure");
                    return;
                }

                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            Assignment? ass = MemoryPackSerializer.Deserialize<Assignment>(ms.ToArray());
            
            await Task.Delay(TimeSpan.FromSeconds(1));
            
            int count = FindSubstrings(ass.Text, ass.Substring);

            AssignmentResponse response = new AssignmentResponse(ass.JobId, ass.ChunkId, count);

            Console.WriteLine("Handle Response {0} {1}", response.JobId, response.ChunkIndex);

            buffer = MemoryPackSerializer.Serialize(response);

            await socket.SendAsync(buffer, WebSocketMessageType.Binary, false,
                CancellationToken.None);
            
            byte[] data = MemoryPackSerializer.Serialize(response);

            int offset = 0;

            while (offset < buffer.Length)
            {
                int size = Math.Min(ChunkSize, buffer.Length - offset);
                bool endOfMessage = (offset + size) >= buffer.Length;

                ArraySegment<byte> segment = new ArraySegment<byte>(buffer, offset, size);
                await socket.SendAsync(segment, WebSocketMessageType.Binary, endOfMessage, CancellationToken.None);

                offset += size;
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
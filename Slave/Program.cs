using System.Net;

namespace Slave;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        bool run = true;
        do
        {
            try
            {
                Console.Write("Подключиться к ip: ");
                string? ipString = Console.ReadLine();
                if (!IPAddress.TryParse(ipString ?? "", out IPAddress? ip))
                {
                    ip = IPAddress.Loopback;
                };
                
                Console.Write("Порт: ");
                string? portString = Console.ReadLine();
                int port = int.Parse(portString ?? "");

                UriBuilder builder = new UriBuilder("ws", ip.ToString(), port, "master");
                await Run(builder.Uri);
            }
            catch (Exception e)
            {
                Console.WriteLine("Произошла ошибка {0}", e);
            }

            Console.WriteLine("Повторить попытку подключения? [Д/Н]");
            ConsoleKeyInfo key = Console.ReadKey(true);
            if (key.Key is ConsoleKey.N or ConsoleKey.Y)
            {
                run = false;
            }
        } while (run);
    }

    private static async Task Run(Uri connectionUri)
    {
        AlgorithmProvider provider = new AlgorithmProvider();
        CancellationTokenSource workCts = new CancellationTokenSource();
        SlaveClient client = new SlaveClient(provider);
        Task connection = client.Connect(connectionUri, workCts.Token);
        Console.ReadKey(true);
        await workCts.CancelAsync();
        await connection;
    }
}
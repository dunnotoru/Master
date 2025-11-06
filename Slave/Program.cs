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
                using StreamReader propsFile = File.OpenText("./application.properties");
                string? line = await propsFile.ReadLineAsync();
                if (line is null)
                {
                    throw new ArgumentNullException(nameof(line), "connection-string was null");
                }

                string[] kv = line.Split('=', 2);
                string k = kv[0];
                string v = kv[1];

                if ("connection-string".Equals(k))
                {
                    await Run(new Uri(v));
                }
                else
                {
                    return;
                }

            }
            catch (Exception e)
            {
                Console.WriteLine("An error occured during execution {0}", e);
            }

            Console.WriteLine("Try To Reconnect? [Y/N]");
            ConsoleKeyInfo key = Console.ReadKey();
            if (key.Key == ConsoleKey.N)
            {
                run = false;
            }
        } while (run);
    }

    private static async Task Run(Uri connectionUri)
    {
        AlgorithmProvider provider = new AlgorithmProvider();
        CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        CancellationTokenSource workCts = new CancellationTokenSource();
        SlaveClient client = new SlaveClient(provider);
        Task connection = client.Connect(connectionUri, cts.Token, workCts.Token);
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
        await cts.CancelAsync();
        await workCts.CancelAsync();
        await connection;
    }
}
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
                await Run();
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

    private static async Task Run()
    {
        AlgorithmProvider provider = new AlgorithmProvider();
        CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        CancellationTokenSource workCts = new CancellationTokenSource();
        SlaveClient client = new SlaveClient(provider);
        Task connection = client.Connect(new Uri("ws://localhost:5000/master"), cts.Token, workCts.Token);
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
        await cts.CancelAsync();
        await workCts.CancelAsync();
        await connection;
    }
}
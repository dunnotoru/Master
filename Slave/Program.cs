namespace Slave;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        try
        {
            await Run();
        }
        catch (Exception e)
        {
            Console.WriteLine("An error occured during execution {0}", e);
        }
    }

    private static async Task Run()
    {
        CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        SlaveClient client = new SlaveClient();
        Task connection = client.Connect(new Uri("ws://localhost:5000/master"), cts.Token);
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
        await cts.CancelAsync();
        await connection;
    }
}
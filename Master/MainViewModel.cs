using System.Diagnostics;

namespace Master;

public class MainViewModel : ViewModelBase
{
    private MasterServer _server;

    public MainViewModel()
    {
        Debug.WriteLine("SERVER VIEWMODEL START");
        _server = new MasterServer();
        Task.Run(() => _server.Start("http://localhost:5000/master/", CancellationToken.None));
    }

    public async Task SendJob()
    {
        Debug.WriteLine("SEND JOB");
        _server.EnqueueJob("penisaaa", "hu");
        await _server.RunJobAssignment();
    }
}
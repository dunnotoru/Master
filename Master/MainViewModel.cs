using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Master;

public partial class MainViewModel : ObservableObject
{
    private readonly MasterServer _server;
    private readonly SynchronizationContext? _uiContext;

    [ObservableProperty] private string? _substring = "";
    [ObservableProperty] private string? _fileToOpen = "";
    
    public ObservableCollection<int> Clients { get; } = new ObservableCollection<int>();

    public MainViewModel()
    {
        Debug.WriteLine("SERVER VIEWMODEL START");
        _server = new MasterServer();
        _server.SlaveConnected += ServerOnSlaveConnected;
        _server.SlaveDisconnected += ServerOnSlaveDisconnected;
        Task.Run(() => _server.Start("http://localhost:5000/master/", CancellationToken.None));

        _uiContext = SynchronizationContext.Current;
    }

    private void ServerOnSlaveDisconnected(int obj)
    {
        _uiContext?.Post(_ => Clients.Remove(obj), null);
    }

    private void ServerOnSlaveConnected(int obj)
    {
        _uiContext?.Post(_ => Clients.Add(obj), null);
    }

    [RelayCommand]
    private async Task SendJobAsync()
    {
        Debug.WriteLine("SEND JOB {0}", [FileToOpen]);
        await _server.SendJob(File.ReadAllText(FileToOpen), "dm");
    }
}
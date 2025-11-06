using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Master.FormFields;
using Shared;

namespace Master;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly MasterServer _server;
    private readonly SynchronizationContext? _uiContext;

    public ObservableCollection<int> Clients { get; } = new ObservableCollection<int>();
    public ObservableCollection<JobViewModel> Results { get; } = new ObservableCollection<JobViewModel>();
    public ObservableCollection<Module> Modules { get; } = new ObservableCollection<Module>();

    public ObservableCollection<FormField> Fields { get; } = new ObservableCollection<FormField>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnqueueJobCommand))]
    private Module? _selectedModule;

    private readonly ModuleProvider _provider;

    public MainViewModel()
    {
        Debug.WriteLine("SERVER VIEWMODEL START");
        _provider = new ModuleProvider();

        _provider.ScanModules();
        foreach (Module m in _provider.Modules.Values)
        {
            Modules.Add(m);
        }

        _server = new MasterServer(_provider);
        _server.SlaveConnected += ServerOnSlaveConnected;
        _server.SlaveDisconnected += ServerOnSlaveDisconnected;
        _server.JobDone += ServerOnJobDone;
        _uiContext = SynchronizationContext.Current;
        Clients.CollectionChanged += (_, _) => EnqueueJobCommand.NotifyCanExecuteChanged();

        Task.Run(async () =>
        {
            try
            {
                await _server.RunAsync("http://localhost:5000/master/", CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception caught in viewmodel {0}", ex);
            }
        });
    }

    partial void OnSelectedModuleChanged(Module? value)
    {
        if (value is null)
        {
            Fields.Clear();
            return;
        }

        Fields.Clear();

        foreach ((string k, string v) in value.Executor.Schema)
        {
            if (string.Equals(v, "text"))
            {
                Fields.Add(new TextField(k));
            }
            else if (v.StartsWith("file/"))
            {
                Fields.Add(new FileField(k));
            }
        }
    }

    private void ServerOnJobDone(Job job)
    {
        string name = job.AlgorithmName;
        if (_provider.TryGetExecutor(name, out Module? module))
        {
            object? r = module.Executor.AggregateResults(job.Results.ToList());
            JobViewModel vm = new JobViewModel(job.Id, r.ToString(), TimeSpan.MaxValue);
            _uiContext?.Post(_ => Results.Add(vm), null);
        }
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
    private async Task EnqueueJob()
    {
        if (Fields.Any(f => !f.Validate(out _)))
        {
            return;
        }
        
        IDictionary<string, string> dict = new Dictionary<string, string>();
        foreach (FormField f in Fields)
        {
            dict[f.LabelText] = f.Value!;
        }

        List<Assignment> asses = SelectedModule!.Executor.CreateAssignments(dict, out Guid jobId);
        Job job = new Job(jobId, SelectedModule!.Executor.Name, asses.Select(a => a.Id));
        Debug.WriteLine("SEND JOB");
        await _server.EnqueueJobAsync(job, asses);
    }

    public void Dispose()
    {
        _server.Dispose();
    }
}
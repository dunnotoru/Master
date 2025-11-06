using System.Collections.ObjectModel;
using System.ComponentModel;
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

    private CancellationTokenSource serverCts;
    private Task serverRun;

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

        serverCts = new CancellationTokenSource();
        serverRun = Task.Run(async () =>
        {
            try
            {
                await _server.RunAsync("http://localhost:5000/master/", serverCts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception caught in viewmodel {0}", ex);
            }
        });
    }

    partial void OnSelectedModuleChanged(Module? value)
    {
        foreach (FormField f in Fields)
        {
            f.PropertyChanged -= FieldOnPropertyChanged;
        }

        Fields.Clear();

        if (value is null)
        {
            return;
        }

        foreach ((string k, string v) in value.Executor.Schema)
        {
            FormField field;
            if (v.StartsWith("file/"))
            {
                field = new FileField(k);
                field.PropertyChanged += FieldOnPropertyChanged;
            }
            else
            {
                field = new TextField(k);
                field.PropertyChanged += FieldOnPropertyChanged;
            }

            Fields.Add(field);
        }
    }

    private void FieldOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FormField.Value))
        {
            EnqueueJobCommand.NotifyCanExecuteChanged();
            DoWorkCommand.NotifyCanExecuteChanged();
        }
    }

    private void ServerOnJobDone(Job job)
    {
        string name = job.AlgorithmName;
        if (_provider.TryGetExecutor(name, out Module? module))
        {
            object? r = module.Executor.AggregateResults(job.Results.ToList());
            Debug.Print(r?.ToString());
            string val = r?.ToString();
            if (_serverWork)
            {
                val = $"Работа выполнена на сервере, результат {val}";
            }
            else
            {
                val = $"Работа выполнена удаленно, результат {val}";
            }

            _serverWork = false;

            JobViewModel vm = new JobViewModel(job.Id, val, job.Elapsed);
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

    [RelayCommand(CanExecute = nameof(CanEnqueueJob))]
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

    private bool _serverWork = false;

    [RelayCommand(CanExecute = nameof(CanSubmitJob))]
    private void DoWork()
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

        _serverWork = true;

        IAlgorithmExecutor executor = SelectedModule!.Executor;

        List<Assignment> asses = executor.CreateAssignments(dict, out Guid jobId);
        Job job = new Job(jobId, executor.Name, asses.Select(a => a.Id));

        job.JobDone += ServerOnJobDone;

        foreach (Assignment ass in asses)
        {
            byte[] result = executor.Execute(ass.Parameters);
            job.AddResult(new AssignmentResult(ass.Id, executor.ResultType, result));
        }
    }

    private bool CanSubmitJob()
    {
        if (SelectedModule is null)
        {
            return false;
        }

        return Fields.All(f => f.Validate(out _));
    }

    private bool CanEnqueueJob()
    {
        if (SelectedModule is null ||
            Clients.Count == 0)
        {
            return false;
        }

        return Fields.All(f => f.Validate(out _));
    }

    public void Dispose()
    {
        serverCts.Cancel();
        _server.Dispose();
    }
}
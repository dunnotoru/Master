using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Shared;

namespace Slave;

public class AlgorithmProvider
{
    public string ModuleDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lab", "SlaveModules");

    private Dictionary<string, IAlgorithmExecutor> _modules = new Dictionary<string, IAlgorithmExecutor>();

    public void ScanModules()
    {
        if (!Path.Exists(ModuleDirectory))
        {
            Directory.CreateDirectory(ModuleDirectory);
        }

        string[] files = Directory.GetFiles(ModuleDirectory, "*.dll", SearchOption.TopDirectoryOnly);

        foreach (string file in files)
        {
            LoadFromFile(file);
        }
    }

    private void LoadFromFile(string file)
    {
        Assembly asm = Assembly.LoadFile(file);
        Type? executor = asm.GetTypes()
            .FirstOrDefault(t => typeof(IAlgorithmExecutor).IsAssignableFrom(t) && !t.IsAbstract);
        if (executor is null)
        {
            return;
        }

        IAlgorithmExecutor instance = (IAlgorithmExecutor)Activator.CreateInstance(executor)!;
        _modules[instance.Name] = instance;
    }

    public bool TryGetExecutor(string name, [NotNullWhen(true)] out IAlgorithmExecutor? executor)
    {
        return _modules.TryGetValue(name, out executor);
    }

    public void AddExecutor(string name, byte[] raw)
    {
        Assembly asm = Assembly.Load(raw);
        Type? executor = asm.GetTypes()
            .FirstOrDefault(t => typeof(IAlgorithmExecutor).IsAssignableFrom(t) && !t.IsAbstract);
        if (executor is null)
        {
            return;
        }

        if (!Path.Exists(ModuleDirectory))
        {
            Directory.CreateDirectory(ModuleDirectory);
        }

        string path = Path.Combine(ModuleDirectory, name);
        path = Path.ChangeExtension(path, "dll");
        File.WriteAllBytes(path, raw);
        IAlgorithmExecutor instance = (IAlgorithmExecutor)Activator.CreateInstance(executor)!;

        _modules[instance.Name] = instance;
    }

    public void AddExecutor(string file)
    {
        LoadFromFile(file);
    }
}
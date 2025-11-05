using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Shared;

namespace Master;

public class AlgorithmProvider
{
    public string ModuleDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lab", "MasterModules");

    private Dictionary<string, FileInfo> _modules = new Dictionary<string, FileInfo>();

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
        Assembly asm = Assembly.LoadFrom(file);
        Type? executor = asm.GetTypes().FirstOrDefault(t => typeof(IAlgorithmExecutor).IsAssignableFrom(t) && !t.IsAbstract);
        if (executor is null)
        {
            return;
        }
        
        IAlgorithmExecutor instance = (IAlgorithmExecutor)Activator.CreateInstance(executor)!;
        _modules[instance.Name] = new FileInfo(file);
    }

    public bool TryGetExecutor(string name, [NotNullWhen(true)] out FileInfo? executor)
    {
        return _modules.TryGetValue(name, out executor);
    }

    public void AddExecutor(string file)
    {
        LoadFromFile(file);
    }
}
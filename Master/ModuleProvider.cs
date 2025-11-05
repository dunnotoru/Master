using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Shared;

namespace Master;

public class ModuleProvider
{
    public string ModuleDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lab", "MasterModules");

    private Dictionary<string, Module> _modules = new Dictionary<string, Module>();
    public FrozenDictionary<string, Module> Modules => _modules.ToFrozenDictionary();

    public void ScanModules()
    {
        if (!Path.Exists(ModuleDirectory))
        {
            Directory.CreateDirectory(ModuleDirectory);
        }

        string[] files = Directory.GetFiles(ModuleDirectory, "*.dll", SearchOption.TopDirectoryOnly);

        foreach (string file in files)
        {
            AddModuleIfValid(file);
        }
    }

    public void AddModuleIfValid(string file)
    {
        Assembly asm = Assembly.LoadFile(file);

        Type? t = asm.GetTypes().FirstOrDefault(t =>
            typeof(IAlgorithmExecutor).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });

        if (t is null)
        {
            return;
        }

        IAlgorithmExecutor instance = (IAlgorithmExecutor)Activator.CreateInstance(t)!;
        _modules[instance.Name] = new Module(instance, new FileInfo(file));
    }

    public bool TryGetExecutor(string name, [NotNullWhen(true)] out Module? executor)
    {
        return _modules.TryGetValue(name, out executor);
    }
}
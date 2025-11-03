namespace Shared;

public interface IAlgorithmExecutor
{
    public Guid Id { get; }
    public string Name { get; }
    public object? Execute(IDictionary<string, object?> parameters);
}
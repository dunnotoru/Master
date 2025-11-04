namespace Shared;

public interface IAlgorithmExecutor
{
    public Guid Id { get; }
    public string Name { get; }
    public byte[] Execute(IDictionary<string, byte[]> parameters);
}
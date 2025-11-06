namespace Shared;

public interface IAlgorithmExecutor
{
    public Guid Id { get; }
    public string Name { get; }
    public Type ResultType { get; }
    public string ArgsSchema { get; }
    public IDictionary<string, string> Schema { get; }
    public byte[] Execute(IDictionary<string, byte[]> parameters);
    public List<Assignment> CreateAssignments(IDictionary<string, string> args, out Guid jobId);
    public object? AggregateResults(List<AssignmentResult> results);
}
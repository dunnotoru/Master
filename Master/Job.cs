using System.Collections.ObjectModel;
using System.Diagnostics;
using Shared;

namespace Master;

public class Job
{
    public Guid Id { get; }
    public string AlgorithmName { get; }
    private HashSet<AssignmentIdentifier> _todo;
    private List<AssignmentResult> _receivedResults = new List<AssignmentResult>();
    private Stopwatch Timer { get; }
    public event Action<Job>? JobDone;
    public ReadOnlyCollection<AssignmentResult> Results => _receivedResults.AsReadOnly();
    public TimeSpan Elapsed => Timer.Elapsed;

    public Job(Guid id, string algorithmName, IEnumerable<AssignmentIdentifier> assignments)
    {
        Id = id;
        AlgorithmName = algorithmName;
        _todo = assignments.ToHashSet();
        Timer = Stopwatch.StartNew();
    }

    public void AddResult(AssignmentResult result)
    {
        if (_todo.Remove(result.Id))
        {
            _receivedResults.Add(result);
        }

        if (_todo.Count != 0)
        {
            return;
        }

        Timer.Stop();

        JobDone?.Invoke(this);
    }
}
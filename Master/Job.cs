using System.Diagnostics;
using Shared;

namespace Master;

public class Job(int total)
{
    public int Total { get; } = total;
    public List<AssignmentResult> ReceivedResults { get; } = new List<AssignmentResult>();
    public Stopwatch Timer { get; } = new Stopwatch();
}
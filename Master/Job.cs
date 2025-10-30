using System.Diagnostics;
using Shared;

namespace Master;

public class Job(int total)
{
    public int Total { get; } = total;
    public List<AssignmentResponse> ReceivedResults { get; } = new List<AssignmentResponse>();
    public Stopwatch Timer { get; } = new Stopwatch();
}
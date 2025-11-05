namespace Master;

public class JobViewModel
{
    public Guid Id { get; }
    public string Result { get; }
    public TimeSpan TimeElapsed { get; }

    public JobViewModel(Guid jobId, string result, TimeSpan timeElapsed)
    {
        Id = jobId;
        Result = result;
        TimeElapsed = timeElapsed;
    }
}
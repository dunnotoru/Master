namespace Master;

public record JobResult(Guid JobId, TimeSpan ElapsedTime, int Count);
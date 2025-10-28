namespace Shared;

public record AssignmentResponse(Guid JobId, int ChunkIndex, byte[] payload);
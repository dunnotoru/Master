namespace Shared;

public record Assignment(Guid JobId, int ChunkId, int TotalChunks, byte[] Payload);

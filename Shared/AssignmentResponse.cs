using MemoryPack;

namespace Shared;

[MemoryPackable]
public partial record AssignmentResponse(Guid JobId, int ChunkId, int Count);
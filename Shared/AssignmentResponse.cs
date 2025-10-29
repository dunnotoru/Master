using MemoryPack;

namespace Shared;

[MemoryPackable]
public partial record AssignmentResponse(Guid JobId, int ChunkIndex, int Count);
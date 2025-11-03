using MemoryPack;

namespace Shared;

[MemoryPackable]
public partial record AssignmentIdentifier(Guid JobId, int ChunkId);
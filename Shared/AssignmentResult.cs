using MemoryPack;

namespace Shared;

[MemoryPackable]
public partial record AssignmentResult(AssignmentIdentifier Id, int Count);
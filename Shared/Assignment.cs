using MemoryPack;

namespace Shared;

[MemoryPackable]
public partial record Assignment(
    AssignmentIdentifier Id,
    int TotalChunks,
    string AlgorithmName,
    IDictionary<string, byte[]> Parameters
);
using MemoryPack;

namespace Shared;

[MemoryPackable]
public partial record Assignment(AssignmentIdentifier Id, int TotalChunks, string Text, string Substring);

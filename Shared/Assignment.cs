using MemoryPack;

namespace Shared;

[MemoryPackable]
public partial record Assignment(Guid JobId, int ChunkId, int TotalChunks, string Text, string Substring);

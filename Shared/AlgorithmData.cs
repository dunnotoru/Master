using MemoryPack;

namespace Shared;

[MemoryPackable]
public partial record AlgorithmData(string Name, byte[] RawFileData);

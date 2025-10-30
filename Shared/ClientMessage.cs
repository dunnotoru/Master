using MemoryPack;

namespace Shared;

public enum ClientMessageType
{
    Result = 0,
    AlgorithmRequest,
    Error
}

[MemoryPackable]
public partial class ClientMessage
{
    public ClientMessageType type;
    public byte[] payload;
}
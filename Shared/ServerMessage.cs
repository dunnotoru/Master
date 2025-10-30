using MemoryPack;

namespace Shared;

public enum ServerMessageType
{
    Assignment = 0,
    Algorithm
}

[MemoryPackable]
public partial class ServerMessage
{
    public ServerMessageType type;
    public byte[] payload;
}
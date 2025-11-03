using MemoryPack;

namespace Shared;

public enum ServerMessageType
{
    Assignment = 0,
    Algorithm
}

[MemoryPackable]
public partial record ServerMessage(ServerMessageType MessageType, byte[] Payload)
{
    public Guid Id { get; } = Guid.NewGuid();
}
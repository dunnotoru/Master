using MemoryPack;

namespace Shared;

public enum ClientMessageType
{
    Result = 0,
    AssignmentRequest,
    AlgorithmRequest,
    Error
}

[MemoryPackable]
public partial record ClientMessage(ClientMessageType MessageType, byte[]? Payload)
{
    public Guid Id { get; } = Guid.NewGuid();
}
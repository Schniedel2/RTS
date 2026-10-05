namespace RTS.Network;

public enum LocalRequestState { Pending, Accepted, Rejected, Abandoned }

/// <summary>Game-thread feedback for host-local actors; never serialized onto the wire.</summary>
public sealed class LocalRequestReceipt(NetworkMessage request, long generation)
{
    public NetworkMessage Request { get; } = request;
    public long Generation { get; } = generation;
    public LocalRequestState State { get; internal set; }
    public AIOrderResult Result { get; } = new();
    public string? Reason { get; internal set; }
}

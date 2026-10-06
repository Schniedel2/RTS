using System;
namespace RTS.Network;

public enum LocalRequestState { Pending, Accepted, Rejected, Abandoned }

/// <summary>Game-thread outcome shared by local and remote actors; correlated by wire request id.</summary>
public class RequestReceipt(NetworkMessage request, long generation)
{
    public bool WasSent { get; internal set; }
    public Guid RequestId => Request.RequestId!.Value;
    public NetworkMessage Request { get; } = request;
    public long Generation { get; } = generation;
    public LocalRequestState State { get; internal set; }
    public AIOrderResult Result { get; } = new();
    public string? Reason { get; internal set; }
}

/// <summary>Compatibility name; uses the same contract on local and remote connections.</summary>
public sealed class LocalRequestReceipt(NetworkMessage request, long generation) : RequestReceipt(request, generation);

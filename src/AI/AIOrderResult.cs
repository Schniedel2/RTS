using System.Collections.Generic;

namespace RTS;

public enum AIOrderStatus { Requested, Accepted, Rejected, InProgress, Completed, Failed }
public enum AIOrderFailure { None, Resources, BuildSite, Producer, Perk, InvalidTarget, Validation, Cancelled, Timeout, SessionChanged, RecoveryCooldown }

/// <summary>Shared host-local outcome; execution still uses normal network requests.</summary>
public sealed class AIOrderResult
{
    private readonly List<AIOrderStatus> _transitions = [AIOrderStatus.Requested];
    public AIOrderStatus Status { get; private set; } = AIOrderStatus.Requested;
    public AIOrderFailure Failure { get; private set; }
    public string? Reason { get; private set; }
    public bool WasAccepted { get; private set; }
    public IReadOnlyList<AIOrderStatus> Transitions => _transitions;
    internal bool ManagedByQueue;
    public bool IsTerminal => Status is AIOrderStatus.Completed or AIOrderStatus.Rejected or AIOrderStatus.Failed;

    internal void Set(AIOrderStatus status, AIOrderFailure failure = AIOrderFailure.None, string? reason = null)
    {
        if (IsTerminal) return;
        WasAccepted |= status == AIOrderStatus.Accepted;
        if (Status != status) _transitions.Add(status);
        Status = status; Failure = failure; Reason = reason;
    }
}

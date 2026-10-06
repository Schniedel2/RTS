using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RTS.Network;

public sealed record AIControllerHealth(double HeartbeatAgeSeconds, double ProgressAgeSeconds, string? LastFallbackReason);

public sealed partial class NetworkHandler
{
    public const double AIHeartbeatIntervalSeconds = 1;
    public const double AIControllerTimeoutSeconds = 10;
    public const double AIControllerStartupGraceSeconds = 15;
    private sealed class AILease(long generation, double now)
    {
        public long Generation = generation, Sequence;
        public double Started = now, Heartbeat = now, Progress = now;
        public bool HasHeartbeat;
    }
    private readonly Dictionary<Guid, AILease> _aiLeases = [];
    private readonly Dictionary<Guid, string> _aiFallbackReasons = [];
    // Monotonic wall time; deliberately independent from accelerated simulation time.
    internal Func<double>? AIClockOverride;
    private double AIRealTime => AIClockOverride?.Invoke() ?? _localClock.Elapsed.TotalSeconds;
    internal double AIHeartbeatTime => AIRealTime;
    internal event Action<AIControllerAssignment>? AIControllerAssignmentChanged;
    private void StartAIControllerLease(AIControllerAssignment assignment)
    {
        _aiLeases.Remove(assignment.ArmyId);
        if (assignment.ControllerPeerId is Guid peer && peer != LocalPeerId)
            _aiLeases[assignment.ArmyId] = new(assignment.Generation, AIRealTime);
    }
    internal Task SendAIHeartbeatAsync(AIControllerAssignment assignment, long sequence)
    {
        if (IsHost || !CanRunAI(assignment.ArmyId) || AIControllers.Find(assignment.ArmyId) != assignment) return Task.CompletedTask;
        RunDiagnostics?.Heartbeat(assignment.ArmyId, assignment.Generation, sequence);
        return SendToHostAsync(new(NetworkMessageType.AIControllerHeartbeat, LocalPeerId)
        {
            AIControllerArmyId = assignment.ArmyId, AIControllerActorId = assignment.ActorId,
            AIControllerGeneration = assignment.Generation, ControllerPeerId = LocalPeerId, AIUpdateSequence = sequence
        });
    }
    private void ReceiveAIHeartbeat(NetworkMessage message, Guid transportPeer)
    {
        var bound = message with { ControllerPeerId = transportPeer };
        if (!AIControllers.Authorizes(bound) || bound.AIControllerArmyId is not Guid army ||
            !_aiLeases.TryGetValue(army, out var lease) || lease.Generation != bound.AIControllerGeneration ||
            bound.AIUpdateSequence < lease.Sequence || bound.AIUpdateSequence <= 0) return;
        double now = AIRealTime;
        lease.HasHeartbeat = true; lease.Heartbeat = now;
        if (bound.AIUpdateSequence > lease.Sequence) { lease.Sequence = bound.AIUpdateSequence; lease.Progress = now; }
        if (RunDiagnostics is not null && _members.TryGetValue(transportPeer, out var peer))
            _ = SendAsync(peer.Client, new(NetworkMessageType.AIControllerHeartbeat, LocalPeerId)
            { AIControllerArmyId = army, AIControllerGeneration = bound.AIControllerGeneration, AIUpdateSequence = bound.AIUpdateSequence, TargetId = transportPeer });
    }
    private void UpdateAIControllerLeases()
    {
        if (!IsHost) return;
        double now = AIRealTime;
        foreach (var assignment in AIControllers.Snapshot())
        {
            if (assignment.ControllerPeerId is not Guid peer || peer == LocalPeerId) continue;
            if (!_aiLeases.TryGetValue(assignment.ArmyId, out var lease) || lease.Generation != assignment.Generation)
            { StartAIControllerLease(assignment); lease = _aiLeases[assignment.ArmyId]; }
            string? reason = !_members.ContainsKey(peer) ? "Controller peer disconnected." :
                !lease.HasHeartbeat && now - lease.Started >= AIControllerStartupGraceSeconds ? "Controller did not start." :
                lease.HasHeartbeat && now - lease.Heartbeat >= AIControllerTimeoutSeconds ? "Controller heartbeat timed out." :
                lease.HasHeartbeat && now - lease.Progress >= AIControllerTimeoutSeconds ? "Controller decision updates stalled." : null;
            if (reason is null) continue;
            // Revoke by advancing the generation before any new host decision can issue a request.
            AssignAIController(assignment.ArmyId, assignment.ActorId, LocalPeerId, assignment.Profile);
            _aiFallbackReasons[assignment.ArmyId] = reason;
            Diagnostic?.Invoke($"AI {assignment.ActorId} returned to host: {reason}");
        }
    }
    public AIControllerHealth? GetAIControllerHealth(Guid army)
    {
        if (!_aiLeases.TryGetValue(army, out var lease))
            return _aiFallbackReasons.TryGetValue(army, out var reason) ? new(0, 0, reason) : null;
        return new(Math.Max(0, AIRealTime - lease.Heartbeat), Math.Max(0, AIRealTime - lease.Progress), _aiFallbackReasons.GetValueOrDefault(army));
    }
    private void AbandonSupersededAIRequests(AIControllerAssignment assignment)
    {
        foreach (var receipt in _requestReceipts.Values.ToArray())
            if (receipt.State == LocalRequestState.Pending && receipt.Request.AIControllerArmyId == assignment.ArmyId &&
                receipt.Request.AIControllerGeneration != assignment.Generation)
                AbandonLocalRequest(receipt.Request, "Controller assignment changed.");
    }
}

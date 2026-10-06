using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
namespace RTS.Network;

public sealed record AIControllerAssignment(Guid ArmyId, Guid ActorId, Guid? ControllerPeerId,
    long Generation, AIStrategyProfile Profile)
{
    public int ProfileVersion { get; init; } = 1;
    public string ProfileFingerprint { get; init; } = Fingerprint(Profile);
    public static string Fingerprint(AIStrategyProfile profile) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(profile)));
}
public sealed record BotControllerOffer(int MaximumArmies, string? ProposedProfileId)
{
    public bool IsValid => MaximumArmies is >= 1 and <= 32 && (ProposedProfileId is null ||
        Enum.GetValues<AIStrategyProfileType>().Any(t => AIProfileCatalog.Id(t) == ProposedProfileId));
}

/// <summary>Session-local host authority; revoked entries retain their generation.</summary>
public sealed class AIControllerAssignments
{
    private readonly Dictionary<Guid, AIControllerAssignment> _assignments = [];
    public AIControllerAssignment? Find(Guid army) => _assignments.GetValueOrDefault(army);
    public AIControllerAssignment? ForActor(Guid actor) => _assignments.Values.FirstOrDefault(a => a.ActorId == actor);
    public AIControllerAssignment[] Snapshot() => _assignments.Values.OrderBy(a => a.ArmyId).ToArray();
    internal AIControllerAssignment Assign(Guid army, Guid actor, Guid? peer, AIStrategyProfile profile)
    {
        if (army == Guid.Empty || actor == Guid.Empty || peer == Guid.Empty) throw new ArgumentException("Empty controller identity.");
        if (profile is null || !Enum.IsDefined(profile.Type) || !float.IsFinite(profile.RetreatHealthFraction) || !float.IsFinite(profile.DecisionIntervalSeconds)) throw new ArgumentException("Invalid controller profile.");
        var old = Find(army);
        if (_assignments.Values.Any(a => a.ArmyId != army && a.ActorId == actor)) throw new ArgumentException("Actor already belongs to another army.");
        var assignment = new AIControllerAssignment(army, actor, peer, checked((old?.Generation ?? 0) + 1), profile);
        _assignments[army] = assignment;
        return assignment;
    }
    internal void Apply(AIControllerAssignment assignment)
    {
        if (assignment.ArmyId == Guid.Empty || assignment.ActorId == Guid.Empty || assignment.Generation <= 0 || assignment.Profile is null || assignment.ProfileVersion != 1 || assignment.ProfileFingerprint != AIControllerAssignment.Fingerprint(assignment.Profile))
            throw new ArgumentException("Invalid controller assignment.");
        if (Find(assignment.ArmyId) is { } old && old.Generation >= assignment.Generation) return;
        _assignments[assignment.ArmyId] = assignment;
    }
    internal void Restore(IEnumerable<AIControllerAssignment> assignments)
    {
        var copy = assignments.ToArray();
        if (copy.Select(a => a.ArmyId).Distinct().Count() != copy.Length || copy.Select(a => a.ActorId).Distinct().Count() != copy.Length)
            throw new ArgumentException("Duplicate controller assignments.");
        var validated = new AIControllerAssignments();
        foreach (var assignment in copy) validated.Apply(assignment);
        _assignments.Clear();
        foreach (var assignment in validated.Snapshot()) _assignments[assignment.ArmyId] = assignment;
    }
    internal void Clear() => _assignments.Clear();
    public bool Authorizes(NetworkMessage request)
    {
        if (request.AIControllerArmyId is not Guid army || request.AIControllerActorId is not Guid actor ||
            request.ControllerPeerId is not Guid peer || Find(army) is not { } assignment) return false;
        return assignment.ActorId == actor && assignment.ControllerPeerId == peer && assignment.Generation == request.AIControllerGeneration;
    }
}

public sealed partial class NetworkHandler
{
    public AIControllerAssignments AIControllers { get; } = new();
    public bool CanRunAI(Guid army) => AIControllers.Find(army) is { } assignment
        ? assignment.ControllerPeerId == LocalPeerId && (IsHost || Status == NetworkConnectionStatus.Connected)
        : IsHost;
    public AIControllerAssignment AssignAIController(Guid army, Guid actor, Guid? peer, AIStrategyProfile profile)
    {
        AssertGameThread();
        if (!IsHost) throw new InvalidOperationException("Only host can assign AI controllers.");
        if (peer.HasValue && peer != LocalPeerId && !_members.ContainsKey(peer.Value)) throw new ArgumentException("Controller peer is not connected.");
        if (peer is Guid remote && remote != LocalPeerId && _members[remote].BotOffer is { } offer &&
            AIControllers.Snapshot().Count(a => a.ControllerPeerId == remote && a.ArmyId != army) >= offer.MaximumArmies)
            throw new ArgumentException("Bot controller capacity exhausted.");
        var assignment = AIControllers.Assign(army, actor, peer, profile);
        StartAIControllerLease(assignment);
        AbandonSupersededAIRequests(assignment);
        AIControllerAssignmentChanged?.Invoke(assignment);
        var message = new NetworkMessage(NetworkMessageType.AIControllerAssignmentCommand, LocalPeerId) { AIControllerAssignment = assignment };
        _ = BroadcastAsync(message);
        return assignment;
    }
}

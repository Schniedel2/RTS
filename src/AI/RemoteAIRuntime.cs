using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using RTS.Network;
namespace RTS;

/// <summary>Local decision runtimes; gameplay remains in the host simulation.</summary>
public sealed class RemoteAIRuntime : IDisposable
{
    private sealed class Entry(long generation, long session, AIPlayer player, GameWorld world)
    {
        public long Generation = generation, Session = session, Sequence;
        public AIPlayer Player = player;
        public GameWorld World = world;
        public double NextHeartbeat;
    }
    private readonly int _maximumArmies;
    public RemoteAIRuntime(int maximumArmies = 32) { if (maximumArmies < 1 || maximumArmies > 32) throw new ArgumentOutOfRangeException(nameof(maximumArmies)); _maximumArmies = maximumArmies; }
    private readonly Dictionary<Guid, Entry> _controllers = [];
    public IReadOnlyCollection<AIPlayer> Players => _controllers.Values.Select(e => e.Player).ToArray();
    public void Update(GameTime time, GameWorld world, NetworkHandler network)
    {
        var assigned = network.AIControllers.Snapshot().Where(a => a.ControllerPeerId == network.LocalPeerId).Take(_maximumArmies).ToArray();
        foreach (Guid army in _controllers.Keys.ToArray())
            if (network.IsHost || !network.CanRunAI(army) || !assigned.Any(a => a.ArmyId == army && a.Generation == _controllers[army].Generation) ||
                _controllers[army].Session != network.SessionGeneration || _controllers[army].World != world ||
                world.SimulationArmies.Find(army) is null) Remove(army);
        if (network.IsHost || network.Status != NetworkConnectionStatus.Connected) return;
        foreach (var assignment in assigned)
        {
            if (world.SimulationArmies.Find(assignment.ArmyId) is null) continue;
            if (!_controllers.TryGetValue(assignment.ArmyId, out var entry))
            {
                var actor = new Player(assignment.ActorId, "Remote AI " + assignment.ActorId.ToString("N")[..8]);
                actor.SetArmy(assignment.ArmyId);
                var ai = new AIPlayer(actor);
                ai.Controller.BeginMatch(0, assignment.ArmyId, assignment.Profile);
                ai.SetStatus(AIPlayerStatus.Active);
                entry = new(assignment.Generation, network.SessionGeneration, ai, world);
                _controllers.Add(assignment.ArmyId, entry);
            }
            entry.Player.Update(time, world, network);
            entry.Sequence++;
            if (network.AIHeartbeatTime >= entry.NextHeartbeat)
            {
                _ = network.SendAIHeartbeatAsync(assignment, entry.Sequence);
                entry.NextHeartbeat = network.AIHeartbeatTime + NetworkHandler.AIHeartbeatIntervalSeconds;
            }
        }
        world.ScoutingTargets.UpdateClientPlanning(time.TotalGameTime.TotalSeconds);
    }
    private void Remove(Guid army)
    {
        if (!_controllers.Remove(army, out var entry)) return;
        entry.World.RemoteBuildSites.Clear(army);
        // BeginMatch releases all local jobs/claims and never changes gameplay.
        entry.Player.Controller.BeginMatch(0, army, entry.Player.Controller.StrategyProfile);
        entry.Player.SetStatus(AIPlayerStatus.Idle);
    }
    public void Dispose() { foreach (Guid army in _controllers.Keys.ToArray()) Remove(army); }
}

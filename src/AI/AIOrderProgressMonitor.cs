using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

/// <summary>Army-wide safety net for confirmed jobs, including the legacy economy controllers.</summary>
public sealed class AIOrderProgressMonitor : IDisposable
{
    public const float MovementTimeoutSeconds = 15;
    public const float ConstructionTimeoutSeconds = 20;
    public const float ProductionTimeoutSeconds = 60;
    public const float FailureCooldownSeconds = 60;
    private sealed class Observation
    {
        public AIProgressWatch Watch = new();
        public AIProgressWatch Approach = new();
        public int Recoveries;
        public string? Objective;
    }
    private readonly GameWorld _world;
    private readonly Player _actor;
    private readonly NetworkHandler _network;
    private readonly PlayerCommandService _commands;
    private readonly Dictionary<Guid, Observation> _jobs = [];
    private readonly Dictionary<Point, float> _sites = [];
    private readonly Dictionary<Guid, float> _producers = [];
    private readonly Dictionary<Guid, (string Objective, float Until)> _movementFailures = [];
    private readonly Dictionary<(Guid Unit, Point Cell), float> _failedHarvestTargets = [];
    private long _generation;
    private float _time;
    private float _elapsed;
    private LocalRequestReceipt? _lastRejection;
    public string? LastDecision { get; private set; }

    public AIOrderProgressMonitor(GameWorld world, Player actor, NetworkHandler network)
    {
        _world = world; _actor = actor; _network = network;
        _commands = new(network, actor.Id);
        _generation = network.SessionGeneration;
        world.AIOrderMonitors[actor.ArmyId] = this;
        network.SetLocalAIRequestPolicy(actor.Id, AllowRequest);
    }

    public bool AvoidSite(Point cell) => _sites.Any(pair => pair.Value > _time &&
        Math.Abs(pair.Key.X - cell.X) <= 3 && Math.Abs(pair.Key.Y - cell.Y) <= 3);
    public bool AvoidProducer(Guid id) => _producers.GetValueOrDefault(id) > _time;
    public bool AvoidHarvestTarget(Guid id, Vector3 target)
    {
        Point cell = _world.GameGrid.ToCell(target);
        return _failedHarvestTargets.Any(pair => pair.Key.Unit == id && pair.Value > _time &&
            Math.Abs(pair.Key.Cell.X - cell.X) <= 2 && Math.Abs(pair.Key.Cell.Y - cell.Y) <= 2);
    }

    private bool AllowRequest(NetworkMessage request)
    {
        if (request.Type is NetworkMessageType.TrainUnitRequest or NetworkMessageType.ResearchRequest &&
            request.UnitId is Guid producer && AvoidProducer(producer)) return false;
        if (request.Type == NetworkMessageType.HarvestRequest && request.UnitId is Guid harvesterId &&
            AvoidHarvestTarget(harvesterId, new(request.X, 0, request.Z))) return false;
        foreach (Guid id in request.UnitIds ?? (request.UnitId is Guid unit ? [unit] : []))
            if (_movementFailures.TryGetValue(id, out var failure) && failure.Until > _time &&
                failure.Objective == Objective(request)) return false;
        return true;
    }

    private static string Objective(NetworkMessage request) => request.Type switch
    {
        NetworkMessageType.GotoRequest => $"goto:{request.X:R}:{request.Z:R}",
        NetworkMessageType.AttackTargetRequest => $"attack:{request.TargetId}",
        NetworkMessageType.EnterUnitRequest => $"enter:{request.TargetId}",
        NetworkMessageType.FollowRequest => $"follow:{request.TargetId}",
        _ => request.Type.ToString()
    };

    public void Update(GameTime gameTime)
    {
        using var measurement = PerformanceMeasurements.Measure("AI.OrderProgress");
        if (!_network.IsHost) return;
        if (_generation != _network.SessionGeneration)
        {
            Reset(); _generation = _network.SessionGeneration;
        }
        float dt = Math.Max(0, (float)gameTime.ElapsedGameTime.TotalSeconds);
        _time += dt; _elapsed += dt;
        if (_elapsed < 1) return;
        dt = _elapsed; _elapsed = 0;
        LastDecision = null;
        LocalRequestReceipt? rejection = _network.GetLastLocalRejection(_actor.Id);
        if (rejection is not null && !ReferenceEquals(rejection, _lastRejection))
        {
            _lastRejection = rejection;
            bool localCooldown = rejection.Reason?.StartsWith("AI recovery", StringComparison.Ordinal) == true;
            LastDecision = $"{(localCooldown ? "AI skipped" : "Host rejected")} {rejection.Request.Type}: {rejection.Reason}";
            if (!localCooldown && rejection.Request.Type == NetworkMessageType.BuildRequest &&
                rejection.Result.Failure is AIOrderFailure.BuildSite or AIOrderFailure.InvalidTarget or AIOrderFailure.Validation)
                _sites[_world.GameGrid.ToCell(new(rejection.Request.X, 0, rejection.Request.Z))] =
                    _time + FailureCooldownSeconds;
            if (rejection.Request.Type == NetworkMessageType.GotoRequest &&
                rejection.Reason?.StartsWith("No reachable route", StringComparison.Ordinal) == true)
                foreach (Guid id in rejection.Request.UnitIds ?? [])
                    _movementFailures[id] = (Objective(rejection.Request), _time + FailureCooldownSeconds);
        }
        HashSet<Guid> active = [];
        foreach (Unit unit in _world.Units.GetArmyUnits(_actor.ArmyId).ToArray())
        {
            if (unit.IsDying || unit.IsEmbarked) continue;
            if (unit is Building building)
            {
                if (!building.IsCompleted) CheckConstruction(building, dt, active);
                else if (building.ProductionQueue.ActiveOrder is ProductionOrder order)
                    CheckProduction(building, order, dt, active);
            }
            else if (unit is MobileUnit mobile && !mobile.IsLeavingBuilding && !mobile.IsBuilding &&
                mobile.TargetBuildingId is null) CheckMovement(mobile, dt, active);
        }
        foreach (Guid id in _jobs.Keys.Where(id => !active.Contains(id)).ToArray()) _jobs.Remove(id);
        foreach (Point cell in _sites.Keys.Where(cell => _sites[cell] <= _time).ToArray()) _sites.Remove(cell);
        foreach (Guid id in _producers.Keys.Where(id => _producers[id] <= _time).ToArray()) _producers.Remove(id);
        foreach (Guid id in _movementFailures.Keys.Where(id => _movementFailures[id].Until <= _time).ToArray())
            _movementFailures.Remove(id);
        foreach (var key in _failedHarvestTargets.Keys.Where(key => _failedHarvestTargets[key] <= _time).ToArray())
            _failedHarvestTargets.Remove(key);
    }

    private Observation Observe(Guid id, string objective, HashSet<Guid> active)
    {
        active.Add(id);
        if (!_jobs.TryGetValue(id, out Observation? observation)) _jobs[id] = observation = new();
        if (observation.Objective != objective)
        {
            observation.Objective = objective;
            observation.Recoveries = 0;
            observation.Watch.Reset();
            observation.Approach.Reset();
        }
        return observation;
    }

    private void CheckConstruction(Building site, float dt, HashSet<Guid> active)
    {
        if (_world.AIOrderQueues.TryGetValue(_actor.ArmyId, out var queue) && queue.IsPaused(site.UnitId))
            return; // Intentional scheduler suspension is not a stalled construction job.
        Observation observation = Observe(site.UnitId, "construction", active);
        MobileUnit? worker = _world.Units.GetArmyUnits(_actor.ArmyId).OfType<MobileUnit>()
            .FirstOrDefault(unit => !unit.IsDying && unit.TargetBuildingId == site.UnitId) ??
            AIStrategicCatalog.FindBuilder(_world, _actor.ArmyId, site.GameplayTypeId);
        // Approaching the site is progress, but driving in circles is not.
        if (worker is not null && !worker.IsBuilding)
        {
            observation.Approach.Update(worker.UnitId.ToString(),
                -MathF.Sqrt(DistanceSquared(worker.Position, site.Position)), dt, ConstructionTimeoutSeconds, 0.25f);
            if (observation.Approach.SecondsWithoutProgress == 0) observation.Watch.Reset();
        }
        if (observation.Watch.Update("construction", site.ConstructionProgress, dt, ConstructionTimeoutSeconds, 0.1f))
        {
            if (observation.Watch.MadeProgress) observation.Recoveries = 0;
            return;
        }
        observation.Watch.RestartDeadline();
        if (worker is not null && observation.Recoveries++ < 2)
        {
            _ = _commands.ConstructAsync([worker.UnitId], site.UnitId);
            LastDecision = $"Replanning stalled construction of {site.GameplayTypeId}.";
        }
        else
        {
            _sites[_world.GameGrid.ToCell(site.Position)] = _time + FailureCooldownSeconds;
            _ = _commands.CancelConstructionAsync(site.UnitId);
            LastDecision = $"Cancelling unreachable {site.GameplayTypeId}; its site is avoided for 60 seconds.";
        }
    }

    private void CheckProduction(Building producer, ProductionOrder order, float dt, HashSet<Guid> active)
    {
        Observation observation = Observe(producer.UnitId, order.OrderId.ToString(), active);
        if (observation.Watch.Update(order.OrderId.ToString(), order.ElapsedSeconds, dt, ProductionTimeoutSeconds))
        {
            if (observation.Watch.SecondsWithoutProgress == 0)
            { _producers.Remove(producer.UnitId); observation.Recoveries = 0; }
            return;
        }
        _producers[producer.UnitId] = _time + FailureCooldownSeconds;
        LastDecision = $"Production of {order.UnitTypeId} is stalled; preserving the paid queue and trying other producers.";
        if (!producer.IsEnabled && observation.Recoveries++ == 0)
            _ = _commands.ExecuteActionAsync([producer.UnitId], UnitActionType.ToggleEnabled);
    }

    private void CheckMovement(MobileUnit unit, float dt, HashSet<Guid> active)
    {
        Vector3? target = null;
        string? objective = null;
        float extraProgress = 0;
        if (unit.AttackTargetId is Guid attackId && _world.Units.FindById(attackId) is Unit enemy && !enemy.IsDying)
        { target = enemy.Position; objective = $"attack:{attackId}"; extraProgress = -enemy.HitPoints; }
        else if (unit.PendingEnterContainerId is Guid entryId && _world.Units.FindById(entryId) is Unit container)
        { target = container.Position; objective = $"enter:{entryId}"; }
        else if (unit.FollowUnitId is Guid followId && _world.Units.FindById(followId) is Unit leader)
        {
            if (DistanceSquared(unit.Position, leader.Position) <= MathF.Pow(unit.FollowDistance + 1, 2)) return;
            target = leader.Position; objective = $"follow:{followId}";
        }
        else if (unit.CurrentCommand is GotoCommand command)
        { target = new(command.Target.X, 0, command.Target.Y); objective = $"goto:{command.Target.X:R}:{command.Target.Y:R}"; }
        if (target is not Vector3 destination || objective is null) return;
        if (unit.AttackTargetId is null && DistanceSquared(unit.Position, destination) < 0.25f) return;
        Observation observation = Observe(unit.UnitId, objective, active);
        if (observation.Watch.Update(objective, -MathF.Sqrt(DistanceSquared(unit.Position, destination)) + extraProgress,
                dt, MovementTimeoutSeconds, 0.25f))
        {
            if (observation.Watch.MadeProgress) observation.Recoveries = 0;
            return;
        }
        observation.Watch.RestartDeadline();
        if (observation.Recoveries++ == 0)
        {
            if (unit is Harvester { HarvestPhase: HarvestPhase.ReturningToSilo, CargoAmount: > 0 } returning)
                _ = _commands.ReturnHarvesterAsync(returning.UnitId);
            else if (unit is Harvester { HarvestPhase: HarvestPhase.DrivingToField } harvesting)
                _ = _commands.HarvestAsync(harvesting.UnitId, destination);
            else if (unit.PendingEnterContainerId is Guid containerId)
                _ = _commands.EnterUnitAsync(unit.UnitId, containerId);
            else if (unit.FollowUnitId is Guid followId)
                _ = _commands.FollowAsync([unit.UnitId], followId);
            else
            {
                _ = _commands.GotoAsync([unit.UnitId], destination);
                if (unit.AttackTargetId is Guid targetId)
                    _ = _commands.AttackTargetAsync([unit.UnitId], targetId);
            }
            LastDecision = $"Replanning stalled unit {unit.UnitId.ToString("N")[..8]}.";
        }
        else
        {
            _movementFailures[unit.UnitId] = (objective, _time + FailureCooldownSeconds);
            if (unit is Harvester)
                _failedHarvestTargets[(unit.UnitId, _world.GameGrid.ToCell(destination))] = _time + FailureCooldownSeconds;
            _ = _commands.StopAsync([unit.UnitId]);
            LastDecision = $"Aborted unreachable {objective}; retry cooldown is 60 seconds.";
        }
    }

    private static float DistanceSquared(Vector3 a, Vector3 b) =>
        Vector2.DistanceSquared(new(a.X, a.Z), new(b.X, b.Z));
    public void Reset()
    {
        _jobs.Clear(); _sites.Clear(); _producers.Clear(); _movementFailures.Clear();
        _failedHarvestTargets.Clear();
        _time = _elapsed = 0; _lastRejection = null; LastDecision = null;
    }
    public void Dispose()
    {
        if (_world.AIOrderMonitors.GetValueOrDefault(_actor.ArmyId) == this)
        {
            _world.AIOrderMonitors.Remove(_actor.ArmyId);
            _network.SetLocalAIRequestPolicy(_actor.Id, null);
        }
    }
}

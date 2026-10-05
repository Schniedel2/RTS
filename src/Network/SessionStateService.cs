using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace RTS.Network;

/// <summary>Game-thread capture/reconstruction of replicated state, never host jobs or searches.</summary>
public sealed class SessionStateService
{
    private readonly GameWorld _world;
    private readonly ArmyHandler _armies;
    private readonly Func<string, Vector3, Guid, Guid, int, Unit?> _create;
    public bool IsMatchStarted { get; private set; }

    public SessionStateService(GameWorld world, ArmyHandler armies,
        Func<string, Vector3, Guid, Guid, int, Unit?>? create = null)
    {
        _world = world;
        _armies = armies;
        _create = create ?? ((type, position, id, owner, price) => BuildingFactory.CanCreate(type)
            ? BuildingFactory.SpawnBuilding(type, position, 0, id, owner, price)
            : UnitFactory.SpawnUnit(type, position, 0, id, owner));
    }

    public SessionSnapshot Capture(double hostTime, bool isMatchStarted)
    {
        RuntimeUnitSnapshot[] units = _world.Units.GetSnapshot()
            .Where(unit => unit is not TiberiumSource && !unit.IsDying && !string.IsNullOrWhiteSpace(TypeId(unit)))
            .Select(unit => new RuntimeUnitSnapshot(TypeId(unit), unit.UnitId, unit.CreatorPlayerId, unit.ArmyId,
                unit.Position.X, unit.Position.Y, unit.Position.Z, Yaw(unit.Transform), unit.HitPoints,
                unit.Behavior, unit is Building building ? building.PurchasePrice : 0, unit.GetState(),
                unit.Occupancy?.Occupants.Select(item => new OccupantSnapshot(item.UnitId, item.Role)).ToArray() ?? [],
                unit is Harvester harvester ? harvester.HarvestPhase : null,
                unit is Harvester cargo ? cargo.CargoAmount : 0,
                unit is MobileUnit { IsLeavingBuilding: true, SpawnSourceBuildingId: Guid source } mobile
                    ? new BuildingExitSnapshot(source, mobile.SpawnExitPosition.X, mobile.SpawnExitPosition.Y,
                        mobile.SpawnExitPosition.Z) : null,
                unit is GDIBulldozer worker ? worker.EarthworkOrder : null,
                unit is GDIBulldozer sequence ? sequence.EarthworkSequence : 0,
                unit is Soldier soldier ? soldier.EquippedWeapon : null)).ToArray();
        return new(_world.GetWorldData(), _armies.GetSnapshot(), units, _world.Visibility.GetSnapshot(),
            hostTime, isMatchStarted);
    }

    public void Apply(SessionSnapshot snapshot)
    {
        IsMatchStarted = snapshot.IsMatchStarted;
        _world.PathfindingManager.Reset();
        _world.Units.ClearForNetworkSnapshot();
        _armies.ApplySnapshot(snapshot.Armies);
        _world.ApplyWorldData(snapshot.World);
        // Create every identity without placement/spawn side effects first. Occupants
        // and units leaving factories legitimately overlap a building footprint.
        foreach (RuntimeUnitSnapshot state in snapshot.Units)
        {
            Unit? unit = _create(state.TypeId, new(state.X, state.Y, state.Z), state.UnitId,
                state.CreatorPlayerId, state.PurchasePrice);
            if (unit is null) throw new InvalidOperationException($"Cannot restore snapshot type '{state.TypeId}'.");
            unit.SetCreatorPlayer(state.CreatorPlayerId);
            unit.SetArmy(state.ArmyId);
            unit.SetRotationYDegrees(state.RotationDegrees);
            _world.Units.Register(unit);
            if (unit is MobileUnit mobile && state.BuildingExit is BuildingExitSnapshot exit)
                mobile.BeginLeavingBuilding(exit.SourceBuildingId, new(exit.X, exit.Y, exit.Z));
            unit.HitPoints = Math.Clamp(state.HitPoints, 0, unit.MaxHitPoints);
            unit.Behavior = state.Behavior;
            if (unit is GDIBulldozer worker && state.EarthworkOrder is EarthworkOrder order)
                worker.RestoreEarthwork(order, state.EarthworkSequence);
            if (unit is Soldier soldier && state.SoldierWeapon is Soldier.Weapon weapon) soldier.SetWeapon(weapon);
            unit.ApplyState(state.State);
            if (unit is Harvester harvester && state.HarvestPhase is HarvestPhase phase)
                harvester.ApplyHarvestState(phase, state.CargoAmount);
        }
        foreach (RuntimeUnitSnapshot container in snapshot.Units)
            foreach (OccupantSnapshot occupant in container.Occupants)
                if (!_world.Units.EmbarkUnit(occupant.UnitId, container.UnitId, occupant.Role))
                    throw new InvalidOperationException($"Cannot restore occupant '{occupant.UnitId}'.");
        // Rebuild exact host occupancy. Never use random spawn recovery or create routes.
        foreach (Unit unit in _world.Units.GetSnapshot().OrderBy(unit => unit is Building ? 0 : 1))
        {
            if (unit.IsEmbarked || unit is MobileUnit { IsLeavingBuilding: true } || unit is Helicopter)
                continue;
            bool placed = unit is MobileUnit mobile
                ? _world.GameGrid.TryMove(mobile, _world.GameGrid.ToCell(mobile.Position))
                : _world.GameGrid.TryPlace(unit, unit.Position, Yaw(unit.Transform));
            if (!placed) throw new InvalidOperationException($"Cannot restore footprint '{unit.UnitId}'.");
        }
        // Embarking must not override the authoritative container behavior.
        foreach (RuntimeUnitSnapshot state in snapshot.Units)
            _world.Units.FindById(state.UnitId)!.Behavior = state.Behavior;
        _world.Visibility.ApplySnapshot(snapshot.Visibility);
        _world.ClearTransientEffects();
    }

    public Vector3? ApplyMatchStart(NetworkMessage command, Guid localPlayerId)
    {
        IsMatchStarted = true;
        _world.PathfindingManager.Reset();
        _world.Units.ClearForMatchStart();
        _armies.ClearPerks();
        _world.Visibility.Reset();
        _world.ClearTransientEffects();
        Vector3? localPosition = null;
        foreach (MatchStartAssignment assignment in command.MatchStartAssignments ?? [])
        {
            if (_armies.Find(assignment.ArmyId) is Army army) army.Resources = command.ResourceAmount;
            Vector3 position = new(assignment.X, assignment.Y, assignment.Z);
            Unit unit = _create("gdi-bulldozer", position, assignment.BulldozerId, assignment.PlayerId, 0)
                ?? throw new InvalidOperationException("Cannot create match-start bulldozer.");
            unit.SetCreatorPlayer(assignment.PlayerId);
            unit.SetArmy(assignment.ArmyId);
            unit.SetRotationYDegrees(assignment.RotationDegrees);
            _world.Units.Register(unit);
            if (assignment.PlayerId == localPlayerId) localPosition = position;
            if (unit is not MobileUnit mobile || !_world.GameGrid.TryMove(mobile, _world.GameGrid.ToCell(position)))
            {
                _world.Units.Unregister(unit);
                continue;
            }
            if (assignment.DriverUnitId is Guid driverId)
            {
                Unit driver = _create("soldier", position, driverId, assignment.PlayerId, 0)
                    ?? throw new InvalidOperationException("Cannot create match-start driver.");
                driver.SetCreatorPlayer(assignment.PlayerId);
                driver.SetArmy(assignment.ArmyId);
                if (driver is Soldier soldier) soldier.SetWeapon(Soldier.Weapon.Brok17);
                _world.Units.Register(driver);
                UnitBehavior behavior = unit.Behavior;
                if (!_world.Units.EmbarkUnit(driverId, unit.UnitId, OccupantRole.Driver))
                    _world.Units.Unregister(driver);
                unit.Behavior = behavior;
            }
            if (assignment.PlayerId == localPlayerId) localPosition = position;
        }
        return localPosition;
    }

    private static string TypeId(Unit unit) => unit switch
    {
        GenericBuilding => "building-1", TerrainEditorTool => "editor",
        Soldier when string.IsNullOrWhiteSpace(unit.GameplayTypeId) => "soldier",
        Car when string.IsNullOrWhiteSpace(unit.GameplayTypeId) => "car", _ => unit.GameplayTypeId
    };
    private static float Yaw(Matrix transform) => MathHelper.ToDegrees(MathF.Atan2(-transform.Forward.X, -transform.Forward.Z));
}

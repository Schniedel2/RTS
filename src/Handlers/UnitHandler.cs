using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RTS;

public class UnitHandler
{
    private static readonly IReadOnlyList<Unit> EmptyUnits = Array.AsReadOnly(Array.Empty<Unit>());
    private readonly UnitCollection _units;
    private readonly object _unitsSync = new();
    private readonly Dictionary<Guid, Guid> _perkSourceArmies = [];
    private readonly Dictionary<Guid, Unit> _byId = [];
    private readonly Dictionary<Guid, HashSet<Unit>> _byArmy = [];
    private readonly Dictionary<Guid, IReadOnlyList<Unit>> _armySnapshots = [];
    private readonly HashSet<Unit> _registered = [];
    private IReadOnlyList<Unit>? _snapshot;
    private long _membershipRevision;

    /// <summary>Stable, read-only membership snapshot. Unit objects retain their live state.</summary>
    // Membership operations are internal; gameplay spawn/removal still validate placement and lifecycle.
    internal void Register(Unit unit) => _units.Add(unit);
    internal bool Unregister(Unit unit) => _units.Remove(unit);
    internal void ClearMembership() => _units.Clear();

    public IReadOnlyList<Unit> Units => GetSnapshot();
    public long MembershipRevision { get { lock (_unitsSync) return _membershipRevision; } }
    public int Count { get { lock (_unitsSync) return _units.Count; } }

    private readonly GameWorld? _world;
    private GameWorld World => _world ?? Globals.World;
    private ArmyHandler Armies => _world?.SimulationArmies ?? Globals.Game.Armies;
    public UnitHandler(GameWorld? world = null)
    {
        _world = world;
        _units = new UnitCollection(this);
    }

    public IReadOnlyList<Unit> GetSnapshot()
    {
        lock (_unitsSync)
            return _snapshot ??= Array.AsReadOnly(_units.ToArray());
    }

    /// <summary>Includes embarked units and dying wrecks, in the same order as the world snapshot.</summary>
    public IReadOnlyList<Unit> GetArmyUnits(Guid armyId)
    {
        lock (_unitsSync)
        {
            if (_armySnapshots.TryGetValue(armyId, out var cached)) return cached;
            if (!_byArmy.TryGetValue(armyId, out var members)) return EmptyUnits;
            IReadOnlyList<Unit> result = Array.AsReadOnly(_units.Where(members.Contains).ToArray());
            _armySnapshots.Add(armyId, result);
            return result;
        }
    }

    private void ValidateRegistration(Unit unit, Unit? replacing = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_registered.Contains(unit) && unit != replacing ||
            unit.UnitId != Guid.Empty && _byId.TryGetValue(unit.UnitId, out Unit? existing) && existing != replacing)
            throw new InvalidOperationException($"Unit '{unit.UnitId}' is already registered.");
        if (_world is not null) unit.BindWorld(_world);
    }

    private void Attach(Unit unit)
    {
        _registered.Add(unit);
        if (unit.UnitId != Guid.Empty) _byId.Add(unit.UnitId, unit);
        AddArmyMember(unit, unit.ArmyId);
        unit.ArmyChanged += OnArmyChanged;
        InvalidateMembership(unit.ArmyId);
    }

    private void Detach(Unit unit)
    {
        unit.ArmyChanged -= OnArmyChanged;
        _registered.Remove(unit);
        if (unit.UnitId != Guid.Empty) _byId.Remove(unit.UnitId);
        RemoveArmyMember(unit, unit.ArmyId);
        InvalidateMembership(unit.ArmyId);
    }

    private void AddArmyMember(Unit unit, Guid? armyId)
    {
        if (armyId is not Guid id) return;
        if (!_byArmy.TryGetValue(id, out var members)) _byArmy.Add(id, members = []);
        members.Add(unit);
    }

    private void RemoveArmyMember(Unit unit, Guid? armyId)
    {
        if (armyId is Guid id && _byArmy.TryGetValue(id, out var members))
        {
            members.Remove(unit);
            if (members.Count == 0) _byArmy.Remove(id);
        }
    }

    private void OnArmyChanged(Unit unit, Guid? previousArmy, Guid? currentArmy)
    {
        lock (_unitsSync)
        {
            if (!_registered.Contains(unit)) return;
            RemoveArmyMember(unit, previousArmy);
            AddArmyMember(unit, currentArmy);
            if (previousArmy is Guid oldId) _armySnapshots.Remove(oldId);
            if (currentArmy is Guid newId) _armySnapshots.Remove(newId);
            _membershipRevision++;
        }
    }

    private void InvalidateMembership(Guid? armyId = null, bool clearAll = false)
    {
        _snapshot = null;
        if (clearAll) _armySnapshots.Clear();
        else if (armyId is Guid id) _armySnapshots.Remove(id);
        _membershipRevision++;
    }

    // All insertion/removal paths (including atomic snapshot replacement) maintain indices here.
    // The collection stays private; callers receive immutable copies, never a live collection.
    private sealed class UnitCollection(UnitHandler owner) : Collection<Unit>
    {
        protected override void InsertItem(int index, Unit item)
        {
            lock (owner._unitsSync)
            {
                owner.ValidateRegistration(item);
                base.InsertItem(index, item);
                owner.Attach(item);
            }
        }
        protected override void SetItem(int index, Unit item)
        {
            lock (owner._unitsSync)
            {
                Unit previous = this[index];
                if (previous == item) return;
                owner.ValidateRegistration(item, previous);
                owner.Detach(previous);
                base.SetItem(index, item);
                owner.Attach(item);
            }
        }
        protected override void RemoveItem(int index)
        {
            lock (owner._unitsSync)
            {
                Unit previous = this[index];
                base.RemoveItem(index);
                owner.Detach(previous);
            }
        }
        protected override void ClearItems()
        {
            lock (owner._unitsSync)
            {
                foreach (Unit unit in this) unit.ArmyChanged -= owner.OnArmyChanged;
                base.ClearItems();
                owner._registered.Clear(); owner._byId.Clear(); owner._byArmy.Clear();
                owner.InvalidateMembership(clearAll: true);
            }
        }
    }

    public Unit? SpawnUnit(
        string unitTypeName,
        Vector3 position,
        float RotateYDegrees,
        Guid unitId,
        Guid creatorPlayerId,
        Guid? driverUnitId = null)
    {
        if (FindById(unitId) is not null) return null;
        Unit? unit = UnitFactory.SpawnUnit(unitTypeName, position, RotateYDegrees, unitId, creatorPlayerId);
        if (unit is null)
            return SpawnBuilding(unitTypeName, position, RotateYDegrees, unitId, creatorPlayerId);

        if (unit is Building building && !building.EvaluatePlacement(World, position, RotateYDegrees).IsAllowed)
            return null;
        AssignCurrentArmy(unit, creatorPlayerId);
        if (SetFootprints(unit, RotateYDegrees))
        {
            lock (_unitsSync) _units.Add(unit);
            RefreshPerkSource(unit);
            if (unit.Occupancy?.Slots.Any(slot => slot.Role == OccupantRole.Driver) == true &&
                creatorPlayerId != Guid.Empty &&
                driverUnitId is Guid initialDriverId)
            {
                AddInitialDriver(unit, creatorPlayerId, initialDriverId);
            }
            return unit;
        }
        return null;
    }

    /// <summary>
    /// Creates a produced mobile unit inside a building without replacing the
    /// building's occupied GameGrid cells. The unit registers itself when it
    /// reaches the supplied exterior exit position.
    /// </summary>
    public MobileUnit? SpawnUnitFromBuilding(
        string unitTypeName,
        Vector3 spawnPosition,
        Vector3 exitPosition,
        float rotateYDegrees,
        Guid unitId,
        Guid creatorPlayerId,
        Guid? armyId,
        Guid sourceBuildingId,
        Guid? driverUnitId = null)
    {
        if (FindById(unitId) is not null) return null;
        MobileUnit? unit = UnitFactory.SpawnUnit(
            unitTypeName,
            spawnPosition,
            rotateYDegrees,
            unitId,
            creatorPlayerId);
        if (unit is null)
            return null;

        unit.SetArmy(armyId);
        if (unit is Helicopter helicopter && FindById(sourceBuildingId) is Helipad pad)
        {
            helicopter.InitializeDelivery(World, pad, spawnPosition);
            pad.DeliveryPending = false;
        }
        else
            unit.BeginLeavingBuilding(sourceBuildingId, exitPosition);
        lock (_unitsSync) _units.Add(unit);
        RefreshPerkSource(unit);
        if (unit.Occupancy?.Slots.Any(slot => slot.Role == OccupantRole.Driver) == true &&
            creatorPlayerId != Guid.Empty &&
            driverUnitId is Guid initialDriverId)
        {
            AddInitialDriver(unit, creatorPlayerId, initialDriverId);
        }
        return unit;
    }

    public Building? SpawnBuilding(
        string buildingTypeName,
        Vector3 position,
        float RotateYDegrees,
        Guid unitId,
        Guid creatorPlayerId,
        int? purchasePrice = null, Guid? armyId = null)
    {
        if (FindById(unitId) is not null) return null;
        Building? unit = BuildingFactory.SpawnBuilding(buildingTypeName, position, RotateYDegrees,
            unitId, creatorPlayerId, purchasePrice);
        if (unit is null)
            return null;

        if (armyId is Guid assignedArmy) unit.SetArmy(assignedArmy);
        else AssignCurrentArmy(unit, creatorPlayerId);
        if (!unit.EvaluatePlacement(World, position, RotateYDegrees).IsAllowed)
            return null;
        if (SetFootprints(unit, RotateYDegrees))
        {
            lock (_unitsSync) _units.Add(unit);
            RefreshPerkSource(unit);
            return unit;
        }
        return null;
    }

    public Unit? FindById(Guid unitId)
    {
        lock (_unitsSync)
            return unitId == Guid.Empty ? _units.FirstOrDefault(unit => unit.UnitId == Guid.Empty) :
                _byId.GetValueOrDefault(unitId);
    }

    public void RemoveMapObjects<T>() where T : Unit
    {
        foreach (T unit in Units.OfType<T>())
            RemoveImmediately(unit);
    }

    public void RemoveMapObject(Unit unit)
    {
        if (Units.Contains(unit)) RemoveImmediately(unit);
    }

    public MobileUnit? FindMobileUnitById(Guid unitId)
    {
        return FindById(unitId) as MobileUnit;
    }

    public void Update(GameTime gameTime)
    {
        foreach (Unit unit in Units)
        {
            if (unit.IsEmbarked &&
                unit.ContainerUnitId is Guid containerId &&
                FindById(containerId) is Unit container)
            {
                unit.SetPosition(container.Position);
            }
            else if (!unit.IsEmbarked)
                unit.Update(gameTime);
            RefreshPerkSource(unit);
        }

        foreach (Unit unit in Units)
            if (unit.IsReadyForRemoval)
                RemoveImmediately(unit);
    }

    public void DrawShadow(Effect effect)
    {
        foreach (Unit unit in Units)
            if (!unit.IsEmbarked && World.Visibility.IsUnitVisibleToLocalPlayer(unit))
            {
                unit.DrawShadow(effect);
                unit.DrawSeatedOccupants(effect);
            }
    }

    public void DrawBuildings(Effect effect)
    {
        foreach (Building unit in Units.OfType<Building>())
        {
            if (unit.IsEmbarked || !World.Visibility.IsUnitVisibleToLocalPlayer(unit))
                continue;

            // Restore the normal building atlas before a BBModel sub-mesh
            // optionally selects its own TextureHandler atlas.
            Player? owner = ResolveArmyOwner(unit);
            if (unit.ArmyId is null)
                Globals.SkinHandler.DisableForEffect(effect);
            else
                Globals.SkinHandler.ApplyToEffect(effect, owner?.Skin ?? PlayerSkin.Green);
            effect.Parameters["UnitTextureUVOffset"]?.SetValue(unit.UnitTextureUVOffset);
            unit.Draw(effect);
            unit.DrawSeatedOccupants(effect);
            unit.DrawOwnerFlag(effect, ResolveArmyFlagColor(unit));
        }
    }

    private static Color ResolveArmyFlagColor(Unit unit)
    {
        Player? owner = null;
        if (unit.ArmyId is Guid armyId)
        {
            Army? army = Globals.Game.Armies.Find(armyId);
            Guid? colorOwnerId = army?.OwnerPlayerIds.OrderBy(id => id).FirstOrDefault();
            if (colorOwnerId is Guid playerId && playerId != Guid.Empty)
                owner = Globals.Game.Players.FirstOrDefault(player => player.Id == playerId);
        }
        owner ??= Globals.Game.Players.FirstOrDefault(player => player.Id == unit.CreatorPlayerId);
        return Globals.SkinHandler.GetDisplayColor(owner?.Skin ?? PlayerSkin.Green);
    }

    public void DrawMobileUnits(Effect effect)
    {
        foreach (MobileUnit unit in Units.OfType<MobileUnit>())
        {
            if (unit.IsEmbarked || !World.Visibility.IsUnitVisibleToLocalPlayer(unit))
                continue;

            // Restore the normal unit atlas before a BBModel sub-mesh
            // optionally selects its own TextureHandler atlas.
            Player? owner = ResolveArmyOwner(unit);
            if (unit.ArmyId is null)
                Globals.SkinHandler.DisableForEffect(effect);
            else
                Globals.SkinHandler.ApplyToEffect(effect, owner?.Skin ?? PlayerSkin.Green);
            effect.Parameters["UnitTextureUVOffset"]?.SetValue(unit.UnitTextureUVOffset);
            unit.Draw(effect);
            unit.DrawSeatedOccupants(effect);
        }
    }

    public bool Destroy(Guid unitId)
    {
        Unit? unit = FindById(unitId);
        if (unit is null)
            return false;

        // A repeated destroy command while the local death animation is still
        // running must not make the ghost vanish prematurely.
        if (unit.IsDying)
            return true;

        if (unit is MobileUnit movingUnit && movingUnit.PendingEnterContainerId is not null)
            movingUnit.Stop();

        if (unit.Occupancy is OccupancyComponent occupancy)
            foreach (OccupantAssignment occupant in occupancy.Occupants.ToArray())
                RemoveEmbarkedImmediately(occupant.UnitId);

        if (unit.IsEmbarked && unit.ContainerUnitId is Guid parentId)
            FindById(parentId)?.Occupancy?.TryRemove(unit.UnitId, out _);

        if (unit is SquadLeader destroyedLeader)
            foreach (Soldier member in Units.OfType<Soldier>().Where(
                member => member.SquadLeaderId == destroyedLeader.UnitId))
                member.SquadLeaderId = null;

        // An attack target may disappear before the next host simulation
        // tick. Clear every reference immediately, on host and clients alike.
        foreach (Unit other in Units)
            if (other != unit)
            {
                other.ClearReferencesToDestroyedUnit(unitId);
                if (other is MobileUnit mobileUnit && mobileUnit.PendingEnterContainerId == unitId)
                    mobileUnit.Stop();
            }

        World.GameGrid.Remove(unit);
        RemovePerkSource(unit.UnitId);
        if (!unit.BeginDeathSequence())
            lock (_unitsSync) _units.Remove(unit);
        return true;
    }

    public void ClearAll()
    {
        foreach (Unit unit in Units)
            RemoveImmediately(unit);
    }

    /// <summary>Removes the previous match while retaining authored map objects.</summary>
    public void ClearForMatchStart()
    {
        foreach (Unit unit in Units.Where(unit =>
            unit is not GenericBuilding && unit is not TiberiumSource))
            RemoveImmediately(unit);
    }

    public bool SellBuilding(Guid unitId)
    {
        if (FindById(unitId) is not Building building || building is GenericBuilding ||
            building.IsDying || building.Occupancy?.Occupants.Count > 0)
            return false;

        foreach (Unit other in Units)
            if (other != building)
                other.ClearReferencesToDestroyedUnit(unitId);

        World.GameGrid.Remove(building);
        RemovePerkSource(building.UnitId);
        building.BeginSelling();
        return true;
    }

    public bool EmbarkUnit(Guid occupantId, Guid containerId, OccupantRole? requestedRole = null)
    {
        if (FindById(occupantId) is not MobileUnit occupant ||
            FindById(containerId) is not Unit container ||
            container.Occupancy is not OccupancyComponent occupancy ||
            !occupancy.TryAdd(occupant, requestedRole, out OccupantRole role))
        {
            return false;
        }

        World.GameGrid.Remove(occupant);
        occupant.Embark(container.UnitId);
        occupant.SetPosition(container.Position);
        if (occupancy.ControllerRole == role)
            container.Behavior = occupant.Behavior;
        return true;
    }

    public bool DisembarkUnit(Guid containerId, Guid occupantUnitId, Vector3 exitPosition)
    {
        if (FindById(containerId) is not Unit container ||
            FindById(occupantUnitId) is not MobileUnit occupant ||
            container.Occupancy is not OccupancyComponent occupancy ||
            !occupancy.Occupants.Any(item => item.UnitId == occupantUnitId))
        {
            return false;
        }

        if (container is Helicopter { IsLanded: false }) return false;
        occupant.Disembark(exitPosition);
        Point exitCell = World.GameGrid.ToCell(exitPosition);
        if (!World.GameGrid.TryMove(occupant, exitCell))
        {
            occupant.Embark(container.UnitId);
            occupant.SetPosition(container.Position);
            return false;
        }

        return occupancy.TryRemove(occupantUnitId, out _);
    }

    private void AddInitialDriver(Unit container, Guid creatorPlayerId, Guid driverUnitId)
    {
        if (FindById(driverUnitId) is not null) return;
        Soldier driver = new(container.Position, driverUnitId);
        // Crew must be identical on every peer; the normal Soldier constructor
        // currently chooses a random weapon locally.
        driver.SetWeapon(Soldier.Weapon.Brok17); // drivers only have a pistol (Brok17)
        driver.SetCreatorPlayer(creatorPlayerId);
        driver.SetArmy(container.ArmyId);
        if (container.Occupancy is not OccupancyComponent occupancy ||
            !occupancy.TryReserve(driver, OccupantRole.Driver, out _) ||
            !occupancy.TryAdd(driver, OccupantRole.Driver, out _))
            return;

        driver.Embark(container.UnitId);
        lock (_unitsSync) _units.Add(driver);
    }

    private void RemoveEmbarkedImmediately(Guid unitId)
    {
        Unit? occupant = FindById(unitId);
        if (occupant is null)
            return;

        foreach (Unit other in Units)
            if (other != occupant)
                other.ClearReferencesToDestroyedUnit(unitId);
        lock (_unitsSync) _units.Remove(occupant);
    }

    private Player? ResolveArmyOwner(Unit unit)
    {
        if (unit.Occupancy?.GetController() is OccupantAssignment controller &&
            World.Units.FindById(controller.UnitId) is Unit driver)
        {
            Player? driverOwner = Globals.Game.Players.FirstOrDefault(
                player => player.Id == driver.CreatorPlayerId);
            if (driverOwner is not null)
                return driverOwner;
        }

        if (unit.ArmyId is Guid armyId && Globals.Game.Armies.Find(armyId) is Army army)
        {
            Guid playerId = army.OwnerPlayerIds.OrderBy(id => id).FirstOrDefault();
            if (playerId != Guid.Empty)
                return Globals.Game.Players.FirstOrDefault(player => player.Id == playerId);
        }

        return Globals.Game.Players.FirstOrDefault(player => player.Id == unit.CreatorPlayerId);
    }

    private void RemoveImmediately(Unit unit)
    {
        if (unit is SquadLeader leader)
        {
            foreach (Soldier member in Units.OfType<Soldier>().Where(
                member => member.SquadLeaderId == leader.UnitId))
                member.SquadLeaderId = null;
        }
        World.GameGrid.Remove(unit);
        RemovePerkSource(unit.UnitId);
        lock (_unitsSync) _units.Remove(unit);
    }

    /// <summary>Immediate replacement used only while applying one atomic host snapshot.</summary>
    internal void ClearForNetworkSnapshot()
    {
        lock (_unitsSync)
        {
            foreach (Unit unit in _units) World.GameGrid.Remove(unit);
            _units.Clear();
        }
        _perkSourceArmies.Clear();
    }

    private void RefreshPerkSource(Unit unit)
    {
        Guid? currentArmyId = unit.ArmyId;
        if (_perkSourceArmies.TryGetValue(unit.UnitId, out Guid previousArmyId) &&
            previousArmyId != currentArmyId)
        {
            Armies.Find(previousArmyId)?.Perks.RemoveSource(unit.UnitId);
            _perkSourceArmies.Remove(unit.UnitId);
        }

        if (unit is not IPerkProvider provider || currentArmyId is not Guid armyId ||
            Armies.Find(armyId) is not Army army)
        {
            RemovePerkSource(unit.UnitId);
            return;
        }

        IReadOnlyList<PerkGrant> grants = provider.GetProvidedPerks();
        army.Perks.SetSource(unit.UnitId, grants);
        if (grants.Count > 0)
            _perkSourceArmies[unit.UnitId] = armyId;
        else
            _perkSourceArmies.Remove(unit.UnitId);
    }

    private void RemovePerkSource(Guid sourceId)
    {
        if (_perkSourceArmies.Remove(sourceId, out Guid armyId))
            Armies.Find(armyId)?.Perks.RemoveSource(sourceId);
    }

    public void Draw2D(SpriteBatch spriteBatch, Camera camera, Viewport viewport)
    {
        Player? viewer = Globals.Game.Players.FirstOrDefault(
            player => player.Id == Globals.Game.Network.LocalPeerId);
        Army? viewerArmy = viewer is null ? null : Globals.Game.Armies.Find(viewer.ArmyId);

        foreach (Unit unit in Units)
        {
            if (unit.IsEmbarked || !World.Visibility.IsUnitVisibleToLocalPlayer(unit))
                continue;
            unit.Draw2D(spriteBatch, camera, viewport);
            HealthInformationLevel healthInformation = HealthBarRenderer.GetInformationLevel(
                viewerArmy, unit.ArmyId, unit.Position);
            HealthBarRenderer.Draw(spriteBatch, camera, viewport, unit, healthInformation);
        }
    }

    private bool SetFootprints(Unit unit, float rotateYDegrees)
    {
        if (unit is Helicopter helicopter) return helicopter.InitializeOnGround(World);
        Point cell = World.GameGrid.ToCell(unit.Position);
        MobileUnit? mobileUnit = unit as MobileUnit;
        if (mobileUnit != null)
        {
            if (!World.GameGrid.TryMove(mobileUnit, cell))
            {
                Random rnd = new Random();
                cell.X += rnd.Next(-10, 10);
                cell.Y += rnd.Next(-10, 10);
                return World.GameGrid.TryMove(mobileUnit, cell);
            }
        }
        else if (!World.GameGrid.TryPlace(unit, unit.Position, rotateYDegrees))
        {
            Console.WriteLine($"Cannot place building '{unit.GetType().Name}' at {unit.Position}: footprint is blocked.");
            return false;
        }
        return true;
    }

    private void AssignCurrentArmy(Unit unit, Guid creatorPlayerId)
    {
        if (creatorPlayerId == Guid.Empty)
        {
            unit.SetArmy(null);
            return;
        }

        Player? owner = Globals.Game?.Players.FirstOrDefault(player => player.Id == creatorPlayerId);
        unit.SetArmy(owner?.ArmyId ?? Armies.Armies.FirstOrDefault(army => army.OwnerPlayerIds.Contains(creatorPlayerId))?.Id ?? creatorPlayerId);
    }
}

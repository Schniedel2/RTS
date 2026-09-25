using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class UnitHandler
{
    private readonly List<Unit> _units = new List<Unit>();
    private readonly object _unitsSync = new();
    private readonly Dictionary<Guid, Guid> _perkSourceArmies = [];

    public IReadOnlyList<Unit> Units
    {
        get { lock (_unitsSync) return _units.ToArray(); }
    }

    public UnitHandler()
    {
    }

    public Unit? SpawnUnit(
        string unitTypeName,
        Vector3 position,
        float RotateYDegrees,
        Guid unitId,
        Guid creatorPlayerId,
        Guid? driverUnitId = null)
    {
        Unit? unit = UnitFactory.SpawnUnit(unitTypeName, position, RotateYDegrees, unitId, creatorPlayerId);
        if (unit == null)
            unit = SpawnBuilding(unitTypeName, position, RotateYDegrees, unitId, creatorPlayerId);
        if (unit == null)
            return null;

        if (unit is Building building && !building.EvaluatePlacement(Globals.World, position, RotateYDegrees).IsAllowed)
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
            helicopter.InitializeDelivery(Globals.World, pad, spawnPosition);
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
        Guid creatorPlayerId)
    {
        Building? unit = BuildingFactory.SpawnBuilding(buildingTypeName, position, RotateYDegrees, unitId, creatorPlayerId);
        if (unit is null)
            return null;

        if (unit is Building building && !building.EvaluatePlacement(Globals.World, position, RotateYDegrees).IsAllowed)
            return null;
        AssignCurrentArmy(unit, creatorPlayerId);
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
        return Units.FirstOrDefault(unit => unit.UnitId == unitId);
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
        return Units.FirstOrDefault(unit => unit.UnitId == unitId) as MobileUnit;
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
            if (!unit.IsEmbarked && Globals.Game.World.Visibility.IsUnitVisibleToLocalPlayer(unit))
                unit.DrawShadow(effect);
    }

    public void DrawBuildings(Effect effect)
    {
        foreach (Building unit in Units.OfType<Building>())
        {
            if (unit.IsEmbarked || !Globals.Game.World.Visibility.IsUnitVisibleToLocalPlayer(unit))
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
            if (unit.IsEmbarked || !Globals.Game.World.Visibility.IsUnitVisibleToLocalPlayer(unit))
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

        // An attack target may disappear before the next host simulation
        // tick. Clear every reference immediately, on host and clients alike.
        foreach (Unit other in Units)
            if (other != unit)
            {
                other.ClearReferencesToDestroyedUnit(unitId);
                if (other is MobileUnit mobileUnit && mobileUnit.PendingEnterContainerId == unitId)
                    mobileUnit.Stop();
            }

        Globals.World.GameGrid.Remove(unit);
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

        Globals.World.GameGrid.Remove(building);
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

        Globals.World.GameGrid.Remove(occupant);
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
        Point exitCell = Globals.World.GameGrid.ToCell(exitPosition);
        if (!Globals.World.GameGrid.TryMove(occupant, exitCell))
        {
            occupant.Embark(container.UnitId);
            occupant.SetPosition(container.Position);
            return false;
        }

        return occupancy.TryRemove(occupantUnitId, out _);
    }

    private void AddInitialDriver(Unit container, Guid creatorPlayerId, Guid driverUnitId)
    {
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

    private static Player? ResolveArmyOwner(Unit unit)
    {
        if (unit.Occupancy?.GetController() is OccupantAssignment controller &&
            Globals.World.Units.FindById(controller.UnitId) is Unit driver)
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
        Globals.World.GameGrid.Remove(unit);
        RemovePerkSource(unit.UnitId);
        lock (_unitsSync) _units.Remove(unit);
    }

    private void RefreshPerkSource(Unit unit)
    {
        Guid? currentArmyId = unit.ArmyId;
        if (_perkSourceArmies.TryGetValue(unit.UnitId, out Guid previousArmyId) &&
            previousArmyId != currentArmyId)
        {
            Globals.Game.Armies.Find(previousArmyId)?.Perks.RemoveSource(unit.UnitId);
            _perkSourceArmies.Remove(unit.UnitId);
        }

        if (unit is not IPerkProvider provider || currentArmyId is not Guid armyId ||
            Globals.Game.Armies.Find(armyId) is not Army army)
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
            Globals.Game.Armies.Find(armyId)?.Perks.RemoveSource(sourceId);
    }

    public void Draw2D(SpriteBatch spriteBatch, Camera camera, Viewport viewport)
    {
        Player? viewer = Globals.Game.Players.FirstOrDefault(
            player => player.Id == Globals.Game.Network.LocalPeerId);
        Army? viewerArmy = viewer is null ? null : Globals.Game.Armies.Find(viewer.ArmyId);

        foreach (Unit unit in Units)
        {
            if (unit.IsEmbarked || !Globals.Game.World.Visibility.IsUnitVisibleToLocalPlayer(unit))
                continue;
            unit.Draw2D(spriteBatch, camera, viewport);
            HealthInformationLevel healthInformation = HealthBarRenderer.GetInformationLevel(
                viewerArmy, unit.ArmyId, unit.Position);
            HealthBarRenderer.Draw(spriteBatch, camera, viewport, unit, healthInformation);
        }
    }

    private bool SetFootprints(Unit unit, float rotateYDegrees)
    {
        if (unit is Helicopter helicopter) return helicopter.InitializeOnGround(Globals.World);
        Point cell = Globals.World.GameGrid.ToCell(unit.Position);
        MobileUnit? mobileUnit = unit as MobileUnit;
        if (mobileUnit != null)
        {
            if (!Globals.World.GameGrid.TryMove(mobileUnit, cell))
            {
                Random rnd = new Random();
                cell.X += rnd.Next(-10, 10);
                cell.Y += rnd.Next(-10, 10);
                return Globals.World.GameGrid.TryMove(mobileUnit, cell);
            }
        }
        else if (!Globals.World.GameGrid.TryPlace(unit, unit.Position, rotateYDegrees))
        {
            Console.WriteLine($"Cannot place building '{unit.GetType().Name}' at {unit.Position}: footprint is blocked.");
            return false;
        }
        return true;
    }

    private static void AssignCurrentArmy(Unit unit, Guid creatorPlayerId)
    {
        if (creatorPlayerId == Guid.Empty)
        {
            unit.SetArmy(null);
            return;
        }

        Player? owner = Globals.Game.Players.FirstOrDefault(player => player.Id == creatorPlayerId);
        unit.SetArmy(owner?.ArmyId ?? creatorPlayerId);
    }
}

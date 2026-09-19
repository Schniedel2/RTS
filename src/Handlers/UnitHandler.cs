using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class UnitHandler
{
    private readonly List<Unit> _units = new List<Unit>();

    public IReadOnlyList<Unit> Units => _units;

    public UnitHandler()
    {
    }

    public Unit? SpawnUnit(
        string unitTypeName,
        Vector3 position,
        float RotateYDegrees,
        Guid unitId,
        Guid creatorPlayerId)
    {
        Unit? unit = UnitFactory.SpawnUnit(unitTypeName, position, RotateYDegrees, unitId, creatorPlayerId);
        if (unit == null)
            unit = SpawnBuilding(unitTypeName, position, RotateYDegrees, unitId, creatorPlayerId);
        if (unit == null)
            return null;

        AssignCurrentArmy(unit, creatorPlayerId);
        if (SetFootprints(unit, RotateYDegrees))
        {
            _units.Add(unit);
            return unit;
        }
        return null;
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

        AssignCurrentArmy(unit, creatorPlayerId);
        if (SetFootprints(unit, RotateYDegrees))
        {
            _units.Add(unit);
            return unit;
        }
        return null;
    }

    public Unit? FindById(Guid unitId)
    {
        return _units.FirstOrDefault(unit => unit.UnitId == unitId);
    }

    public MobileUnit? FindMobileUnitById(Guid unitId)
    {
        return _units.FirstOrDefault(unit => unit.UnitId == unitId) as MobileUnit;
    }

    public void Update(GameTime gameTime)
    {
        foreach (Unit unit in _units)
            unit.Update(gameTime);

        for (int index = _units.Count - 1; index >= 0; index--)
            if (_units[index].IsReadyForRemoval)
                RemoveImmediately(_units[index]);
    }

    public void DrawShadow(Effect effect)
    {
        foreach (Unit unit in _units)
            unit.DrawShadow(effect);
    }

    public void DrawBuildings(Effect effect)
    {
        foreach (Building unit in _units.OfType<Building>())
        {
            // Restore the normal building atlas before a BBModel sub-mesh
            // optionally selects its own TextureHandler atlas.
            effect.Parameters["UnitTexture"]?.SetValue(Globals.BuildingsTexture);
            Player? owner = Globals.Game.Players.FirstOrDefault(
                player => player.Id == unit.CreatorPlayerId);
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
        foreach (MobileUnit unit in _units.OfType<MobileUnit>())
        {
            // Restore the normal unit atlas before a BBModel sub-mesh
            // optionally selects its own TextureHandler atlas.
            effect.Parameters["UnitTexture"]?.SetValue(Globals.UnitsTexture);
            Player? owner = Globals.Game.Players.FirstOrDefault(
                player => player.Id == unit.CreatorPlayerId);
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

        // An attack target may disappear before the next host simulation
        // tick. Clear every reference immediately, on host and clients alike.
        foreach (Unit other in _units)
            if (other != unit)
                other.ClearReferencesToDestroyedUnit(unitId);

        Globals.World.GameGrid.Remove(unit);
        if (!unit.BeginDeathSequence())
            _units.Remove(unit);
        return true;
    }

    private void RemoveImmediately(Unit unit)
    {
        Globals.World.GameGrid.Remove(unit);
        _units.Remove(unit);
    }

    public void Draw2D(SpriteBatch spriteBatch, Camera camera, Viewport viewport)
    {
        foreach (Building building in _units.OfType<Building>())
            building.Draw2D(spriteBatch, camera, viewport);
    }

    private bool SetFootprints(Unit unit, float rotateYDegrees)
    {
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

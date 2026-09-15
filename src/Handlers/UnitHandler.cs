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
        }
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

        Globals.World.GameGrid.Remove(unit);
        _units.Remove(unit);
        return true;
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
}

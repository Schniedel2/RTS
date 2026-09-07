using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class UnitHandler
{
    private readonly List<MobileUnit> _units = new List<MobileUnit>();

    public IReadOnlyList<MobileUnit> Units => _units;

    public UnitHandler()
    {
    }

    public MobileUnit SpawnUnit(
        string unitTypeName,
        Vector3 position,
        Guid unitId,
        Guid creatorPlayerId)
    {
        MobileUnit unit = UnitFactory.SpawnUnit(unitTypeName, position, unitId, creatorPlayerId);
        AddUnit(unit);
        return unit;
    }

    public MobileUnit SpawnBuilding(
        string buildingTypeName,
        Vector3 position,
        Guid unitId,
        Guid creatorPlayerId)
    {
        MobileUnit unit = BuildingFactory.SpawnBuilding(buildingTypeName, position, unitId, creatorPlayerId);
        if (unit != null)
            AddUnit(unit);
        return unit;
    }

    public MobileUnit? FindById(Guid unitId)
    {
        return _units.FirstOrDefault(unit => unit.UnitId == unitId);
    }

    public void Update(GameTime gameTime)
    {
        foreach (MobileUnit unit in _units)
                unit.Update(gameTime);
    }

    public void DrawShadow(GraphicsDevice graphicsDevice, Effect effect)
    {
        foreach (MobileUnit unit in _units)
            unit.DrawShadow(graphicsDevice, effect);
    }

    public void Draw(GraphicsDevice graphicsDevice, Effect effect)
    {
        foreach (MobileUnit unit in _units)
            unit.Draw(graphicsDevice, effect);
    }

        private void AddUnit(MobileUnit unit)
        {
            Point cell = Globals.World.GameGrid.ToCell(unit.Position);
            if (!Globals.World.GameGrid.TryMove(unit, cell))
            {
                Random rnd = new Random();
                cell.X += rnd.Next(-10, 10);
                cell.Y += rnd.Next(-10, 10);
                Globals.World.GameGrid.TryMove(unit, cell);
            }
            _units.Add(unit);
        }
}
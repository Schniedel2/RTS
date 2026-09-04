using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class UnitHandler
{
    private readonly Terrain _terrain;
    private readonly GameWorld _map;
    private readonly List<Unit> _units = [];

    public IReadOnlyList<Unit> Units => _units;

    public UnitHandler(
        Terrain terrain,
        GameWorld map)
    {
        _terrain = terrain;
        _map = map;
    }

    public Unit SpawnUnit(string unitTypeName, Vector3 position, Guid unitId)
    {
        Unit unit;            
        switch (unitTypeName.ToLower())
        {
            case "soldier":
                unit = new Soldier(position, unitId);
                break;
            case "car":
                unit = new Car(position, unitId);
                break;
            case "tank":
                unit = new Tank(position, unitId);
                break;
            case "editor":
                unit = new TerrainEditorTool(position, unitId);
                break;
            default:
                throw new ArgumentException($"Unknown unit type: {unitTypeName}");
        }
        AddUnit(unit);
        return unit;
    }
    
    public Unit? FindById(Guid unitId)
    {
        return _units.FirstOrDefault(unit => unit.UnitId == unitId);
    }

    public void Update(GameTime gameTime)
    {
        foreach (Unit unit in _units)
                unit.Update(gameTime, _terrain, _map);
    }

    public void DrawShadow(GraphicsDevice graphicsDevice, Effect effect)
    {
        foreach (Unit unit in _units)
            unit.DrawShadow(graphicsDevice, effect);
    }

    public void Draw(GraphicsDevice graphicsDevice, Effect effect)
    {
        foreach (Unit unit in _units)
            unit.Draw(graphicsDevice, effect);
    }

        private void AddUnit(Unit unit)
        {
            Point cell = _map.GameGrid.ToCell(unit.Position);

            if (!_map.GameGrid.TryMove(unit, cell))
                throw new InvalidOperationException("Unit footprint overlaps another unit or leaves the game grid.");

            _units.Add(unit);
        }
}
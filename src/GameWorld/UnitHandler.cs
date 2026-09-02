using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class UnitHandler
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Terrain _terrain;
    private readonly GameWorld _map;
    private readonly List<Unit> _units = [];

    public IReadOnlyList<Unit> Units => _units;

    public UnitHandler(
        GraphicsDevice graphicsDevice,
        Terrain terrain,
        GameWorld map)
    {
        _graphicsDevice = graphicsDevice;
        _terrain = terrain;
        _map = map;
    }

    public Unit SpawnUnit(
        Vector3 position,
        int length,
        int width,
        float height)
    {
        Unit unit = new(
            _graphicsDevice,
            position,
            length,
            width,
            height);

        AddUnit(unit);

        return unit;
    }

    public Soldier SpawnSoldier(Vector3 position)
    {
        return SpawnSoldier(position, null);
    }

    public Soldier SpawnSoldier(Vector3 position, Guid? unitId)
    {
        Soldier soldier = new(_graphicsDevice, position, unitId: unitId);
            AddUnit(soldier);

        return soldier;
    }

    public Car SpawnCar(Vector3 position)
    {
        return SpawnCar(position, null);
    }

    public Car SpawnCar(Vector3 position, Guid? unitId)
    {
        Car car = new(_graphicsDevice, position, unitId: unitId);
            AddUnit(car);

        return car;
    }

    public Tank SpawnTank(Vector3 position)
    {
        return SpawnTank(position, null);
    }

    public Tank SpawnTank(Vector3 position, Guid? unitId)
    {
        Tank tank = new(_graphicsDevice, position, unitId: unitId);
            AddUnit(tank);

        return tank;
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

    public void DrawShadow(Effect effect)
    {
        foreach (Unit unit in _units)
            unit.DrawShadow(effect);
    }

    public void Draw(Effect effect)
    {
        foreach (Unit unit in _units)
            unit.Draw(effect);
    }

        private void AddUnit(Unit unit)
        {
            Point cell = _map.Grid.ToCell(unit.Position);

            if (!_map.Grid.TryMove(unit, cell))
                throw new InvalidOperationException("Unit footprint overlaps another unit or leaves the game grid.");

            _units.Add(unit);
        }
}
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
namespace RTS;
public sealed class ScoutingController(GameWorld world)
{
    private sealed class State { public Point Target; public float ReconsiderIn; }
    private readonly Dictionary<Guid, State> _scouts = [];
    public void Start(IEnumerable<Unit> units) { foreach (MobileUnit unit in units.OfType<MobileUnit>()) if (!unit.IsDying && !unit.IsEmbarked) _scouts[unit.UnitId] = new(); }
    public void Stop(IEnumerable<Unit> units) { foreach (Unit unit in units) _scouts.Remove(unit.UnitId); }
    public void Update(GameTime gameTime)
    {
        foreach ((Guid id, State state) in _scouts.ToArray())
        {
            if (world.Units.FindById(id) is not MobileUnit unit || unit.IsDying || unit.IsEmbarked || unit.ArmyId is not Guid army) { _scouts.Remove(id); continue; }
            state.ReconsiderIn -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            Point current = world.GameGrid.ToCell(unit.Position);
            if (state.ReconsiderIn > 0 && current != state.Target && world.Visibility.GetDisplayedTerrainVisibility(army, state.Target, false) == VisibilityState.Unexplored) continue;
            if (!TryFindTarget(unit, army, current, out Point target)) { state.ReconsiderIn = 2; continue; }
            state.Target = target; state.ReconsiderIn = 8;
            Vector3 position = world.GameGrid.ToWorldPosition(target, 0);
            _ = Globals.Game.NetworkClient.RequestGotoAsync([unit.UnitId], position.X, position.Y, position.Z);
        }
    }
    private bool TryFindTarget(MobileUnit unit, Guid army, Point current, out Point target)
    {
        List<Point> candidates = []; int min = Math.Max(4, unit.GetSightRange() / 2), max = Math.Max(12, unit.GetSightRange() * 3);
        for (int z = Math.Max(0, current.Y - max); z <= Math.Min(world.GameGrid.Height - 1, current.Y + max); z++)
        for (int x = Math.Max(0, current.X - max); x <= Math.Min(world.GameGrid.Width - 1, current.X + max); x++)
        { Point cell = new(x, z); int d = Math.Max(Math.Abs(x-current.X), Math.Abs(z-current.Y));
          if (d >= min && d <= max && world.Visibility.GetDisplayedTerrainVisibility(army, cell, false) == VisibilityState.Unexplored && !world.GameGrid.GetCell(cell).IsBlocked) candidates.Add(cell); }
        if (candidates.Count == 0) { target = default; return false; }
        target = candidates[Random.Shared.Next(candidates.Count)]; return true;
    }
}

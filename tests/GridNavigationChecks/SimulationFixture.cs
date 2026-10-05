using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using RTS;

// Regular constructors initialize gameplay state; model/GPU presentation is optional.
internal static class SimulationFixture
{
    public static GameWorld World() => new(41, 41, 1, graphicsEnabled: false);
    public sealed class Membership(GameWorld world)
    {
        public void Add(Unit unit) => world.Units.Register(unit);
        public void AddRange(IEnumerable<Unit> units) { foreach (Unit unit in units) Add(unit); }
        public bool Remove(Unit unit) => world.Units.Unregister(unit);
        public void Clear() => world.Units.ClearMembership();
    }
    public sealed class Patient(Vector3 position, Guid id) : Soldier(position, id, loadModel: false)
    {
        public bool Dead { get; set; }
        public override bool IsDying => Dead;
    }
    public static T Owned<T>(T unit, Guid army, float health = 100) where T : Unit
    {
        unit.SetArmy(army);
        unit.HitPoints = health;
        return unit;
    }
}

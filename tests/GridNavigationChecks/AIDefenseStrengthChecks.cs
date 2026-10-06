using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    private static int RunDefenseStrengthChecks()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        using var scenario = new Scenario(false);
        scenario.AddBuilding("gdi-base", new(15.5f, 0, 15.5f));
        List<Unit> defenders = [];
        for (int i = 0; i < 10; i++) defenders.Add(scenario.Add(new Gunner(new(20.5f + i, 0, 20.5f), scenario.NextId())));
        Unit enemy = scenario.AddVisibleEnemy(new(20.5f, 0, 25.5f));
        AIDefenseForce Select(params Unit[] threats) => AIDefenseForceSelector.Select(defenders, threats, enemy, 1);
        var small = Select(enemy);
        Check(small.Defenders.Length > 0 && small.Defenders.Length < defenders.Count, "One attacker does not pull the entire defending army");
        Check(small.AssignedPower >= small.RequiredPower, "Chosen subset reaches the estimated force requirement");
        var large = Select(enemy, enemy, enemy, enemy, enemy);
        Check(large.RequiredPower > small.RequiredPower && large.Defenders.Length > small.Defenders.Length, "Larger local threat calls for additional defenders");
        var near = small.Defenders[0]; near.HitPoints = 1;
        Check(!Select(enemy).Defenders.Contains(near), "Healthy defenders are preferred over a critically wounded nearby soldier");
        near.HitPoints = 100;
        Unit far = defenders[^1];
        Check(!small.Defenders.Contains(far), "Equivalent distant defender stays on its existing assignment");
        var reversed = AIDefenseForceSelector.Select(defenders.AsEnumerable().Reverse(), [enemy], enemy, 1);
        Check(reversed.Defenders.Select(u => u.UnitId).SequenceEqual(small.Defenders.Select(u => u.UnitId)), "Equal evaluations are deterministic despite registry order");
        var armored = scenario.Add(new CatalogVehicle("tank", new(42.5f, 0, 42.5f), scenario.NextId()), enemy.ArmyId);
        var antiTank = scenario.Add(new Gunner(new(37.5f, 0, 42.5f), scenario.NextId()));
        antiTank.AttackDamage = 80; antiTank.AttackDamageType = DamageType.AntiTank;
        var counters = AIDefenseForceSelector.Select(defenders.Append(antiTank), [armored], armored, 1);
        Check(counters.Defenders.Contains(antiTank), "Armor counterweapon outranks ineffective small arms against a heavy vehicle");
        var air = scenario.Add(new CatalogVehicle("helicopter", new(40.5f, 0, 40.5f), scenario.NextId()), enemy.ArmyId);
        Check(AIDefenseForceSelector.Select(defenders, [air], air, 1).Defenders.Length == 0, "Ground-only weapons cannot be assigned to an air target");
        var aa = scenario.Add(new CatalogVehicle("gepard", new(38.5f, 0, 40.5f), scenario.NextId()));
        Check(AIDefenseForceSelector.Select(defenders.Append(aa), [air], air, 1).Defenders.Single() == aa, "Air-capable defender is selected for an aircraft");
        Check(AIDefenseForceSelector.Select([], [enemy], enemy, 1) is { AssignedPower: 0, Defenders.Length: 0 }, "Insufficient available force produces an honest empty estimate");
        scenario.Remove(armored); scenario.Remove(air); scenario.Remove(antiTank); scenario.Remove(aa);
        var controller = new AIBaseDefenseController(scenario.World, scenario.AI.Id, scenario.Army.Id, scenario.Network);
        void Tick(int time) { controller.Update(new GameTime(TimeSpan.FromSeconds(time), TimeSpan.FromSeconds(1)), null); scenario.Network.Update(); }
        Tick(1);
        Guid[] initially = controller.AssignedDefenders.ToArray();
        Check(initially.Length < defenders.Count && scenario.Messages.Any(m => m.Type == NetworkMessageType.AttackTargetRequest && m.UnitIds!.SequenceEqual(initially)),
            "Proportionate controller assignment travels through normal player requests");
        Check(defenders.All(u => u.AttackTargetId is null), "Selecting defenders does not directly apply combat state");
        scenario.PumpHost();
        Check(initially.All(id => scenario.World.Units.FindById(id)?.AttackTargetId == enemy.UnitId), "Real host applies the proportionate assignment");
        List<Unit> extra = [];
        for (int i = 0; i < 4; i++) extra.Add(scenario.AddVisibleEnemy(new(21.5f + i, 0, 25.5f)));
        Tick(2);
        Guid[] expanded = controller.AssignedDefenders.ToArray();
        Check(expanded.Length > initially.Length, "Controller reinforces an expanded local incident");
        foreach (Unit unit in extra) scenario.Remove(unit);
        scenario.Messages.Clear(); Tick(3);
        Guid[] released = expanded.Except(controller.AssignedDefenders).ToArray();
        Check(released.Length > 0 && scenario.Messages.Any(m => m.Type == NetworkMessageType.StopRequest && m.UnitIds!.Order().SequenceEqual(released.Order())),
            "Surplus defenders are released through Stop requests when threat shrinks");
        Check(released.All(id => scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest && m.UnitIds!.Contains(id))),
            "Released defenders receive their captured return destinations");
        scenario.Remove(enemy); scenario.Messages.Clear(); Tick(4);
        Check(!controller.IsEngaging && controller.AssignedDefenders.Count == 0 && scenario.Messages.Any(m => m.Type == NetworkMessageType.StopRequest),
            "Ending the incident returns the remaining proportional defense force");
        return checks;
    }
}

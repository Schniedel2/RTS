using System;
using System.Linq;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    private static int RunScoutingReservationChecks()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        using var scenario = new Scenario(false);
        var first = scenario.Add(new Gunner(new(30.5f, 0, 30.5f), scenario.NextId()));
        var second = scenario.Add(new Gunner(new(31.5f, 0, 30.5f), scenario.NextId()));
        using var scouts = new ScoutingController(scenario.World, scenario.AI.Id, scenario.Network);
        using var otherController = new ScoutingController(scenario.World, scenario.AI.Id, scenario.Network);
        scouts.Start([first]); otherController.Start([second]);
        var time = new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        scouts.Update(time); otherController.Update(time);
        for (int step = 0; step < 100; step++) scenario.World.PathfindingManager.Scheduler.Update(4096, double.PositiveInfinity);
        scenario.Network.Update();
        Point a = scenario.World.ScoutingTargets.TargetOf(first.UnitId)!.Value;
        Point b = scenario.World.ScoutingTargets.TargetOf(second.UnitId)!.Value;
        Check(scenario.World.Visibility.GetDisplayedTerrainVisibility(scenario.Army.Id, a, false) == VisibilityState.Unexplored &&
            scenario.World.Visibility.GetDisplayedTerrainVisibility(scenario.Army.Id, b, false) == VisibilityState.Unexplored,
            "Both assignments prefer genuinely unexplored terrain");
        Check(ScoutingTargets.Sector(a) != ScoutingTargets.Sector(b), "Independent controllers share army-wide sector reservations");
        Vector2 da = Vector2.Normalize(new Vector2(a.X - 30, a.Y - 30));
        Vector2 db = Vector2.Normalize(new Vector2(b.X - 31, b.Y - 30));
        Check(Vector2.Dot(da, db) < 0.9f, "Nearby scouts depart into different directions on unknown flat terrain");
        Check(scenario.Messages.Count(message => message.Type == NetworkMessageType.GotoRequest) == 2,
            "Both scouting targets use ordinary player network requests");
        scouts.Start([first]); scouts.Update(new GameTime(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
        Check(scenario.World.ScoutingTargets.TargetOf(first.UnitId) == a, "Repeated Start does not reset an active scout's assignment");
        scouts.Stop([first]);
        Check(scenario.World.ScoutingTargets.TargetOf(first.UnitId) is null && !scouts.IsScouting(first.UnitId),
            "Stop releases target and local scout state immediately");
        Check(scenario.World.ScoutingTargets.TargetOf(second.UnitId) == b, "Stopping one controller preserves another's lease");
        otherController.Dispose();
        Check(scenario.World.ScoutingTargets.TargetOf(second.UnitId) is null, "Controller disposal releases its reservations");
        scouts.Start([first]); scouts.Update(new GameTime(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1)));
        scenario.Remove(first); scouts.Update(new GameTime(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1)));
        Check(scenario.World.ScoutingTargets.TargetOf(first.UnitId) is null && scouts.ActiveScoutCount == 0,
            "Removed scouts free sectors for replacement units");
        Guid owner = Guid.NewGuid(); var targets = scenario.World.ScoutingTargets;
        Check(targets.Claim(second.UnitId, scenario.Army.Id, new(20, 20), owner, 5), "Free sector can be reserved");
        Check(!targets.Claim(Guid.NewGuid(), scenario.Army.Id, new(21, 21), owner, 5), "Same-army scouts cannot claim the same sector");
        Check(targets.Claim(Guid.NewGuid(), Guid.NewGuid(), new(21, 21), owner, 5), "Different armies have independent reservations");
        targets.Clean(18);
        Check(targets.TargetOf(second.UnitId) is null, "Expired lease cannot indefinitely obstruct an abandoned controller");
        targets.Claim(second.UnitId, scenario.Army.Id, new(20, 20), owner, 20);
        targets.Clean(0);
        Check(targets.TargetOf(second.UnitId) is null, "Reset simulation clock clears old-match reservations");
        scenario.World.Visibility.GetGrid(scenario.Army.Id).Reveal(new(32, 32), 100);
        scenario.Network.Update(); scenario.Messages.Clear(); scouts.Start([second]);
        scouts.Update(new GameTime(TimeSpan.FromSeconds(21), TimeSpan.FromSeconds(1))); scenario.Network.Update();
        Check(targets.TargetOf(second.UnitId) is null && !scenario.Messages.Any(message => message.Type == NetworkMessageType.GotoRequest),
            "Fully explored area produces no random scouting order or stale lease");
        return checks;
    }
}


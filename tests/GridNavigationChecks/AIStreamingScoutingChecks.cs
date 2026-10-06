using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    private static int RunStreamingScoutingChecks()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        using var scenario = new Scenario(false);
        var unit = scenario.Add(new Gunner(new(30.5f, 0, 30.5f), scenario.NextId()));
        var search = new ScoutingCandidateSearch();
        using (var work = search.Work(scenario.World, unit, scenario.Army.Id, new(30, 30), _ => false,
            (_, count) => count, new Random(42)).GetEnumerator())
        {
            for (int i = 0; i < 64; i++) CheckStep(work);
            Check(search.ExaminedCells <= 64 && search.Target is null, "A short slice does not synchronously scan or decide the full area");
            Check(search.StoredSectorCount <= 25, "Streaming scan stores sector summaries rather than cell candidates");
            while (work.MoveNext()) { }
        }
        Check(search.Target is Point target && scenario.World.Visibility.GetDisplayedTerrainVisibility(scenario.Army.Id, target, false) == VisibilityState.Unexplored,
            "Completed streaming search selects unknown terrain");
        Check(search.StoredSectorCount <= 25 && search.ExaminedCells > 1000, "Thousands of examined cells retain at most twenty-five summaries on the fixture map");
        Point previous = search.Target!.Value;
        int attempts = 0;
        foreach (int step in search.Work(scenario.World, unit, scenario.Army.Id, new(30, 30), _ => false,
            (_, count) => count, new Random(42), reserve: _ => ++attempts > 1)) { }
        Check(attempts == 2 && ScoutingTargets.Sector(search.Target!.Value) != ScoutingTargets.Sector(previous),
            "Competing reservation selects another retained sector without a cell rescan");
        Check(search.ExaminedCells <= 4225, "Reservation fallback scans the local cells only once");
        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (int step in search.Work(scenario.World, unit, scenario.Army.Id, new(30, 30), _ => false,
            (_, count) => count)) { }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 16384, "Reused full-map candidate scan allocates less than 16 KiB without cell lists");
        foreach (int step in search.Work(scenario.World, unit, scenario.Army.Id, new(30, 30), _ => true, (_, count) => count)) { }
        Check(search.Target is null && search.StoredSectorCount == 0, "Reused search clears summaries and honors excluded sectors");
        using var controller = new ScoutingController(scenario.World, scenario.AI.Id, scenario.Network);
        List<Unit> scouts = [unit];
        for (int i = 0; i < 15; i++) scouts.Add(scenario.Add(new Gunner(new(10.5f + i, 0, 20.5f), scenario.NextId())));
        controller.Start(scouts); controller.Update(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        scenario.World.PathfindingManager.Scheduler.Update(64, double.PositiveInfinity);
        Check(scenario.World.PathfindingManager.Scheduler.LastSteps <= 64 && scenario.World.PathfindingManager.Scheduler.PendingJobs == 16,
            "Sixteen scouts share one bounded planning step budget");
        scenario.Network.Update();
        Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest), "Many scouts do not bypass incremental selection in the think tick");
        controller.Stop(scouts);
        for (int i = 0; i < 10; i++) scenario.World.PathfindingManager.Scheduler.Update(4096, double.PositiveInfinity);
        scenario.Network.Update();
        Check(scenario.World.PathfindingManager.Scheduler.PendingJobs == 0 && !scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest),
            "Stopping many scouts discards searches without stale requests");
        scenario.World.Visibility.GetGrid(scenario.Army.Id).Reveal(new(32, 32), 100);
        foreach (int step in search.Work(scenario.World, unit, scenario.Army.Id, new(30, 30), _ => false, (_, count) => count)) { }
        Check(search.Target is null && search.StoredSectorCount == 0, "Newly explored terrain is not kept in stale sector summaries");
        return checks;
    }
    private static void CheckStep(IEnumerator<int> work)
    {
        if (!work.MoveNext()) throw new Exception("Candidate selection should still be running after a short slice");
    }
}

using System;
using System.Linq;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    private static int RunScoutingReachabilityChecks()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        using var scenario = new Scenario(false);
        var scout = scenario.Add(new Gunner(new(30.5f, 0, 30.5f), scenario.NextId()));
        using var controller = new ScoutingController(scenario.World, scenario.AI.Id, scenario.Network);
        void Tick(double seconds)
        {
            controller.Update(new GameTime(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1)));
            scenario.World.PathfindingManager.Scheduler.Update(100000, double.PositiveInfinity);
            scenario.Network.Update();
        }
        controller.Start([scout]);
        controller.Update(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        Point failed = scenario.World.ScoutingTargets.TargetOf(scout.UnitId)!.Value;
        Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest), "Scouting waits for incremental reachability before issuing Goto");
        scenario.World.GameGrid.GetCell(failed).IsBlocked = true;
        scenario.World.PathfindingManager.Scheduler.Update(100000, double.PositiveInfinity);
        scenario.Network.Update();
        Check(scenario.World.ScoutingTargets.TargetOf(scout.UnitId) is null && !scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest), "Failed reachability releases sector without movement request");
        scenario.World.GameGrid.GetCell(failed).IsBlocked = false;
        Tick(2);
        Point replacement = scenario.World.ScoutingTargets.TargetOf(scout.UnitId)!.Value;
        Check(ScoutingTargets.Sector(replacement) != ScoutingTargets.Sector(failed), "Failed sector remains excluded even after obstacle disappears");
        Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest), "Reachable replacement uses normal network gateway");
        controller.Stop([scout]); scenario.Messages.Clear();
        // Walk-capable infantry cannot reach an unknown area separated by drive-only terrain.
        for (int x = 29; x <= 31; x++)
        for (int y = 29; y <= 31; y++)
            if (x != 30 || y != 30) scenario.World.GameGrid.GetCell(x, y).AllowedMovement = MovementModes.Drive;
        controller.Start([scout]);
        for (int i = 3; i < 20; i++) Tick(i);
        Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest), "Free but disconnected terrain never receives an infantry scouting order");
        Check(scenario.World.ScoutingTargets.TargetOf(scout.UnitId) is null, "Exhausted disconnected sectors do not keep reservations");
        for (int x = 29; x <= 31; x++)
        for (int y = 29; y <= 31; y++) scenario.World.GameGrid.GetCell(x, y).AllowedMovement = MovementModes.All;
        for (int i = 60; i < 65; i++) Tick(i);
        Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest), "Cooldown expires and reopened connected terrain is explored again");
        controller.Stop([scout]); scenario.Messages.Clear(); controller.Start([scout]);
        controller.Update(new GameTime(TimeSpan.FromSeconds(65), TimeSpan.FromSeconds(1)));
        controller.Stop([scout]); scenario.World.PathfindingManager.Scheduler.Update(100000, double.PositiveInfinity); scenario.Network.Update();
        Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest), "Stop cancels pending reachability without late Goto");
        return checks;
    }
}

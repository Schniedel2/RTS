using System;
using System.Linq;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    private static int RunUnitTaskChecks()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        using var scenario = new Scenario(false);
        var unit = scenario.Add(new Gunner(new(30.5f, 0, 30.5f), scenario.NextId()));
        var initial = new AIUnitTasks(scenario.World);
        initial.Claim(unit, "initial", AIUnitTask.Recovery, 80); initial.Update(1);
        Check(initial.Assignment(unit)?.Owner == "initial", "Initial ownership survives the first registry update");
        var tasks = scenario.World.UnitTasks; tasks.Update(1);
        string squadOwner = $"{scenario.Army.Id}:squad", defenderOwner = $"{scenario.Army.Id}:defense";
        var squad = new AIUnitTaskAgent(scenario.World, squadOwner, AIUnitTask.SquadMember, 40);
        var defense = new AIUnitTaskAgent(scenario.World, defenderOwner, AIUnitTask.BaseDefender, 60);
        Check(tasks.Assignment(unit)?.Task == AIUnitTask.Reserve, "Unassigned fighter is visible as reserve");
        Check(squad.Authorize([unit.UnitId]) && tasks.Assignment(unit)?.Task == AIUnitTask.SquadMember, "Squad command claims a central assignment");
        Check(!tasks.CanUse(unit, "other", 40), "Equal priority cannot steal a different controller's unit");
        Check(defense.Authorize([unit.UnitId]) && !squad.CanUse(unit), "Higher priority explicitly preempts a squad assignment");
        var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id, squad);
        _ = commands.GotoAsync([unit.UnitId], new(35.5f, 0, 35.5f)); scenario.Network.Update();
        Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest) && commands.LastRequest?.State == LocalRequestState.Rejected,
            "Losing controller's movement request is rejected locally with a receipt");
        _ = commands.AttackTerrainAsync([unit.UnitId], new(35.5f, 0, 35.5f)); scenario.Network.Update();
        Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.AttackGroundRequest), "Terrain attacks obey task ownership too");
        var defenderCommands = new PlayerCommandService(scenario.Network, scenario.AI.Id, defense);
        _ = defenderCommands.GotoAsync([unit.UnitId], new(34.5f, 0, 34.5f)); scenario.Network.Update();
        Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest), "Current owner still uses ordinary host requests");
        defense.Release(unit.UnitId);
        Check(tasks.Assignment(unit)?.Owner == squadOwner && squad.CanUse(unit), "Release restores the displaced assignment");
        var second = scenario.Add(new Gunner(new(31.5f, 0, 30.5f), scenario.NextId()));
        tasks.Claim(second, "healing", AIUnitTask.Recovery, 80);
        Check(!defense.Authorize([unit.UnitId, second.UnitId]) && tasks.Assignment(unit)?.Owner == squadOwner,
            "Mixed recipients are claimed atomically without partially stealing a group");
        tasks.Update(17);
        Check(tasks.Assignment(unit)?.Task == AIUnitTask.Reserve && tasks.Assignment(second)?.Task == AIUnitTask.Reserve,
            "Abandoned explicit assignments expire");
        tasks.Claim(unit, squadOwner, AIUnitTask.SquadMember, 40); unit.SetArmy(Guid.NewGuid()); tasks.Update(18);
        Check(tasks.Assignment(unit)?.Task == AIUnitTask.Reserve, "Army transfer drops stale ownership");
        unit.SetArmy(scenario.Army.Id); tasks.Claim(unit, squadOwner, AIUnitTask.SquadMember, 40); tasks.Update(0);
        Check(tasks.Assignment(unit)?.Task == AIUnitTask.Reserve, "Simulation reset removes explicit tasks");
        using var scout = new ScoutingController(scenario.World, scenario.AI.Id, scenario.Network);
        scout.Start([unit]); scout.Update(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        Check(tasks.Assignment(unit)?.Task == AIUnitTask.Scout, "Automatic scouting marks the unit centrally");
        scout.Stop([unit]);
        Check(tasks.Assignment(unit)?.Task == AIUnitTask.Reserve, "Stop scouting releases its assignment");
        tasks.Claim(unit, "healing", AIUnitTask.Recovery, 80);
        scenario.AddBuilding("gdi-base", new(28.5f, 0, 28.5f));
        var enemy = scenario.AddVisibleEnemy(new(32.5f, 0, 32.5f));
        var controller = new AIBaseDefenseController(scenario.World, scenario.AI.Id, scenario.Army.Id, scenario.Network);
        controller.Update(new GameTime(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)), null); scenario.Network.Update();
        Check(!controller.AssignedDefenders.Contains(unit.UnitId) && tasks.Assignment(unit)?.Task == AIUnitTask.Recovery,
            "Base defense does not override higher-priority recovery");
        var leader = scenario.Add(new SquadLeader(new(35.5f, 0, 30.5f), scenario.NextId(), loadModel: false));
        scenario.ConfirmSquad(leader, [second], UnitActionType.AssembleSquad);
        Check(tasks.Assignment(second)?.Task == AIUnitTask.SquadMember, "Host-confirmed squad membership supplies persistent fallback ownership");
        Check(defense.Authorize([leader.UnitId]) && tasks.Assignment(second)?.Task == AIUnitTask.BaseDefender,
            "Priority takeover of a leader claims the living squad as an atomic group");
        Check(!squad.Authorize([second.UnitId]), "Preempted follower cannot restart an offensive command independently");
        defense.Release(leader.UnitId);
        Check(tasks.Assignment(second)?.Task == AIUnitTask.SquadMember && squad.CanUse(second),
            "Releasing a leader also restores its displaced living members");
        tasks.Claim(unit, "temporary", AIUnitTask.Escort, 50); scenario.Remove(unit); tasks.Update(3);
        Check(!tasks.Owns(unit, "temporary"), "Removed units cannot keep controller assignments");
        return checks;
    }
}

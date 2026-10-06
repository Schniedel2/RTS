using System;
using System.Linq;
using System.Collections.Generic;
namespace RTS;

/// <summary>Shared CommandCenter goal actions for graphical and dedicated hosts.</summary>
public static class ManualArmyGoals
{
    public static bool Apply(CommandCenter commandCenter, UnitActionType actionType,
        GameWorld World, Network.NetworkHandler Network, ArmyHandler Armies, IReadOnlyList<Player> _players,
        Dictionary<Guid, (Player Actor, ArmyGoalController Controller)> _manualArmyGoals,
        Dictionary<Guid, ScoutingController> _manualArmyScouts)
    {
        if (!Network.IsHost || !commandCenter.IsOperational || commandCenter.ArmyId is not Guid armyId ||
            Armies.Find(armyId) is not Army army)
            return false;

        if (actionType == UnitActionType.AIStopGoals)
        {
            if (_manualArmyGoals.Remove(armyId, out var running))
                running.Controller.Stop();
            _manualArmyScouts.Remove(armyId);
            return true;
        }

        Player? actor = _players.FirstOrDefault(player => army.OwnerPlayerIds.Contains(player.Id));
        if (actor is null)
            return false;
        if (actionType == UnitActionType.AIStartScouting)
        {
            ScoutingController scouting = new(World, actor.Id);
            scouting.Start(World.Units.Units.Where(unit => unit.ArmyId == armyId));
            _manualArmyScouts[armyId] = scouting;
            return true;
        }

        AIArmyGoal goal = actionType switch
        {
            UnitActionType.AIStartReactor => AIArmyGoal.BuildReactor,
            UnitActionType.AIStartRefinery => AIArmyGoal.BuildRefinery,
            UnitActionType.AIStartEconomy => AIArmyGoal.EstablishEconomy,
            _ => AIArmyGoal.None
        };
        if (goal == AIArmyGoal.None)
            return false;

        if (!_manualArmyGoals.TryGetValue(armyId, out var runningGoal))
            runningGoal = (actor, new ArmyGoalController());
        runningGoal.Controller.Start(goal);
        _manualArmyGoals[armyId] = runningGoal;
        return true;
    }

}

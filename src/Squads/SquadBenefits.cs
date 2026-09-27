using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace RTS;

/// <summary>Small passive bonuses granted while a squad remains near its living leader.</summary>
public static class SquadBenefits
{
    public const float CohesionRadiusInCells = 12.0f;
    public const float OutgoingDamageMultiplier = 1.10f;
    public const float IncomingDamageMultiplier = 0.90f;

    public static bool HasCohesion(Unit unit, GameWorld world)
    {
        if (unit is not Soldier soldier || soldier.IsDying || soldier.IsEmbarked ||
            soldier.ArmyId is not Guid armyId)
            return false;

        float radius = CohesionRadiusInCells * world.GameGrid.CellSize;
        float radiusSquared = radius * radius;
        if (soldier is SquadLeader leader)
        {
            return world.Units.Units.OfType<Soldier>().Any(member =>
                member != leader && member.SquadLeaderId == leader.UnitId &&
                !member.IsDying && !member.IsEmbarked && member.ArmyId == armyId &&
                HorizontalDistanceSquared(member.Position, leader.Position) <= radiusSquared);
        }

        return soldier.SquadLeaderId is Guid leaderId &&
            world.Units.FindById(leaderId) is SquadLeader squadLeader &&
            !squadLeader.IsDying && !squadLeader.IsEmbarked && squadLeader.ArmyId == armyId &&
            HorizontalDistanceSquared(soldier.Position, squadLeader.Position) <= radiusSquared;
    }

    public static float ApplyCombatModifiers(
        GameWorld world,
        Unit? attacker,
        Unit target,
        float damage)
    {
        if (!float.IsFinite(damage) || damage <= 0.0f)
            return 0.0f;
        if (attacker is not null && HasCohesion(attacker, world))
            damage *= OutgoingDamageMultiplier;
        if (HasCohesion(target, world))
            damage *= IncomingDamageMultiplier;
        return damage;
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float x = first.X - second.X;
        float z = first.Z - second.Z;
        return x * x + z * z;
    }
}

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public sealed record AIDefenseForce(Unit[] Defenders, float RequiredPower, float AssignedPower);

/// <summary>Small deterministic combat estimate, using actual weapon/armor rules rather than unit classes.</summary>
public static class AIDefenseForceSelector
{
    public static AIDefenseForce Select(IEnumerable<Unit> candidates, IReadOnlyList<Unit> threats,
        Unit target, float cellSize, IReadOnlyCollection<Guid>? alreadyAssigned = null)
    {
        float required = 0;
        foreach (Unit threat in threats)
        {
            float damage = threat.CanFireWeapon ? DamageCalculator.Calculate(threat.AttackDamage,
                threat.AttackDamageType, ArmorClass.Infantry) / Math.Max(0.1f, threat.AttackCooldown) : 0;
            required += Math.Max(0, threat.HitPoints) / 10f + damage * 0.5f;
        }
        required = Math.Max(10, required);
        var ranked = candidates.Where(unit => unit.CanFireWeapon && !unit.IsDying && !unit.IsEmbarked && unit.Occupancy?.IsOperational != false && unit.CanAttackTarget(target) && unit.HitPoints > 0)
            .Select(unit => new { Unit = unit, Power = Power(unit, target), Score = Priority(unit, target, cellSize,
                alreadyAssigned?.Contains(unit.UnitId) == true) })
            .Where(candidate => candidate.Power > 0)
            .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Unit.UnitId);
        List<Unit> selected = [];
        float assigned = 0;
        foreach (var candidate in ranked)
        {
            selected.Add(candidate.Unit); assigned += candidate.Power;
            if (assigned >= required) break;
        }
        return new(selected.OrderBy(unit => unit.UnitId).ToArray(), required, assigned);
    }

    private static float Power(Unit unit, Unit target) => DamageCalculator.Calculate(unit.AttackDamage,
        unit.AttackDamageType, target.Armor) / Math.Max(0.1f, unit.AttackCooldown) *
        Math.Clamp(unit.HitPoints / Math.Max(1, unit.MaxHitPoints), 0.1f, 1);

    private static float Priority(Unit unit, Unit target, float cellSize, bool retained)
    {
        Vector2 delta = new(unit.Position.X - target.Position.X, unit.Position.Z - target.Position.Z);
        float approach = Math.Max(0, delta.Length() - unit.AttackRange);
        float retaliation = target.CanAttackDomain(unit.Domain) && target.CanFireWeapon
            ? DamageCalculator.Calculate(target.AttackDamage, target.AttackDamageType, unit.Armor) : 0;
        float safety = 1f / (1f + retaliation / Math.Max(1, unit.HitPoints));
        return Power(unit, target) * safety / (1 + approach / Math.Max(1, cellSize * 24)) * (retained ? 1.15f : 1);
    }
}

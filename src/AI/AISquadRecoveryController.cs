using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Linq;

namespace RTS;

/// <summary>Temporarily disbands a returned squad so its free medic can heal nearby survivors.</summary>
public sealed class AISquadRecoveryController(
    GameWorld world,
    Guid playerId,
    Guid armyId,
    NetworkHandler network)
{
    public const float RequiredAverageHealthFraction = 0.80f;
    private readonly PlayerCommandService _commands = new(network, playerId);
    private Guid[] _soldierIds = [];
    private Guid? _leaderId;

    public bool IsActive { get; private set; }
    public bool IsRecovered { get; private set; }
    public float AverageHealthFraction { get; private set; }
    public string LastDecision { get; private set; } = "Waiting for a returned squad.";

    public bool Begin()
    {
        SquadLeader? leader = world.Units.GetArmyUnits(armyId).OfType<SquadLeader>()
            .Where(unit => unit.ArmyId == armyId && !unit.IsDying && !unit.IsEmbarked)
            .OrderByDescending(unit => world.Units.Units.OfType<Soldier>()
                .Count(member => member.SquadLeaderId == unit.UnitId && !member.IsDying))
            .FirstOrDefault();
        if (leader is null)
            return false;

        Soldier[] members = world.Units.GetArmyUnits(armyId).OfType<Soldier>()
            .Where(member => member.SquadLeaderId == leader.UnitId && member.ArmyId == armyId &&
                !member.IsDying && !member.IsEmbarked)
            .ToArray();
        _soldierIds = [leader.UnitId, .. members.Select(member => member.UnitId)];
        _leaderId = leader.UnitId;
        IsActive = true;
        IsRecovered = false;
        AverageHealthFraction = CalculateAverageHealth();
        LastDecision = $"Returned squad is recovering ({AverageHealthFraction * 100.0f:0}% health).";
        _ = _commands.ExecuteActionAsync([leader.UnitId], UnitActionType.DisbandSquad,
            new UnitActionContext(TargetUnitId: leader.UnitId));
        return true;
    }

    public void Update()
    {
        using var measurement = PerformanceMeasurements.Measure("AI.SquadRecovery");
        if (!IsActive || IsRecovered)
            return;
        AverageHealthFraction = CalculateAverageHealth();
        Soldier[] survivors = GetSurvivors();
        bool hasMedic = survivors.Any(member => HasRole(member, AIUnitRole.Healer));
        bool disbandConfirmed = survivors
            .Where(unit => unit.UnitId != _leaderId)
            .All(unit => unit.SquadLeaderId is null);
        IsRecovered = disbandConfirmed && hasMedic &&
            AverageHealthFraction >= RequiredAverageHealthFraction;
        LastDecision = !disbandConfirmed
            ? "Waiting for host-confirmed squad recovery formation."
            : IsRecovered
                ? $"Squad recovery complete at {AverageHealthFraction * 100.0f:0}% health."
                : $"Medic is treating the returned squad ({AverageHealthFraction * 100.0f:0}% health).";
    }

    public void Reset()
    {
        IsActive = false;
        IsRecovered = false;
        AverageHealthFraction = 0.0f;
        _soldierIds = [];
        _leaderId = null;
        LastDecision = "Waiting for a returned squad.";
    }

    private Soldier[] GetSurvivors() => _soldierIds
        .Select(world.Units.FindById)
        .OfType<Soldier>()
        .Where(unit => unit.ArmyId == armyId && !unit.IsDying && !unit.IsEmbarked && unit.HitPoints > 0.0f)
        .ToArray();

    private float CalculateAverageHealth()
    {
        Soldier[] survivors = GetSurvivors();
        return survivors.Length == 0
            ? 0.0f
            : survivors.Average(unit => unit.HitPoints / Math.Max(1.0f, unit.MaxHitPoints));
    }

    private static bool HasRole(Unit unit, AIUnitRole roles) =>
        GameplayCatalog.Find(PurchasableType.Unit, unit.GameplayTypeId)?.AI is AIUnitMetadata ai &&
        (ai.Roles & roles) == roles;
}

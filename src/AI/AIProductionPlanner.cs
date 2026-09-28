using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public enum AIProductionPlanStepKind
{
    BuildBuilding,
    TrainUnit,
    Research,
    AssignCrew
}

public sealed record AIProductionPlanStep(
    AIProductionPlanStepKind Kind,
    string TypeId,
    string ProducerTypeId,
    Guid? UnitId = null,
    Guid? TargetUnitId = null,
    OccupantRole? OccupantRole = null)
{
    public static AIProductionPlanStep AssignCrew(Guid unitId, Guid targetUnitId,
        OccupantRole role = RTS.OccupantRole.Crew) =>
        new(AIProductionPlanStepKind.AssignCrew, string.Empty, string.Empty,
            unitId, targetUnitId, role);
}

public sealed record AIProductionPlan(
    IReadOnlyList<AIProductionPlanStep> Steps,
    string? Failure = null)
{
    public bool IsValid => string.IsNullOrWhiteSpace(Failure);
    public AIProductionPlanStep? NextStep => Steps.FirstOrDefault();
}

/// <summary>Resolves catalog prerequisites for one desired building, unit or research project.</summary>
public static class AIProductionPlanner
{
    public const int DefaultPowerHeadroom = 10;

    public static AIProductionPlan CreatePlan(
        GameplayDefinition target,
        IEnumerable<string> ownedTypeIds,
        IEnumerable<PerkType> activePerks,
        int currentPowerBalance,
        int powerHeadroom = DefaultPowerHeadroom)
    {
        var state = new PlanningState(ownedTypeIds, activePerks, currentPowerBalance,
            Math.Max(0, powerHeadroom));
        if (!Resolve(target, state, dependencyOnly: false, []))
            return new AIProductionPlan(state.Steps, state.Failure ?? "Unknown dependency failure.");
        return new AIProductionPlan(state.Steps);
    }

    private static bool Resolve(GameplayDefinition definition, PlanningState state,
        bool dependencyOnly, HashSet<string> chain)
    {
        string key = $"{definition.Type}:{definition.TypeId}";
        if (dependencyOnly && (state.Owned.Contains(definition.TypeId) || state.Planned.Contains(key)))
            return true;
        if (!chain.Add(key))
            return state.Fail($"Circular production dependency at {definition.TypeId}.");

        foreach (PerkType perk in definition.RequiredPerks.Where(perk => !state.Perks.Contains(perk)))
        {
            GameplayDefinition? research = GameplayCatalog.All.FirstOrDefault(candidate =>
                candidate.Type == PurchasableType.Research && candidate.GrantedPerk == perk);
            if (research is null)
                return state.Fail($"No research grants required perk {perk} for {definition.TypeId}.");
            if (!Resolve(research, state, dependencyOnly: true, new HashSet<string>(chain)))
                return false;
            state.Perks.Add(perk);
        }

        ProducerDefinition? producer = definition.Producers.FirstOrDefault(candidate =>
            state.Owned.Contains(candidate.TypeId));
        producer ??= definition.Producers.FirstOrDefault();
        if (producer is null)
            return state.Fail($"No producer is registered for {definition.TypeId}.");

        if (!state.Owned.Contains(producer.TypeId))
        {
            GameplayDefinition? producerDefinition = FindProductDefinition(producer.TypeId);
            if (producerDefinition is null)
                return state.Fail($"Producer {producer.TypeId} for {definition.TypeId} is unknown.");
            if (!Resolve(producerDefinition, state, dependencyOnly: true,
                    new HashSet<string>(chain)))
                return false;
        }

        if (definition.Type == PurchasableType.Building &&
            definition.Building is BuildingMetadata building &&
            building.PowerConsumption > 0 &&
            state.PowerBalance < building.PowerConsumption + state.PowerHeadroom)
        {
            GameplayDefinition? powerPlant = GameplayCatalog.All
                .Where(candidate => candidate.Type == PurchasableType.Building &&
                    candidate.Building?.PowerProduction > 0)
                .OrderByDescending(candidate => candidate.Building!.PowerProduction)
                .ThenBy(candidate => candidate.BasePrice)
                .FirstOrDefault();
            if (powerPlant is null || string.Equals(powerPlant.TypeId, definition.TypeId,
                    StringComparison.OrdinalIgnoreCase))
                return state.Fail($"No power producer can supply {definition.TypeId}.");
            int guard = 0;
            while (state.PowerBalance < building.PowerConsumption + state.PowerHeadroom && guard++ < 8)
            {
                if (!Resolve(powerPlant, state, dependencyOnly: false,
                        new HashSet<string>(chain)))
                    return false;
            }
            if (state.PowerBalance < building.PowerConsumption + state.PowerHeadroom)
                return state.Fail($"Power dependency for {definition.TypeId} could not be satisfied.");
        }

        state.Steps.Add(new AIProductionPlanStep(
            definition.Type switch
            {
                PurchasableType.Building => AIProductionPlanStepKind.BuildBuilding,
                PurchasableType.Unit => AIProductionPlanStepKind.TrainUnit,
                PurchasableType.Research => AIProductionPlanStepKind.Research,
                _ => throw new ArgumentOutOfRangeException()
            },
            definition.TypeId,
            producer.TypeId));
        state.Planned.Add(key);
        if (definition.Type == PurchasableType.Building && definition.Building is BuildingMetadata stats)
        {
            state.PowerBalance += stats.PowerProduction - stats.PowerConsumption;
            state.Owned.Add(definition.TypeId);
        }
        else if (definition.GrantedPerk is PerkType granted)
        {
            state.Perks.Add(granted);
        }
        return true;
    }

    private static GameplayDefinition? FindProductDefinition(string typeId) =>
        GameplayCatalog.Find(PurchasableType.Building, typeId) ??
        GameplayCatalog.Find(PurchasableType.Unit, typeId);

    private sealed class PlanningState(
        IEnumerable<string> ownedTypeIds,
        IEnumerable<PerkType> activePerks,
        int powerBalance,
        int powerHeadroom)
    {
        public HashSet<string> Owned { get; } = new(
            ownedTypeIds.Select(GameplayCatalog.Canonicalize), StringComparer.OrdinalIgnoreCase);
        public HashSet<PerkType> Perks { get; } = [.. activePerks];
        public int PowerBalance { get; set; } = powerBalance;
        public int PowerHeadroom { get; } = powerHeadroom;
        public List<AIProductionPlanStep> Steps { get; } = [];
        public HashSet<string> Planned { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Failure { get; private set; }

        public bool Fail(string message)
        {
            Failure = message;
            return false;
        }
    }
}

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
            GameplayDefinition[] providers = GameplayCatalog.All.Where(candidate =>
                candidate.Type == PurchasableType.Research && candidate.GrantedPerk == perk ||
                candidate.Type == PurchasableType.Building && candidate.ProvidedPerks?.Contains(perk) == true)
                .OrderBy(candidate => candidate.BasePrice).ThenBy(candidate => candidate.TypeId, StringComparer.Ordinal).ToArray();
            if (!TryResolveChoice(providers, state, chain, dependencyOnly: true))
                return state.Fail($"No feasible provider grants required perk {perk} for {definition.TypeId}.");
            state.Perks.Add(perk);
        }

        ProducerDefinition? producer = null;
        foreach (ProducerDefinition option in definition.Producers.OrderByDescending(candidate => state.Owned.Contains(candidate.TypeId))
            .ThenBy(candidate => candidate.ProductionSeconds).ThenBy(candidate => candidate.TypeId, StringComparer.Ordinal))
        {
            if (state.Owned.Contains(option.TypeId)) { producer = option; break; }
            GameplayDefinition? producerDefinition = FindProductDefinition(option.TypeId);
            if (producerDefinition is not null && TryResolveChoice([producerDefinition], state, chain, dependencyOnly: true))
            { producer = option; break; }
        }
        if (producer is null) return state.Fail($"No feasible producer is registered for {definition.TypeId}.");

        if (definition.Type == PurchasableType.Building &&
            definition.Building is BuildingMetadata building &&
            building.PowerConsumption > 0 &&
            definition.ProvidedPerks?.Contains(PerkType.BaseEstablished) != true &&
            state.PowerBalance < building.PowerConsumption + state.PowerHeadroom)
        {
            GameplayDefinition[] plants = GameplayCatalog.All
                .Where(candidate => candidate.Type == PurchasableType.Building && candidate.Building is BuildingMetadata stats &&
                    stats.PowerProduction > stats.PowerConsumption && candidate.TypeId != definition.TypeId)
                .OrderByDescending(candidate => candidate.Building!.PowerProduction - candidate.Building.PowerConsumption)
                .ThenBy(candidate => candidate.BasePrice).ThenBy(candidate => candidate.TypeId, StringComparer.Ordinal).ToArray();
            int guard = 0;
            while (state.PowerBalance < building.PowerConsumption + state.PowerHeadroom && guard++ < 8)
                if (!TryResolveChoice(plants, state, chain, dependencyOnly: false))
                    return state.Fail($"No feasible power producer can supply {definition.TypeId}.");
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
            foreach (PerkType provided in definition.ProvidedPerks ?? []) state.Perks.Add(provided);
        }
        else if (definition.GrantedPerk is PerkType granted)
        {
            state.Perks.Add(granted);
        }
        return true;
    }

    private static bool TryResolveChoice(IEnumerable<GameplayDefinition> choices, PlanningState state,
        HashSet<string> chain, bool dependencyOnly)
    {
        foreach (GameplayDefinition choice in choices)
        {
            var branch = new PlanningState(state.Owned, state.Perks, state.PowerBalance, state.PowerHeadroom);
            branch.Steps.AddRange(state.Steps); branch.Planned.UnionWith(state.Planned);
            if (!Resolve(choice, branch, dependencyOnly, new HashSet<string>(chain))) continue;
            state.Owned.Clear(); state.Owned.UnionWith(branch.Owned);
            state.Perks.Clear(); state.Perks.UnionWith(branch.Perks);
            state.Steps.Clear(); state.Steps.AddRange(branch.Steps);
            state.Planned.Clear(); state.Planned.UnionWith(branch.Planned);
            state.PowerBalance = branch.PowerBalance;
            return true;
        }
        return false;
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

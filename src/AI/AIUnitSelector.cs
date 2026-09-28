using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

/// <summary>Weights describing what a production decision should contribute right now.</summary>
public sealed record AIProductionNeed(
    AIUnitRole RequiredRoles,
    AIMovementDomain? Movement = null,
    float AntiInfantry = 0.0f,
    float AntiVehicle = 0.0f,
    float AntiBuilding = 0.0f,
    float AntiAir = 0.0f,
    float Defense = 0.0f,
    float Mobility = 0.0f,
    float Scouting = 0.0f);

/// <summary>Selects a producible unit from shared gameplay metadata.</summary>
public static class AIUnitSelector
{
    public static GameplayDefinition? SelectBest(
        string producerTypeId,
        AIProductionNeed need,
        IReadOnlyDictionary<string, int>? currentCounts = null,
        PurchasableType productType = PurchasableType.Unit)
    {
        return GameplayCatalog.GetProducedBy(producerTypeId, productType)
            .Where(definition => IsCandidate(definition.AI, need))
            .Select(definition => (Definition: definition,
                Score: Score(definition, need, currentCounts)))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Definition.BasePrice)
            .ThenBy(candidate => candidate.Definition.TypeId, StringComparer.Ordinal)
            .Select(candidate => candidate.Definition)
            .FirstOrDefault();
    }

    public static float Score(GameplayDefinition definition, AIProductionNeed need,
        IReadOnlyDictionary<string, int>? currentCounts = null)
    {
        AIUnitMetadata? ai = definition.AI;
        if (!IsCandidate(ai, need))
            return float.NegativeInfinity;

        float score =
            ai!.AntiInfantry * need.AntiInfantry +
            ai.AntiVehicle * need.AntiVehicle +
            ai.AntiBuilding * need.AntiBuilding +
            ai.AntiAir * need.AntiAir +
            ai.Defense * need.Defense +
            ai.Mobility * need.Mobility +
            ai.Scouting * need.Scouting;

        int count = currentCounts?.GetValueOrDefault(definition.TypeId) ?? 0;
        if (ai.PreferredMaximumCount > 0 && count >= ai.PreferredMaximumCount)
            score -= 1000.0f;
        return score;
    }

    private static bool IsCandidate(AIUnitMetadata? ai, AIProductionNeed need) =>
        ai is not null &&
        (need.Movement is null || ai.Movement == need.Movement) &&
        (ai.Roles & need.RequiredRoles) == need.RequiredRoles;
}

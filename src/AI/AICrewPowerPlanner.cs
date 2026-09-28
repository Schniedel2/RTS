using System;
using System.Linq;

namespace RTS;

public enum AIPowerSolutionKind
{
    None,
    AssignCrew,
    WaitForCrew,
    TrainCrew,
    BuildPower
}

public sealed record AIPowerSolution(
    AIPowerSolutionKind Kind,
    Guid? CrewUnitId = null,
    Guid? TargetBuildingId = null,
    Guid? ProducerBuildingId = null,
    string? CrewTypeId = null,
    int PowerGain = 0);

/// <summary>Chooses between available crew, training one crew member and a new power plant.</summary>
public static class AICrewPowerPlanner
{
    public static AIPowerSolution Evaluate(GameWorld world, Guid armyId, int requiredAdditionalPower)
    {
        if (requiredAdditionalPower <= 0)
            return new(AIPowerSolutionKind.None);

        Reaktor[] reactors = world.Units.Units.OfType<Reaktor>()
            .Where(reactor => reactor.ArmyId == armyId && reactor.IsCompleted && !reactor.IsDying)
            .OrderBy(reactor => reactor.UnitId)
            .ToArray();
        BuildingMetadata? reactorMetadata = GameplayCatalog.Find(
            PurchasableType.Building, "reaktor")?.Building;
        int bonus = reactorMetadata?.PowerProductionPerCrew ?? 0;
        int capacity = reactorMetadata?.CrewCapacity ?? 0;
        if (bonus <= 0 || capacity <= 0 || reactors.Length == 0)
            return new(AIPowerSolutionKind.BuildPower);

        if (world.Units.Units.OfType<MobileUnit>().Any(unit =>
                unit.ArmyId == armyId && unit.PendingEnterContainerId is Guid targetId &&
                reactors.Any(reactor => reactor.UnitId == targetId) &&
                GameplayCatalog.HasAIRoles(unit.GameplayTypeId, AIUnitRole.Crew)))
        {
            return new(AIPowerSolutionKind.WaitForCrew, PowerGain: bonus);
        }

        MobileUnit? availableCrew = world.Units.Units.OfType<MobileUnit>()
            .Where(unit => unit.ArmyId == armyId && !unit.IsDying && !unit.IsEmbarked &&
                unit.PendingEnterContainerId is null &&
                GameplayCatalog.HasAIRoles(unit.GameplayTypeId, AIUnitRole.Crew))
            .OrderBy(unit => unit.UnitId)
            .FirstOrDefault();
        if (availableCrew is not null)
        {
            Reaktor? target = reactors.FirstOrDefault(reactor =>
                reactor.Occupancy is not null &&
                reactor.Occupancy.Count(OccupantRole.Crew) < capacity &&
                reactor.Occupancy.CanEnter(availableCrew, OccupantRole.Crew));
            if (target is not null)
                return new(AIPowerSolutionKind.AssignCrew, availableCrew.UnitId, target.UnitId,
                    PowerGain: bonus);
        }

        bool hasFreeSlot = reactors.Any(reactor =>
            (reactor.Occupancy?.Count(OccupantRole.Crew) ?? capacity) < capacity);
        if (requiredAdditionalPower <= bonus && hasFreeSlot)
        {
            GDIBarracks? barracks = world.Units.Units.OfType<GDIBarracks>()
                .Where(building => building.ArmyId == armyId && building.IsCompleted && !building.IsDying)
                .OrderBy(building => building.UnitId)
                .FirstOrDefault();
            if (barracks is not null)
            {
                GameplayDefinition? crewDefinition = AIUnitSelector.SelectBest(
                    barracks.GameplayTypeId,
                    new AIProductionNeed(AIUnitRole.Crew, AIMovementDomain.Infantry));
                if (crewDefinition is null)
                    return new(AIPowerSolutionKind.BuildPower);
                bool queued = barracks.ProductionQueue.Orders.Any(order =>
                    GameplayCatalog.HasAIRoles(order.UnitTypeId, AIUnitRole.Crew));
                return queued
                    ? new(AIPowerSolutionKind.WaitForCrew, ProducerBuildingId: barracks.UnitId,
                        CrewTypeId: crewDefinition.TypeId, PowerGain: bonus)
                    : new(AIPowerSolutionKind.TrainCrew, ProducerBuildingId: barracks.UnitId,
                        CrewTypeId: crewDefinition.TypeId, PowerGain: bonus);
            }
        }

        return new(AIPowerSolutionKind.BuildPower);
    }
}

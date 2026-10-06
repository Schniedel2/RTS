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

        Building[] producers = world.Units.GetArmyUnits(armyId).OfType<Building>()
            .Where(b => b.ArmyId == armyId && b.IsCompleted && !b.IsDying &&
                Metadata(b) is { PowerProductionPerCrew: > 0, CrewCapacity: > 0 })
            .OrderByDescending(b => Metadata(b)!.PowerProductionPerCrew).ThenBy(b => b.UnitId).ToArray();
        if (producers.Length == 0) return new(AIPowerSolutionKind.BuildPower);

        foreach (MobileUnit crew in world.Units.GetArmyUnits(armyId).OfType<MobileUnit>().Where(u =>
            u.ArmyId == armyId && !u.IsDying && GameplayCatalog.HasAIRoles(u.GameplayTypeId, AIUnitRole.Crew)))
        {
            Building? pending = producers.FirstOrDefault(b => b.UnitId == crew.PendingEnterContainerId);
            if (pending is not null)
                return new(AIPowerSolutionKind.WaitForCrew, PowerGain: Metadata(pending)!.PowerProductionPerCrew);
            if (crew.IsEmbarked || crew.PendingEnterContainerId is not null) continue;
            Building? target = producers.FirstOrDefault(b => HasFreeSlot(b) &&
                b.Occupancy?.CanEnter(crew, OccupantRole.Crew) == true);
            if (target is not null)
                return new(AIPowerSolutionKind.AssignCrew, crew.UnitId, target.UnitId,
                    PowerGain: Metadata(target)!.PowerProductionPerCrew);
        }

        Building? freeTarget = producers.FirstOrDefault(b => HasFreeSlot(b) &&
            Metadata(b)!.PowerProductionPerCrew >= requiredAdditionalPower);
        if (freeTarget is null) return new(AIPowerSolutionKind.BuildPower);
        int bonus = Metadata(freeTarget)!.PowerProductionPerCrew;
        foreach (Building trainer in world.Units.GetArmyUnits(armyId).OfType<Building>().Where(b =>
            b.ArmyId == armyId && b.IsCompleted && !b.IsDying).OrderBy(b => b.UnitId))
        {
            GameplayDefinition? offer = GameplayCatalog.GetProducedBy(trainer.GameplayTypeId)
                .Where(d => d.Type == PurchasableType.Unit && d.AI?.Roles.HasFlag(AIUnitRole.Crew) == true &&
                    (AIStrategicCatalog.HasQueuedProduct(trainer, d.TypeId) || trainer.CanProduceUnit(world, d.TypeId)) &&
                    world.SimulationPricing.GetQuote(new(PurchasableType.Unit, d.TypeId, armyId, trainer.UnitId)).IsAvailable)
                .OrderBy(d => world.SimulationPricing.GetQuote(new(PurchasableType.Unit, d.TypeId, armyId, trainer.UnitId)).FinalPrice)
                .ThenBy(d => d.TypeId, StringComparer.Ordinal).FirstOrDefault();
            if (offer is null) continue;
            bool queued = trainer.ProductionQueue.Orders.Any(order =>
                GameplayCatalog.HasAIRoles(order.UnitTypeId, AIUnitRole.Crew));
            return new(queued ? AIPowerSolutionKind.WaitForCrew : AIPowerSolutionKind.TrainCrew,
                ProducerBuildingId: trainer.UnitId, CrewTypeId: offer.TypeId, PowerGain: bonus);
        }
        return new(AIPowerSolutionKind.BuildPower);
    }

    private static BuildingMetadata? Metadata(Building building) =>
        GameplayCatalog.Find(PurchasableType.Building, building.GameplayTypeId)?.Building;
    private static bool HasFreeSlot(Building building) => building.Occupancy is not null &&
        building.Occupancy.Count(OccupantRole.Crew) < Metadata(building)!.CrewCapacity;
}

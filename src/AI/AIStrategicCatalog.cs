using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public enum AIStrategicBuildingNeed { Base, Power, Storage, Vision, Economy, InfantryProduction, ArmoredProduction }

/// <summary>Strategic needs select feasible catalog offers, using the same quotes as human players.</summary>
public static class AIStrategicCatalog
{
    public static bool Matches(GameplayDefinition definition, AIStrategicBuildingNeed need)
    {
        if (definition.Type != PurchasableType.Building || definition.Building is not BuildingMetadata stats) return false;
        return need switch
        {
            AIStrategicBuildingNeed.Base => definition.ProvidedPerks?.Contains(PerkType.BaseEstablished) == true,
            AIStrategicBuildingNeed.Power => stats.PowerProduction > stats.PowerConsumption,
            AIStrategicBuildingNeed.Storage => stats.ResourceCapacity > 0,
            AIStrategicBuildingNeed.Vision => stats.VisionRange > 0,
            AIStrategicBuildingNeed.Economy => stats.ResourceCapacity > 0 && ProducesRole(definition.TypeId, AIUnitRole.Harvester),
            AIStrategicBuildingNeed.InfantryProduction => ProducesRole(definition.TypeId, AIUnitRole.Defender, AIMovementDomain.Infantry),
            AIStrategicBuildingNeed.ArmoredProduction => ProducesRole(definition.TypeId, AIUnitRole.Attacker, AIMovementDomain.GroundVehicle),
            _ => false
        };
    }
    private static bool ProducesRole(string producer, AIUnitRole role, AIMovementDomain? movement = null) =>
        GameplayCatalog.GetProducedBy(producer).Any(product => product.AI is AIUnitMetadata ai &&
            (ai.Roles & role) == role && (movement is null || ai.Movement == movement));

    public static Building? FindBuilding(GameWorld world, Guid armyId, AIStrategicBuildingNeed need, Guid? preferredId = null)
    {
        bool Eligible(Building b) => b.ArmyId == armyId && !b.IsDying &&
            GameplayCatalog.Find(PurchasableType.Building, b.GameplayTypeId) is GameplayDefinition d && Matches(d, need);
        if (preferredId is Guid id && world.Units.FindById(id) is Building preferred && Eligible(preferred)) return preferred;
        return world.Units.Units.OfType<Building>().Where(Eligible).OrderByDescending(b => b.IsCompleted).ThenBy(b => b.UnitId).FirstOrDefault();
    }

    public static MobileUnit? FindBuilder(GameWorld world, Guid armyId, string? productId = null) =>
        world.Units.GetArmyUnits(armyId).OfType<MobileUnit>().Where(unit => unit.ArmyId == armyId && !unit.IsDying &&
            !unit.IsEmbarked && !unit.IsLeavingBuilding && unit.BuildRate > 0 && unit.Occupancy?.IsOperational != false &&
            GameplayCatalog.HasAIRoles(unit.GameplayTypeId, AIUnitRole.Builder) &&
            (productId is null || GameplayCatalog.Find(PurchasableType.Building, productId)?.Producers.Any(p =>
                GameplayCatalog.Canonicalize(p.TypeId) == GameplayCatalog.Canonicalize(unit.GameplayTypeId)) == true))
            .OrderBy(unit => world.AIOrderQueues.TryGetValue(armyId, out var queue) && !queue.CanUse(unit.UnitId))
            .ThenBy(unit => unit.IsBuilding || unit.TargetBuildingId is not null).ThenBy(unit => unit.UnitId).FirstOrDefault();

    public static GameplayDefinition? SelectBuilding(GameWorld world, Guid armyId, AIStrategicBuildingNeed need, bool requireAvailable = false) =>
        Select(world, armyId, GameplayCatalog.All.Where(d => Matches(d, need) && (!requireAvailable ||
            world.SimulationPricing.GetQuote(new(PurchasableType.Building, d.TypeId, armyId)).IsAvailable && FindBuilder(world, armyId, d.TypeId) is not null)), d => need switch
        {
            AIStrategicBuildingNeed.Power => d.Building!.PowerProduction - d.Building.PowerConsumption,
            AIStrategicBuildingNeed.Storage => d.Building!.ResourceCapacity / (1 + d.Building.PowerConsumption),
            AIStrategicBuildingNeed.Vision => d.Building!.VisionRange,
            _ => 0
        });

    public static GameplayDefinition? SelectUnit(GameWorld world, Guid armyId, AIProductionNeed need, bool requireProducer = false,
        IReadOnlyDictionary<string, int>? currentCounts = null, Func<GameplayDefinition, bool>? candidateFilter = null) =>
        Select(world, armyId, GameplayCatalog.All.Where(d => d.Type == PurchasableType.Unit &&
            (candidateFilter is null || candidateFilter(d)) &&
            !float.IsNegativeInfinity(AIUnitSelector.Score(d, need, currentCounts)) &&
            (!requireProducer || FindAvailableProducer(world, armyId, d) is not null)), d => AIUnitSelector.Score(d, need, currentCounts));

    public static Building? FindAvailableProducer(GameWorld world, Guid armyId, GameplayDefinition product) =>
        world.Units.GetArmyUnits(armyId).OfType<Building>().Where(b => b.ArmyId == armyId && b.IsCompleted && !b.IsDying &&
            (!world.AIOrderMonitors.TryGetValue(armyId, out var monitor) || !monitor.AvoidProducer(b.UnitId)) &&
            product.Producers.Any(p => p.TypeId == GameplayCatalog.Canonicalize(b.GameplayTypeId)) &&
            (product.Type != PurchasableType.Unit || HasQueuedProduct(b, product.TypeId) || b.CanProduceUnit(world, product.TypeId)) &&
            world.SimulationPricing.GetQuote(new(product.Type, product.TypeId, armyId, b.UnitId)).IsAvailable)
            .OrderBy(b => world.AIOrderQueues.TryGetValue(armyId, out var queue) && !queue.CanUse(b.UnitId))
            .ThenByDescending(b => HasQueuedProduct(b, product.TypeId))
            .ThenBy(b => world.SimulationPricing.GetQuote(new(product.Type, product.TypeId, armyId, b.UnitId)).FinalPrice)
            .ThenBy(b => b.UnitId).FirstOrDefault();

    public static bool HasQueuedProduct(Building producer, string typeId) => producer.ProductionQueue.Orders.Any(order =>
        GameplayCatalog.Canonicalize(order.UnitTypeId) == GameplayCatalog.Canonicalize(typeId));

    public static GameplayDefinition? SelectDefense(GameWorld world, Guid armyId, AIProductionNeed need) =>
        Select(world, armyId, GameplayCatalog.All.Where(d => d.Type == PurchasableType.Building &&
            !float.IsNegativeInfinity(AIUnitSelector.Score(d, need))), d => AIUnitSelector.Score(d, need));

    public static AIProductionPlan Plan(GameWorld world, Guid armyId, GameplayDefinition target) =>
        AIProductionPlanner.CreatePlan(target, world.Units.GetArmyUnits(armyId).Where(u => u.ArmyId == armyId && !u.IsDying &&
            !u.IsEmbarked && (u is not Building b || b.IsCompleted) &&
            (u is not MobileUnit m || m.BuildRate <= 0 || m.Occupancy?.IsOperational != false)).Select(u => u.GameplayTypeId),
            world.SimulationArmies.Find(armyId)?.Perks.ActivePerks ?? [],
            ArmyPowerStatus.Calculate(world.Units.GetArmyUnits(armyId), armyId).Balance);

    private static GameplayDefinition? Select(GameWorld world, Guid armyId, IEnumerable<GameplayDefinition> candidates,
        Func<GameplayDefinition, float> score)
    {
        var viable = new List<(GameplayDefinition Product, float Score, long Cost)>();
        foreach (GameplayDefinition candidate in candidates)
        {
            AIProductionPlan plan = Plan(world, armyId, candidate);
            if (!plan.IsValid || plan.NextStep is null) continue;
            long cost = 0; bool available = true;
            foreach (AIProductionPlanStep step in plan.Steps)
            {
                PurchasableType type = step.Kind == AIProductionPlanStepKind.BuildBuilding ? PurchasableType.Building :
                    step.Kind == AIProductionPlanStepKind.Research ? PurchasableType.Research : PurchasableType.Unit;
                Unit? producer = world.Units.GetArmyUnits(armyId).FirstOrDefault(u => u.ArmyId == armyId && !u.IsDying &&
                    u.GameplayTypeId == step.ProducerTypeId);
                PurchaseQuote quote = world.SimulationPricing.GetQuote(new(type, step.TypeId, armyId, producer?.UnitId));
                if (quote.UnavailableReason is not null || step == plan.NextStep && !quote.IsAvailable)
                { available = false; break; }
                cost += quote.FinalPrice;
            }
            if (available) viable.Add((candidate, score(candidate), cost));
        }
        return viable.OrderByDescending(c => c.Score).ThenBy(c => c.Cost).ThenBy(c => c.Product.TypeId, StringComparer.Ordinal)
            .Select(c => c.Product).FirstOrDefault();
    }
}

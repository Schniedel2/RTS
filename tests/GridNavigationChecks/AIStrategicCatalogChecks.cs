using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static class AIStrategicCatalogChecks
{
    private static void Set(object target, Type owner, string field, object value)
    {
        if (target is Unit && field == "<ArmyId>k__BackingField")
            typeof(Unit).GetMethod("SetArmy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, [value]);
        else owner.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }
    private static T Empty<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    // Regular test units need no renderer; only the world/service fixture still uses reflection (TODO 11).
    private sealed class CatalogBuilding : Building
    {
        private readonly string _type;
        public override string GameplayTypeId => _type;
        public override IReadOnlyList<UnitAction> Actions => WithSellAction(GameplayCatalog.CreateProductionActions(_type));
        public CatalogBuilding(string type, Vector3 position, Guid id, int price) : base(position, id, price)
        {
            _type = type; ApplyCatalogMetadata(); AdvanceConstruction(TotalBuildingPointsNeeded);
            Occupancy!.EntryEnabled = true;
        }
    }
    private sealed class CatalogMobile : MobileUnit
    {
        private readonly string _type;
        private readonly bool _builder;
        public override string GameplayTypeId => _type;
        public override float BuildRate => _builder ? 4 : 0;
        public CatalogMobile(string type, Vector3 position, Guid id, bool builder = false, bool crew = false)
            : base(position, id) { _type = type; _builder = builder; IsCrewMember = crew; }
    }

    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var definitions = (List<GameplayDefinition>)typeof(GameplayCatalog).GetField("Definitions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var buildings = (Dictionary<string, Func<Vector3, Guid, int, Building>>)typeof(BuildingFactory).GetField("Creators", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var mobiles = (Dictionary<string, Func<Vector3, Guid, MobileUnit>>)typeof(UnitFactory).GetField("Creators", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        GameplayDefinition[] extra =
        [
            new(PurchasableType.Building, "test-home", "Alternate Home", 100, [], [new("test-builder", 0)],
                Building: new(100, 10, PowerConsumption: 20), ProvidedPerks: [PerkType.BaseEstablished]),
            new(PurchasableType.Building, "test-generator", "Alternate Generator", 50, [PerkType.BaseEstablished], [new("test-builder", 0)],
                Building: new(100, 10, PowerProduction: 150, CrewCapacity: 2, PowerProductionPerCrew: 45)),
            new(PurchasableType.Building, "test-store", "Alternate Storage", 50, [PerkType.BaseEstablished], [new("test-builder", 0)],
                Building: new(100, 10, ResourceCapacity: 8000)),
            new(PurchasableType.Building, "test-air-port", "Alternate Air Port", 80, [PerkType.AirTechnology], [new("test-builder", 0)],
                Building: new(100, 10, PowerConsumption: 15)),
            new(PurchasableType.Building, "test-impossible-port", "Unreachable Port", 1, [], [], Building: new(100, 10)),
            new(PurchasableType.Unit, "test-builder", "Alternate Builder", 20, [], [new("test-home", 2)],
                AI: new(AIUnitRole.Builder, AIMovementDomain.GroundVehicle)),
            new(PurchasableType.Unit, "test-air", "Alternate Air Support", 40, [], [new("test-impossible-port", 1), new("test-air-port", 2), new("test-home", 3)],
                AI: new(AIUnitRole.Attacker, AIMovementDomain.Air, AntiVehicle: 3, Mobility: 1)),
            new(PurchasableType.Unit, "test-crew", "Alternate Crew", 20, [], [new("test-home", 2)],
                AI: new(AIUnitRole.Crew, AIMovementDomain.Infantry)),
            new(PurchasableType.Research, "test-air-research", "Alternate Air Research", 30, [], [new("test-home", 2)], GrantedPerk: PerkType.AirTechnology)
        ];
        var savedWorld = Globals.World; var savedGame = Globals.Game;
        try
        {
            definitions.AddRange(extra);
            foreach (GameplayDefinition d in extra.Where(d => d.Type == PurchasableType.Building))
                buildings.Add(d.TypeId, (p, id, price) => new CatalogBuilding(d.TypeId, p, id, price));
            foreach (GameplayDefinition d in extra.Where(d => d.Type == PurchasableType.Unit))
                mobiles.Add(d.TypeId, (p, id) => new CatalogMobile(d.TypeId, p, id, d.AI!.Roles.HasFlag(AIUnitRole.Builder), d.AI.Roles.HasFlag(AIUnitRole.Crew)));
            GameplayCatalog.Validate(); Check(true, "Alternative catalog and factory registrations validate without AI type switches");
            using var network = new NetworkHandler();
            var actor = new Player(network.LocalPeerId, "Catalog AI");
            var world = Empty<GameWorld>(); var units = new UnitHandler();
            Set(world, typeof(GameWorld), "<Units>k__BackingField", units);
            Set(world, typeof(GameWorld), "<GameGrid>k__BackingField", new GameGrid(40, 40, 1));
            var armies = new ArmyHandler(); Army army = armies.EnsureArmy(actor.ArmyId, actor.Id); army.Resources = 2000;
            var pricing = new PricingService(armies, units.FindById);
            var game = Empty<RTSGame>();
            Set(game, typeof(RTSGame), "<World>k__BackingField", world);
            Set(game, typeof(RTSGame), "<Armies>k__BackingField", armies);
            Set(game, typeof(RTSGame), "<Pricing>k__BackingField", pricing);
            Globals.World = world; Globals.Game = game;
            var list = (IList<Unit>)typeof(UnitHandler).GetField("_units", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(units)!;
            CatalogBuilding AddBuilding(string id) { var b = (CatalogBuilding)BuildingFactory.SpawnBuilding(id, Vector3.Zero, 0, Guid.NewGuid(), actor.ArmyId)!; list.Add(b); return b; }
            CatalogMobile AddUnit(string id) { var u = (CatalogMobile)UnitFactory.SpawnUnit(id, Vector3.Zero, 0, Guid.NewGuid(), actor.ArmyId)!; list.Add(u); return u; }
            var builder = AddUnit("test-builder");
            Check(AIStrategicCatalog.FindBuilder(world, army.Id, "test-store") == builder, "Alternative operational builder is selected for its declared offer");
            Check(AIStrategicCatalog.FindBuilder(world, army.Id, "silo") is null, "Builder cannot execute another producer's offer");
            var baseChoice = AIStrategicCatalog.SelectBuilding(world, army.Id, AIStrategicBuildingNeed.Base);
            Check(baseChoice?.TypeId == "test-home", "Base is found via provided perk rather than GDI class");
            var home = AddBuilding("test-home");
            Check(AIStrategicCatalog.FindBuilding(world, army.Id, AIStrategicBuildingNeed.Base) == home, "Owned alternate headquarters recognized by capability");
            army.Perks.GrantPermanent(PerkType.BaseEstablished, Guid.NewGuid());
            Check(AIStrategicCatalog.SelectBuilding(world, army.Id, AIStrategicBuildingNeed.Power)?.TypeId == "test-generator", "Net power selects alternate generator");
            Check(AIStrategicCatalog.SelectBuilding(world, army.Id, AIStrategicBuildingNeed.Storage)?.TypeId == "test-store", "Storage selects alternate capacity offer");
            Check(AIStrategicCatalog.SelectUnit(world, army.Id, new(AIUnitRole.Builder), requireProducer: true)?.TypeId == "test-builder", "Builder replacement comes from alternate headquarters");
            Check(AIStrategicCatalog.SelectUnit(world, army.Id, new(AIUnitRole.Attacker, AIMovementDomain.Air, AntiVehicle: 1))?.TypeId == "test-air", "Air support selected by roles and domain");
            AIProductionPlan plan = AIProductionPlanner.CreatePlan(extra.Single(d => d.TypeId == "test-air") with { Producers = [new("test-impossible-port", 1), new("test-air-port", 2)] },
                ["test-builder", "test-home"], [PerkType.BaseEstablished], 100);
            Check(plan.IsValid && plan.Steps.Select(s => s.TypeId).SequenceEqual(["test-air-research", "test-air-port", "test-air"]), "Unavailable first producer falls back to second, resolving research without leaked branch steps");
            Check(plan.Steps.Last().ProducerTypeId == "test-air-port", "Train step carries successful second producer");
            AIProductionPlan bootstrap = AIProductionPlanner.CreatePlan(extra.Single(d => d.TypeId == "test-store"), ["test-builder"], [], 0);
            Check(bootstrap.IsValid && bootstrap.Steps.Select(s => s.TypeId).SequenceEqual(["test-home", "test-store"]), "Building perk provider bootstraps prerequisites without a hardcoded base");
            AIProductionPlan powered = AIProductionPlanner.CreatePlan(extra.Single(d => d.TypeId == "test-air-port"), ["test-builder", "test-home"], [PerkType.AirTechnology], 0);
            Check(powered.IsValid && powered.Steps.Select(s => s.TypeId).SequenceEqual(["test-generator", "test-air-port"]), "Power dependency uses alternate feasible generator");
            var generator = AddBuilding("test-generator");
            var crew = AddUnit("test-crew");
            AIPowerSolution solution = AICrewPowerPlanner.Evaluate(world, army.Id, 40);
            Check(solution.Kind == AIPowerSolutionKind.AssignCrew && solution.CrewUnitId == crew.UnitId && solution.TargetBuildingId == generator.UnitId && solution.PowerGain == 45,
                "Alternate crew and power building use declared runtime slot and metadata bonus");
            list.Remove(crew);
            solution = AICrewPowerPlanner.Evaluate(world, army.Id, 40);
            Check(solution.Kind == AIPowerSolutionKind.TrainCrew && solution.ProducerBuildingId == home.UnitId && solution.CrewTypeId == "test-crew", "Power crew training works in an alternate producer");
            Check(AICrewPowerPlanner.Evaluate(world, army.Id, 46).Kind == AIPowerSolutionKind.BuildPower, "Crew benefit uses alternate bonus threshold");
            Check(home.Actions.Any(a => a.TargetObjectName == "test-air") && home.TryGetProductionDuration("test-air", out float duration) && duration == 3,
                "Human actions and AI producer share catalog offer and duration");
            var input = new NetworkInput(network); var host = new NetworkHost(network, input, world);
            var admit = typeof(NetworkHost).GetMethod("TryCreateTrainUnitCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
            NetworkMessage? command = (NetworkMessage?)admit.Invoke(host, [NetworkCommands.CreateTrainUnitRequest(actor.Id, home.UnitId, "test-air")]);
            Check(command is { Type: NetworkMessageType.TrainUnitCommand, ProductionSeconds: 3 } && army.Resources == 1960, "Host accepts alternate product at same catalog price and second-producer duration");
            Check(AIStrategicCatalog.FindAvailableProducer(world, army.Id, extra.Single(d => d.TypeId == "test-air")) == home, "AI uses existing second producer instead of impossible preferred producer");
            army.Resources = 0;
            var executor = new AIProductionPlanExecutor(world, actor, network);
            executor.Start(new([new(AIProductionPlanStepKind.TrainUnit, "test-air", "test-home")]));
            executor.Update(new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
            Check(executor.State == AIPlanExecutionState.InProgress && home.ProductionQueue.Orders.Count == 1, "Already queued air support is awaited without money or duplicate ordering");
            AddUnit("test-air"); executor.Update(new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
            Check(executor.State == AIPlanExecutionState.Completed, "Awaited catalog unit completes plan after spawn");
            army.Resources = 2000;
            home.ProductionQueue.ApplyState(null);
            home.ProductionQueue.Capacity = 1;
            home.ProductionQueue.Enqueue(Guid.NewGuid(), "test-crew", actor.Id, 2);
            var port = AddBuilding("test-air-port");
            Check(!home.CanProduceUnit(world, "test-air") && port.CanProduceUnit(world, "test-air"),
                "Shared admission rule distinguishes full and free producers");
            Check(AIStrategicCatalog.FindAvailableProducer(world, army.Id, extra.Single(d => d.TypeId == "test-air")) == port,
                "AI switches to another producer when the first queue is full");
            Check(admit.Invoke(host, [NetworkCommands.CreateTrainUnitRequest(actor.Id, home.UnitId, "test-air")]) is null && army.Resources == 2000,
                "Host rejects the same full producer without spending resources");
            command = (NetworkMessage?)admit.Invoke(host, [NetworkCommands.CreateTrainUnitRequest(actor.Id, port.UnitId, "test-air")]);
            Check(command is { ProductionSeconds: 2 } && army.Resources == 1960, "Alternate producer uses its own duration and shared purchase price");
            executor.Start(new([new(AIProductionPlanStepKind.TrainUnit, "test-air", "test-impossible-port")]));
            executor.Update(new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1)));
            Check(executor.State == AIPlanExecutionState.InProgress && port.ProductionQueue.Orders.Count == 1,
                "Executor uses an existing alternate producer when the planned producer is absent");
            AddUnit("test-air"); executor.Update(new(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1)));
            Check(executor.State == AIPlanExecutionState.Completed, "Alternate-producer result advances the active plan");
            var infrastructure = new AIInfrastructureController(world, actor, network);
            var airExecutor = (AIProductionPlanExecutor)typeof(AIInfrastructureController).GetField("_executor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(infrastructure)!;
            airExecutor.Start(new([new(AIProductionPlanStepKind.TrainUnit, "test-air", "test-air-port")]));
            Set(infrastructure, typeof(AIInfrastructureController), "_airPlan", true);
            infrastructure.Update(new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1)));
            Check(infrastructure.IsAirSupportReady && !airExecutor.IsBusy && port.ProductionQueue.Orders.Count == 1,
                "Existing air unit completes infrastructure goal before another training step executes");
            var foreignHome = AddBuilding("test-home"); Set(foreignHome, typeof(Unit), "<ArmyId>k__BackingField", Guid.NewGuid());
            Check(AIStrategicCatalog.FindBuilding(world, army.Id, AIStrategicBuildingNeed.Base, foreignHome.UnitId) == home,
                "Preferred building ID cannot select another army's producer");
            Set(builder, typeof(Unit), "<IsEmbarked>k__BackingField", true);
            Check(AIStrategicCatalog.FindBuilder(world, army.Id) is null, "Embarked builder is unavailable for construction");
            Set(builder, typeof(Unit), "<IsEmbarked>k__BackingField", false);
            var locked = extra.Single(d => d.TypeId == "test-store") with { RequiredPerks = [PerkType.DetailedHealth] };
            int index = definitions.FindIndex(d => d.TypeId == locked.TypeId); var original = definitions[index]; definitions[index] = locked;
            Check(!pricing.GetQuote(new(PurchasableType.Building, locked.TypeId, army.Id)).IsAvailable &&
                AIStrategicCatalog.SelectBuilding(world, army.Id, AIStrategicBuildingNeed.Storage) is null,
                "Unobtainable perk blocks the same otherwise compatible storage offer for human and AI");
            definitions[index] = original;
            try { GameplayCatalog.Validate(GameplayCatalog.All.Select(d => d.TypeId == "test-home" ? d with { ProvidedPerks = [(PerkType)999] } : d)); throw new Exception("Invalid capability accepted"); }
            catch (InvalidOperationException) { Check(true, "Invalid provided perk metadata rejected"); }
        }
        finally
        {
            foreach (GameplayDefinition d in extra) { definitions.RemoveAll(x => x.TypeId == d.TypeId); buildings.Remove(d.TypeId); mobiles.Remove(d.TypeId); }
            Globals.World = savedWorld; Globals.Game = savedGame;
        }
        return checks;
    }
}

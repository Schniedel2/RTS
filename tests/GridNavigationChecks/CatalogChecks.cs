using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static class CatalogChecks
{
    private static void Set(object target, Type owner, string field, object value)
    {
        if (target is Unit && field == "<ArmyId>k__BackingField")
            typeof(Unit).GetMethod("SetArmy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, [value]);
        else owner.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }
    private static T Empty<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        void Invalid(IEnumerable<GameplayDefinition> definitions, string reason)
        {
            try { GameplayCatalog.Validate(definitions); throw new Exception("Validation accepted " + reason); }
            catch (InvalidOperationException error) { Check(error.Message.Length > 0, "Validation explains " + reason); }
        }
        var savedWorld = Globals.World; var savedGame = Globals.Game; var savedMeshes = Globals.MeshHandler;
        var definitions = (List<GameplayDefinition>)typeof(GameplayCatalog).GetField("Definitions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var research = new GameplayDefinition(PurchasableType.Research, "test-radio-research", "Radio Test", 125,
            [], [new("gdi-base", 3), new("gdi-barracks", 4)], GrantedPerk: PerkType.DetailedHealth);
        try
        {
            GameplayCatalog.Validate(); Check(true, "Current catalog matches complete factory registration");
            Invalid(GameplayCatalog.All.Append(research with { Type = PurchasableType.Unit }), "missing factory");
            Invalid(GameplayCatalog.All.Append(research with { GrantedPerk = null }), "missing research perk");
            Invalid(GameplayCatalog.All.Append(research with { BasePrice = -1 }), "negative price");
            Invalid(GameplayCatalog.All.Append(research with { Producers = [new("missing-producer", 3)] }), "unknown producer");
            Invalid(GameplayCatalog.All.Append(research with { Producers = [new("gdi-base", float.NaN)] }), "invalid duration");
            Invalid(GameplayCatalog.All.Append(GameplayCatalog.All[0]), "duplicate identity");
            Invalid(GameplayCatalog.All.Where(d => d.TypeId != "jeep"), "factory missing catalog entry");

            using var network = new NetworkHandler();
            var world = Empty<GameWorld>(); var units = new UnitHandler();
            Set(world, typeof(GameWorld), "<Units>k__BackingField", units);
            Set(world, typeof(GameWorld), "<GameGrid>k__BackingField", new GameGrid(40, 40, 1));
            var armies = new ArmyHandler(); Guid armyId = Guid.NewGuid();
            Army army = armies.EnsureArmy(armyId, network.LocalPeerId); army.Resources = 2000;
            var pricing = new PricingService(armies, units.FindById);
            var game = Empty<RTSGame>();
            Set(game, typeof(RTSGame), "<World>k__BackingField", world);
            Set(game, typeof(RTSGame), "<Armies>k__BackingField", armies);
            Set(game, typeof(RTSGame), "<Pricing>k__BackingField", pricing);
            Globals.World = world; Globals.Game = game;
            foreach (PurchasableType type in Enum.GetValues<PurchasableType>())
            {
                var unknown = pricing.GetQuote(new(type, "missing-product", armyId));
                Check(!unknown.IsAvailable && !unknown.CanAfford(int.MaxValue) && unknown.UnavailableReason?.Contains("missing-product") == true,
                    $"Unknown {type} is unavailable with an explanatory quote, never a free purchase");
            }
            try { EconomyCatalog.GetBasePrice(PurchasableType.Unit, "missing-product"); throw new Exception("Unknown base price accepted"); }
            catch (ArgumentException error) { Check(error.Message.Contains("missing-product"), "Strict price API explains unknown product"); }
            Check(UnitFactory.SpawnUnit("missing-product", Vector3.Zero, 0, Guid.NewGuid(), Guid.Empty) is null &&
                BuildingFactory.SpawnBuilding("missing-product", Vector3.Zero, 0, Guid.NewGuid(), Guid.Empty) is null,
                "Factories reject unknown products before construction or price resolution");
            Check(pricing.GetQuote(new(PurchasableType.Unit, "editor", armyId)) is { IsAvailable: true, FinalPrice: 0 } &&
                pricing.GetQuote(new(PurchasableType.Building, "building-1", armyId)) is { IsAvailable: true, FinalPrice: 0 },
                "Explicit sandbox editor and generic building retain free debug creation");

            Globals.MeshHandler = new MeshHandler();
            Mesh mesh = savedMeshes.Meshes["barracks-1"];
            foreach (string name in new[] { "Soldier-2", "blue-pick-up-truck", "bulldozer-1", "harvester-1", "heli-1", "motorbike-1",
                "tank-1", "TankBody-1", "TankTurret-1", "TankBarrel-2", "barracks-1", "gdi-base", "reaktor-1", "reaktor-2", "gatling-tower-1",
                "vehicle-factory-1", "antenna-1", "helipad-1", "silo-1", "tiberium-refinery-1", "tiberiumSource-1", "building-1", "default",
                "ak47", "m16", "breda-m1935pg", "brok17", "kraber-ap-sniper", "uzi-mac-10", "minigun", "hunting", "ar-15", "sten-mk2-apocalypse",
                "rpg", "toolKit", "medKit", "rpg-projectile" }) Globals.MeshHandler.Meshes[name] = mesh;
            var flatTerrain = Empty<Terrain>();
            Set(flatTerrain, typeof(Terrain), "<Width>k__BackingField", 41);
            Set(flatTerrain, typeof(Terrain), "<Height>k__BackingField", 41);
            Set(flatTerrain, typeof(Terrain), "HeightMap", new float[41 * 41]);
            Set(world, typeof(GameWorld), "_terrain", flatTerrain);
            world.GameGrid.BindTerrain(flatTerrain);
            var startActor = new Player(network.LocalPeerId, "Match AI", armyId: armyId);
            Set(game, typeof(RTSGame), "_players", new List<Player> { startActor });
            Guid startDriverId = Guid.NewGuid();
            Unit? startingWorker = units.SpawnUnit("gdi-bulldozer", new(12, 0, 12), 0, Guid.NewGuid(), startActor.Id, startDriverId);
            Check(startingWorker is GDIBulldozer && startingWorker.Occupancy?.IsOperational == true &&
                units.FindById(startDriverId) is Soldier { IsEmbarked: true } driver && driver.ContainerUnitId == startingWorker.UnitId && driver.ArmyId == armyId,
                "Actual start bulldozer with confirmed driver spawns operational with matching embarked ownership");
            Check(AIStrategicCatalog.FindBuilder(world, armyId, "gdi-base") == startingWorker &&
                AIStrategicCatalog.SelectBuilding(world, armyId, AIStrategicBuildingNeed.Base, requireAvailable: true)?.TypeId == "gdi-base",
                "Real occupied GDI starter passes strategic builder and initial-base selection");
            Set(network, typeof(NetworkHandler), "<IsHost>k__BackingField", true);
            var startRequests = new List<NetworkMessage>(); network.MessageReceived += startRequests.Add;
            var startGoals = new ArmyGoalController(); startGoals.Start(AIArmyGoal.EstablishEconomy);
            startGoals.Update(new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), startActor, world, network);
            network.Update();
            Check(startGoals.Goal == AIGoalState.BaseBuildRequested && startRequests.Any(m =>
                m.Type == NetworkMessageType.BuildRequest && GameplayCatalog.Canonicalize(m.UnitTypeId) == "gdi-base"),
                "Real match starter advances AI through placement into a network base-build request");
            network.MessageReceived -= startRequests.Add;
            Check(units.GetArmyUnits(armyId).Count == 2 && units.FindById(startDriverId) is not null,
                "Actual spawn indexes vehicle and embarked initial driver in the same army");
            Check(units.DisembarkUnit(startingWorker!.UnitId, startDriverId, new(25, 0, 25)) &&
                startingWorker.ArmyId is null && units.GetArmyUnits(armyId).Count == 1,
                "Actual driver exit neutralizes vehicle and updates army index immediately");
            Check(units.EmbarkUnit(startDriverId, startingWorker.UnitId, OccupantRole.Driver) &&
                startingWorker.ArmyId == armyId && units.GetArmyUnits(armyId).Count == 2,
                "Actual controller capture reassigns indexed vehicle to driver's army");
            Check(units.SpawnUnit("gdi-bulldozer", new(30, 0, 30), 0, startingWorker.UnitId, startActor.Id, Guid.NewGuid()) is null &&
                units.Count == 2 && units.FindById(startingWorker.UnitId) == startingWorker,
                "Duplicate actual spawn is rejected before modifying membership or grid");
            Guid fallbackBuildingId = Guid.NewGuid();
            Check(units.SpawnUnit("gdi-base", new(30, 0, 30), 0, fallbackBuildingId, startActor.Id) is GDIBase &&
                units.GetSnapshot().Count(u => u.UnitId == fallbackBuildingId) == 1 && units.Count == 3,
                "SpawnUnit building fallback registers the building exactly once");
            ((IList<Unit>)typeof(UnitHandler).GetField("_units", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(units)!).Clear();
            Set(world, typeof(GameWorld), "<GameGrid>k__BackingField", new GameGrid(40, 40, 1));
            foreach (GameplayDefinition product in GameplayCatalog.All.Where(d => d.Type != PurchasableType.Research))
            {
                Guid id = Guid.NewGuid();
                Unit? created = product.Type == PurchasableType.Unit
                    ? UnitFactory.SpawnUnit(product.TypeId.ToUpperInvariant(), new(5, 0, 5), 45, id, network.LocalPeerId)
                    : BuildingFactory.SpawnBuilding(product.TypeId.ToUpperInvariant(), new(5, 0, 5), 45, id, network.LocalPeerId);
                Check(created is not null && created.UnitId == id && created.GameplayTypeId == product.TypeId,
                    $"Registered factory constructs catalog {product.Type}:{product.TypeId}");
                if (created is Building b) Check(b.PurchasePrice == product.BasePrice, $"Building {product.TypeId} copies central purchase price");
            }
            foreach (string id in UnitFactory.RegisteredTypeIds.Where(UnitFactory.IsSandboxType))
                Check(UnitFactory.SpawnUnit(id, Vector3.Zero, 0, Guid.NewGuid(), Guid.Empty) is not null, "Explicit unit sandbox registration constructs " + id);
            foreach (string id in BuildingFactory.RegisteredTypeIds.Where(BuildingFactory.IsSandboxType))
                Check(BuildingFactory.SpawnBuilding(id, Vector3.Zero, 0, Guid.NewGuid(), Guid.Empty) is GenericBuilding, "Explicit generic registration constructs " + id);

            definitions.Add(research); GameplayCatalog.Validate();
            Check(ResearchProjects.TryGetGrantedPerk(" TEST-RADIO-RESEARCH ", out PerkType perk) && perk == PerkType.DetailedHealth,
                "Second research maps perk solely from catalog without a research switch");
            var list = (IList<Unit>)typeof(UnitHandler).GetField("_units", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(units)!;
            var barracks = new GDIBarracks(Vector3.Zero, Guid.NewGuid());
            barracks.AdvanceConstruction(barracks.TotalBuildingPointsNeeded);
            Set(barracks, typeof(Unit), "<ArmyId>k__BackingField", armyId); list.Add(barracks);
            var headquarters = new GDIBase(Vector3.Zero, Guid.NewGuid());
            headquarters.AdvanceConstruction(headquarters.TotalBuildingPointsNeeded);
            Set(headquarters, typeof(Unit), "<ArmyId>k__BackingField", armyId); list.Add(headquarters);
            army.Perks.GrantPermanent(PerkType.AirTechnology, Guid.NewGuid());
            Check(headquarters.Actions.Any(a => a.TargetObjectName == research.TypeId && a.Type == UnitActionType.Research),
                "Research action remains available after an unrelated research has been completed");
            Check(GameplayCatalog.CreateResearchActions("gdi-barracks").Any(a => a.TargetObjectName == research.TypeId) &&
                barracks.Actions.Any(a => a.Type == UnitActionType.Research && a.TargetObjectName == research.TypeId) &&
                barracks.TryGetProductionDuration(research.TypeId, out float seconds) && seconds == 4,
                "Second research producer obtains action and duration from the same catalog");
            var input = new NetworkInput(network); var host = new NetworkHost(network, input, world);
            var admit = typeof(NetworkHost).GetMethod("TryCreateResearchCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
            NetworkMessage request = NetworkCommands.CreateResearchRequest(network.LocalPeerId, barracks.UnitId, research.TypeId);
            NetworkMessage? command = (NetworkMessage?)admit.Invoke(host, [request]);
            Check(command is { Type: NetworkMessageType.ResearchCommand, ProductionSeconds: 4 } && army.Resources == 1875,
                "Host accepts catalog research in a second producer and spends the central price");
            Check(!headquarters.Actions.Any(a => a.TargetObjectName == ResearchProjects.AirTechnologyId) &&
                !headquarters.Actions.Any(a => a.TargetObjectName == research.TypeId),
                "Queued research in another producer hides the same project across the army");
            Check(admit.Invoke(host, [request]) is null && army.Resources == 1875, "Duplicate pending research is rejected without spending twice");
            Check(admit.Invoke(host, [request with { UnitTypeId = " TEST-RADIO-RESEARCH " }]) is null,
                "Aliases, case and whitespace cannot bypass the pending research guard");
            typeof(NetworkHost).GetMethod("UpdateProduction", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, [4f]);
            Check(army.Perks.Has(PerkType.DetailedHealth) && barracks.ProductionQueue.Orders.Count == 0,
                "Actual host production completes second catalog research and grants its declared perk");
            var completion = JsonSerializer.Deserialize<NetworkMessage>(JsonSerializer.Serialize(
                NetworkCommands.CreateResearchCompletedCommand(network.LocalPeerId, armyId, command!.ProductionOrderId!.Value, research.TypeId), NetworkJson.Options), NetworkJson.Options)!;
            network.ApplyLocalCommand(completion);
            Check(army.Perks.Has(PerkType.DetailedHealth), "Actual wire and NetworkInput grant the additional catalog research perk");
            Check(!headquarters.Actions.Any(a => a.TargetObjectName == research.TypeId) && admit.Invoke(host, [request]) is null,
                "Completed catalog perk hides and blocks repurchase of its research");
        }
        finally
        {
            definitions.Remove(research);
            Globals.World = savedWorld; Globals.Game = savedGame; Globals.MeshHandler = savedMeshes;
        }
        return checks;
    }
}



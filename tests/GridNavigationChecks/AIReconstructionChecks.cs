using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    private sealed class CatalogBuilding : Building
    {
        private readonly string _type;
        public override string GameplayTypeId => _type;
        public override bool SupportsRallyPoint => true;
        public CatalogBuilding(string type, Vector3 position, Guid id, bool completed = true)
            : base(position, id)
        {
            _type = type;
            Width = Length = 1;
            ApplyCatalogMetadata();
            if (completed) AdvanceConstruction(TotalBuildingPointsNeeded);
        }
    }

    private sealed class Gunner : Soldier
    {
        public override string GameplayTypeId => "gunner";
        public Gunner(Vector3 position, Guid id) : base(position, id, loadModel: false)
        {
            Width = Length = 1;
            SetWeapon(Weapon.M16);
        }
    }

    private sealed class Builder(Vector3 position, Guid id) : MobileUnit(position, id)
    {
        public override string GameplayTypeId => "gdi-bulldozer";
        public override float BuildRate => 10;
    }

    private sealed class CatalogVehicle : MobileUnit
    {
        private readonly string _type;
        public override string GameplayTypeId => _type;
        public override TargetDomain Domain => _type == "helicopter" ? TargetDomain.Air : TargetDomain.Ground;
        public override ArmorClass Armor => ArmorClass.HeavyVehicle;
        public CatalogVehicle(string type, Vector3 position, Guid id) : base(position, id)
        {
            _type = type;
            AllowedTargetDomains = type == "gepard" ? TargetDomain.Air : TargetDomain.Ground;
            Width = Length = 1;
        }
    }

    private sealed class MissionHelicopter : Helicopter
    {
        public MissionHelicopter(Vector3 position, Guid id) : base(position, id) { Width = Length = 1; }
    }

    // AI still uses the RTSGame economy facade. Only this presentation shell
    // needs reflection; world, units, AI and network use regular constructors.
    private sealed class Scenario : IDisposable
    {
        private readonly RTSGame _savedGame = Globals.Game;
        private readonly GameWorld _savedWorld = Globals.World;
        private readonly MeshHandler _savedMeshes = Globals.MeshHandler;
        private int _id = 1;
        private double _time;
        public GameWorld World { get; }
        public List<AIPlayer> AdditionalAI { get; } = [];
        public NetworkHandler Network { get; }
        public AIPlayer AI { get; }
        public Army Army { get; }
        public List<NetworkMessage> Messages { get; } = [];
        public List<Soldier> Soldiers { get; } = [];
        public NetworkInput Input { get; }
        public NetworkHost Host { get; }
        public CatalogBuilding Home { get; }
        public CatalogBuilding Reactor { get; }
        public CatalogBuilding Refinery { get; }
        public CatalogBuilding Barracks { get; }

        public Scenario(bool startReady = true, int worldSize = 65, ClientRunDiagnostics? diagnostics = null)
        {
            Network = new("AI reconstruction checks") { RunDiagnostics = diagnostics };
            World = new(worldSize, worldSize, 1, graphicsEnabled: false);
            Globals.MeshHandler = new MeshHandler();
            foreach (var mesh in _savedMeshes.Meshes) Globals.MeshHandler.Meshes[mesh.Key] = mesh.Value;
            // Low-resource proposals now inspect building footprints before a purchase is funded.
            foreach (string name in new[] { "gdi-base", "reaktor-1", "reaktor-2", "antenna-1", "gatling-tower-1",
                "vehicle-factory-1", "helipad-1", "silo-1", "tiberium-refinery-1" })
                Globals.MeshHandler.Meshes.TryAdd(name, _savedMeshes.Meshes["barracks-1"]);
            Network.CreateSessionAsync("AI reconstruction").GetAwaiter().GetResult();
            var actor = new Player(Network.LocalPeerId, "Test AI");
            // Stable army/seed keep the squad profile and troop requirements reproducible.
            actor.SetArmy(Guid.Parse("a030a67e-e93e-435b-814a-34734830baec"));
            AI = new AIPlayer(actor);
            Army = World.SimulationArmies.EnsureArmy(actor.ArmyId, actor.Id, Guid.NewGuid());
            Army.Resources = 0;
            Army.Perks.GrantPermanent(PerkType.BaseEstablished, NextId());
            var game = (RTSGame)RuntimeHelpers.GetUninitializedObject(typeof(RTSGame));
            void Set(string property, object value) => typeof(RTSGame).GetField(
                $"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(game, value);
            typeof(RTSGame).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(game, new List<Player> { actor });
            Set("World", World);
            Set("Armies", World.SimulationArmies);
            Set("Pricing", new PricingService(World.SimulationArmies, World.Units.FindById));
            Set("Network", Network);
            Globals.Game = game;
            Globals.World = World;
            Input = new NetworkInput(Network, World, World.SimulationArmies);
            Host = new NetworkHost(Network, Input, World, World.SimulationArmies,
                () => new[] { actor }.Concat(AdditionalAI.Select(ai => ai.Player)).ToArray(), () => new[] { AI }.Concat(AdditionalAI).ToArray(), (_, _) => { }, () => true);
            // Initialize the host generation before sending requests.
            Host.Update(new GameTime());
            Network.MessageReceived += Messages.Add;
            Home = AddBuilding("gdi-base", new(5.5f, 0, 5.5f));
            Home.SetRallyPoint(new(8.5f, 0, 12.5f));
            Reactor = AddBuilding("reaktor", new(10.5f, 0, 5.5f));
            Refinery = AddBuilding("tiberium-refinery", new(15.5f, 0, 5.5f));
            Barracks = AddBuilding("gdi-barracks", new(5.5f, 0, 10.5f));
            Barracks.SetRallyPoint(Home.RallyPoint);
            Add(new Builder(new(2.5f, 0, 5.5f), NextId()));
            var harvester = Add(new Harvester(new(20.5f, 0, 5.5f), NextId(), loadModel: false));
            harvester.ApplyHarvestState(HarvestPhase.Harvesting, 0);
            World.Tiberium.ApplySeed(new(22, 5, 0, 100, 0, 0, 1, 1, 1));
            for (int index = 0; index < 4; index++)
                Soldiers.Add(Add(new Gunner(new(8.5f + index, 0, 12.5f), NextId())));
            AI.BeginMatch(1234, actor.ArmyId);
            if (startReady)
            {
                Tick(); Tick(); Messages.Clear();
                // Isolate each scenario's new orders from unfunded bootstrap proposals.
                AI.Controller.OrderQueue?.Dispose();
            }
        }

        public Guid NextId() => new(_id++, 0, 0, new byte[8]);
        public T Add<T>(T unit, Guid? armyId = null) where T : Unit
        {
            unit.SetArmy(armyId ?? Army.Id);
            World.Units.Register(unit);
            bool placed = unit is MobileUnit mobile
                ? World.GameGrid.TryMove(mobile, World.GameGrid.ToCell(unit.Position))
                : World.GameGrid.TryPlace(unit, unit.Position, 0);
            if (!placed) throw new Exception("AI reconstruction fixture placement failed.");
            return unit;
        }
        public CatalogBuilding AddBuilding(string type, Vector3 position, bool completed = true) =>
            Add(new CatalogBuilding(type, position, NextId(), completed));
        public void Remove(Unit unit)
        {
            World.GameGrid.Remove(unit);
            World.Units.Unregister(unit);
        }
        public void Tick()
        {
            _time++;
            AI.Controller.Update(new GameTime(TimeSpan.FromSeconds(_time), TimeSpan.FromSeconds(1)), AI, World, Network);
            Network.Update();
        }
        public void PumpHost()
        {
            for (int step = 0; step < 100; step++)
            {
                World.PathfindingManager.Update(2048, double.PositiveInfinity);
                Host.Update(new GameTime(TimeSpan.FromSeconds(_time), TimeSpan.Zero));
                Network.Update();
            }
        }
        public Soldier AddVisibleEnemy(Vector3 position)
        {
            Guid armyId = Guid.Parse("b030a67e-e93e-435b-814a-34734830baec");
            World.SimulationArmies.EnsureArmy(armyId, Guid.NewGuid(), Guid.NewGuid());
            var enemy = Add(new Gunner(position, NextId()), armyId);
            World.Visibility.GetGrid(Army.Id).Reveal(World.GameGrid.ToCell(position), 0);
            return enemy;
        }
        public void ConfirmSquad(SquadLeader leader, IEnumerable<Soldier> members, UnitActionType action) =>
            Network.ApplyLocalCommand(new(NetworkMessageType.UnitActionCommand, Network.LocalPeerId,
                UnitIds: members.Select(unit => unit.UnitId).ToArray(), UnitActionType: action,
                UnitActionContext: new(TargetUnitId: leader.UnitId)));
        public void Dispose()
        {
            Input.Dispose();
            Network.Dispose();
            Globals.Game = _savedGame;
            Globals.World = _savedWorld;
            Globals.MeshHandler = _savedMeshes;
        }
    }

    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        foreach (string lostType in new[] { "reaktor", "tiberium-refinery", "gdi-barracks" })
        {
            using var scenario = new Scenario();
            Check(scenario.AI.Controller.Goal == AIGoalState.Scouting, "AI naturally finishes its initial core plan");
            Building lost = lostType switch
            {
                "reaktor" => scenario.Reactor,
                "tiberium-refinery" => scenario.Refinery,
                _ => scenario.Barracks
            };
            scenario.Remove(lost);
            var enemy = scenario.AddVisibleEnemy(new(14.5f, 0, 12.5f));
            scenario.Tick();
            NetworkMessage? attack = scenario.Messages.FirstOrDefault(message =>
                message.Type == NetworkMessageType.AttackTargetRequest && message.TargetId == enemy.UnitId);
            Check(attack is { UnitIds.Length: > 0 and < 3 } && attack.SenderId == scenario.AI.Id,
                $"{lostType} loss does not suspend defender requests through the player network gateway");
            Check(scenario.AI.Controller.Threats.AntiInfantryNeed > AIThreatSnapshot.Baseline.AntiInfantryNeed,
                $"Threat observation remains active after {lostType} loss");
            if (lostType != "reaktor")
                Check(scenario.AI.Controller.Goal != AIGoalState.Scouting &&
                    scenario.AI.Controller.LastDecision.Contains("Core reconstruction:"),
                    $"{lostType} rebuild waits for resources while showing both maintenance and tactics");
            else
                Check(ArmyPowerStatus.Calculate(scenario.World.Units.GetArmyUnits(scenario.Army.Id), scenario.Army.Id).Balance < 0,
                    "Defense requests remain active during the power deficit");
            Check(scenario.Soldiers.All(unit => unit.AttackTargetId is null),
                "AI requests do not directly mutate authoritative combat targets");
            scenario.PumpHost();
            Check(attack!.UnitIds!.All(id => scenario.World.Units.FindById(id)?.AttackTargetId == enemy.UnitId),
                $"Real host confirmation applies defense targets during {lostType} loss");
            scenario.Remove(enemy);
            scenario.Messages.Clear();
            scenario.Tick();
            Check(scenario.Messages.Any(message => message.Type == NetworkMessageType.StopRequest &&
                message.UnitIds!.Order().SequenceEqual(attack.UnitIds!.Order())) &&
                scenario.Messages.Any(message => message.Type == NetworkMessageType.GotoRequest),
                $"Defenders return through normal requests when the alarm ends during {lostType} loss");
        }

        foreach (string lostType in new[] { "tiberium-refinery", "gdi-barracks" })
        {
            using var scenario = new Scenario();
            Building lost = lostType == "tiberium-refinery" ? scenario.Refinery : scenario.Barracks;
            scenario.Remove(lost);
            scenario.Tick(); // Enter maintenance while there are insufficient resources.
            Building replacement = scenario.AddBuilding(lostType, lost.Position, completed: false);
            scenario.Army.Resources = 10000;
            scenario.Messages.Clear();
            var enemy = scenario.AddVisibleEnemy(new(14.5f, 0, 12.5f));
            scenario.Tick();
            Check(scenario.Messages.Any(message => message.Type == NetworkMessageType.BuildConstructionRequest &&
                message.ConstructionSiteId == replacement.UnitId), $"Maintenance assigns a worker to the replacement {lostType}");
            Check(scenario.Messages.Any(message => message.Type == NetworkMessageType.AttackTargetRequest &&
                message.TargetId == enemy.UnitId), $"Defense continues during replacement {lostType} construction");
            Check(!scenario.Messages.Any(message => message.Type is NetworkMessageType.TrainUnitRequest or
                NetworkMessageType.ResearchRequest or NetworkMessageType.BuildRequest),
                "Discretionary production and expansion do not compete with core reconstruction");
            scenario.Army.Resources = 0;
            replacement.AdvanceConstruction(replacement.TotalBuildingPointsNeeded);
            scenario.Tick();
            if (lostType == "tiberium-refinery") scenario.Tick(); // Reissue harvesting, then finish the core plan.
            Check(scenario.AI.Controller.Goal == AIGoalState.Scouting &&
                !scenario.AI.Controller.LastDecision.Contains("Core reconstruction:"),
                "Completing reconstruction resumes normal AI planning without resetting tactics");
        }

        using (var scenario = new Scenario())
        {
            var leader = scenario.Add(new SquadLeader(new(8.5f, 0, 14.5f), scenario.NextId(), loadModel: false));
            var medic = scenario.Add(new Medic(new(9.5f, 0, 14.5f), scenario.NextId(), loadModel: false));
            int combatCount = scenario.AI.Controller.StrategyProfile!.RequiredGunners + scenario.AI.Controller.StrategyProfile.RequiredRakZero;
            var members = scenario.Soldiers.Skip(1).ToList(); // The first soldier is the reserved scout.
            while (members.Count < combatCount)
                members.Add(scenario.Add(new Gunner(new(10.5f + members.Count, 0, 14.5f), scenario.NextId())));
            members.Add(medic);
            scenario.ConfirmSquad(leader, members, UnitActionType.AssembleSquad);
            Guid enemyArmy = Guid.NewGuid();
            scenario.World.SimulationArmies.EnsureArmy(enemyArmy, Guid.NewGuid(), Guid.NewGuid());
            var objective = scenario.Add(new CatalogBuilding("vehicle-factory", new(58.5f, 0, 58.5f), scenario.NextId()), enemyArmy);
            scenario.World.Visibility.GetGrid(scenario.Army.Id).Reveal(new(58, 58), 0);
            for (int tick = 0; tick < 10; tick++) scenario.Tick();
            Check(scenario.Messages.Any(message => message.Type == NetworkMessageType.AttackTargetRequest &&
                message.TargetId == objective.UnitId && message.UnitIds!.Contains(leader.UnitId)),
                $"An assembled squad naturally begins a mission before infrastructure loss: {scenario.AI.Controller.LastDecision}");
            scenario.Remove(scenario.Refinery);
            scenario.Messages.Clear();
            scenario.Tick(); scenario.Tick();
            Check(scenario.Messages.Any(message => message.Type == NetworkMessageType.AttackTargetRequest &&
                message.TargetId == objective.UnitId && message.UnitIds!.Contains(leader.UnitId)),
                "Existing squad mission continues during refinery reconstruction");
            foreach (Soldier soldier in members.Append<Soldier>(leader)) soldier.HitPoints = 10;
            scenario.Messages.Clear();
            scenario.Tick();
            Check(scenario.Messages.Any(message => message.Type == NetworkMessageType.StopRequest && message.UnitIds!.Contains(leader.UnitId)) &&
                scenario.Messages.Any(message => message.Type == NetworkMessageType.GotoRequest && message.UnitIds!.Contains(leader.UnitId)) &&
                scenario.AI.Controller.LastDecision.Contains("retreating"),
                "Damaged squad issues Stop and retreat requests while the refinery is missing");
            scenario.Tick(); // Survivors are at the home rally point; the mission can complete.
            scenario.AddBuilding("tiberium-refinery", scenario.Refinery.Position);
            scenario.Messages.Clear();
            scenario.Tick(); scenario.Tick(); // Restart harvesting, then resume the preserved squad cycle.
            Check(scenario.Messages.Any(message => message.Type == NetworkMessageType.UnitActionRequest &&
                message.UnitActionType == UnitActionType.DisbandSquad),
                "Returned squad continues its preserved reinforcement/recovery cycle after reconstruction");
            scenario.ConfirmSquad(leader, members, UnitActionType.DisbandSquad);
            scenario.Remove(scenario.Barracks);
            scenario.Tick();
            Check(scenario.AI.Controller.LastDecision.Contains("Medic is treating") &&
                scenario.AI.Controller.LastDecision.Contains("Core reconstruction:"),
                "Active squad recovery remains observable during a later barracks loss");
            foreach (Soldier soldier in members.Append<Soldier>(leader)) soldier.HitPoints = soldier.MaxHitPoints;
            scenario.Tick(); // Recovery completes, but reassembly waits for the core producer.
            scenario.AddBuilding("gdi-barracks", scenario.Barracks.Position);
            scenario.Messages.Clear();
            scenario.Tick();
            Check(scenario.Messages.Any(message => message.Type == NetworkMessageType.UnitActionRequest &&
                message.UnitActionType == UnitActionType.AssembleSquad),
                "Healing progress recorded during reconstruction allows the recovered squad to reassemble");
        }

        using (var scenario = new Scenario(startReady: false))
        {
            scenario.Remove(scenario.Refinery);
            scenario.AddVisibleEnemy(new(14.5f, 0, 12.5f));
            scenario.Tick();
            Check(!scenario.Messages.Any(message => message.Type == NetworkMessageType.AttackTargetRequest),
                "Initial setup still gates advanced tactical controllers");
        }
        using (var scenario = new Scenario())
        {
            scenario.Remove(scenario.Refinery);
            scenario.AddVisibleEnemy(new(14.5f, 0, 12.5f));
            scenario.AI.BeginMatch(5678, scenario.Army.Id);
            scenario.Messages.Clear();
            scenario.Tick();
            Check(!scenario.Messages.Any(message => message.Type == NetworkMessageType.AttackTargetRequest),
                "Match restart clears tactical activation and stale controller missions");
            scenario.AI.SetStatus(AIPlayerStatus.Idle);
            scenario.Messages.Clear();
            scenario.Tick();
            Check(scenario.Messages.Count == 0, "Explicitly idle AI remains stopped during reconstruction");
            scenario.AI.SetStatus(AIPlayerStatus.Active);
            using var clientNetwork = new NetworkHandler("non-host AI check");
            scenario.AI.Controller.Update(new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), scenario.AI, scenario.World, clientNetwork);
            Check(clientNetwork.PendingMessages == 0, "Non-host never advances reconstruction or tactical commands");
        }
        using (var scenario = new Scenario())
        {
            scenario.Army.Resources = 10000;
            var factory = scenario.AddBuilding("vehicle-factory", new(26.5f, 0, 5.5f));
            scenario.AddBuilding("reaktor", new(30.5f, 0, 5.5f));
            var profile = scenario.AI.Controller.StrategyProfile!;
            var mobileThreats = new AIThreatAssessment(scenario.Army.Id);
            var armored = new AIArmoredSupportController(scenario.World, scenario.AI.Player,
                scenario.Network, profile, mobileThreats);
            for (int i = 0; i < profile.RequiredTanks; i++)
                scenario.Add(new CatalogVehicle("tank", new(30.5f + i, 0, 12.5f), scenario.NextId()));
            var tick = new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            armored.Update(tick); scenario.Network.Update();
            Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.TrainUnitRequest &&
                m.UnitId == factory.UnitId && m.UnitTypeId == "gepard"),
                "A full ground complement still orders one catalog-selected Gepard through the host gateway");
            Check(armored.HasOperationalFactory && !armored.IsReady,
                "Completed factory unlocks expansion while mobile air defense is still training");
            scenario.PumpHost();
            Check(factory.ProductionQueue.Orders.Count(o => o.UnitTypeId == "gepard") == 1 &&
                scenario.Army.Resources == 9200,
                "Host accepts Gepard production at the catalog price in the vehicle factory");
            scenario.Messages.Clear();
            armored.Update(tick); scenario.Network.Update();
            Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.TrainUnitRequest),
                "Queued air-defense unit satisfies its pending quota without duplicate purchases");
            factory.ProductionQueue.Update(8, out _);
            var gepard = scenario.Add(new CatalogVehicle("gepard", new(28.5f, 0, 12.5f), scenario.NextId()));
            armored.Update(tick);
            Check(armored.IsReady, "Ground and mobile air-defense complements reach Ready together");
            Guid threatArmy = Guid.NewGuid();
            scenario.World.SimulationArmies.EnsureArmy(threatArmy, Guid.NewGuid(), Guid.NewGuid());
            var airThreat = scenario.Add(new CatalogVehicle("helicopter", new(45.5f, 0, 45.5f), scenario.NextId()), threatArmy);
            mobileThreats.RecordLoss(scenario.Soldiers[0], airThreat, AICombatContext.BaseDefense);
            mobileThreats.RecordLoss(scenario.Soldiers[0], airThreat, AICombatContext.BaseDefense);
            mobileThreats.Update(tick, scenario.World);
            mobileThreats.Update(tick, scenario.World);
            scenario.Messages.Clear(); armored.Update(tick); scenario.Network.Update();
            Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.TrainUnitRequest && m.UnitTypeId == "gepard"),
                "Aircraft losses trigger additional mobile air defense after both original quotas were fulfilled");
            scenario.PumpHost();
            factory.ProductionQueue.Update(8, out _);
            scenario.Remove(gepard); scenario.Messages.Clear();
            armored.Update(tick); scenario.Network.Update();
            Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.TrainUnitRequest && m.UnitTypeId == "gepard"),
                "Destroyed mobile air defense is replaced even with a complete ground force");
            scenario.Army.Resources = 1599; scenario.Messages.Clear();
            armored.Update(tick); scenario.Network.Update();
            Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.TrainUnitRequest),
                "Air-defense production preserves the common 800-resource reserve");
            Check(AIArmoredSupportController.DesiredMobileAirDefenseCount(0.15f) == 1 &&
                AIArmoredSupportController.DesiredMobileAirDefenseCount(1.4f) == 2 &&
                AIArmoredSupportController.DesiredMobileAirDefenseCount(5) == 3,
                "Mobile anti-air demand has a baseline, scales with threat and stays bounded");
        }
        using (var scenario = new Scenario())
        {
            scenario.Army.Resources = 20000;
            scenario.AddBuilding("vehicle-factory", new(26.5f, 0, 5.5f));
            scenario.AddBuilding("communicationstower", new(30.5f, 0, 5.5f));
            scenario.AddBuilding("reaktor", new(34.5f, 0, 5.5f));
            AIProductionPlan airPlan = AIStrategicCatalog.Plan(scenario.World, scenario.Army.Id,
                GameplayCatalog.Find(PurchasableType.Unit, "helicopter")!);
            Check(airPlan.IsValid && airPlan.Steps.Select(s => s.TypeId).SequenceEqual(
                [ResearchProjects.AirTechnologyId, "helipad", "helicopter"]),
                "Air-combat goal resolves Air Technology, helipad and helicopter as executable catalog dependencies");
            scenario.Tick();
            Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.ResearchRequest &&
                m.UnitTypeId == ResearchProjects.AirTechnologyId),
                "AI starts Air Technology after factory completion even before its ground combat quota is ready");
            scenario.PumpHost();
            Check(scenario.Home.ProductionQueue.Orders.Any(o => o.UnitTypeId == ResearchProjects.AirTechnologyId),
                "Host accepts the air prerequisite through the normal research request");
            scenario.Messages.Clear(); scenario.Tick(); scenario.Tick();
            Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.ResearchRequest),
                "Active research continues while ground production waits without being ordered twice");
        }
        using (var scenario = new Scenario())
        {
            scenario.Army.Resources = 20000;
            scenario.AddBuilding("communicationstower", new(30.5f, 0, 5.5f));
            scenario.AddBuilding("reaktor", new(34.5f, 0, 5.5f));
            scenario.Army.Perks.GrantPermanent(PerkType.AirTechnology, scenario.NextId());
            var pad = scenario.AddBuilding("helipad", new(26.5f, 0, 5.5f));
            var first = scenario.Add(new CatalogVehicle("helicopter", new(28.5f, 0, 12.5f), scenario.NextId()));
            var infrastructure = new AIInfrastructureController(scenario.World, scenario.AI.Player, scenario.Network);
            var tick = new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            infrastructure.Update(tick); scenario.Network.Update();
            Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.TrainUnitRequest &&
                m.UnitId == pad.UnitId && m.UnitTypeId == "helicopter"),
                "Existing free helicopter counts toward the air force while the second is purchased at the helipad");
            scenario.PumpHost();
            Check(pad.ProductionQueue.Orders.Count(o => o.UnitTypeId == "helicopter") == 1,
                "Host accepts one additional helicopter into the normal production queue");
            scenario.Messages.Clear(); infrastructure.Update(tick); scenario.Network.Update();
            Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.TrainUnitRequest),
                "Pending helicopter is not purchased again while training");
            pad.ProductionQueue.Update(10, out _);
            scenario.Add(new CatalogVehicle("helicopter", new(29.5f, 0, 12.5f), scenario.NextId()));
            infrastructure.Update(tick);
            Check(infrastructure.IsAirSupportReady, "Two air attackers complete the air-force quota: " + infrastructure.LastDecision);
            scenario.Remove(first); scenario.Messages.Clear();
            infrastructure.Update(tick); scenario.Network.Update();
            Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.TrainUnitRequest && m.UnitTypeId == "helicopter"),
                "Lost helicopter is replaced through a new catalog production plan");
        }
        using (var scenario = new Scenario())
        {
            var leader = scenario.Add(new SquadLeader(new(8.5f, 0, 14.5f), scenario.NextId(), loadModel: false));
            var medic = scenario.Add(new Medic(new(9.5f, 0, 14.5f), scenario.NextId(), loadModel: false));
            var members = scenario.Soldiers.Skip(1).Append<Soldier>(medic).ToArray();
            scenario.ConfirmSquad(leader, members, UnitActionType.AssembleSquad);
            var gepard = scenario.Add(new CatalogVehicle("gepard", new(15.5f, 0, 14.5f), scenario.NextId()));
            var helicopter = scenario.Add(new CatalogVehicle("helicopter", new(16.5f, 0, 14.5f), scenario.NextId()));
            var resupplying = new MissionHelicopter(new(24.5f, 0, 14.5f), scenario.NextId());
            scenario.Add(resupplying);
            Check(resupplying.IsReadyForCombatMission, "Refueled landed helicopter can start an AI combat mission");
            resupplying.TakeOff(scenario.World); resupplying.TakeOff(scenario.World);
            for (int round = 0; round < resupplying.MaximumAmmunition; round++) resupplying.TryConsumeAmmunition();
            Check(!resupplying.IsReadyForCombatMission && resupplying.Ammunition == 0,
                "Helicopter with empty ammunition stays unavailable to assault orders");
            Check(resupplying.ReturnToHelipad(scenario.World) && !resupplying.IsReadyForCombatMission,
                "Automatic landing or supply return is not taken over by a new AI combat mission");
            Guid enemyArmy = Guid.NewGuid();
            scenario.World.SimulationArmies.EnsureArmy(enemyArmy, Guid.NewGuid(), Guid.NewGuid());
            var objective = scenario.Add(new CatalogBuilding("vehicle-factory", new(58.5f, 0, 58.5f), scenario.NextId()), enemyArmy);
            scenario.World.Visibility.GetGrid(scenario.Army.Id).Reveal(new(58, 58), 0);
            var assault = new AISquadAssaultController(scenario.World, scenario.AI.Id, scenario.Army.Id, scenario.Network,
                scenario.AI.Controller.StrategyProfile! with { AttackReadinessSeconds = 0, RequiredGunners = 3, RequiredRakZero = 0 });
            assault.Update(new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))); scenario.Network.Update();
            Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.FollowRequest &&
                m.TargetId == leader.UnitId && m.UnitIds!.Contains(gepard.UnitId)),
                "Air-only Gepard escorts follow the squad while it attacks a ground building");
            Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.AttackTargetRequest &&
                m.TargetId == objective.UnitId && m.UnitIds!.Contains(helicopter.UnitId)),
                "Catalog air-combat unit joins the squad assault through ordinary attack commands");
            Check(!scenario.Messages.Any(m => m.UnitIds?.Contains(resupplying.UnitId) == true),
                "Assault controller emits no movement or combat request for a helicopter returning for supplies");
            var enemyAir = scenario.Add(new CatalogVehicle("helicopter", new(14.5f, 0, 16.5f), scenario.NextId()), enemyArmy);
            scenario.World.Visibility.GetGrid(scenario.Army.Id).Reveal(new(14, 16), 0);
            scenario.Messages.Clear();
            assault.Update(new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1))); scenario.Network.Update();
            Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.AttackTargetRequest &&
                m.TargetId == enemyAir.UnitId && m.UnitIds!.SequenceEqual([gepard.UnitId])),
                "A single suitable air-defense escort engages nearby aircraft without incompatible infantry orders");
        }
        return checks + RunOrderProgressChecks() + RunOrderQueueChecks() + RunResourcePlanningChecks() + RunReservationChecks() + RunOrderResultChecks() + RunScoutingReservationChecks() + RunScoutingReachabilityChecks() + RunStreamingScoutingChecks() + RunEnemyKnowledgeChecks() + RunDefenseStrengthChecks() + RunUnitTaskChecks();
    }
}

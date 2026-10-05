using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

internal static class SessionLifecycleChecks
{
    private sealed class CatalogBuilding(string type, Vector3 position, Guid id, int price = 0) : Building(position, id, price)
    {
        public override string GameplayTypeId => type;
        public void Initialize() { ApplyCatalogMetadata(); Width = Length = 1; }
    }
    private sealed class Storage : TiberiumRefinery
    {
        public Storage(Vector3 position, Guid id, int price) : base(position, id, purchasePrice: price, loadModel: false)
        { IncludedUnitGranted = true; Width = Length = 1; }
    }
    private sealed class Crew : Soldier
    {
        public Crew(Vector3 position, Guid id) : base(position, id, loadModel: false) { IsCrewMember = true; }
        public override string GameplayTypeId => "engineer";
    }
    private sealed class RocketShooter(Vector3 position, Guid id) : Unit(position, id)
    {
        public override ProjectileKind ProjectileKind => ProjectileKind.Rocket;
        public override void PlayShotEffects() { }
    }
    private static Unit? Create(string type, Vector3 position, Guid id, Guid owner, int price)
    {
        Unit unit;
        switch (type)
        {
            case "harvester": unit = new Harvester(position, id, loadModel: false); break;
            case "soldier": unit = new Soldier(position, id, loadModel: false); break;
            case "engineer": unit = new Crew(position, id); break;
            case "car": unit = new Car(position, id); break;
            case "gdi-bulldozer": unit = new GDIBulldozer(position, id, loadModel: false); break;
            case "tiberium-refinery": unit = new Storage(position, id, price); break;
            default:
                if (!BuildingFactory.CanCreate(type)) return null;
                var building = new CatalogBuilding(type, position, id, price);
                building.Initialize(); unit = building; break;
        }
        unit.SetCreatorPlayer(owner);
        return unit;
    }

    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        using var network = new NetworkHandler("lifecycle-host");
        int port = network.CreateSessionAsync("lifecycle").GetAwaiter().GetResult();
        var hostWorld = SimulationFixture.World();
        var armies = hostWorld.SimulationArmies;
        Guid owner = network.LocalPeerId, armyId = Guid.NewGuid();
        var player = new Player(owner, "lifecycle-owner"); player.SetArmy(armyId);
        Army army = armies.EnsureArmy(armyId, owner);
        army.Resources = 4321;
        army.Perks.GrantPermanent(PerkType.DetailedHealth, Guid.NewGuid());
        army.GrantedPermissions[Guid.NewGuid()] = ArmyPermission.CommandUnits;
        army.Intelligence.ShareExploredMinimap = true;
        var state = new SessionStateService(hostWorld, armies, Create);
        using var input = new NetworkInput(network, hostWorld, armies, state);
        var host = new NetworkHost(network, input, hostWorld, armies, () => new[] { player },
            () => Array.Empty<AIPlayer>(), (_, _) => { }, () => true);
        var commands = new List<NetworkMessage>();
        network.MessageReceived += message => commands.Add(message);
        double now = 0;
        void Tick(double elapsed = 0)
        {
            network.Update(); now += elapsed;
            host.Update(new GameTime(TimeSpan.FromSeconds(now), TimeSpan.FromSeconds(elapsed)));
        }
        T Add<T>(T unit, bool place = true) where T : Unit
        {
            unit.SetCreatorPlayer(owner); unit.SetArmy(armyId); hostWorld.Units.Register(unit);
            if (place)
            {
                bool placed = unit is MobileUnit mobile ? hostWorld.GameGrid.TryMove(mobile, hostWorld.GameGrid.ToCell(unit.Position))
                    : hostWorld.GameGrid.TryPlace(unit, unit.Position, 0);
                if (!placed) throw new Exception("Lifecycle fixture placement failed.");
            }
            return unit;
        }
        var research = (CatalogBuilding)Create("gdi-base", new(5.5f, 0, 5.5f), Guid.NewGuid(), owner, 1000)!;
        research.AdvanceConstruction(research.TotalBuildingPointsNeeded); Add(research);
        Guid researchId = Guid.NewGuid();
        research.ProductionQueue.Enqueue(researchId, "air-technology", owner, 1);
        research.ProductionQueue.Update(.25f, out _);
        var factory = (CatalogBuilding)Create("vehicle-factory", new(10.5f, 0, 10.5f), Guid.NewGuid(), owner, 900)!;
        factory.AdvanceConstruction(factory.TotalBuildingPointsNeeded); Add(factory);
        factory.ProductionQueue.Enqueue(Guid.NewGuid(), "jeep", owner, 100);
        factory.ProductionQueue.Enqueue(Guid.NewGuid(), "tank", owner, 200);
        factory.ProductionQueue.Update(3, out _);
        var storage = Add(new Storage(new(20.5f, 0, 20.5f), Guid.NewGuid(), 1500));
        storage.AdvanceConstruction(storage.TotalBuildingPointsNeeded); storage.StoreResources(700);
        var harvester = (Harvester)Create("harvester", new(28.5f, 0, 28.5f), Guid.NewGuid(), owner, 0)!;
        Add(harvester);
        var driver = Add(new Soldier(harvester.Position, Guid.NewGuid(), loadModel: false), false);
        driver.SetWeapon(Soldier.Weapon.Brok17);
        Check(hostWorld.Units.EmbarkUnit(driver.UnitId, harvester.UnitId, OccupantRole.Driver), "Fixture driver enters harvester");
        var crew = Add(new Crew(research.Position, Guid.NewGuid()), false);
        Check(hostWorld.Units.EmbarkUnit(crew.UnitId, research.UnitId, OccupantRole.Crew), "Fixture engineer enters research building");
        var leaving = Add(new Car(factory.Position, Guid.NewGuid()), false);
        leaving.BeginLeavingBuilding(factory.UnitId, new(14.5f, 0, 10.5f));
        hostWorld.Tiberium.ApplySeed(new(30, 30, 0, 100, 0, 0, 1, 1, 1));
        var marker = hostWorld.GameplayMarkers.Add(GameplayMarkerType.ResourceField, new(30, 0, 30), 0);
        network.ApplyLocalCommand(NetworkCommands.CreateHarvestRequest(owner, harvester.UnitId, new(30.5f, 0, 30.5f)));
        Tick();
        network.ApplyLocalCommand(ComplexCommandPayloads.Create(owner,
            new HarvestCommandPayload(harvester.UnitId, HarvestPhase.DrivingToField, 284)));
        network.ApplyLocalCommand(ComplexCommandPayloads.Create(owner,
            new GotoCommandPayload(owner, [harvester.UnitId], new(30.5f, 0, 30.5f),
                [new(harvester.UnitId, [new(29, 29), new(30, 30)], 30.5f, 30.5f)])));
        network.ApplyLocalCommand(ComplexCommandPayloads.Create(owner,
            new GotoCommandPayload(owner, [harvester.UnitId], new(34.5f, 0, 30.5f),
                [new(harvester.UnitId, [new(31, 30), new(32, 30), new(33, 30), new(34, 30)], 34.5f, 30.5f)], AppendToQueue: true)));
        hostWorld.Visibility.GetGrid(armyId).Reveal(new(28, 28), 3);
        SessionSnapshot snapshot = state.Capture(host.HostTime, true);
        Check(snapshot.Units.Length == 7 && snapshot.IsMatchStarted, "Snapshot captures all live units and match state");
        Check(snapshot.Units.Single(unit => unit.UnitId == leaving.UnitId).BuildingExit?.SourceBuildingId == factory.UnitId,
            "Snapshot preserves in-progress factory exit without a pathfinding job");
        network.SetSessionSnapshotProvider(() => new(NetworkMessageType.SessionSnapshot, owner, SessionSnapshot: state.Capture(host.HostTime, true)));
        network.SetHostTimeProvider(() => host.HostTime);
        using var client = new NetworkHandler("lifecycle-client");
        var clientWorld = SimulationFixture.World();
        var clientState = new SessionStateService(clientWorld, clientWorld.SimulationArmies, Create);
        using var clientInput = new NetworkInput(client, clientWorld, clientWorld.SimulationArmies, clientState);
        void PumpUntil(Func<bool> done)
        {
            var timer = Stopwatch.StartNew();
            while (!done() && timer.ElapsedMilliseconds < 5000) { network.Update(); client.Update(); Thread.Sleep(5); }
            if (!done()) throw new Exception("Lifecycle TCP timeout.");
        }
        client.JoinSessionAsync("127.0.0.1", port).GetAwaiter().GetResult();
        PumpUntil(() => client.Status == NetworkConnectionStatus.Connected);
        Army replicaArmy = clientWorld.SimulationArmies.Find(armyId)!;
        var replicaResearch = (Building)clientWorld.Units.FindById(research.UnitId)!;
        var replicaFactory = (Building)clientWorld.Units.FindById(factory.UnitId)!;
        var replicaHarvester = (Harvester)clientWorld.Units.FindById(harvester.UnitId)!;
        var replicaLeaving = (MobileUnit)clientWorld.Units.FindById(leaving.UnitId)!;
        Check(clientState.IsMatchStarted && clientWorld.Units.Count == hostWorld.Units.Count, "Actual late join reconstructs every live identity before SessionReady");
        Check(replicaArmy.Resources == army.Resources && replicaArmy.Perks.Has(PerkType.DetailedHealth), "Late join preserves resources and completed research grants");
        Check(replicaArmy.GrantedPermissions.SequenceEqual(army.GrantedPermissions) && replicaArmy.Intelligence.ShareExploredMinimap,
            "Late join preserves control permissions and intelligence settings");
        Check(replicaResearch.ProductionQueue.ActiveOrder!.OrderId == researchId && replicaResearch.ProductionQueue.ActiveOrder.ElapsedSeconds == .25f,
            "Late join restores pending research identity and elapsed time");
        Check(replicaFactory.ProductionQueue.Orders.Select(order => order.UnitTypeId).SequenceEqual(new[] { "jeep", "tank" }) &&
            replicaFactory.ProductionQueue.ActiveOrder!.ElapsedSeconds == 3, "Late join restores ordered production and elapsed time");
        Check(((Building)clientWorld.Units.FindById(storage.UnitId)!).StoredResources == 700 && replicaResearch.PurchasePrice == 1000,
            "Late join preserves storage contents and paid building price");
        Check(replicaHarvester.CargoAmount == 284 && replicaHarvester.HarvestPhase == HarvestPhase.DrivingToField &&
            replicaHarvester.PlannedPath.SequenceEqual(harvester.PlannedPath), "Late join preserves harvest cargo, phase and confirmed route");
        Check(replicaHarvester.LastQueuedTarget == harvester.LastQueuedTarget,
            "Late join preserves queued movement endpoints without recomputing their routes");
        Check(((Soldier)clientWorld.Units.FindById(driver.UnitId)!).EquippedWeapon == Soldier.Weapon.Brok17,
            "Late join restores the driver's pistol rather than the Soldier constructor default");
        Check(clientWorld.Units.FindById(driver.UnitId)!.ContainerUnitId == harvester.UnitId &&
            !clientWorld.GameGrid.IsRegistered(clientWorld.Units.FindById(driver.UnitId)!), "Embarked driver overlaps vehicle without separate grid occupancy");
        Check(replicaResearch.Occupancy!.Count(OccupantRole.Crew) == 1 && clientWorld.Units.FindById(crew.UnitId)!.ContainerUnitId == research.UnitId,
            "Late join restores engineer role rather than a generic garrison");
        Check(replicaLeaving.IsLeavingBuilding && replicaLeaving.SpawnExitPosition == leaving.SpawnExitPosition &&
            !clientWorld.GameGrid.IsRegistered(replicaLeaving), "Late join restores interior production transit without occupying the factory");
        Check(clientWorld.GameGrid.GetOccupant(new(10, 10))?.UnitId == factory.UnitId,
            "Production transit cannot overwrite the reconstructed factory footprint");
        Check(clientWorld.PathfindingManager.PendingRequests == 0 && clientWorld.PathfindingManager.Scheduler.PendingJobs == 0,
            "Late join creates neither host harvest jobs nor client route searches");
        Check(clientWorld.GameplayMarkers.Markers.Single().Id == marker.Id && clientWorld.Tiberium.GetStates().Length == 1,
            "Late join carries authored markers and current lightweight resources");
        Check(clientWorld.Visibility.GetGrid(armyId).GetSnapshot().SequenceEqual(hostWorld.Visibility.GetGrid(armyId).GetSnapshot()),
            "Late join restores explored and visible cells");
        // Reapply the same host snapshot: no duplicate occupants, charges or included units.
        client.ApplyLocalCommand(new(NetworkMessageType.SessionSnapshot, owner, SessionSnapshot: snapshot));
        Check(clientWorld.Units.Count == snapshot.Units.Length && clientWorld.SimulationArmies.Find(armyId)!.Resources == 4321,
            "Snapshot replacement is repeatable without charging or spawning duplicates");
        Tick(.4); // Crew does not grant a speed bonus in this fixture.
        PumpUntil(() => ((Building)clientWorld.Units.FindById(factory.UnitId)!).ProductionQueue.ActiveOrder!.ElapsedSeconds > 3);
        Check(clientWorld.SimulationArmies.Find(armyId)!.Resources == army.Resources, "Replicated production progress does not spend resources again");
        Tick(.7);
        PumpUntil(() => clientWorld.SimulationArmies.Find(armyId)!.Perks.Has(PerkType.AirTechnology));
        Check(army.Perks.Has(PerkType.AirTechnology) && research.ProductionQueue.Orders.Count == 0,
            "Host completes research once and late-joined client receives its permanent perk");
        Check(clientWorld.SimulationArmies.Find(armyId)!.Resources == army.Resources, "Research completion does not duplicate a debit or refund");

        // A second real late join during unloading must never run or credit the host job.
        network.ApplyLocalCommand(new(NetworkMessageType.HarvesterReturnRequest, owner, UnitId: harvester.UnitId));
        Tick();
        hostWorld.GameGrid.Remove(harvester);
        harvester.SetPosition(storage.GetResourceUnloadPosition());
        Check(hostWorld.GameGrid.TryMove(harvester, hostWorld.GameGrid.ToCell(harvester.Position)), "Unload fixture occupies its accepted approach");
        Tick(); Tick();
        Check(harvester.HarvestPhase == HarvestPhase.Unloading && host.Harvest.ActiveJobCount == 1,
            "Host owns an active timed unload before the second join");
        using (var unloadingClient = new NetworkHandler("unloading-client"))
        {
            var unloadingWorld = SimulationFixture.World();
            var unloadingState = new SessionStateService(unloadingWorld, unloadingWorld.SimulationArmies, Create);
            using var unloadingInput = new NetworkInput(unloadingClient, unloadingWorld, unloadingWorld.SimulationArmies, unloadingState);
            unloadingClient.JoinSessionAsync("127.0.0.1", port).GetAwaiter().GetResult();
            var timer = Stopwatch.StartNew();
            while (unloadingClient.Status != NetworkConnectionStatus.Connected && timer.ElapsedMilliseconds < 5000)
            { network.Update(); client.Update(); unloadingClient.Update(); Thread.Sleep(5); }
            Check(unloadingClient.Status == NetworkConnectionStatus.Connected &&
                ((Harvester)unloadingWorld.Units.FindById(harvester.UnitId)!).HarvestPhase == HarvestPhase.Unloading,
                "Actual TCP late join receives timed unloading phase and cargo");
            Check(unloadingWorld.PathfindingManager.Scheduler.PendingJobs == 0 && unloadingWorld.SimulationArmies.Find(armyId)!.Resources == army.Resources,
                "Late join during unloading cannot create a search or credit cargo locally");
            int beforeUnload = army.Resources;
            float cargoBeforeUnload = harvester.CargoAmount;
            Tick(2.1); Tick();
            PumpUntil(() => ((Harvester)clientWorld.Units.FindById(harvester.UnitId)!).CargoAmount == 0 &&
                ((Building)clientWorld.Units.FindById(storage.UnitId)!).StoredResources > 700);
            timer.Restart();
            while (((Harvester)unloadingWorld.Units.FindById(harvester.UnitId)!).CargoAmount != 0 && timer.ElapsedMilliseconds < 5000)
            { unloadingClient.Update(); Thread.Sleep(5); }
            Check(army.Resources == beforeUnload + (int)cargoBeforeUnload && storage.StoredResources == 700 + cargoBeforeUnload,
                "Only host unloads once into storage and army resources");
            Check(unloadingWorld.SimulationArmies.Find(armyId)!.Resources == army.Resources &&
                ((Harvester)unloadingWorld.Units.FindById(harvester.UnitId)!).CargoAmount == 0,
                "Second client receives absolute unload result without duplicate transfer");
            Tick(.1); Tick();
            Check(army.Resources == beforeUnload + (int)cargoBeforeUnload && host.Harvest.ActiveJobCount == 0,
                "Completed manual unload ends the host job without a second credit");
        }

        // Pending host planning, stop and destruction travel through real command application.
        var moving = Add(new Soldier(new(34.5f, 0, 5.5f), Guid.NewGuid(), loadModel: false));
        network.ApplyLocalCommand(NetworkCommands.CreateGotoRequest(owner, [moving.UnitId], 35.5f, 0, 35.5f)); Tick();
        Check(hostWorld.PathfindingManager.Scheduler.PendingJobs > 0, "Host begins budgeted movement planning");
        network.ApplyLocalCommand(NetworkCommands.CreateStopRequest(owner, [moving.UnitId]));
        for (int i = 0; i < 200 && hostWorld.PathfindingManager.Scheduler.PendingJobs > 0; i++)
        { hostWorld.PathfindingManager.Update(1); Tick(); }
        Tick();
        Check(moving.CurrentCommand is null && moving.MovementStatus != MovementStatus.Planning &&
            !commands.Any(message => message.Type == NetworkMessageType.GotoCommand && message.UnitIds!.Contains(moving.UnitId)),
            "Stop during planning prevents every stale route from resurrecting movement");
        network.ApplyLocalCommand(NetworkCommands.CreateGotoRequest(owner, [moving.UnitId], 35.5f, 0, 35.5f)); Tick();
        hostWorld.Units.Destroy(moving.UnitId);
        for (int i = 0; i < 200 && hostWorld.PathfindingManager.Scheduler.PendingJobs > 0; i++)
        { hostWorld.PathfindingManager.Update(1); Tick(); }
        Tick();
        Check(!commands.Any(message => message.Type == NetworkMessageType.GotoCommand && message.UnitIds!.Contains(moving.UnitId)),
            "Destroyed issuer cannot publish its pending route");
        research.ProductionQueue.Enqueue(Guid.NewGuid(), "air-technology", owner, .1f);
        int completedResearch = commands.Count(message => message.Type == NetworkMessageType.ResearchCompletedCommand);
        network.ApplyLocalCommand(NetworkCommands.CreateDestroyUnitCommand(owner, research.UnitId));
        Check(research.ProductionQueue.Orders.Count == 0 && research.IsDying,
            "Destroyed producer clears queued work immediately");
        Tick(.2);
        Check(commands.Count(message => message.Type == NetworkMessageType.ResearchCompletedCommand) == completedResearch,
            "Destroyed producer cannot grant the pending research or refund its cost");

        // Rejected start retains a still-valid route; accepted repeated starts clear old work.
        network.ApplyLocalCommand(NetworkCommands.CreateGotoRequest(owner, [harvester.UnitId], 35.5f, 0, 35.5f)); Tick();
        network.ApplyLocalCommand(NetworkCommands.CreateStartMultiplayerGameRequest(owner));
        for (int i = 0; i < 300 && hostWorld.PathfindingManager.Scheduler.PendingJobs > 0; i++)
        { hostWorld.PathfindingManager.Update(1); Tick(); }
        Tick();
        Check(harvester.CurrentCommand is not null && harvester.MovementStatus != MovementStatus.Planning,
            "Rejected game-start leaves the admitted movement plan valid");
        var start = hostWorld.GameplayMarkers.Add(GameplayMarkerType.PlayerStart, new(3.5f, 0, 30.5f), 90);
        var medic = Add(new Medic(new(30.5f, 0, 4.5f), Guid.NewGuid(), loadModel: false));
        var patient = Add(new Soldier(new(35.5f, 0, 4.5f), Guid.NewGuid(), loadModel: false));
        patient.HitPoints = 40;
        network.ApplyLocalCommand(NetworkCommands.CreateHarvestRequest(owner, harvester.UnitId, new(30.5f, 0, 30.5f)));
        Tick(); Tick();
        Check(host.Medics.ActiveJobCount == 1 && host.Harvest.ActiveJobCount == 1,
            "Restart fixture has actual active healing and harvest jobs");
        hostWorld.Projectiles.Fire(new(1, 5, 1), new(35, 0, 35));
        clientWorld.Projectiles.Fire(new(1, 5, 1), new(35, 0, 35));
        Check(hostWorld.Projectiles.ActiveProjectileCount == 1 && clientWorld.Projectiles.ActiveProjectileCount == 1,
            "Restart fixture contains transient projectile presentation on both worlds");
        var rocketShooter = Add(new RocketShooter(new(30.5f, 1, 15.5f), Guid.NewGuid()) { Behavior = UnitBehavior.Passive });
        host.Combat.ResolveAttackAsync(NetworkCommands.CreateAttackRequest(owner, [rocketShooter.UnitId], 35, 0, 35)).GetAwaiter().GetResult();
        Check(host.Combat.ActiveProjectileCount == 1, "Restart fixture contains an actual authoritative rocket in flight");
        GDIBulldozer? previousEarthworkWorker = null;
        for (int restart = 0; restart < 2; restart++)
        {
            network.ApplyLocalCommand(NetworkCommands.CreateStartMultiplayerGameRequest(owner)); Tick();
            NetworkMessage started = commands.Last(message => message.Type == NetworkMessageType.StartMultiplayerGameCommand);
            Guid dozerId = started.MatchStartAssignments![0].BulldozerId;
            PumpUntil(() => clientWorld.Units.FindById(dozerId) is not null);
            Check(hostWorld.Units.Count == 2 && clientWorld.Units.Count == 2 && hostWorld.Units.FindById(dozerId) is GDIBulldozer,
                "Each game-start replaces old units with exactly one bulldozer and its driver");
            Check(army.Resources == 10000 && clientWorld.SimulationArmies.Find(armyId)!.Resources == 10000 &&
                !army.Perks.Has(PerkType.AirTechnology) && !clientWorld.SimulationArmies.Find(armyId)!.Perks.Has(PerkType.AirTechnology),
                "Each game-start resets resources and research on host and client");
            Check(hostWorld.PathfindingManager.Scheduler.PendingJobs == 0 && clientWorld.PathfindingManager.Scheduler.PendingJobs == 0,
                "Each game-start discards previous planning and client recovery jobs");
            Check(host.Harvest.ActiveJobCount == 0 && host.Medics.ActiveJobCount == 0 && host.Earthworks.ActiveJobs == 0 &&
                host.Combat.ActiveProjectileCount == 0 && host.Combat.PendingImpactCount == 0,
                "Each game-start leaves no host harvest, medic, earthwork or projectile jobs");
            Check(hostWorld.Projectiles.ActiveProjectileCount == 0 && clientWorld.Projectiles.ActiveProjectileCount == 0 &&
                hostWorld.Visibility.GetSnapshot().Length == 0 && clientWorld.Visibility.GetSnapshot().Length == 0,
                "Each game-start clears projectile presentation and previous fog discovery");
            if (previousEarthworkWorker is not null)
                Check(previousEarthworkWorker.EarthworkOrder is null && previousEarthworkWorker.MoveSpeed == 7,
                    "Reset ends the previous worker's earthwork mode and restores its driving parameters");
            if (restart == 0)
            {
                var worker = hostWorld.Units.GetSnapshot().OfType<GDIBulldozer>().Single();
                network.ApplyLocalCommand(new(NetworkMessageType.EarthworkRequest, owner, UnitId: worker.UnitId,
                    EarthworkKind: EarthworkKind.LevelAndConcrete, X: 15.5f, Z: 30.5f)); Tick();
                previousEarthworkWorker = worker;
                Check(host.Earthworks.ActiveJobs == 1 && worker.EarthworkOrder is not null,
                    "Restart fixture owns an active earthwork job");
                SessionSnapshot earthworkSnapshot = state.Capture(host.HostTime, true);
                client.ApplyLocalCommand(System.Text.Json.JsonSerializer.Deserialize<NetworkMessage>(
                    System.Text.Json.JsonSerializer.Serialize(new NetworkMessage(NetworkMessageType.SessionSnapshot, owner,
                        SessionSnapshot: earthworkSnapshot), NetworkJson.Options), NetworkJson.Options)!);
                var replicaWorker = (GDIBulldozer)clientWorld.Units.FindById(worker.UnitId)!;
                Check(replicaWorker.EarthworkOrder?.Id == worker.EarthworkOrder!.Id &&
                    replicaWorker.EarthworkSequence == worker.EarthworkSequence && clientWorld.PathfindingManager.Scheduler.PendingJobs == 0,
                    "Snapshot restores replicated earthwork mode and sequence without resuming a host job");
            }
            Check(hostWorld.Tiberium.GetStates().Length == 1 && hostWorld.GameplayMarkers.Markers.Contains(start),
                "Match restart retains published terrain resources and authored markers");
        }
        var newDozer = hostWorld.Units.GetSnapshot().OfType<GDIBulldozer>().Single();
        network.ApplyLocalCommand(NetworkCommands.CreateGotoRequest(owner, [newDozer.UnitId], 30.5f, 0, 3.5f)); Tick();
        Check(hostWorld.PathfindingManager.Scheduler.PendingJobs > 0, "Session-change fixture has live planning");
        network.Disconnect(); host.EnsureSessionGeneration();
        Check(hostWorld.PathfindingManager.Scheduler.PendingJobs == 0 && newDozer.CurrentCommand is null && host.HostTime == 0,
            "Session generation change clears searches, temporary movement intent and host clock");
        int oldCount = commands.Count;
        hostWorld.PathfindingManager.Update(100);
        host.Update(new GameTime());
        Check(commands.Count == oldCount, "Old session callbacks cannot publish after disconnect");
        return checks;
    }
}

using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

internal static class HarvestSystemChecks
{
    private static T Empty<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object target, Type type, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var savedWorld = Globals.World;
        var savedGame = Globals.Game;
        using var network = new NetworkHandler();
        try
        {
            var terrain = Empty<Terrain>();
            Set(terrain, typeof(Terrain), "<Width>k__BackingField", 41);
            Set(terrain, typeof(Terrain), "<Height>k__BackingField", 41);
            Set(terrain, typeof(Terrain), "HeightMap", new float[41 * 41]);
            var grid = new GameGrid(40, 40, 1); grid.BindTerrain(terrain);
            var world = Empty<GameWorld>();
            var units = new UnitHandler();
            var tiberium = new TiberiumHandler();
            Set(world, typeof(GameWorld), "_terrain", terrain);
            Set(world, typeof(GameWorld), "<GameGrid>k__BackingField", grid);
            Set(world, typeof(GameWorld), "<Units>k__BackingField", units);
            Set(world, typeof(GameWorld), "<Tiberium>k__BackingField", tiberium);
            Set(world, typeof(GameWorld), "<Markers>k__BackingField", new MarkerHandler());
            Set(world, typeof(GameWorld), "<PathfindingManager>k__BackingField", new PathfindingManager(world));
            var armies = new ArmyHandler();
            Guid owner = network.LocalPeerId, armyId = Guid.NewGuid();
            Army army = armies.EnsureArmy(armyId, owner);
            var game = Empty<RTSGame>();
            Set(game, typeof(RTSGame), "<World>k__BackingField", world);
            Set(game, typeof(RTSGame), "<Armies>k__BackingField", armies);
            Set(game, typeof(RTSGame), "<Network>k__BackingField", network);
            Globals.World = world; Globals.Game = game;
            var list = (List<Unit>)typeof(UnitHandler).GetField("_units", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(units)!;
            var harvester = new Harvester(new Vector3(5.5f, 0, 5.5f), Guid.NewGuid());
            var silo = new TiberiumRefinery(new Vector3(25.5f, 0, 25.5f), Guid.NewGuid());
            silo.AdvanceConstruction(silo.TotalBuildingPointsNeeded);
            Set(harvester, typeof(Unit), "<ArmyId>k__BackingField", armyId);
            Set(silo, typeof(Unit), "<ArmyId>k__BackingField", armyId);
            list.Add(harvester); list.Add(silo);
            var input = new NetworkInput(network);
            var commands = new List<NetworkMessage>();
            Task Publish(NetworkMessage command) { commands.Add(command); network.ApplyLocalCommand(command); return Task.CompletedTask; }
            Func<bool>? valid = null;
            Action<Point?, NetworkMessage?>? completion = null;
            Action? cancelled = null;
            int planCount = 0;
            void Plan(Harvester unit, IEnumerable<(Point? Resource, Vector3 Position)> candidates,
                Func<bool> isValid, Action<Point?, NetworkMessage?> done, Action abort)
            { planCount++; valid = isValid; completion = done; cancelled = abort; }
            long session = 0, version = 0;
            var system = new HarvestSystem(world, armies, owner, Publish, Plan, () => session, _ => version);
            NetworkMessage StartRequest() => new(NetworkMessageType.HarvestRequest, owner,
                UnitId: harvester.UnitId, X: 5.5f, Z: 5.5f);
            void Start() { var command = system.TryStart(StartRequest()); if (command is null) throw new Exception("Harvest fixture start rejected."); Publish(command).GetAwaiter().GetResult(); }
            void Cargo(float amount, HarvestPhase phase = HarvestPhase.DrivingToField) =>
                Publish(new(NetworkMessageType.HarvestCommand, owner, UnitId: harvester.UnitId, HarvestPhase: phase, CargoAmount: amount)).GetAwaiter().GetResult();
            double now = 0;
            void Tick(float seconds = 0) { now += seconds; system.UpdateAsync(new GameTime(TimeSpan.FromSeconds(now), TimeSpan.FromSeconds(seconds)), now).GetAwaiter().GetResult(); }
            void Seed(Point cell) => tiberium.ApplySeed(new(cell.X, cell.Y, now, 100, 0, 0, 1, 1, 1));

            Check(system.TryStart(StartRequest() with { SenderId = Guid.NewGuid() }) is null && system.ActiveJobCount == 0,
                "HarvestSystem rejects foreign requests before creating jobs");
            Start(); Tick();
            Check(system.ActiveJobCount == 1 && planCount == 0 && harvester.CargoAmount == 0,
                "Empty fields keep an empty harvester waiting without issuing movement");
            system.Reset(); Start(); Cargo(284.6f); Seed(new Point(5, 5)); Tick();
            Check(harvester.HarvestPhase == HarvestPhase.Harvesting && harvester.CurrentCommand is null,
                "HarvestSystem begins stationary harvesting in reach");
            Tick(1);
            Check(harvester.CargoAmount == 300 && harvester.HarvestPhase == HarvestPhase.ReturningToSilo &&
                tiberium.Cells[new Point(5, 5)].Amount > 84 && tiberium.Cells[new Point(5, 5)].Amount < 85,
                "HarvestSystem clamps cargo at capacity and begins return after harvesting");
            Tick();
            Check(planCount == 1 && valid!(), "Returning harvester requests an incremental storage approach");
            completion!(null, null); int beforeRetry = planCount; Tick(1);
            Check(planCount == beforeRetry && harvester.CargoAmount == 300,
                "Unreachable storage approach preserves cargo and waits before retrying");
            Tick(3);
            Check(planCount == beforeRetry + 1, "Failed storage approach is retried after its bounded delay");
            system.Reset(); int beforeReset = commands.Count;
            Check(!valid!(), "Reset invalidates an outstanding harvest route job");
            completion!(null, new(NetworkMessageType.GotoCommand, owner, UnitIds: [harvester.UnitId]));
            Check(commands.Count == beforeReset && system.ActiveJobCount == 0,
                "Late planning completion cannot publish movement after match reset");

            Start(); Cargo(300); harvester.SetPosition(silo.GetResourceUnloadPosition()); Tick(); Tick();
            Check(harvester.HarvestPhase == HarvestPhase.Unloading, "HarvestSystem accepts arrival at the storage approach");
            int money = army.Resources; Tick(1);
            Check(harvester.CargoAmount == 300 && silo.StoredResources == 0,
                "Unload timer preserves cargo until the unload duration expires");
            Tick(1);
            Check(harvester.CargoAmount == 0 && silo.StoredResources == 300 && army.Resources == money + 300 &&
                harvester.HarvestPhase == HarvestPhase.DrivingToField,
                "Unload transfers cargo into storage and army resources exactly once then resumes harvest");
            Tick();
            Check(silo.StoredResources == 300 && army.Resources == money + 300,
                "Following updates cannot duplicate the completed unload");

            system.Reset(); Cargo(100);
            var returnCommand = system.TryReturn(new(NetworkMessageType.HarvesterReturnRequest, owner, UnitId: harvester.UnitId));
            Check(returnCommand is not null, "HarvestSystem accepts a manual return with cargo and owned storage");
            Publish(returnCommand!).GetAwaiter().GetResult(); Tick(); Tick(2);
            Check(system.ActiveJobCount == 0 && harvester.CargoAmount == 0 && harvester.HarvestPhase == HarvestPhase.Idle,
                "Manual return unloads and ends rather than restarting automatic harvest");

            Start(); Cargo(300); harvester.SetPosition(silo.GetResourceUnloadPosition()); Tick(); Tick();
            Check(harvester.HarvestPhase == HarvestPhase.Unloading, "Destroyed-storage fixture enters unload");
            list.Remove(silo); money = army.Resources; Tick(2);
            Check(harvester.CargoAmount == 300 && army.Resources == money && harvester.HarvestPhase == HarvestPhase.ReturningToSilo,
                "Destroyed or removed storage cannot receive cargo or credit resources");
            Tick();
            Check(system.ActiveJobCount == 1, "Missing storage retains the loaded return job for later recovery");

            list.Add(silo); harvester.SetPosition(new Vector3(5.5f, 0, 5.5f));
            system.Reset(); Start(); Cargo(300); Tick(); Tick();
            Check(valid!(), "Cancellation fixture has a live pending route");
            system.CancelForRequest(new(NetworkMessageType.GotoRequest, Guid.NewGuid(), UnitIds: [harvester.UnitId]));
            Check(system.ActiveJobCount == 1 && valid!(), "A foreign movement request cannot cancel harvest");
            system.Cancel(harvester.UnitId);
            Check(system.ActiveJobCount == 0 && !valid!(), "Host Stop cancellation invalidates a pending harvest route");
            cancelled!();
            Start(); Cargo(300); Tick(); Tick(); session++;
            Check(!valid!(), "Session change invalidates an outstanding route before reset");
            system.Reset(); Tick();
            Check(system.ActiveJobCount == 0, "Session reset leaves no transient harvest jobs");
            Start(); Cargo(300); Tick(); Tick(); version++;
            Check(!valid!(), "A replacing admitted command invalidates harvest movement versions");
            system.Reset();
            Start(); Cargo(100); tiberium.ApplyHarvest(new Point(5, 5), 0, now); Tick();
            Check(harvester.HarvestPhase == HarvestPhase.ReturningToSilo,
                "No remaining Tiberium sends a loaded harvester back to storage");
            system.Reset(); Start(); Cargo(300); harvester.SetPosition(silo.GetResourceUnloadPosition());
            silo.StoreResources(silo.AvailableResourceCapacity - 100);
            Tick(); Tick(); money = army.Resources; Tick(2);
            Check(harvester.CargoAmount == 200 && silo.AvailableResourceCapacity == 0 &&
                army.Resources == money + 100 && harvester.HarvestPhase == HarvestPhase.ReturningToSilo,
                "Partly full storage accepts only its remaining capacity and preserves leftover cargo");
            list.Remove(harvester); Tick();
            Check(system.ActiveJobCount == 0, "Removing a harvester removes its job");
        }
        finally { Globals.World = savedWorld; Globals.Game = savedGame; }
        return checks;
    }
}

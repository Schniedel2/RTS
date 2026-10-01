using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

/// <summary>Deterministic, graphics-free workload; uses production controllers and route planning.</summary>
static class PerformanceBaseline
{
    static void Set(object target, Type type, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    public static string Run(bool incremental = false)
    {
        var grid = new GameGrid(48, 48, 1);
        var terrain = Empty<Terrain>();
        Set(terrain, typeof(Terrain), "<Width>k__BackingField", 49);
        Set(terrain, typeof(Terrain), "<Height>k__BackingField", 49);
        Set(terrain, typeof(Terrain), "HeightMap", new float[49 * 49]);
        grid.BindTerrain(terrain);
        var world = Empty<GameWorld>();
        Set(world, typeof(GameWorld), "<GameGrid>k__BackingField", grid);
        Set(world, typeof(GameWorld), "_terrain", terrain);
        var units = new UnitHandler();
        Set(world, typeof(GameWorld), "<Units>k__BackingField", units);
        Set(world, typeof(GameWorld), "<PathfindingManager>k__BackingField", new PathfindingManager(world));
        var list = (IList<Unit>)typeof(UnitHandler).GetField("_units", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(units)!;
        var game = Empty<RTSGame>();
        var armies = new ArmyHandler();
        Set(game, typeof(RTSGame), "<Armies>k__BackingField", armies);
        Set(game, typeof(RTSGame), "<Pricing>k__BackingField", new PricingService(armies, units.FindById));
        using var network = new NetworkHandler("PerformanceBaseline");
        Set(network, typeof(NetworkHandler), "<IsHost>k__BackingField", true);
        Set(game, typeof(RTSGame), "<Network>k__BackingField", network);
        Globals.Game = game;
        Globals.World = world;
        var players = new List<Player>();
        var ais = new List<AIPlayer>();
        var groups = new List<Guid[]>();
        for (int armyIndex = 0; armyIndex < 2; armyIndex++)
        {
            Guid playerId = new(armyIndex + 1, 0, 0, new byte[8]);
            Guid armyId = new(armyIndex + 101, 0, 0, new byte[8]);
            var player = new Player(playerId, "Baseline-" + armyIndex, armyId: armyId);
            players.Add(player);
            armies.EnsureArmy(armyId, playerId).Resources = 0; // Goals wait before loading a model.
            var ai = new AIPlayer(player);
            ai.BeginMatch(12345, armyId);
            ais.Add(ai);
            var builder = Empty<GDIBulldozer>();
            Set(builder, typeof(Unit), "<ArmyId>k__BackingField", armyId);
            list.Add(builder);
            var ids = new List<Guid>();
            for (int index = 0; index < 8; index++)
            {
                var unit = new MobileUnit(new Vector3(4.5f + armyIndex * 6, 0, 3.5f + index * 2),
                    new Guid(1000 + armyIndex * 10 + index, 0, 0, new byte[8]));
                Set(unit, typeof(Unit), "<Width>k__BackingField", 1);
                Set(unit, typeof(Unit), "<Length>k__BackingField", 1);
                Set(unit, typeof(Unit), "<ArmyId>k__BackingField", armyId);
                list.Add(unit);
                if (!grid.TryMove(unit, grid.ToCell(unit.Position))) throw new Exception("Baseline placement failed.");
                ids.Add(unit.UnitId);
            }
            groups.Add(ids.ToArray());
        }
        Set(game, typeof(RTSGame), "_players", players);
        var host = new NetworkHost(network, new NetworkInput(network), world);
        var plan = typeof(NetworkHost).GetMethod("TryCreateGotoCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var newPlan = typeof(NetworkHost).GetMethod("NewGotoPlan", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var planWork = typeof(NetworkHost).GetMethod("PlanGoto", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int planningUpdates = 0, maximumSteps = 0;
        double maximumUpdateMilliseconds = 0;
        var siteSearch = typeof(ArmyGoalController).GetMethod("TryFindBuildingSite", BindingFlags.Static | BindingFlags.NonPublic)!;
        var preview = new Building(Vector3.Zero, Guid.NewGuid());
        Set(preview, typeof(Unit), "<Width>k__BackingField", 2);
        Set(preview, typeof(Unit), "<Length>k__BackingField", 2);
        void Work(bool unreachable)
        {
            for (int index = 0; index < 2; index++)
            {
                ais[index].Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)), world, network);
                var request = NetworkCommands.CreateGotoRequest(players[index].Id, groups[index], unreachable ? 40.5f : 18.5f, 0, 30.5f);
                NetworkMessage? command;
                using (PerformanceMeasurements.Measure(unreachable ? "Baseline.UnreachableGroup" : "Baseline.ReachableGroup"))
                {
                    if (!incremental) command = (NetworkMessage?)plan.Invoke(host, [request]);
                    else
                    {
                        object pending = newPlan.Invoke(host, [request])!;
                        bool done = false;
                        var work = (IEnumerable<int>)planWork.Invoke(host, [pending])!;
                        world.PathfindingManager.Scheduler.Enqueue(work, () => true, () => done = true,
                            measurementName: "Host.GotoPlanningSlice");
                        for (int update = 0; !done; update++)
                        {
                            if (update > 100000) throw new Exception("Incremental baseline starved.");
                            world.PathfindingManager.Scheduler.Update();
                            var scheduler = world.PathfindingManager.Scheduler;
                            if (scheduler.LastSteps > PlanningScheduler.MaximumStepsPerUpdate)
                                throw new Exception("Incremental baseline exceeded work budget.");
                            if (PerformanceMeasurements.Enabled)
                            {
                                planningUpdates++;
                                maximumSteps = Math.Max(maximumSteps, scheduler.LastSteps);
                                maximumUpdateMilliseconds = Math.Max(maximumUpdateMilliseconds, scheduler.LastMilliseconds);
                            }
                        }
                        command = (NetworkMessage?)pending.GetType().GetField("Result")!.GetValue(pending);
                    }
                }
                if (command?.Routes is not { Length: 8 } routes ||
                    routes.Any(route => unreachable ? route.Cells.Length != 0 : route.Cells.Length == 0))
                    throw new Exception("Baseline group did not produce the expected reachable/unreachable routes.");
                if (!(bool)siteSearch.Invoke(null, [world, preview, new Vector3(16.5f, 0, 16.5f), 4, 14, null])!)
                    throw new Exception("Baseline building site search failed.");
            }
        }
        PerformanceMeasurements.Enabled = false;
        Work(false); // Warm JIT, catalogs, telemetry key initialization and terrain cache.
        for (int y = 0; y < 48; y++) grid.GetCell(24, y).IsBlocked = true;
        Work(true);
        for (int y = 0; y < 48; y++) grid.GetCell(24, y).IsBlocked = false;
        PerformanceMeasurements.Reset();
        long searchesBeforeMeasurement = Globals.Telemetry.TryFindPath_Calls;
        PerformanceMeasurements.Enabled = true;
        for (int repeat = 0; repeat < 3; repeat++) Work(false);
        for (int y = 0; y < 48; y++) grid.GetCell(24, y).IsBlocked = true;
        for (int repeat = 0; repeat < 3; repeat++) Work(true);
        PerformanceMeasurements.Enabled = false;
        long searchesMeasured = Globals.Telemetry.TryFindPath_Calls - searchesBeforeMeasurement;
        string budget = incremental
            ? FormattableString.Invariant($"\nIncremental mode: {planningUpdates} planning updates; maximum {maximumSteps} steps/update; maximum {maximumUpdateMilliseconds:F3} ms/update. Default budget: {PlanningScheduler.MaximumStepsPerUpdate} steps and {PlanningScheduler.MaximumMillisecondsPerUpdate} ms; the time check occurs between individual work steps. Updates are advanced without frame sleeps, so group totals are CPU work, not in-game delivery latency.\n")
            : "\nSynchronous compatibility mode.\n";
        return $"# Graphics-free performance baseline\n\nUTC: {DateTime.UtcNow:O}\nOS: {RuntimeInformation.OSDescription}\nRuntime: {RuntimeInformation.FrameworkDescription}\nArchitecture: {RuntimeInformation.ProcessArchitecture}; logical CPUs: {Environment.ProcessorCount}\nGC server: {System.Runtime.GCSettings.IsServerGC}\nTiered compilation override: {Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default"}\nActual searches started during measured workload: {searchesMeasured}\n" + budget +
            "\nScenario: flat 48x48 grid, seed 12345, two active AI players waiting for base resources, 8 mobile units per army; three reachable and three disconnected group goals per army. Full-height wall at x=24 disconnects targets. Twelve building-site searches. One reachable and one unreachable unmeasured warmup per army. No movement, rendering, transport or complete match simulation. Host Goto planning is invoked directly; request dispatch is measured in-game separately.\n\n" +
            "These results establish route-planning and resource-wait baselines, not the cost of a mature AI battle.\n\n```text\n" + PerformanceMeasurements.Report() + "```\n";
    }
}

using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

static class IncrementalPlanningChecks
{
    static void Set(object target, Type type, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); checks++; }
        GameWorld savedWorld = Globals.World;
        RTSGame savedGame = Globals.Game;
        using var network = new NetworkHandler("IncrementalPlanningChecks");
        try
        {
            var grid = new GameGrid(32, 32, 1);
            var terrain = Empty<Terrain>();
            Set(terrain, typeof(Terrain), "<Width>k__BackingField", 33);
            Set(terrain, typeof(Terrain), "<Height>k__BackingField", 33);
            Set(terrain, typeof(Terrain), "HeightMap", new float[33 * 33]);
            grid.BindTerrain(terrain);
            var world = Empty<GameWorld>();
            Set(world, typeof(GameWorld), "<GameGrid>k__BackingField", grid);
            Set(world, typeof(GameWorld), "_terrain", terrain);
            var units = new UnitHandler();
            Set(world, typeof(GameWorld), "<Units>k__BackingField", units);
            Set(world, typeof(GameWorld), "<Markers>k__BackingField", new MarkerHandler());
            var manager = new PathfindingManager(world);
            Set(world, typeof(GameWorld), "<PathfindingManager>k__BackingField", manager);
            var list = (List<Unit>)typeof(UnitHandler).GetField("_units", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(units)!;
            var game = Empty<RTSGame>();
            Set(game, typeof(RTSGame), "<World>k__BackingField", world);
            var armies = new ArmyHandler();
            Set(game, typeof(RTSGame), "<Armies>k__BackingField", armies);
            Set(game, typeof(RTSGame), "<Network>k__BackingField", network);
            Set(network, typeof(NetworkHandler), "<IsHost>k__BackingField", true);
            Guid owner = Guid.NewGuid(), army = Guid.NewGuid();
            armies.EnsureArmy(army, owner);
            Set(game, typeof(RTSGame), "_players", new List<Player> { new(owner, "owner", armyId: army) });
            Globals.Game = game; Globals.World = world;
            MobileUnit Add(int x, int y)
            {
                var unit = new MobileUnit(new Vector3(x + .5f, 0, y + .5f), Guid.NewGuid());
                Set(unit, typeof(Unit), "<Width>k__BackingField", 1);
                Set(unit, typeof(Unit), "<Length>k__BackingField", 1);
                Set(unit, typeof(Unit), "<ArmyId>k__BackingField", army);
                unit.NextNetworkUpdateTime = double.MaxValue;
                list.Add(unit);
                Check(grid.TryMove(unit, new Point(x, y)), "Incremental fixture occupies a valid cell");
                return unit;
            }
            var mover = Add(2, 2);
            Guid localArmy = Guid.NewGuid();
            armies.EnsureArmy(localArmy, network.LocalPeerId);
            var controlledSelectionUnit = new MobileUnit(Vector3.Zero, Guid.NewGuid());
            Set(controlledSelectionUnit, typeof(Unit), "<ArmyId>k__BackingField", localArmy);
            Check(PlayerHandler.ControllableUnits(new Unit[] { mover, controlledSelectionUnit })
                .SequenceEqual(new Unit[] { controlledSelectionUnit }),
                "Mixed debug selection sends orders only to locally controlled units");
            var inspectionPlayer = Empty<PlayerHandler>();
            Set(inspectionPlayer, typeof(PlayerHandler), "_selectedUnits", new List<Unit> { mover });
            Check(!inspectionPlayer.SelectAction(new UnitAction(UnitActionType.Stop, "Stop", 7, 1), false) &&
                !inspectionPlayer.SelectAction(new UnitAction(UnitActionType.Goto, "Goto", 0, 1), false),
                "Foreign debug selection cannot activate movement or Stop actions");
            var finder = new Pathfinder(world);
            var scheduler = manager.Scheduler;
            var search = finder.CreateSearch(mover, mover.MovementProfile, new Point(2, 2), new Vector2(29.5f, 29.5f));
            bool completed = false;
            scheduler.Enqueue(search.Work(), () => true, () => completed = true);
            scheduler.Update(4, double.PositiveInfinity);
            Check(!completed && search.ExpandedNodes <= 4 && scheduler.LastSteps == 4,
                "A route search suspends after the shared work budget");
            int slices = 0;
            while (!completed && slices++ < 10000)
            {
                scheduler.Update(32, double.PositiveInfinity);
                if (scheduler.LastSteps > 32) throw new Exception("Planning exceeded work budget.");
            }
            Check(completed && search.Succeeded && slices > 1, "Incremental search eventually completes without exceeding its step budget");
            Check(finder.TryFindPathFrom(mover, mover.MovementProfile, new Point(2, 2), new Vector2(29.5f, 29.5f), out var expected) &&
                expected.SequenceEqual(search.Path), "Incremental A* preserves the synchronous route");

            for (int y = 0; y < 32; y++) grid.GetCell(16, y).IsBlocked = true;
            var failed = finder.CreateSearch(mover, mover.MovementProfile, new Point(2, 2), new Vector2(29.5f, 29.5f));
            var quick = finder.CreateSearch(mover, mover.MovementProfile, new Point(2, 2), new Vector2(4.5f, 2.5f));
            bool failedDone = false, quickDone = false;
            scheduler.Enqueue(failed.Work(), () => true, () => failedDone = true);
            scheduler.Enqueue(quick.Work(), () => true, () => quickDone = true);
            scheduler.Update(64, double.PositiveInfinity);
            Check(quickDone && !failedDone, "Round-robin planning lets a small request overtake an expensive independent search");
            while (!failedDone && slices++ < 10000) scheduler.Update(64, double.PositiveInfinity);
            Check(failedDone && !failed.Succeeded, "An unreachable search finishes rather than starving other jobs");
            for (int y = 0; y < 32; y++) grid.GetCell(16, y).IsBlocked = false;
            var changed = finder.CreateSearch(mover, mover.MovementProfile, new Point(2, 2), new Vector2(29.5f, 29.5f));
            bool changedDone = false;
            scheduler.Enqueue(changed.Work(), () => true, () => changedDone = true);
            scheduler.Update(8, double.PositiveInfinity);
            for (int y = 0; y < 32; y++) grid.GetCell(16, y).IsBlocked = true;
            while (!changedDone && slices++ < 10000) scheduler.Update(64, double.PositiveInfinity);
            Check(changedDone && !changed.Succeeded && changed.Restarts > 0,
                "A changed obstacle invalidates an in-progress search cache");
            long revision = grid.NavigationRevision;
            grid.GetCell(5, 5).MovementCost = 4;
            Check(grid.NavigationRevision > revision, "Terrain rule and cost changes invalidate search work");
            for (int y = 0; y < 32; y++) grid.GetCell(16, y).IsBlocked = false;

            var unstable = finder.CreateSearch(mover, mover.MovementProfile, new Point(2, 2), new Vector2(29.5f, 29.5f));
            bool unstableDone = false;
            scheduler.Enqueue(unstable.Work(), () => true, () => unstableDone = true);
            for (int update = 0; !unstableDone && update < 100; update++)
            {
                scheduler.Update(4, double.PositiveInfinity);
                grid.GetCell(5, 5).MovementCost = update % 2 == 0 ? 2 : 3;
            }
            Check(unstableDone && !unstable.Succeeded && unstable.Restarts == 3,
                "Continually changing terrain exhausts bounded restarts instead of keeping a search alive forever");
            bool validJob = true, disposedJob = false, obsoleteApplied = false, cancelledJob = false;
            IEnumerable<int> ObsoleteWork()
            {
                try { while (true) yield return 0; }
                finally { disposedJob = true; }
            }
            scheduler.Enqueue(ObsoleteWork(), () => validJob, () => obsoleteApplied = true, () => cancelledJob = true);
            scheduler.Update(4, double.PositiveInfinity);
            validJob = false;
            scheduler.Update(4, double.PositiveInfinity);
            Check(disposedJob && cancelledJob && !obsoleteApplied,
                "Cancelled work releases iterator state and never invokes its result callback");
            scheduler.Enqueue(ObsoleteWork(), () => true, () => obsoleteApplied = true);
            scheduler.Update(4, 0);
            Check(scheduler.LastSteps == 0 && scheduler.PendingJobs == 1, "An exhausted time budget performs no navigation steps");
            scheduler.Reset();
            Check(scheduler.PendingJobs == 0 && !obsoleteApplied, "Reset drops unfinished navigation results");
            mover.TryReceiveGotoCommand(world, new GotoCommand(new Vector2(29.5f, 29.5f)));
            manager.Update(4, double.PositiveInfinity);
            mover.Stop();
            manager.Update(16, double.PositiveInfinity);
            Check(mover.CurrentCommand is null && mover.PlannedPath.Count == 0 && mover.MovementStatus == MovementStatus.Idle,
                "Local recovery planning cannot overwrite a Stop that arrives between slices");
            mover.TryReceiveGotoCommand(world, new GotoCommand(new Vector2(29.5f, 29.5f)));
            manager.Update(4, double.PositiveInfinity);
            mover.TryReceiveGotoCommand(world, new GotoCommand(new Vector2(4.5f, 2.5f)));
            for (int update = 0; manager.PendingRequests > 0 && update < 100; update++) manager.Update(64, double.PositiveInfinity);
            Check(mover.CurrentCommand?.Target == new Vector2(4.5f, 2.5f) && mover.PlannedPath.Last() == new Point(4, 2),
                "Local planning applies only the latest request ID and target");
            mover.Stop();

            // Actual host admission, FIFO fence, local application and wire payload.
            var host = new NetworkHost(network, new NetworkInput(network), world);
            var published = new List<NetworkMessage>();
            network.MessageReceived += message => { if (message.Type is NetworkMessageType.GotoCommand or NetworkMessageType.StopCommand or NetworkMessageType.AttackGroundCommand) published.Add(message); };
            void Send(NetworkMessage request)
            {
                network.EnqueueLocalMessage(request); network.Update();
                Set(host, typeof(NetworkHost), "_nextExploredVisibilitySync", double.MaxValue);
            }
            void Tick() => host.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero));
            void Drain(int limit = 10000)
            {
                for (int index = 0; index < limit; index++)
                {
                    scheduler.Update(64, double.PositiveInfinity); Tick();
                    if (scheduler.PendingJobs == 0 && typeof(NetworkHost).GetField("_pendingGoto", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) is null) return;
                }
                throw new Exception("Host planning did not finish.");
            }
            Guid[] group = [mover.UnitId, Add(2, 4).UnitId, Add(2, 6).UnitId];
            Send(NetworkCommands.CreateGotoRequest(owner, group, 28.5f, 0, 28.5f));
            Tick();
            Check(scheduler.PendingJobs == 1 && published.All(message => message.Type != NetworkMessageType.GotoCommand),
                "A group Goto is queued without publishing partial routes");
            Check(group.All(id => units.FindMobileUnitById(id) is MobileUnit unit &&
                unit.CurrentCommand?.Target == new Vector2(28.5f, 28.5f) &&
                unit.MovementStatus == MovementStatus.Planning && unit.PlannedPath.Count == 0),
                "Accepted host planning remains a busy movement intent for AI instead of an idle unit");
            Drain();
            NetworkMessage groupCommand = published.Last(message => message.Type == NetworkMessageType.GotoCommand);
            Check(groupCommand.Routes is { Length: 3 } && groupCommand.Routes.All(route => route.Cells.Length > 0),
                "The completed host command contains a full route for every group member");
            var wire = System.Text.Json.JsonSerializer.Deserialize<NetworkMessage>(
                System.Text.Json.JsonSerializer.Serialize(groupCommand, NetworkJson.Options), NetworkJson.Options)!;
            Check(wire.Routes!.Zip(groupCommand.Routes!).All(pair => pair.First.Cells.SequenceEqual(pair.Second.Cells)),
                "Wire serialization retains every confirmed waypoint");
            published.Clear();
            Send(NetworkCommands.CreateGotoRequest(owner, group, 29.5f, 0, 29.5f)); Tick();
            scheduler.Update(4, double.PositiveInfinity);
            Send(NetworkCommands.CreateStopRequest(owner, group));
            Drain();
            Check(published.All(message => message.Type != NetworkMessageType.GotoCommand) &&
                group.All(id => units.FindMobileUnitById(id)!.CurrentCommand is null),
                "Stop invalidates active planning and no stale Goto resurrects movement");
            published.Clear();
            Send(NetworkCommands.CreateGotoRequest(owner, group, 28.5f, 0, 28.5f));
            Send(NetworkCommands.CreateGotoRequest(owner, group, 29.5f, 0, 29.5f));
            Send(NetworkCommands.CreateStopRequest(owner, group));
            Tick(); Drain();
            Check(published.All(message => message.Type != NetworkMessageType.GotoCommand) &&
                group.All(id => units.FindMobileUnitById(id)!.CurrentCommand is null),
                "Admission versions survive request filtering when Goto A, Goto B and Stop arrive together");
            published.Clear();
            Send(NetworkCommands.CreateGotoRequest(owner, [mover.UnitId], 27.5f, 0, 27.5f)); Tick();
            Send(NetworkCommands.CreateGotoRequest(owner, [mover.UnitId], 5.5f, 0, 5.5f));
            Drain();
            Check(published.Count(message => message.Type == NetworkMessageType.GotoCommand) == 1 &&
                mover.CurrentCommand?.Target == new Vector2(5.5f, 5.5f),
                "A newer Goto replaces obsolete planning before its result is applied");
            published.Clear();
            Send(NetworkCommands.CreateGotoRequest(owner, [mover.UnitId], 6.5f, 0, 6.5f)); Tick();
            Send(NetworkCommands.CreateGotoRequest(owner, [mover.UnitId], 9.5f, 0, 9.5f, appendToQueue: true));
            Drain();
            Check(published.Where(message => message.Type == NetworkMessageType.GotoCommand).Select(message => message.X)
                    .SequenceEqual(new[] { 6.5f, 9.5f }) && mover.LastQueuedTarget == new Vector2(9.5f, 9.5f),
                "Shift orders retain FIFO and use the previous confirmed endpoint");
            published.Clear();
            Send(NetworkCommands.CreateGotoRequest(owner, [mover.UnitId], 10.5f, 0, 10.5f)); Tick();
            Send(NetworkCommands.CreateAttackGroundRequest(owner, [mover.UnitId], new Vector3(15.5f, 0, 15.5f)));
            Drain();
            Check(published.Where(message => message.Type != NetworkMessageType.StopCommand).Select(message => message.Type)
                .SequenceEqual(new[] { NetworkMessageType.GotoCommand, NetworkMessageType.AttackGroundCommand }),
                "An attack cannot overtake a Goto or silently erase the preceding route");
            published.Clear();
            Send(NetworkCommands.CreateGotoRequest(owner, [mover.UnitId], 12.5f, 0, 12.5f)); Tick();
            Send(NetworkCommands.CreateStopRequest(Guid.NewGuid(), [mover.UnitId]));
            Send(NetworkCommands.CreateGotoRequest(owner, [mover.UnitId], float.NaN, 0, 5));
            Drain();
            Check(published.Count(message => message.Type == NetworkMessageType.GotoCommand) == 1 &&
                mover.CurrentCommand?.Target == new Vector2(12.5f, 12.5f),
                "Foreign Stop and invalid destinations cannot cancel an authorized pending route");
            published.Clear();
            Send(NetworkCommands.CreateGotoRequest(owner, [mover.UnitId], 28.5f, 0, 28.5f)); Tick();
            units.RemoveMapObject(mover);
            Drain();
            Check(published.All(message => message.Type != NetworkMessageType.GotoCommand),
                "Removing a unit invalidates its pending host route");
            var survivor = units.FindMobileUnitById(group[1])!;
            published.Clear();
            Send(NetworkCommands.CreateGotoRequest(owner, [survivor.UnitId], 28.5f, 0, 28.5f)); Tick();
            manager.Reset(); Tick();
            Check(scheduler.PendingJobs == 0 && published.All(message => message.Type != NetworkMessageType.GotoCommand),
                "A world/match reset clears navigation jobs and releases the host FIFO fence");
            Check(survivor.CurrentCommand is null && survivor.MovementStatus != MovementStatus.Planning,
                "Cancelled host planning releases its temporary intent instead of leaving a unit planning forever");
            Send(NetworkCommands.CreateGotoRequest(owner, [survivor.UnitId], 28.5f, 0, 28.5f)); Tick();
            network.Disconnect();
            Tick();
            Check(scheduler.PendingJobs == 0, "Session disconnect invalidates navigation work");
        }
        finally { Globals.World = savedWorld; Globals.Game = savedGame; }
        return checks;
    }
}

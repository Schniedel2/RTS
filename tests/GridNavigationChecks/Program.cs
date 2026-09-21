using RTS.Network;
using System.Text.Json;
using Microsoft.Xna.Framework;
using RTS;
using System.Reflection;
using System.Runtime.CompilerServices;

// Exercise production navigation without creating a graphics device or loading assets.
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
void Field(object target, Type owner, string name, object value) =>
    owner.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
MobileUnit Unit(GroundMovementProfile? profile = null, int width = 1, int length = 1)
{
    MobileUnit unit = Empty<MobileUnit>();
    Field(unit, typeof(Unit), "<Width>k__BackingField", width);
    Field(unit, typeof(Unit), "<Length>k__BackingField", length);
    Field(unit, typeof(MobileUnit), "<MovementProfile>k__BackingField", profile ?? new GroundMovementProfile());
    unit.SetTransform(Matrix.CreateTranslation(1.5f, 0, 1.5f));
    return unit;
}
Terrain Terrain(int width, int height)
{
    Terrain terrain = Empty<Terrain>();
    Field(terrain, typeof(Terrain), "<Width>k__BackingField", width);
    Field(terrain, typeof(Terrain), "<Height>k__BackingField", height);
    Field(terrain, typeof(Terrain), "HeightMap", new float[width * height]);
    return terrain;
}
GameWorld World(GameGrid grid)
{
    GameWorld world = Empty<GameWorld>();
    Field(world, typeof(GameWorld), "<GameGrid>k__BackingField", grid);
    return world;
}

var terrain = Terrain(6, 6);
var grid = new GameGrid(6, 6, 1);
grid.BindTerrain(terrain);
Check(grid.GetCell(1, 1).MaxSlopeDegrees == 0, "Flat slope");
terrain.SetHeight(2, 1, 1);
terrain.SetHeight(2, 2, 1);
Check(Math.Abs(grid.GetCell(1, 1).MaxSlopeDegrees - 45) < 0.001f, "45-degree ramp and revision refresh");
Check(!grid.CanPlace(Unit(), new Point(1, 1)), "Vehicles reject 45-degree ramp");
Check(grid.CanPlace(Unit(new GroundMovementProfile(MovementModes.Walk, 50)), new Point(1, 1)), "Infantry accepts ramp");
terrain.SetHeight(2, 1, 4);
terrain.SetHeight(2, 2, 4);
Check(!grid.CanPlace(Unit(new GroundMovementProfile(MovementModes.Walk, 50)), new Point(1, 1)), "Infantry rejects steep slope");
Check(grid.CanPlace(Unit(new GroundMovementProfile(MovementModes.Walk | MovementModes.Climb, 50)), new Point(1, 1)), "Climber accepts steep slope");
Check(grid.CanPlace(Unit(new GroundMovementProfile(MovementModes.Drive | MovementModes.SteepDrive)), new Point(1, 1)), "Motorcycle capability accepts steep slope");
grid.GetCell(1, 1).AllowedMovement = MovementModes.Climb;
Check(!grid.CanPlace(Unit(new GroundMovementProfile(MovementModes.SteepDrive)), new Point(1, 1)), "Climb-only excludes motorcycle");
grid.GetCell(1, 1).IsBlocked = true;
Check(!grid.CanPlace(Unit(new GroundMovementProfile(MovementModes.Climb)), new Point(1, 1)), "Absolute block excludes climber");
Check(!grid.CanPlace(Unit(), new Point(5, 1)), "No movement outside rendered terrain");
Check(grid.ToCell(new Vector3(-0.1f, 0, 0)).X == -1, "Negative coordinates do not truncate into grid");

var saddle = Terrain(2, 2);
saddle.SetHeight(1, 1, 1);
Check(Math.Abs(saddle.GetMaxSlopeDegrees(0, 0) - 54.73561f) < 0.001f, "Second triangle controls maximum slope");
var coarse = new GameGrid(3, 3, 2);
coarse.BindTerrain(terrain);
Check(coarse.GetCell(0, 0).MaxSlopeDegrees > 70, "Coarse grid includes all covered triangles");
var flat = Terrain(6, 6);
grid.BindTerrain(flat);
Check(grid.GetCell(1, 1).MaxSlopeDegrees == 0, "Terrain replacement invalidates slope cache");
Check(grid.GetCell(1, 1).IsBlocked, "Slope refresh preserves authored rules");

grid = new GameGrid(8, 8, 1);
MobileUnit car = Unit();
Check(grid.TryMove(car, new Point(1, 1)), "Initial registration");
Check(ReferenceEquals(grid.GetOccupant(1, 1), car), "Occupant identity");
grid.GetCell(2, 1).IsBlocked = true;
Check(!grid.TryMove(car, new Point(2, 2)), "Movement cannot cut blocked corner");
Check(!grid.TryMove(car, new Point(4, 1)), "Long movement cannot jump blocked cell");
grid.GetCell(2, 1).IsBlocked = false;
grid.GetCell(2, 1).ExcludeFromPathfinding = true;
Check(grid.TryMove(car, new Point(2, 1)), "Planning exclusion allows explicit movement");
Check(grid.GetOccupant(1, 1) is null && ReferenceEquals(grid.GetOccupant(2, 1), car), "Move updates occupancy");
grid.Remove(car);
Check(grid.GetOccupant(2, 1) is null, "Remove clears occupancy");
grid.GetCell(3, 2).AllowedMovement = MovementModes.Walk;
Check(!grid.CanPlace(Unit(width: 2), new Point(2, 2)), "All footprint cells enforce access rules");
grid.GetCell(3, 2).MovementCost = 4;
Check(grid.GetMovementCost(Unit(width: 2), new Point(2, 2)) == 4, "Footprint cost includes expensive edge");
foreach (float invalid in new[] { 0, -1, float.NaN, float.PositiveInfinity, 0.5f })
{
    bool rejected = false;
    try { grid.GetCell(0, 0).MovementCost = invalid; }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "Invalid cost rejected");
}

grid = new GameGrid(7, 5, 1);
var world = World(grid);
var finder = new Pathfinder(world);
car = Unit();
car.SetPosition(new Vector3(0.5f, 0, 2.5f));
for (int x = 1; x <= 5; x++) grid.GetCell(x, 2).MovementCost = 20;
bool aFound = finder.TryFindPath_AStar(car, car.MovementProfile, new Vector2(6.5f, 2.5f), out var aPath);
bool dFound = finder.TryFindPath_Dijkstra(car, car.MovementProfile, new Vector2(6.5f, 2.5f), out var dPath);
float Cost(List<Point> path)
{
    Point from = grid.ToCell(car.Position);
    float cost = 0;
    foreach (Point to in path) { cost += car.MovementProfile.GetMovementCost(world, car, from, to); from = to; }
    return cost;
}
Check(aFound && dFound && Cost(aPath) < 10 && Math.Abs(Cost(aPath) - Cost(dPath)) < 0.001f, "Both searches choose cheaper detour");
grid.GetCell(6, 2).ExcludeFromPathfinding = true;
Check(!finder.TryFindPath_AStar(car, car.MovementProfile, new Vector2(6.5f, 2.5f), out _), "Excluded destination rejected");
grid.GetCell(6, 2).ExcludeFromPathfinding = false;
grid.GetCell(0, 2).ExcludeFromPathfinding = true;
Check(finder.TryFindPath_AStar(car, car.MovementProfile, new Vector2(6.5f, 2.5f), out _), "Can leave excluded starting cell");
for (int y = 0; y < 5; y++) grid.GetCell(3, y).ExcludeFromPathfinding = true;
Check(!finder.TryFindPath_AStar(car, car.MovementProfile, new Vector2(6.5f, 2.5f), out _), "Excluded strip cannot be crossed");
grid = new GameGrid(9, 7, 1);
world = World(grid);
finder = new Pathfinder(world);
car = Unit(width: 3, length: 3);
car.SetPosition(new Vector3(2.5f, 0, 3.5f));
for (int y = 2; y <= 4; y++)
    for (int x = 1; x <= 3; x++) grid.GetCell(x, y).ExcludeFromPathfinding = true;
Check(finder.TryFindPath_AStar(car, car.MovementProfile, new Vector2(6.5f, 3.5f), out _), "Large unit can leave excluded initial footprint");

grid = new GameGrid(8, 8, 1);
var building = Empty<Building>();
Field(building, typeof(Unit), "<Width>k__BackingField", 1);
Field(building, typeof(Unit), "<Length>k__BackingField", 1);
building.SetTransform(Matrix.CreateTranslation(3.5f, 0, 3.5f));
grid.GetCell(3, 3).MovementCost = 3;
Check(grid.TryPlace(building, building.Position, 0), "Building placement");
Check(ReferenceEquals(grid.GetOccupant(3, 3), building), "Building identity available");
Check(!grid.CanPlace(Unit(), new Point(3, 3)), "Building blocks movement");
grid.Remove(building);
Check(grid.CanPlace(Unit(), new Point(3, 3)) && grid.GetCell(3, 3).MovementCost == 3, "Demolition clears occupancy and preserves terrain rules");

// Compare the searches over varying costs and obstacles, including unreachable targets.
Random random = new(42);
for (int sample = 0; sample < 30; sample++)
{
    grid = new GameGrid(9, 9, 1);
    world = World(grid);
    finder = new Pathfinder(world);
    car = Unit();
    car.SetPosition(new Vector3(0.5f, 0, 0.5f));
    for (int y = 0; y < 9; y++)
        for (int x = 0; x < 9; x++)
        {
            grid.GetCell(x, y).MovementCost = random.Next(1, 8);
            grid.GetCell(x, y).IsBlocked = random.NextDouble() < 0.18;
        }
    grid.GetCell(0, 0).IsBlocked = grid.GetCell(8, 8).IsBlocked = false;
    aFound = finder.TryFindPath_AStar(car, car.MovementProfile, new Vector2(8.5f, 8.5f), out aPath);
    dFound = finder.TryFindPath_Dijkstra(car, car.MovementProfile, new Vector2(8.5f, 8.5f), out dPath);
    Check(aFound == dFound && (!aFound || Math.Abs(Cost(aPath) - Cost(dPath)) < 0.001f), "Weighted A*/Dijkstra agreement");
}
// Selection uses precise authored bounds, including offset, mesh scale and building progress.
building = Empty<Building>();
building.TotalBuildingPointsNeeded = 0;
building.SetTransform(Matrix.CreateRotationY(0.6f) * Matrix.CreateTranslation(3, 0, 2));
BoundingBox authored = new(new Vector3(-5.6f, 0, -2.4f), new Vector3(0.8f, 7.2f, 2.4f));
var vertices = authored.GetCorners().Select(p => new VertexPositionColorNormalTexture(p, Color.White, Vector3.Up, Vector2.Zero)).ToArray();
var mesh = new Mesh("offset-building", new[] { new SubMesh("body", vertices, new[] { 0, 1, 2 }, Vector3.Zero) });
mesh.LocalTransform = Matrix.CreateScale(0.75f);
typeof(Unit).GetMethod("SetMeshSet", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(building, new object[] { new MeshSet(mesh), true, 4.0f });
var viewport = new Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 1280, 720);
Matrix view = Matrix.CreateLookAt(new Vector3(15, 14, 20), Vector3.Zero, Vector3.Up);
Matrix projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1280f / 720, 0.1f, 100);
Rectangle ExpectedBounds()
{
    var corners = authored.GetCorners().Select(p => viewport.Project(p, projection, view, mesh.LocalTransform * building.GetWorldMatrix())).ToArray();
    int left = (int)MathF.Floor(corners.Min(p => p.X)), top = (int)MathF.Floor(corners.Min(p => p.Y));
    int right = (int)MathF.Ceiling(corners.Max(p => p.X)), bottom = (int)MathF.Ceiling(corners.Max(p => p.Y));
    return new Rectangle(left, top, right - left, bottom - top);
}
Check(building.GetScreenBounds(view, projection, viewport) == ExpectedBounds(), "Selection preserves mesh origin, scale and rotation without grid padding");
building.TotalBuildingPointsNeeded = 100;
Check(building.GetScreenBounds(view, projection, viewport) == ExpectedBounds(), "Selection follows construction scale");
building.SetTransform(Matrix.CreateRotationY(-0.9f) * Matrix.CreateTranslation(-2, 0, 1));
Check(building.GetScreenBounds(view, projection, viewport) == ExpectedBounds(), "Selection follows changed world transform");
// Host validation, network serialization, replay and production rally behavior.
grid = new GameGrid(12, 12, 1);
world = World(grid);
terrain = Terrain(12, 12);
grid.BindTerrain(terrain);
Field(world, typeof(GameWorld), "_terrain", terrain);
var units = new UnitHandler();
Field(world, typeof(GameWorld), "<Units>k__BackingField", units);
Field(world, typeof(GameWorld), "<PathfindingManager>k__BackingField", new PathfindingManager(world));
Globals.World = world;
var game = Empty<RTSGame>();
var armies = new ArmyHandler();
Field(game, typeof(RTSGame), "<Armies>k__BackingField", armies);
Globals.Game = game;
var transport = Empty<NetworkHandler>();
Guid hostId = Guid.NewGuid(), ownerId = Guid.NewGuid(), armyId = Guid.NewGuid();
Field(transport, typeof(NetworkHandler), "<LocalPeerId>k__BackingField", hostId);
Field(transport, typeof(NetworkHandler), "<IsHost>k__BackingField", true);
Field(game, typeof(RTSGame), "<Network>k__BackingField", transport);
armies.EnsureArmy(armyId, ownerId);
GDIBarracks Barracks(Guid id)
{
    var result = Empty<GDIBarracks>();
    Field(result, typeof(Unit), "<UnitId>k__BackingField", id);
    Field(result, typeof(Unit), "<ArmyId>k__BackingField", armyId);
    Field(result, typeof(Building), "<ProductionQueue>k__BackingField", new ProductionQueue());
    result.SetTransform(Matrix.Identity);
    return result;
}
var barracks = Barracks(Guid.NewGuid());
((List<Unit>)units.Units).Add(barracks);
var input = new NetworkInput(transport);
var host = new NetworkHost(transport, input, world);
NetworkMessage? Request(NetworkMessage request) => (NetworkMessage?)typeof(NetworkHost)
    .GetMethod("TryCreateSetRallyPointCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { request });
NetworkMessage Wire(NetworkMessage message) => JsonSerializer.Deserialize<NetworkMessage>(JsonSerializer.Serialize(message))!;
void Deliver(NetworkMessage command) => typeof(NetworkInput)
    .GetMethod("HandleNetworkMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(input, new object[] { Wire(command) });
Vector3 rallyTarget = new(8.5f, 99, 7.5f);
var request = NetworkCommands.CreateSetRallyPointRequest(ownerId, barracks.UnitId, rallyTarget);
Check(Request(request with { SenderId = Guid.NewGuid() }) is null, "Foreign player cannot set rally point");
Check(Request(request with { UnitId = Guid.NewGuid() }) is null, "Unknown rally owner rejected");
foreach (Vector3 bad in new[] { new Vector3(float.NaN, 0, 2), new Vector3(-1, 0, 2), new Vector3(12, 0, 2) })
    Check(Request(NetworkCommands.CreateSetRallyPointRequest(ownerId, barracks.UnitId, bad)) is null, "Invalid rally coordinates rejected");
grid.GetCell(8, 7).IsBlocked = true;
Check(Request(request) is null, "Blocked rally target rejected");
grid.GetCell(8, 7).IsBlocked = false;
grid.GetCell(8, 7).ExcludeFromPathfinding = true;
Check(Request(request) is null, "Excluded rally target rejected");
grid.GetCell(8, 7).ExcludeFromPathfinding = false;
NetworkMessage confirmed = Request(Wire(request))!;
Check(confirmed.Type == NetworkMessageType.SetRallyPointCommand && confirmed.SenderId == hostId, "Host confirms rally point");
Check(barracks.RallyPoint == new Vector3(8.5f, 0, 7.5f) && barracks.RallyPointRevision == 1, "Host sets terrain height and revision");
Check(barracks.Actions.Any(action => action.Type == UnitActionType.SetRallyPoint), "Barracks exposes rally action");
Check(!Unit().SupportsRallyPoint, "Ordinary units opt out");
var replica = Barracks(barracks.UnitId);
((List<Unit>)units.Units)[0] = replica;
Field(transport, typeof(NetworkHandler), "<IsHost>k__BackingField", false);
Deliver(confirmed);
Check(replica.RallyPoint == barracks.RallyPoint && replica.RallyPointRevision == barracks.RallyPointRevision, "Client applies serialized host confirmation");
replica.ApplyRallyPointState(new RallyPointState(0, false));
Check(replica.RallyPoint is not null, "Old rally update cannot overwrite confirmed point");
var lateReplica = Barracks(barracks.UnitId);
lateReplica.ApplyState(barracks.GetState());
Check(lateReplica.RallyPoint == barracks.RallyPoint, "Building state restores rally point");
((List<Unit>)units.Units)[0] = barracks;
Field(transport, typeof(NetworkHandler), "<IsHost>k__BackingField", true);
Deliver(confirmed with { SenderId = ownerId, RallyPoint = new RallyPointState(100, false) });
Check(barracks.RallyPoint is not null && barracks.RallyPointRevision == 1, "Host rejects forged client confirmation");
barracks.ProductionQueue.Enqueue(Guid.NewGuid(), "grunt", ownerId, 5);
var spawn = Wire(NetworkCommands.CreateProducedUnitCommand(hostId, barracks, barracks.ProductionQueue.ActiveOrder!, Vector3.Zero, new Vector3(2.5f, 0, 2.5f)));
Check(spawn.RallyPoint == barracks.GetRallyPointState(), "Production transmits rally snapshot");
var cleared = Request(NetworkCommands.CreateSetRallyPointRequest(ownerId, barracks.UnitId, null))!;
Check(barracks.RallyPoint is null && cleared.RallyPoint is { HasPosition: false, Revision: 2 }, "Host can clear rally point");
Check(spawn.RallyPoint is { HasPosition: true }, "Existing production snapshot survives later rally changes");
replica.ApplyRallyPointState(cleared.RallyPoint!.Value);
replica.ApplyState(barracks.GetState());
Check(replica.RallyPoint is null, "Clear survives state synchronization");

// Run the actual building-exit transition without graphics or model loading.
var produced = new MobileUnit(new Vector3(2.5f, 0, 2.5f), 1, 1, 1, Guid.NewGuid());
void SetSpawnRally(MobileUnit unit) => typeof(MobileUnit)
    .GetMethod("SetProductionRallyPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(unit, new object?[] { spawn.RallyPoint });
void FinishExit(MobileUnit unit) => typeof(MobileUnit)
    .GetMethod("UpdateLeavingBuilding", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(unit, new object[] { new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.1)) });
produced.BeginLeavingBuilding(barracks.UnitId, produced.Position);
SetSpawnRally(produced);
Check(produced.CurrentCommand is null && world.PathfindingManager.PendingRequests == 0, "Rally waits for exit");
FinishExit(produced);
Check(!produced.IsLeavingBuilding && produced.CurrentCommand?.Target == new Vector2(8.5f, 7.5f) && world.PathfindingManager.PendingRequests == 1, "After exit unit requests normal rally path");
grid.Remove(produced);
produced = new MobileUnit(new Vector3(2.5f, 0, 2.5f), 1, 1, 1, Guid.NewGuid());
produced.BeginLeavingBuilding(barracks.UnitId, produced.Position);
SetSpawnRally(produced);
produced.Stop();
FinishExit(produced);
Check(produced.CurrentCommand is null && world.PathfindingManager.PendingRequests == 1, "Manual stop cancels pending rally order");
grid.Remove(produced);
var waitingSoldier = Unit();
waitingSoldier.SetPosition(new Vector3(8.5f, 0, 7.5f));
Check(grid.TryMove(waitingSoldier, new Point(8, 7)), "First recruit occupies rally cell");
produced = new MobileUnit(new Vector3(2.5f, 0, 2.5f), 1, 1, 1, Guid.NewGuid());
produced.BeginLeavingBuilding(barracks.UnitId, produced.Position);
SetSpawnRally(produced);
FinishExit(produced);
Check(produced.CurrentCommand is GotoCommand nearby && nearby.Target != new Vector2(8.5f, 7.5f) &&
    Vector2.Distance(nearby.Target, new Vector2(8.5f, 7.5f)) < 2, "Later recruit gathers beside occupied rally point");
Console.WriteLine($"Passed {checks} navigation, selection and rally point checks.");

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
// Building placement samples every vertex under the rotated footprint.
grid = new GameGrid(12, 12, 1);
world = World(grid);
terrain = Terrain(12, 12);
grid.BindTerrain(terrain);
Field(world, typeof(GameWorld), "_terrain", terrain);
units = new UnitHandler();
Field(world, typeof(GameWorld), "<Units>k__BackingField", units);
Globals.World = world;
building = new Building(new Vector3(3.25f, 0, 3.25f), Guid.NewGuid());
var placement = building.EvaluatePlacement(world, building.Position, 0);
Check(placement.IsAllowed && placement.HeightDifference == 0, "Flat build site allowed");
Point far = new(placement.Cells.Max(c => c.Cell.X) + 1, placement.Cells.Max(c => c.Cell.Y) + 1);
terrain.SetHeight(far.X, far.Y, 0.5f);
Check(building.EvaluatePlacement(world, building.Position, 0).IsAllowed, "Exact terrain tolerance accepted");
terrain.SetHeight(far.X, far.Y, 0.6f);
placement = building.EvaluatePlacement(world, building.Position, 0);
Check(!placement.IsAllowed && placement.HeightDifference > 0.59f, "Far boundary vertex prevents construction");
Check(placement.Cells.Any(c => c.Issues.HasFlag(PlacementIssue.UnevenTerrain)), "Uneven cells identified for red preview");
Check(!building.CanPlace(building.Position, 0), "Public placement uses same building tolerance");
building.MaximumTerrainHeightDifference = 1;
Check(building.CanPlace(building.Position, 0), "Tolerance configurable per building");
building.MaximumTerrainHeightDifference = 0.5f;
terrain.SetHeight(far.X, far.Y, 0);
Point firstCell = placement.Cells[0].Cell;
grid.GetCell(firstCell).IsBlocked = true;
placement = building.EvaluatePlacement(world, building.Position, 0);
Check(!placement.IsAllowed && placement.Cells.Any(c => c.Issues == PlacementIssue.Blocked), "Blocked cell identified");
grid.GetCell(firstCell).IsBlocked = false;
var blocker = Unit();
blocker.SetPosition(grid.ToWorldPosition(firstCell, 0));
Check(grid.TryMove(blocker, firstCell), "Place blocking mobile unit");
placement = building.EvaluatePlacement(world, building.Position, 0);
Check(!placement.IsAllowed && placement.Cells.Any(c => c.Issues.HasFlag(PlacementIssue.Occupied)), "Occupied cell identified");
grid.Remove(blocker);
Check(!building.EvaluatePlacement(world, new Vector3(0.1f, 0, 0.1f), 0).IsAllowed, "Partial footprint outside map rejected");
Check(!building.EvaluatePlacement(world, new Vector3(float.NaN, 0, 3), 0).IsAllowed, "Nonfinite build position rejected");
Field(building, typeof(Unit), "<Width>k__BackingField", 3);
placement = building.EvaluatePlacement(world, building.Position, 37);
Check(placement.IsAllowed && placement.Cells.Select(c => c.Cell).ToHashSet().SetEquals(
    grid.GetFootprintCells(building, building.Position, 37)), "Preview uses rotated occupancy footprint");
grid = new GameGrid(6, 6, 2);
grid.BindTerrain(terrain);
Field(world, typeof(GameWorld), "<GameGrid>k__BackingField", grid);
Field(building, typeof(Unit), "<Width>k__BackingField", 1);
terrain.SetHeight(3, 3, 1);
Check(!building.EvaluatePlacement(world, building.Position, 0).IsAllowed, "Coarse grid interior vertices tested");
terrain.SetHeight(3, 3, 0);

// Host reserves accepted sites immediately, rejecting competing requests.
grid = new GameGrid(12, 12, 1);
grid.BindTerrain(terrain);
Field(world, typeof(GameWorld), "<GameGrid>k__BackingField", grid);
Field(game, typeof(RTSGame), "_players", new List<Player>());
Globals.MeshHandler = new MeshHandler();
var smallBox = new BoundingBox(new Vector3(-0.4f, 0, -0.4f), new Vector3(0.4f, 1, 0.4f));
var smallVertices = smallBox.GetCorners().Select(p => new VertexPositionColorNormalTexture(p, Color.White, Vector3.Up, Vector2.Zero)).ToArray();
Globals.MeshHandler.Meshes["barracks-1"] = new Mesh("test-barracks", new[] { new SubMesh("body", smallVertices, new[] { 0, 1, 2 }, Vector3.Zero) });
host = new NetworkHost(transport, input, world);
NetworkMessage? BuildRequest(NetworkMessage request) => (NetworkMessage?)typeof(NetworkHost)
    .GetMethod("TryCreateBuildCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { request });
var buildRequest = NetworkCommands.CreateBuildRequest(ownerId, "gdi-barracks", 6.5f, 50, 6.5f, 0, Guid.NewGuid());
terrain.SetHeight(7, 7, 1);
Check(BuildRequest(buildRequest) is null && units.Units.Count == 0, "Host rejects uneven site without registering it");
Check(units.SpawnBuilding("gdi-barracks", new Vector3(6.5f, 0, 6.5f), 0, Guid.NewGuid(), ownerId) is null, "Direct spawn also validates height");
terrain.SetHeight(7, 7, 0);
var buildCommand = BuildRequest(buildRequest);
Check(buildCommand is { Type: NetworkMessageType.BuildCommand, Y: 0 } && units.Units.Count == 1, "Host places valid site at terrain height");
Check(BuildRequest(buildRequest with { UnitId = Guid.NewGuid() }) is null && units.Units.Count == 1, "Host rejects overlapping request in same tick");
Deliver(buildCommand!);
Check(units.Units.Count == 1, "Host confirmation does not duplicate building");
// Shared texel density must be independent of model bounds and ordinary UVs.
var sharedRegion = new TextureHandler.TextureRegion { AtlasIndex = 0, X = 16, Y = 32, Width = 256, Height = 128, AtlasWidth = 1024, AtlasHeight = 1024 };
SubMesh TexturedPart(string? sharedName, float length)
{
    VertexPositionColorNormalTexture[] v =
    [
        new(new Vector3(0, 0, 0), Color.White, Vector3.Forward, new Vector2(0.7f, 0.8f)),
        new(new Vector3(length, 0, 0), Color.White, Vector3.Forward, new Vector2(0.9f, 0.8f)),
        new(new Vector3(0, 1, 0), Color.White, Vector3.Forward, new Vector2(0.7f, 0.9f))
    ];
    return new SubMesh("face", v, new[] { 0, 1, 2 }, Vector3.Zero, 0, sharedRegion, sharedTextureName: sharedName);
}
var sharedPart = TexturedPart("bricks", 1);
var plainPart = TexturedPart(null, 1);
var uvMesh = new Mesh("mixed", new[] { sharedPart, plainPart });
uvMesh.ApplySharedTextureMapping();
float PixelDistance(SubMesh part) => Math.Abs(part.Vertices[1].TextureCoordinate.X - part.Vertices[0].TextureCoordinate.X) * sharedRegion.AtlasWidth;
Check(Math.Abs(PixelDistance(sharedPart) - 32) < 0.001f, "Default shared density is 32 pixels per unit");
Check(plainPart.Vertices[0].TextureCoordinate == new Vector2(0.7f, 0.8f) && !plainPart.RepeatSharedTexture, "Ordinary UVs remain untouched");
Check(sharedPart.RepeatSharedTexture, "Shared region uses shader-local repeating");
var largePart = TexturedPart("bricks", 20);
new Mesh("large", new[] { largePart }).ApplySharedTextureMapping();
Check(Math.Abs(PixelDistance(largePart) - 640) < 0.001f, "Larger buildings retain pixel density and multiple repeats");
uvMesh.LocalTransform = Matrix.CreateScale(2);
uvMesh.ApplySharedTextureMapping(64);
Check(Math.Abs(PixelDistance(sharedPart) - 128) < 0.001f, "Explicit density includes permanent mesh scale");
var previousUv = sharedPart.Vertices.Select(v => v.TextureCoordinate).ToArray();
uvMesh.ApplySharedTextureMapping(64);
Check(previousUv.SequenceEqual(sharedPart.Vertices.Select(v => v.TextureCoordinate)), "Repeated mapping is idempotent");
Check(Math.Abs((sharedPart.Vertices[2].TextureCoordinate.Y - sharedPart.Vertices[0].TextureCoordinate.Y) * sharedRegion.AtlasHeight + 128) < 0.001f, "Non-square textures preserve vertical density");
foreach (float invalid in new[] { 0, -1, float.NaN, float.PositiveInfinity })
{
    bool rejected = false;
    try { uvMesh.ApplySharedTextureMapping(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "Invalid shared texel density rejected");
}

// Seed atlas metadata only, so the real BBModel importer can be tested without a GPU.
var textureHandler = new TextureHandler(null!);
Type atlasType = typeof(TextureHandler).GetNestedType("Atlas", BindingFlags.NonPublic)!;
object atlas = Activator.CreateInstance(atlasType, new object?[] { null, 2 })!;
var atlasRegions = (Dictionary<string, TextureHandler.TextureRegion>)atlasType.GetField("Regions")!.GetValue(atlas)!;
((System.Collections.IList)typeof(TextureHandler).GetField("_atlases", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(textureHandler)!).Add(atlas);
Globals.TextureHandler = textureHandler;
Globals.MaterialMaskTextureHandler = new TextureHandler(null!);
string fixture = Path.GetTempFileName();
try
{
    atlasRegions["bricks"] = sharedRegion;
    atlasRegions[$"bbmodel:{Path.GetFullPath(fixture)}:texture:0"] = new TextureHandler.TextureRegion
        { AtlasIndex = 1, X = 8, Y = 8, Width = 32, Height = 32, AtlasWidth = 1024, AtlasHeight = 1024 };
    File.WriteAllText(fixture, """
    {"name":"mixed-model","resolution":{"width":32,"height":32},
     "textures":[{"id":"0","name":"ordinary.png"},{"id":"1","name":"shared:bricks"}],
     "elements":[
       {"uuid":"cube","name":"mixed-cube","type":"cube","from":[0,0,0],"to":[10,10,10],"origin":[0,0,0],
        "faces":{"north":{"texture":0,"uv":[0,0,32,32]},"south":{"texture":1,"uv":[0,0,32,32]}}},
       {"uuid":"mesh","name":"mixed-mesh","type":"mesh","origin":[0,0,0],
        "vertices":{"a":[0,0,0],"b":[10,0,0],"c":[0,10,0],"d":[10,10,0]},
        "faces":{"plain":{"texture":0,"vertices":["a","b","c"],"uv":{"a":[0,0],"b":[32,0],"c":[0,32]}},
                 "shared":{"texture":1,"vertices":["b","d","c"],"uv":{"b":[0,0],"d":[32,0],"c":[0,32]}}}}
     ],"outliner":["cube","mesh"]}
    """);
    var imported = BBModelLoader.Load(fixture);
    Check(imported.SubMeshes.Count == 4 && imported.Root.Children.Count == 2, "Mixed materials split into batches without changing hierarchy");
    Check(imported.SubMeshes.Count(p => p.SharedTextureName == "bricks") == 2, "Shared provenance retained for cubes and meshes");
    Check(imported.SubMeshes.Select(p => p.TextureAtlasIndex).Distinct().Count() == 2, "Shared and local textures can use separate atlases");
    var originalUvs = imported.SubMeshes.Where(p => p.SharedTextureName is null).SelectMany(p => p.Vertices.Select(v => v.TextureCoordinate)).ToArray();
    imported.ApplySharedTextureMapping();
    Check(originalUvs.SequenceEqual(imported.SubMeshes.Where(p => p.SharedTextureName is null).SelectMany(p => p.Vertices.Select(v => v.TextureCoordinate))), "Importer preserves authored ordinary face UVs");
    Check(imported.SubMeshes.Where(p => p.SharedTextureName is not null).All(p => p.RepeatSharedTexture), "All shared cube and mesh batches opt into repeating");
}
finally { File.Delete(fixture); }
// Continuous bulldozer earthwork: real host controller, headless terrain and wire replay.
Globals.MeshHandler.Meshes["bulldozer-1"] = Globals.MeshHandler.Meshes["barracks-1"];
GDIBulldozer SetupEarthwork()
{
    grid = new GameGrid(40, 40, 1);
    terrain = Terrain(40, 40);
    Field(terrain, typeof(Terrain), "_tiles", new TerrainTile[40, 40]);
    grid.BindTerrain(terrain);
    world = World(grid);
    Field(world, typeof(GameWorld), "_terrain", terrain);
    units = new UnitHandler();
    Field(world, typeof(GameWorld), "<Units>k__BackingField", units);
    Field(world, typeof(GameWorld), "<PathfindingManager>k__BackingField", new PathfindingManager(world));
    Globals.World = world;
    var dozer = new GDIBulldozer(new Vector3(8.5f, 0, 8.5f), Guid.NewGuid());
    Field(dozer, typeof(Unit), "<ArmyId>k__BackingField", armyId);
    var driver = Unit();
    Field(driver, typeof(Unit), "<UnitId>k__BackingField", Guid.NewGuid());
    Field(driver, typeof(Unit), "<ArmyId>k__BackingField", armyId);
    if (!dozer.Occupancy!.TryAdd(driver, OccupantRole.Driver, out _)) throw new Exception("Driver fixture failed");
    ((List<Unit>)units.Units).Add(dozer);
    grid.TryMove(dozer, grid.ToCell(dozer.Position));
    return dozer;
}
NetworkMessage DriveRequest(GDIBulldozer dozer, float x = 20.5f, float z = 8.5f) => new(
    NetworkMessageType.EarthworkRequest, ownerId, UnitId: dozer.UnitId, X: x, Z: z, EarthworkKind: EarthworkKind.LevelAndConcrete);
var bulldozer = SetupEarthwork();
var earthMessages = new List<NetworkMessage>();
var earthController = new EarthworkController(world, hostId, earthMessages.Add, false);
Check(earthController.Start(DriveRequest(bulldozer) with { SenderId = Guid.NewGuid() }) is null, "Earthwork rejects foreign control");
Check(earthController.Start(DriveRequest(bulldozer) with { X = float.NaN }) is null, "Earthwork rejects invalid target");
var driveStart = earthController.Start(DriveRequest(bulldozer))!;
Check(driveStart.EarthworkOrder is { IsDrive: true, TargetHeight: 0 }, "Drive order freezes initial terrain height");
Check(Wire(driveStart).EarthworkOrder == driveStart.EarthworkOrder, "Drive order survives network serialization");
earthController.Update(0.1f);
Check(bulldozer.Position.X > 8.5f && terrain.GetTile(8, 8) == TerrainTile.Concrete, "First simulation tick moves and concretes without per-cell delay");
Check(terrain.GetTile(8, 7) == TerrainTile.Concrete && terrain.GetTile(8, 9) == TerrainTile.Concrete && terrain.GetTile(8, 6) != TerrainTile.Concrete, "Cardinal strip matches bulldozer width");
for (int i = 0; i < 70 && earthController.ActiveJobs > 0; i++) earthController.Update(0.1f);
Check(earthController.ActiveJobs == 0 && Math.Abs(bulldozer.Position.X - 20.5f) < 0.001f, "Drive reaches target and terminates");
Check(bulldozer.MoveSpeed == 7 && bulldozer.EarthworkOrder is null, "Completion restores normal movement");
Check(terrain.GetTile(15, 8) == TerrainTile.Concrete && terrain.GetTile(15, 10) != TerrainTile.Concrete, "Drive paints a strip rather than an 8x8 area");
Check(!earthMessages.Any(m => m.Type == NetworkMessageType.GotoCommand), "Drive uses no per-cell pathfinding orders");
var hostHeights = Enumerable.Range(0, 40).SelectMany(z => Enumerable.Range(0, 40).Select(x => terrain.GetHeight(x, z))).ToArray();
var hostTiles = Enumerable.Range(0, 40).SelectMany(z => Enumerable.Range(0, 40).Select(x => terrain.GetTile(x, z))).ToArray();
var replayWorker = SetupEarthwork();
replayWorker.BeginEarthwork(Wire(driveStart).EarthworkOrder!);
foreach (var original in earthMessages.Where(m => m.Type == NetworkMessageType.EarthworkCellCommand))
{
    var m = Wire(original);
    if (!replayWorker.ApplyEarthworkDrive(world, m.EarthworkOrderId!.Value, m.EarthworkSequence, m.EarthworkCells!, new(m.X, m.Z), false))
        throw new Exception("Client rejected authoritative drive patch");
}
Check(hostHeights.SequenceEqual(Enumerable.Range(0, 40).SelectMany(z => Enumerable.Range(0, 40).Select(x => terrain.GetHeight(x, z)))) && hostTiles.SequenceEqual(Enumerable.Range(0, 40).SelectMany(z => Enumerable.Range(0, 40).Select(x => terrain.GetTile(x, z)))), "Client replay exactly matches host terrain");
var lastPatch = earthMessages.Last(m => m.Type == NetworkMessageType.EarthworkCellCommand);
Check(!replayWorker.ApplyEarthworkDrive(world, lastPatch.EarthworkOrderId!.Value, lastPatch.EarthworkSequence, lastPatch.EarthworkCells!, new(lastPatch.X, lastPatch.Z), false), "Duplicate patches are ignored");

bulldozer = SetupEarthwork();
terrain.SetHeight(15, 8, Earthwork.MaximumHeightChange + 1);
var limitedPreview = Earthwork.Preview(world, bulldozer, new(20, 8), EarthworkKind.LevelAndConcrete);
Check(limitedPreview.IsAllowed && limitedPreview.Cells.Any(c => !c.Allowed), "Preview allows a safe prefix and marks the blocked continuation");
earthMessages.Clear();
earthController = new(world, hostId, earthMessages.Add, false);
Check(earthController.Start(DriveRequest(bulldozer)) is not null, "Distant obstacle does not reject usable prefix");
earthController.Update(20);
Check(earthController.ActiveJobs == 0 && bulldozer.Position.X < 15 && terrain.GetHeight(15, 8) == Earthwork.MaximumHeightChange + 1, "Large tick stops before excessive height without modifying it");
Check(terrain.GetTile(10, 8) == TerrainTile.Concrete && terrain.GetTile(16, 8) != TerrainTile.Concrete, "Completed strip remains after obstacle stop");

bulldozer = SetupEarthwork();
earthController = new(world, hostId, earthMessages.Add, false);
earthController.Start(DriveRequest(bulldozer));
earthController.Update(0.1f);
grid.GetCell(12, 8).IsBlocked = true;
earthController.Update(10);
Check(earthController.ActiveJobs == 0 && bulldozer.Position.X < 12 && terrain.GetTile(12, 8) != TerrainTile.Concrete, "New obstacle immediately stops drive");

bulldozer = SetupEarthwork();
var neighbor = Unit();
neighbor.SetPosition(new Vector3(11.5f, 0, 10.5f));
grid.TryMove(neighbor, new Point(11, 10));
Check(!Earthwork.Preview(world, bulldozer, new(20, 8), EarthworkKind.LevelAndConcrete).IsAllowed, "Shared-vertex neighbor occupancy protects adjacent structures");

bulldozer = SetupEarthwork();
earthController = new(world, hostId, earthMessages.Add, false);
earthController.Start(DriveRequest(bulldozer));
earthController.Update(0.1f);
var stoppedPosition = bulldozer.Position;
earthController.CancelForRequest(new(NetworkMessageType.StopRequest, ownerId, UnitIds: new[] { bulldozer.UnitId }));
earthController.Update(1);
Check(earthController.ActiveJobs == 0 && bulldozer.Position == stoppedPosition && bulldozer.MoveSpeed == 7, "Stop cancels drive and restores speed");

bulldozer = SetupEarthwork();
for (int z = 0; z < 40; z++) for (int x = 0; x < 40; x++) terrain.SetHeight(x, z, 1);
terrain.SetTile(8, 8, TerrainTile.Concrete);
earthController = new(world, hostId, earthMessages.Add, false);
var raisedStart = earthController.Start(DriveRequest(bulldozer, 16.5f, 16.5f))!;
Check(raisedStart.EarthworkOrder!.TargetHeight == 1, "Starting concrete determines target height");
terrain.SetHeight(15, 15, 1.5f);
earthController.Update(10);
Check(earthController.ActiveJobs == 0 && terrain.GetHeight(15, 15) == 1, "Diagonal drive regrades terrain to frozen height");
Check(terrain.GetTile(12, 12) == TerrainTile.Concrete && terrain.GetTile(12, 7) != TerrainTile.Concrete, "Diagonal strip has no gaps or distant side effects");
var removePreview = Earthwork.Preview(world, bulldozer, new(13, 13), EarthworkKind.RemoveConcrete);
Check(!removePreview.Order.IsDrive && removePreview.Cells.Count == 64, "Concrete removal retains area workflow");
Earthwork.ApplyCell(world, removePreview.Order, new(12, 12), false);
Check(terrain.GetTile(12, 12) == TerrainTile.Dirt && terrain.GetHeight(12, 12) == 1, "Concrete removal preserves height");

bulldozer = SetupEarthwork();
grid.GetCell(9, 8).AllowedMovement = MovementModes.Walk;
Check(!Earthwork.Preview(world, bulldozer, new(20, 8), EarthworkKind.LevelAndConcrete).IsAllowed, "Drive respects no-vehicle cells");
Check(!Earthwork.Preview(world, bulldozer, new(0, 0), EarthworkKind.RemoveConcrete).IsAllowed, "Removal rejects map-edge area");
bulldozer = SetupEarthwork();
earthController = new(world, hostId, earthMessages.Add, false);
earthController.Start(DriveRequest(bulldozer));
earthController.Update(0.1f);
var beforeBlockedSection = Enumerable.Range(0, 40).SelectMany(z => Enumerable.Range(0, 40).Select(x => terrain.GetTile(x, z))).ToArray();
grid.GetCell(11, 9).IsBlocked = true;
earthController.Update(0.1f);
Check(earthController.ActiveJobs == 0 && beforeBlockedSection.SequenceEqual(Enumerable.Range(0, 40).SelectMany(z => Enumerable.Range(0, 40).Select(x => terrain.GetTile(x, z)))), "One blocked blade edge rejects entire section atomically");

bulldozer = SetupEarthwork();
earthController = new(world, hostId, earthMessages.Add, false);
earthController.Start(DriveRequest(bulldozer, 0.5f, 8.5f));
earthController.Update(10);
Check(earthController.ActiveJobs == 0 && bulldozer.Position.X > 0.5f, "Vehicle width stops drive before map edge");
Check(grid.GetOccupant(grid.ToCell(bulldozer.Position)) == bulldozer, "Stopped worker retains grid occupancy");

bulldozer = SetupEarthwork();
earthController = new(world, hostId, earthMessages.Add, false);
earthController.Start(DriveRequest(bulldozer));
bulldozer.Occupancy!.TryRemove(bulldozer.Occupancy.Occupants[0].UnitId, out _);
earthController.Update(1);
Check(earthController.ActiveJobs == 0 && bulldozer.EarthworkOrder is null, "Driver loss stops earthwork");
Check(earthController.Start(DriveRequest(bulldozer)) is null, "Empty bulldozer cannot start earthwork");
// Helicopter host simulation, supplies, landing reservations and snapshot replay.
Globals.MeshHandler.Meshes["heli-1"] = Globals.MeshHandler.Meshes["barracks-1"];
Globals.MeshHandler.Meshes["helipad-1"] = Globals.MeshHandler.Meshes["barracks-1"];
Helicopter SetupHelicopter(int passengers = 0)
{
    var old = SetupEarthwork();
    grid.Remove(old);
    ((List<Unit>)units.Units).Clear();
    var helicopter = new Helicopter(new(10.5f, 0, 10.5f), Guid.NewGuid(), passengers);
    Field(helicopter, typeof(Unit), "<ArmyId>k__BackingField", armyId);
    ((List<Unit>)units.Units).Add(helicopter);
    if (!helicopter.InitializeOnGround(world)) throw new Exception("Helicopter fixture could not land");
    return helicopter;
}
void FlyTicks(Helicopter helicopter, int count)
{
    for (int i = 0; i < count; i++) helicopter.SimulateFlight(world, 0.1f);
}
Helipad AddPad(Vector3 position)
{
    var pad = new Helipad(position, Guid.NewGuid()) { LandingLocalPosition = new(0, 0.1f, 0) };
    Field(pad, typeof(Unit), "<ArmyId>k__BackingField", armyId);
    pad.AdvanceConstruction(pad.TotalBuildingPointsNeeded);
    ((List<Unit>)units.Units).Add(pad);
    if (!grid.TryPlace(pad, position, 0)) throw new Exception("Helipad fixture placement failed");
    return pad;
}
var heli = SetupHelicopter();
Check(heli.IsLanded && grid.GetOccupant(10, 10) == heli, "Helicopter starts landed with occupied ground footprint");
Check(UnitFactory.SpawnUnit("heli", new(20, 0, 20), 0, Guid.NewGuid(), ownerId) is Helicopter, "Helicopter console/factory alias");
Check(!heli.CanFireWeapon && !heli.TryConsumeAmmunition(), "Landed helicopter cannot shoot");
float fullFuel = heli.Fuel;
Check(heli.TryReceiveGotoCommand(world, new(new(24.5f, 10.5f))), "Fly command takes off");
Check(grid.GetOccupant(10, 10) is null && world.PathfindingManager.PendingRequests == 0, "Flying aircraft releases ground grid and bypasses pathfinding");
grid.GetCell(18, 10).IsBlocked = true;
terrain.SetHeight(18, 10, 4);
FlyTicks(heli, 150);
Check(Math.Abs(heli.Position.X - 24.5f) < 0.01f && heli.Position.Y >= 8, "Flight crosses blocked ground and raised terrain");
Check(heli.Fuel < fullFuel && heli.Fuel > 0, "Flight and hover consume fuel");
var beforeTurn = heli.Position;
heli.TryReceiveGotoCommand(world, new(new(10.5f, 10.5f)));
FlyTicks(heli, 10);
Check(heli.Position.X < beforeTurn.X && Math.Abs(heli.Position.Z - beforeTurn.Z) < 0.01f, "Helicopter reverses direction without a ground turning radius");
heli.Stop();
var hoveringAt = new Vector2(heli.Position.X, heli.Position.Z);
FlyTicks(heli, 5);
Check(new Vector2(heli.Position.X, heli.Position.Z) == hoveringAt && heli.FlightState == HelicopterFlightState.Hovering, "Stop hovers in place");
Check(heli.TryConsumeAmmunition() && heli.Ammunition == 39, "Successful helicopter shot consumes one round");
heli.PlayShotEffects();
Check(heli.VisualRecoilOffset == Vector3.Zero && heli.VisualRecoilPitchDegrees == 0, "Helicopter has no recoil");
float fuelBeforeGround = heli.Fuel;
Check(heli.RequestLanding(world, new(24.5f, 24.5f)), "Safe ground landing accepted");
FlyTicks(heli, 200);
Check(heli.IsLanded && heli.AssignedHelipadId is null && grid.GetOccupant(24, 24) == heli, "Ground landing reserves and occupies destination");
float landedFuel = heli.Fuel;
FlyTicks(heli, 30);
Check(heli.Fuel == landedFuel && heli.Fuel < fuelBeforeGround && heli.Ammunition == 39, "Ground landing stops consumption but provides no supplies");

var pad = AddPad(new(30.5f, 0, 30.5f));
Check(pad.CanAccept(world, heli) && heli.ReturnToHelipad(world), "Completed allied helipad accepts return");
var rivalHeli = new Helicopter(new(6.5f, 0, 30.5f), Guid.NewGuid());
Field(rivalHeli, typeof(Unit), "<ArmyId>k__BackingField", armyId);
((List<Unit>)units.Units).Add(rivalHeli);
Check(!pad.CanAccept(world, rivalHeli), "Approaching helicopter reserves helipad against a second aircraft");
Check(rivalHeli.ReturnToHelipad(world) && rivalHeli.AssignedHelipadId is null, "Occupied pad falls back to a ground landing");
FlyTicks(heli, 200);
Check(heli.IsLanded && heli.AssignedHelipadId == pad.UnitId && Math.Abs(heli.Position.Y - pad.GetLandingPosition(heli).Y) < 0.001f, "Helicopter lands on pad surface instead of terrain or flag height");
Check(heli.Fuel == heli.MaximumFuel && heli.Ammunition == heli.MaximumAmmunition, "Landed pad refills fuel and ammunition");
Check(grid.GetOccupant(30, 30) == pad, "Landing on pad never replaces building occupancy");
heli.TakeOff(world);
Check(pad.CanAccept(world, rivalHeli), "Takeoff releases helipad reservation");

heli = SetupHelicopter(4);
var passenger = Unit();
Field(passenger, typeof(Unit), "<UnitId>k__BackingField", Guid.NewGuid());
Field(passenger, typeof(Unit), "<ArmyId>k__BackingField", armyId);
Check(heli.Occupancy!.CanEnter(passenger, OccupantRole.Passenger), "Transport extension allows passengers while landed");
heli.TakeOff(world);
Check(!heli.Occupancy.CanEnter(passenger, OccupantRole.Passenger), "Passengers cannot board during flight");
heli.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.1)));
Check(heli.MainRotorRadians > 0 && heli.RearRotorRadians > 0, "Rotor animation runs even when optional model pivots are absent");
FlyTicks(heli, 40);
while (heli.TryConsumeAmmunition()) { }
Check(heli.Ammunition == 0 && !heli.CanFireWeapon, "Ammunition cannot become negative and empty magazine blocks fire");
FlyTicks(heli, 100);
Check(heli.IsLanded && heli.Ammunition == 0, "Empty magazine triggers landing fallback when no pad is available");

heli = SetupHelicopter();
heli.TakeOff(world);
FlyTicks(heli, 40);
Field(heli, typeof(Helicopter), "<Fuel>k__BackingField", 0.01f);
FlyTicks(heli, 50);
Check(heli.IsLanded && heli.Fuel == 0 && heli.HitPoints > 0, "Empty fuel tank performs safe emergency landing");
Check(!heli.TakeOff(world), "Empty tank prevents another takeoff");

heli = SetupHelicopter();
pad = AddPad(new(28.5f, 0, 28.5f));
heli.TakeOff(world);
FlyTicks(heli, 40);
Field(heli, typeof(Helicopter), "<Fuel>k__BackingField", 23f);
heli.SimulateFlight(world, 0.1f);
Check(heli.AssignedHelipadId == pad.UnitId, "Low fuel automatically selects a free allied pad");
((List<Unit>)units.Units).Remove(pad);
grid.Remove(pad);
heli.SimulateFlight(world, 0.1f);
Check(heli.AssignedHelipadId is null, "Destroyed helipad invalidates reservation");

heli = SetupHelicopter();
host = new NetworkHost(transport, input, world);
NetworkMessage? HeliOrder(NetworkMessage request) => (NetworkMessage?)typeof(NetworkHost).GetMethod("TryCreateHelicopterOrder", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { request });
var takeoffRequest = new NetworkMessage(NetworkMessageType.HelicopterOrderRequest, ownerId, UnitId: heli.UnitId, HelicopterOrder: HelicopterOrder.TakeOff);
Check(HeliOrder(takeoffRequest with { SenderId = Guid.NewGuid() }) is null, "Host rejects unauthorized flight orders");
Check(HeliOrder(takeoffRequest with { HelicopterOrder = (HelicopterOrder)999 }) is null, "Host rejects unknown flight orders");
Check(HeliOrder(takeoffRequest) is { Type: NetworkMessageType.UnitStateCommand }, "Host confirms takeoff through authoritative state");
FlyTicks(heli, 40);
heli.TryConsumeAmmunition();
var flightState = Wire(NetworkCommands.CreateUnitStateCommand(hostId, heli.GetState())).UnitState!;
var clientHeli = new Helicopter(Vector3.Zero, heli.UnitId);
clientHeli.ApplyState(flightState);
Check(clientHeli.Position == heli.Position && clientHeli.Fuel == heli.Fuel && clientHeli.Ammunition == heli.Ammunition && clientHeli.FlightState == heli.FlightState, "Flight snapshot wire replay matches position, supplies and phase");
clientHeli.ApplyState(flightState with { Revision = flightState.Revision - 1, Payload = JsonSerializer.SerializeToUtf8Bytes(new HelicopterState(0, 0, 0, 0, HelicopterFlightState.Landed, 0, 0, null, null, null, null)) });
Check(clientHeli.Position == heli.Position, "Stale flight state cannot rewind client");
var fireRequest = new NetworkMessage(NetworkMessageType.AttackRequest, hostId, UnitIds: new[] { heli.UnitId }, X: heli.Position.X + 1, Y: 0, Z: heli.Position.Z);
NetworkMessage? HeliFire(NetworkMessage request) => (NetworkMessage?)typeof(NetworkHost).GetMethod("TryCreateAttackCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { request });
int ammunitionBefore = heli.Ammunition;
Check(HeliFire(fireRequest with { SenderId = Guid.NewGuid() }) is null && heli.Ammunition == ammunitionBefore, "Unauthorized attack cannot consume ammunition");
Check(HeliFire(fireRequest with { X = 1000 }) is null && heli.Ammunition == ammunitionBefore, "Out-of-range shot rejected before ammunition consumption");
Check(HeliFire(fireRequest) is not null && heli.Ammunition == ammunitionBefore - 1, "Authoritative attack consumes ammunition exactly once");
Check(HeliFire(fireRequest) is null && heli.Ammunition == ammunitionBefore - 1, "Duplicate rapid attack request cannot bypass weapon cooldown");
Check(!heli.Actions.Any(a => a.Type == UnitActionType.LeaveContainer), "Combat helicopter exposes no empty transport action");
heli = SetupHelicopter();
pad = AddPad(new(25.5f, 0, 25.5f));
Check(heli.ReturnToHelipad(world), "Pad reservation for diversion test");
FlyTicks(heli, 5);
heli.SetAttackGroundTarget(new(15.5f, 0, 15.5f));
Check(heli.AssignedHelipadId is null, "Explicit attack releases previous landing reservation");
FlyTicks(heli, 50);
Check(heli.CanFireWeapon && !heli.IsLanded, "Attack can replace a return-to-pad order");
heli.RequestLanding(world, new(22.5f, 22.5f));
FlyTicks(heli, 20);
Check(heli.RequestLanding(world, new(8.5f, 28.5f)), "Landing destination can be replaced during approach");
FlyTicks(heli, 200);
Check(heli.IsLanded && Math.Abs(heli.Position.Z - 28.5f) < 0.01f, "Diverted landing reaches the new position before descending");

// Import the actual user-authored models with atlas metadata, without a graphics device.
Globals.MaterialMaskTextureHandler = textureHandler;
foreach (string relative in new[] { "vehicles/heli-1.bbmodel", "buildings/helipad-1.bbmodel" })
{
    string modelPath = Path.GetFullPath(Path.Combine("Content/models", relative));
    using var modelJson = JsonDocument.Parse(File.ReadAllText(modelPath));
    int textureIndex = 0;
    foreach (var texture in modelJson.RootElement.GetProperty("textures").EnumerateArray())
    {
        string name = texture.GetProperty("name").GetString()!;
        atlasRegions[name.StartsWith("shared:") ? name[7..] : $"bbmodel:{modelPath}:texture:{textureIndex}"] = sharedRegion;
        atlasRegions[$"bbmodel:{modelPath}:material-mask:{textureIndex}"] = sharedRegion;
        string sourceName = texture.TryGetProperty("relative_path", out var rel) ? rel.GetString()! : name;
        string sourcePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(modelPath)!, sourceName));
        atlasRegions[Path.Combine(Path.GetDirectoryName(sourcePath)!, Path.GetFileNameWithoutExtension(sourcePath) + "-MaterialMask.png")] = sharedRegion;
        textureIndex++;
    }
    var actualMesh = BBModelLoader.Load(modelPath);
    Check(actualMesh.SubMeshes.Count > 0, "Authored helicopter/helipad geometry imports");
    Globals.MeshHandler.Meshes[Path.GetFileNameWithoutExtension(relative)] = actualMesh;
}
var actualHeli = new Helicopter(new(10, 0, 10), Guid.NewGuid());
var actualMeshSet = new MeshSet(Globals.MeshHandler.Meshes["heli-1"]);
Check(actualMeshSet.SetPivotRotation("pivot:rotor_main", Quaternion.CreateFromAxisAngle(Vector3.Up, 1)), "Actual main rotor pivot is animated");
Check(actualMeshSet.Pivots.Any(p => p.Name == "pivot:turret"), "Actual helicopter turret pivot imports");
Check(!actualMeshSet.SetPivotRotation("pivot:rotor_rear", Quaternion.Identity), "Absent rear rotor pivot is optional");
var unanimatedMeshSet = new MeshSet(Globals.MeshHandler.Meshes["heli-1"]);
actualMeshSet.TryGetPivotWorldTransform("pivot:rotor_main", Matrix.Identity, out var animatedRotor);
unanimatedMeshSet.TryGetPivotWorldTransform("pivot:rotor_main", Matrix.Identity, out var restingRotor);
Check(animatedRotor != restingRotor, "Rotor animation is isolated per helicopter instance");
Check(float.IsFinite(actualHeli.GroundOffset) && actualHeli.Height > 0, "Actual helicopter bounds provide valid landing offset");
// Height-aware damage: an aircraft and ground unit can share X/Z without sharing hits.
heli = SetupHelicopter();
heli.TakeOff(world);
FlyTicks(heli, 40);
var belowAircraft = Unit();
Field(belowAircraft, typeof(Unit), "<UnitId>k__BackingField", Guid.NewGuid());
Field(belowAircraft, typeof(Unit), "<Height>k__BackingField", 1f);
belowAircraft.SetPosition(new(heli.Position.X, 0, heli.Position.Z));
((List<Unit>)units.Units).Insert(0, belowAircraft);
host = new NetworkHost(transport, input, world);
Unit? ImpactTarget(Guid attacker, Vector3 point) => (Unit?)typeof(NetworkHost).GetMethod("FindImpactTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { attacker, point });
Check(ImpactTarget(Guid.NewGuid(), heli.Position + Vector3.Up * (heli.Height * 0.5f)) == heli, "Aerial hit selects helicopter rather than unit below");
Check(ImpactTarget(heli.UnitId, belowAircraft.Position) == belowAircraft, "Helicopter ground attack cannot hit itself");
Check(ImpactTarget(belowAircraft.UnitId, belowAircraft.Position) is null, "Ground impact cannot hit an aircraft above it");

heli = SetupHelicopter();
pad = AddPad(new(25.5f, 0, 25.5f));
heli.ReturnToHelipad(world);
for (int i = 0; i < 200 && heli.FlightState != HelicopterFlightState.Landing; i++) heli.SimulateFlight(world, 0.1f);
Check(heli.FlightState == HelicopterFlightState.Landing, "Pad approach enters descent phase");
Field(heli, typeof(Helicopter), "<Fuel>k__BackingField", 0f);
FlyTicks(heli, 100);
Check(heli.IsLanded && heli.HitPoints > 0 && heli.AssignedHelipadId == pad.UnitId && heli.Fuel > 0, "Fuel exhaustion above reserved pad completes landing and refuels");

heli = SetupHelicopter();
heli.TakeOff(world);
FlyTicks(heli, 40);
for (int z = 0; z < 40; z++) for (int x = 0; x < 40; x++) grid.GetCell(x, z).IsBlocked = true;
Field(heli, typeof(Helicopter), "<Fuel>k__BackingField", 0f);
FlyTicks(heli, 100);
Check(heli.HitPoints == 0 && heli.IsLanded, "No safe ground and no fuel results in emergency touchdown failure rather than unlimited hovering");
var actualBounds = new MeshSet(Globals.MeshHandler.Meshes["heli-1"]).GetBounds();
Check(actualHeli.Width >= actualBounds.Max.X - actualBounds.Min.X && actualHeli.Length >= actualBounds.Max.Z - actualBounds.Min.Z, "Ground landing footprint includes the authored helicopter geometry");
Console.WriteLine($"Passed {checks} gameplay, UV, earthwork and helicopter checks.");

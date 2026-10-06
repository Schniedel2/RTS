using System;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using RTS;

internal static class UnitSelectionGeometryChecks
{
    private sealed class Vehicle : MobileUnit
    {
        public bool Air;
        public bool Dying;
        public override TargetDomain Domain => Air ? TargetDomain.Air : TargetDomain.Ground;
        public override bool IsDying => Dying;
        public Vehicle(Vector3 position) : base(position, Guid.NewGuid())
        {
            Width = Length = 1;
            BoundingBox bounds = new(new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 1, 0.5f));
            var vertices = bounds.GetCorners().Select(p => new VertexPositionColorNormalTexture(p, Color.White, Vector3.Up, Vector2.Zero)).ToArray();
            SetMeshSet(new MeshSet(new Mesh("selection-test", new[] { new SubMesh("body", vertices, new[] { 0, 1, 2 }, Vector3.Zero) })));
        }
    }
    private sealed class GroundBuilding(Vector3 position) : Building(position, Guid.NewGuid())
    {
        public void Dimensions(int width, int length) { Width = width; Length = length; }
    }
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new Exception("Selection geometry: " + message); checks++; }
        GameWorld world = new(65, 65, 1, graphicsEnabled: false);
        Vehicle ground = new(new Vector3(10.25f, 0, 10.25f));
        Ray ray = new(new Vector3(10.25f, 20, 10.25f), Vector3.Down);
        Vector3 hit = ground.Position;
        Check(UnitSelectionGeometry.ContainsGroundPoint(ground, hit, 1), "moving world position is hit without grid registration");
        Check(!UnitSelectionGeometry.ContainsGroundPoint(ground, new(12, 0, 10), 1), "distant ground point rejected");
        Check(UnitSelectionGeometry.ContainsGroundPoint(ground, new(10.9f, 0, 10.25f), 1), "small edge tolerance");
        GroundBuilding building = new(hit); building.Dimensions(4, 4);
        Check(UnitSelectionGeometry.Pick(new Unit[] { building, ground }, ray, hit, 1, _ => true) == ground, "mobile prioritized over building");
        Check(UnitSelectionGeometry.Pick(new Unit[] { ground }, ray, null, 1, _ => true) is null, "ground needs terrain intersection");
        Check(UnitSelectionGeometry.Pick(new Unit[] { ground }, ray, hit, 1, _ => false) is null, "hidden unit rejected");
        ground.Dying = true;
        Check(UnitSelectionGeometry.Pick(new Unit[] { ground }, ray, hit, 1, _ => true) is null, "dying unit rejected");
        ground.Dying = false;
        Vehicle air = new(new Vector3(hit.X, 5, hit.Z)) { Air = true };
        Vehicle highAir = new(new Vector3(hit.X, 10, hit.Z)) { Air = true };
        Check(UnitSelectionGeometry.Pick(new Unit[] { ground, air }, ray, hit, 1, _ => true) == air, "aircraft hit wins over terrain candidate");
        Check(UnitSelectionGeometry.Pick(new Unit[] { air, highAir }, ray, hit, 1, _ => true) == highAir, "nearest air intersection wins");
        Check(UnitSelectionGeometry.Pick(new Unit[] { air }, ray, null, 1, _ => true) == air, "aircraft selectable with no terrain hit");
        Ray below = new(new Vector3(hit.X, 2, hit.Z), Vector3.Up);
        Check(UnitSelectionGeometry.Pick(new Unit[] { air }, below, new Vector3(hit.X, 3, hit.Z), 1, _ => true) is null, "terrain occludes aircraft ray");
        air.Embark(ground.UnitId);
        Check(UnitSelectionGeometry.Pick(new Unit[] { air }, ray, hit, 1, _ => true) is null, "embarked aircraft rejected");
        Vehicle landed = new(hit);
        Check(UnitSelectionGeometry.Pick(new Unit[] { building, landed }, ray, hit, 1, _ => true) == landed, "ground aircraft/mobile remains ahead of helipad");
        typeof(Unit).GetField("<FootprintRegions>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(building, new BoundingBox[] {
            new(new Vector3(-1, 0, -1), new Vector3(0, 1, 1)),
            new(new Vector3(0, 0, -1), new Vector3(1, 1, 0)) });
        Check(UnitSelectionGeometry.ContainsGroundPoint(building, hit + new Vector3(-0.5f, 0, 0.5f), 1), "L shape arm hit");
        Check(!UnitSelectionGeometry.ContainsGroundPoint(building, hit + new Vector3(0.7f, 0, 0.7f), 1), "L shape hole not selected");
        building.SetRotationYDegrees(90);
        Vector3 rotatedArm = building.Position + Vector3.TransformNormal(new Vector3(-0.5f, 0, 0.5f), Matrix.CreateRotationY(MathHelper.PiOver2));
        Check(UnitSelectionGeometry.ContainsGroundPoint(building, rotatedArm, 1), "rotated authored footprint hit");
        var contour = UnitSelectionGeometry.CreateMarker(world, building, Color.White);
        Check(contour.Length > 0 && contour.Length % 2 == 0, "building contour creates line pairs");
        Check(contour.All(v => Math.Abs(v.Position.Y - world.Terrain.GetSurfaceHeight(v.Position.X, v.Position.Z) - 0.12f) < 0.001f), "contour follows terrain with depth offset");
        var groundRing = UnitSelectionGeometry.CreateMarker(world, ground, Color.White);
        Check(groundRing.Length == 80, "ground ring is closed forty segment loop");
        var airRing = UnitSelectionGeometry.CreateMarker(world, highAir, Color.White);
        Check(airRing.Length == 162 && airRing.Any(v => v.Position.Y > 9), "air ring includes flight marker and connector");
        Check(UnitSelectionGeometry.CreateMarker(world, landed, Color.White).Length == 80, "landed unit has one ring");
        Check(UnitSelectionGeometry.CreateMarker(world, ground, Color.Cyan, 1.25f)[0].Position.X > groundRing[0].Position.X, "squad leader highlight distinct larger ring");
        return checks;
    }
}

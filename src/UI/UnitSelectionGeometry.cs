using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

/// <summary>Local presentation geometry. Never changes occupancy or navigation footprints.</summary>
public static class UnitSelectionGeometry
{
    public static float ClickTolerance { get; set; } = 0.2f;
    public static bool IsAircraft(Unit unit) => unit is Helicopter || unit.Domain == TargetDomain.Air;

    public static bool ContainsGroundPoint(Unit unit, Vector3 point, int cellSize)
    {
        float yaw = MathF.Atan2(-unit.Transform.Forward.X, -unit.Transform.Forward.Z);
        Vector3 local = Vector3.Transform(point - unit.Position, Matrix.CreateRotationY(-yaw));
        float tolerance = Math.Max(0, ClickTolerance);
        if (unit.HasAuthoredFootprint)
            return unit.FootprintRegions.Any(region => local.X >= region.Min.X - tolerance &&
                local.X <= region.Max.X + tolerance && local.Z >= region.Min.Z - tolerance && local.Z <= region.Max.Z + tolerance);
        return MathF.Abs(local.X - unit.FootprintLocalCenter.X) <= unit.Width * cellSize * 0.5f + tolerance &&
            MathF.Abs(local.Z - unit.FootprintLocalCenter.Z) <= unit.Length * cellSize * 0.5f + tolerance;
    }

    public static Unit? Pick(IEnumerable<Unit> candidates, Ray ray, Vector3? terrainHit, int cellSize,
        Func<Unit, bool> visible)
    {
        Unit? aircraft = null, ground = null;
        float nearestAir = float.PositiveInfinity, nearestGround = float.PositiveInfinity;
        int groundPriority = int.MaxValue;
        float terrainDistance = terrainHit is Vector3 terrain ? Vector3.Distance(ray.Position, terrain) + 0.05f : float.PositiveInfinity;
        foreach (Unit unit in candidates)
        {
            if (!unit.IsSelectable || !visible(unit)) continue;
            if (IsAircraft(unit))
            {
                if (unit.IntersectSelectionRay(ray) is float distance && distance <= terrainDistance && distance < nearestAir)
                { aircraft = unit; nearestAir = distance; }
                // A landed aircraft is also selectable through its ground footprint.
                if (unit.Domain == TargetDomain.Air) continue;
            }
            if (terrainHit is not Vector3 hit || !ContainsGroundPoint(unit, hit, cellSize)) continue;
            int priority = unit is MobileUnit ? 0 : 1;
            float score = Vector2.DistanceSquared(new(hit.X, hit.Z), new(unit.Position.X, unit.Position.Z));
            if (priority < groundPriority || priority == groundPriority && score < nearestGround)
            { ground = unit; groundPriority = priority; nearestGround = score; }
        }
        return aircraft ?? ground;
    }

    public static VertexPositionColor[] CreateMarker(GameWorld world, Unit unit, Color color, float radiusScale = 1)
    {
        List<VertexPositionColor> vertices = [];
        if (unit is Building)
        {
            float yaw = MathHelper.ToDegrees(MathF.Atan2(-unit.Transform.Forward.X, -unit.Transform.Forward.Z));
            HashSet<Point> cells = world.GameGrid.GetFootprintCells(unit, unit.Position, yaw).ToHashSet();
            int size = world.GameGrid.CellSize;
            foreach (Point cell in cells)
            {
                float x = cell.X * size, z = cell.Y * size;
                if (!cells.Contains(new(cell.X, cell.Y - 1))) GroundEdge(x, z, x + size, z);
                if (!cells.Contains(new(cell.X + 1, cell.Y))) GroundEdge(x + size, z, x + size, z + size);
                if (!cells.Contains(new(cell.X, cell.Y + 1))) GroundEdge(x + size, z + size, x, z + size);
                if (!cells.Contains(new(cell.X - 1, cell.Y))) GroundEdge(x, z + size, x, z);
            }
        }
        else
        {
            float yaw = MathHelper.ToDegrees(MathF.Atan2(-unit.Transform.Forward.X, -unit.Transform.Forward.Z));
            Vector3 center = unit.GetFootprintCenter(unit.Position, yaw);
            float radius = Math.Max(0.45f, Math.Max(unit.Width, unit.Length) * world.GameGrid.CellSize * 0.55f) * radiusScale;
            float groundHeight = world.Terrain.GetSurfaceHeight(center.X, center.Z);
            bool elevated = IsAircraft(unit) && unit.Position.Y - groundHeight > 0.7f;
            for (int i = 0; i < 40; i++)
            {
                float a = MathHelper.TwoPi * i / 40, b = MathHelper.TwoPi * (i + 1) / 40;
                float x1 = center.X + MathF.Cos(a) * radius, z1 = center.Z + MathF.Sin(a) * radius;
                float x2 = center.X + MathF.Cos(b) * radius, z2 = center.Z + MathF.Sin(b) * radius;
                GroundEdge(x1, z1, x2, z2, elevated ? color * 0.45f : color);
                if (elevated) Line(new(x1, unit.Position.Y + 0.1f, z1), new(x2, unit.Position.Y + 0.1f, z2), color);
            }
            if (elevated) Line(GroundPoint(center.X, center.Z), new(center.X, unit.Position.Y + 0.1f, center.Z), color * 0.5f);
        }
        return vertices.ToArray();

        Vector3 GroundPoint(float x, float z)
        {
            x = Math.Clamp(x, 0, world.Terrain.Width - 1.001f);
            z = Math.Clamp(z, 0, world.Terrain.Height - 1.001f);
            return new(x, world.Terrain.GetSurfaceHeight(x, z) + 0.12f, z);
        }
        void Line(Vector3 start, Vector3 end, Color tint)
        { vertices.Add(new(start, tint)); vertices.Add(new(end, tint)); }
        void GroundEdge(float x1, float z1, float x2, float z2, Color? tint = null)
        {
            // Sample each world unit so outlines follow slopes and terrain ridges.
            int segments = Math.Max(1, (int)MathF.Ceiling(Math.Max(MathF.Abs(x2 - x1), MathF.Abs(z2 - z1))));
            for (int i = 0; i < segments; i++)
                Line(GroundPoint(MathHelper.Lerp(x1, x2, (float)i / segments), MathHelper.Lerp(z1, z2, (float)i / segments)),
                    GroundPoint(MathHelper.Lerp(x1, x2, (float)(i + 1) / segments), MathHelper.Lerp(z1, z2, (float)(i + 1) / segments)), tint ?? color);
        }
    }
}

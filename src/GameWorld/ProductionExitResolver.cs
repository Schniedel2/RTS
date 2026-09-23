using Microsoft.Xna.Framework;
using System;

namespace RTS;

/// <summary>Finds a free exterior placement for a unit produced inside a building.</summary>
public static class ProductionExitResolver
{
    private static readonly float[] AngleOffsets =
        [0, 22.5f, -22.5f, 45, -45, 67.5f, -67.5f, 90, -90, 112.5f, -112.5f, 135, -135, 180];

    public static bool TryResolve(
        GameWorld world,
        Building building,
        MobileUnit unit,
        Vector3 spawnPosition,
        Vector3 preferredExit,
        out Vector3 exitPosition)
    {
        GameGrid grid = world.GameGrid;
        Vector2 outward = new(preferredExit.X - building.Position.X, preferredExit.Z - building.Position.Z);
        if (outward.LengthSquared() < 0.001f)
            outward = new Vector2(building.Transform.Forward.X, building.Transform.Forward.Z);
        if (outward.LengthSquared() < 0.001f) outward = -Vector2.UnitY;
        outward.Normalize();
        Vector3 chosen = preferredExit;

        // First preserve an authored exit when it already fits. Further candidates
        // move away from the building, like the host-side MoveAway command.
        if (TryCandidate(preferredExit)) { exitPosition = chosen; return true; }
        int searchCells = Math.Max(10, Math.Max(building.Width, building.Length) + Math.Max(unit.Width, unit.Length) + 3);
        for (int distance = 1; distance <= searchCells; distance++)
            foreach (float offset in AngleOffsets)
            {
                Vector2 direction = Vector2.Transform(outward, Matrix.CreateRotationZ(MathHelper.ToRadians(offset)));
                Vector3 raw = preferredExit + new Vector3(direction.X, 0, direction.Y) * distance * grid.CellSize;
                Point cell = grid.ToCell(raw);
                if (!grid.Contains(cell)) continue;
                Vector3 candidate = grid.ToWorldPosition(cell, 0);
                candidate.Y = world.Terrain.GetSurfaceHeight(candidate.X, candidate.Z);
                if (TryCandidate(candidate)) { exitPosition = chosen; return true; }
            }

        exitPosition = preferredExit;
        return false;

        bool TryCandidate(Vector3 candidate)
        {
            Point cell = grid.ToCell(candidate);
            if (!grid.Contains(cell) || !unit.MovementProfile.CanEnter(world, unit, cell) ||
                !grid.IsPathfindingAllowed(unit, cell))
                return false;
            Vector2 travel = new(candidate.X - spawnPosition.X, candidate.Z - spawnPosition.Z);
            float rotation = travel.LengthSquared() > 0.0001f
                ? MathHelper.ToDegrees(MathF.Atan2(-travel.X, -travel.Y))
                : 0.0f;
            if (!grid.CanPlace(unit, candidate, rotation)) return false;
            chosen = candidate;
            return true;
        }
    }
}

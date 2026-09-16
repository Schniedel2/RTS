using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS;

public class GameGrid
{
    private readonly Unit?[,] _occupants;

    public int Width { get; }
    public int Height { get; }
    public int CellSize { get; }
    // Store the exact rectangle that was occupied. A mobile unit may rotate
    // before it leaves a cell, so recalculating this from its current angle
    // when clearing would otherwise leave stale occupied cells behind.
    private readonly Dictionary<Unit, Rectangle> _occupiedFootprints = [];
    // Buildings may have arbitrary yaw, so a rectangle is not sufficient for
    // their occupancy. Keep the exact set that was placed, so Remove can
    // reliably release precisely those cells later.
    private readonly Dictionary<Unit, IReadOnlyList<Point>> _occupiedCells = [];

    public GameGrid(int width, int height, int cellSize)
    {
        Width = width;
        Height = height;
        CellSize = cellSize;
        _occupants = new Unit[width, height];
    }

    private void Clear(Unit unit)
    {
        if (_occupiedFootprints.TryGetValue(unit, out Rectangle footprint))
        {
            for (int y = footprint.Top; y < footprint.Bottom; y++)
            {
                for (int x = footprint.Left; x < footprint.Right; x++)
                {
                    if (_occupants[x, y] == unit)
                        _occupants[x, y] = null;
                }
            }

            _occupiedFootprints.Remove(unit);
        }

        if (_occupiedCells.TryGetValue(unit, out IReadOnlyList<Point>? cells))
        {
            foreach (Point cell in cells)
            {
                if (_occupants[cell.X, cell.Y] == unit)
                    _occupants[cell.X, cell.Y] = null;
            }

            _occupiedCells.Remove(unit);
        }
    }

    public bool CanPlace(Unit unit, Point centerCell)
    {
        Rectangle footprint = GetFootprint(unit, centerCell);
        int left = footprint.Left;
        int top = footprint.Top;
        int right = footprint.Right - 1;
        int bottom = footprint.Bottom - 1;

        if (left < 0 || top < 0 || right >= Width || bottom >= Height)
            return false;

        HashSet<Unit>? testedBuildings = unit is MobileUnit ? [] : null;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                Unit? occupant = _occupants[x, y];

                if (occupant is null || occupant == unit)
                    continue;

                // A rotated building deliberately occupies every touched grid
                // cell. That is conservative for A*, but it must not turn a
                // geometrically clear, edge-hugging route into a collision.
                // Test the real rectangles once per building before rejecting
                // a mobile unit's candidate cell.
                if (unit is MobileUnit mobileUnit && occupant is Building building)
                {
                    if (testedBuildings!.Add(building) &&
                        MobileFootprintIntersectsBuilding(mobileUnit, footprint, building))
                    {
                        return false;
                    }
                    continue;
                }

                return false;
            }
        }

        return true;
    }

    public bool TryMove(MobileUnit unit, Point centerCell)
    {
        Rectangle newFootprint = GetFootprint(unit, centerCell);
        if (_occupiedFootprints.TryGetValue(unit, out Rectangle oldFootprint) &&
            oldFootprint == newFootprint)
        {
            return true;
        }

        if (!CanPlace(unit, centerCell))
            return false;

        Clear(unit);
        Occupy(unit, centerCell);

        return true;
    }

    /// <summary>
    /// Checks all cells touched by a prospective footprint. Mobile units snap
    /// their grid shape to 90° increments; buildings retain the supplied yaw.
    /// </summary>
    public bool CanPlace(Unit unit, Vector3 position, float rotationDegrees)
    {
        foreach (Point cell in GetFootprintCells(unit, position, rotationDegrees))
        {
            if (cell.X < 0 || cell.Y < 0 || cell.X >= Width || cell.Y >= Height)
                return false;
            Unit? occupant = _occupants[cell.X, cell.Y];
            if (occupant is not null && occupant != unit)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Registers an arbitrary (typically building) footprint in the grid.
    /// Unlike mobile units, buildings retain their free rotation.
    /// </summary>
    public bool TryPlace(Unit unit, Vector3 position, float rotationDegrees)
    {
        IReadOnlyList<Point> cells = GetFootprintCells(unit, position, rotationDegrees);
        if (!CanPlace(unit, position, rotationDegrees))
            return false;

        Clear(unit);
        foreach (Point cell in cells)
            _occupants[cell.X, cell.Y] = unit;
        _occupiedCells[unit] = cells;
        return true;
    }

    /// <summary>Returns every grid cell intersected by the unit's rotated local rectangle.</summary>
    public IReadOnlyList<Point> GetFootprintCells(Unit unit, Vector3 position, float rotationDegrees)
    {
        float yawDegrees = unit is MobileUnit
            ? MathF.Round(rotationDegrees / 90.0f) * 90.0f
            : rotationDegrees;
        Matrix yawTransform = Matrix.CreateRotationY(MathHelper.ToRadians(yawDegrees));
        // Use XNA's own rotation transform instead of duplicating its yaw
        // convention. In particular, this keeps the grid in sync with
        // WorldObject.SetRotationYDegrees and rendered BBModel meshes.
        Vector3 right3 = Vector3.TransformNormal(Vector3.Right, yawTransform);
        Vector3 forward3 = Vector3.TransformNormal(Vector3.Forward, yawTransform);
        Vector2 right = new(right3.X, right3.Z);
        Vector2 forward = new(forward3.X, forward3.Z);
        Vector3 footprintCenter = unit.GetFootprintCenter(position, yawDegrees);
        Vector2 center = new(footprintCenter.X / CellSize, footprintCenter.Z / CellSize);
        // Width and Length are specified in GameGrid cells (not world
        // pixels/units), while center above is already converted to cells.
        float halfWidth = unit.Width * 0.5f;
        float halfLength = unit.Length * 0.5f;

        float extentX = MathF.Abs(right.X) * halfWidth + MathF.Abs(forward.X) * halfLength;
        float extentY = MathF.Abs(right.Y) * halfWidth + MathF.Abs(forward.Y) * halfLength;
        int minimumX = (int)MathF.Floor(center.X - extentX) - 1;
        int maximumX = (int)MathF.Floor(center.X + extentX) + 1;
        int minimumY = (int)MathF.Floor(center.Y - extentY) - 1;
        int maximumY = (int)MathF.Floor(center.Y + extentY) + 1;

        List<Point> cells = [];
        for (int y = minimumY; y <= maximumY; y++)
        {
            for (int x = minimumX; x <= maximumX; x++)
            {
                if (IntersectsCell(center, right, forward, halfWidth, halfLength, x, y))
                    cells.Add(new Point(x, y));
            }
        }
        return cells;
    }

    /// <summary>
    /// Updates a registered mobile unit's discrete 0°/90° footprint after a
    /// body turn. Returns false when the newly required cells are blocked.
    /// </summary>
    public bool TryUpdateFootprint(MobileUnit unit)
    {
        if (!_occupiedFootprints.ContainsKey(unit))
            return true;

        Point centerCell = ToCell(unit.Position);
        Rectangle newFootprint = GetFootprint(unit, centerCell);
        if (_occupiedFootprints.TryGetValue(unit, out Rectangle oldFootprint) && oldFootprint == newFootprint)
            return true;

        // Only crossing the 45° orientation threshold changes a vehicle's
        // discrete grid footprint. Until then a smooth visual turn needs no
        // collision query at all.
        if (!CanPlace(unit, centerCell))
            return false;

        Clear(unit);
        Occupy(unit, centerCell);
        return true;
    }

    public bool IsOccupied(int x, int y)
    {
        return _occupants[x, y] != null;
    }

    public void Remove(Unit unit)
    {
        Clear(unit);
    }

    public Point ToCell(Vector3 position)
    {
        return new Point(
            (int)(position.X / CellSize),
            (int)(position.Z / CellSize));
    }

    public Vector3 ToWorldPosition(Point centerCell, float height)
    {
        return new Vector3(
            (centerCell.X + 0.5f) * CellSize,
            height,
            (centerCell.Y + 0.5f) * CellSize);
    }

    private void Occupy(Unit unit, Point centerCell)
    {
        Rectangle footprint = GetFootprint(unit, centerCell);

        for (int y = footprint.Top; y < footprint.Bottom; y++)
        {
            for (int x = footprint.Left; x < footprint.Right; x++)
                _occupants[x, y] = unit;
        }

        _occupiedFootprints[unit] = footprint;
    }

    private static Rectangle GetFootprint(Unit unit, Point centerCell)
    {
        int footprintWidth = unit.Width;
        int footprintHeight = unit.Length;
        if (unit is MobileUnit)
        {
            Vector3 forward = unit.Transform.Forward;
            forward.Y = 0.0f;
            // The grid has two orientations. At 45° the closest axis takes
            // over, so visual movement stays smooth while occupancy is stable.
            if (MathF.Abs(forward.X) > MathF.Abs(forward.Z))
            {
                footprintWidth = unit.Length;
                footprintHeight = unit.Width;
            }
        }

        int left = centerCell.X - (footprintWidth - 1) / 2;
        int top = centerCell.Y - (footprintHeight - 1) / 2;
        return new Rectangle(left, top, footprintWidth, footprintHeight);
    }

    /// <summary>
    /// Precise X/Z collision check used when a vehicle crosses a conservatively
    /// occupied cell of a freely rotated building. Touching edges are allowed;
    /// only a positive-area overlap blocks movement.
    /// </summary>
    private bool MobileFootprintIntersectsBuilding(
        MobileUnit mobileUnit,
        Rectangle mobileFootprint,
        Building building)
    {
        Vector2 mobileCenter = new(
            (mobileFootprint.Left + mobileFootprint.Width * 0.5f) * CellSize,
            (mobileFootprint.Top + mobileFootprint.Height * 0.5f) * CellSize);
        float mobileHalfWidth = mobileFootprint.Width * CellSize * 0.5f;
        float mobileHalfLength = mobileFootprint.Height * CellSize * 0.5f;

        float buildingYawDegrees = GetYawDegrees(building.Transform);
        Matrix buildingYaw = Matrix.CreateRotationY(MathHelper.ToRadians(buildingYawDegrees));
        Vector3 right3 = Vector3.TransformNormal(Vector3.Right, buildingYaw);
        Vector3 forward3 = Vector3.TransformNormal(Vector3.Forward, buildingYaw);
        Vector2 buildingRight = new(right3.X, right3.Z);
        Vector2 buildingForward = new(forward3.X, forward3.Z);
        Vector3 buildingCenter3 = building.GetFootprintCenter(building.Position, buildingYawDegrees);
        Vector2 buildingCenter = new(buildingCenter3.X, buildingCenter3.Z);
        float buildingHalfWidth = building.Width * CellSize * 0.5f;
        float buildingHalfLength = building.Length * CellSize * 0.5f;
        Vector2 delta = buildingCenter - mobileCenter;

        // Test the building's two local axes against the vehicle AABB.
        if (MathF.Abs(Vector2.Dot(delta, buildingRight)) >=
            buildingHalfWidth + mobileHalfWidth * MathF.Abs(buildingRight.X) + mobileHalfLength * MathF.Abs(buildingRight.Y))
        {
            return false;
        }
        if (MathF.Abs(Vector2.Dot(delta, buildingForward)) >=
            buildingHalfLength + mobileHalfWidth * MathF.Abs(buildingForward.X) + mobileHalfLength * MathF.Abs(buildingForward.Y))
        {
            return false;
        }

        // Test the vehicle's world X/Z axes against the building OBB.
        if (MathF.Abs(delta.X) >= mobileHalfWidth +
            buildingHalfWidth * MathF.Abs(buildingRight.X) + buildingHalfLength * MathF.Abs(buildingForward.X))
        {
            return false;
        }
        return MathF.Abs(delta.Y) < mobileHalfLength +
            buildingHalfWidth * MathF.Abs(buildingRight.Y) + buildingHalfLength * MathF.Abs(buildingForward.Y);
    }

    private static float GetYawDegrees(Matrix transform)
    {
        Vector3 forward = transform.Forward;
        return MathHelper.ToDegrees(MathF.Atan2(-forward.X, -forward.Z));
    }

    private static bool IntersectsCell(
        Vector2 rectangleCenter,
        Vector2 right,
        Vector2 forward,
        float halfWidth,
        float halfLength,
        int cellX,
        int cellY)
    {
        Vector2 delta = new(cellX + 0.5f - rectangleCenter.X, cellY + 0.5f - rectangleCenter.Y);
        // Separating-axis test: the rectangle's local axes and the cell's
        // world X/Y axes are sufficient for two rectangles in the plane.
        if (MathF.Abs(Vector2.Dot(delta, right)) > halfWidth + 0.5f * (MathF.Abs(right.X) + MathF.Abs(right.Y)))
            return false;
        if (MathF.Abs(Vector2.Dot(delta, forward)) > halfLength + 0.5f * (MathF.Abs(forward.X) + MathF.Abs(forward.Y)))
            return false;
        if (MathF.Abs(delta.X) > 0.5f + halfWidth * MathF.Abs(right.X) + halfLength * MathF.Abs(forward.X))
            return false;
        return MathF.Abs(delta.Y) <= 0.5f + halfWidth * MathF.Abs(right.Y) + halfLength * MathF.Abs(forward.Y);
    }
}

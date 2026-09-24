using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class GameGrid
{
    private readonly Unit?[,] _occupants;
    private readonly GridCell[,] _cells;
    private Terrain? _terrain;

    public bool Contains(Point cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;

    public Unit? GetOccupant(Point cell) => Contains(cell) ? _occupants[cell.X, cell.Y] : null;

    public Unit? GetOccupant(int x, int y) => GetOccupant(new Point(x, y));
    public GridCell GetCell(int x, int y) => GetCell(new Point(x, y));

    public GridCell GetCell(Point cell)
    {
        if (!Contains(cell))
            throw new ArgumentOutOfRangeException(nameof(cell));
        GridCell data = _cells[cell.X, cell.Y];
        if (_terrain is not null && data.TerrainRevision != _terrain.HeightRevision)
        {
            int left = cell.X * CellSize, top = cell.Y * CellSize;
            data.HasTerrain = left + CellSize < _terrain.Width && top + CellSize < _terrain.Height;
            float slope = 0.0f;
            if (data.HasTerrain)
                for (int z = top; z < top + CellSize; z++)
                    for (int x = left; x < left + CellSize; x++)
                        slope = Math.Max(slope, _terrain.GetMaxSlopeDegrees(x, z));
            data.MaxSlopeDegrees = slope;
            data.TerrainRevision = _terrain.HeightRevision;
        }
        return data;
    }

    public void BindTerrain(Terrain terrain)
    {
        _terrain = terrain;
        foreach (GridCell cell in _cells)
            cell.TerrainRevision = -1;
    }

    public bool IsPathfindingAllowed(Unit unit, Point centerCell, Point? startingCell = null)
    {
        Rectangle footprint = GetFootprint(unit, centerCell);
        Rectangle? startingFootprint = startingCell is Point start ? GetFootprint(unit, start) : null;
        for (int y = footprint.Top; y < footprint.Bottom; y++)
            for (int x = footprint.Left; x < footprint.Right; x++)
            {
                Point cell = new(x, y);
                if (!Contains(cell) || (GetCell(cell).ExcludeFromPathfinding &&
                    !(startingFootprint?.Contains(cell) ?? false)))
                    return false;
            }
        return true;
    }

    public float GetMovementCost(Unit unit, Point centerCell)
    {
        float cost = 1.0f;
        Rectangle footprint = GetFootprint(unit, centerCell);
        for (int y = footprint.Top; y < footprint.Bottom; y++)
            for (int x = footprint.Left; x < footprint.Right; x++)
                cost = Math.Max(cost, GetCell(new Point(x, y)).MovementCost);
        return cost;
    }

    private bool AllowsUnit(Unit unit, Point cell)
    {
        GridCell data = GetCell(cell);
        return data.HasTerrain && !data.IsBlocked &&
            (unit is not MobileUnit mobile || mobile.MovementProfile.CanUseTerrain(data));
    }

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
    private readonly Dictionary<Point, HashSet<Unit>> _clearanceOwners = [];
    private readonly Dictionary<Unit, IReadOnlyList<Point>> _clearanceCells = [];

    public GameGrid(int width, int height, int cellSize)
    {
        Width = width;
        Height = height;
        CellSize = cellSize;
        if (width <= 0 || height <= 0 || cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        _occupants = new Unit[width, height];
        _cells = new GridCell[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                _cells[x, y] = new GridCell();
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

        if (_clearanceCells.Remove(unit, out IReadOnlyList<Point>? clearance))
            foreach (Point cell in clearance)
                if (_clearanceOwners.TryGetValue(cell, out HashSet<Unit>? owners))
                {
                    owners.Remove(unit);
                    if (owners.Count == 0) _clearanceOwners.Remove(cell);
                }
    }

    /// <summary>True when another building requires this cell to remain free of building footprints.</summary>
    public bool IsReservedForBuilding(Point cell, Unit? prospectiveOwner = null) =>
        _clearanceOwners.TryGetValue(cell, out HashSet<Unit>? owners) &&
        owners.Any(owner => owner != prospectiveOwner);

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

                // Terrain can change underneath a registered unit (most
                // notably while a bulldozer levels its own footprint). Let it
                // retain/leave those already occupied cells even if their new
                // slope is temporarily outside its movement profile. Newly
                // entered cells still have to pass the full terrain check.
                if (!AllowsUnit(unit, new Point(x, y)) && occupant != unit)
                    return false;

                if (occupant is null || occupant == unit)
                    continue;

                // A rotated building deliberately occupies every touched grid
                // cell. That is conservative for A*, but it must not turn a
                // geometrically clear, edge-hugging route into a collision.
                // Test the real rectangles once per building before rejecting
                // a mobile unit's candidate cell.
                if (unit is MobileUnit mobileUnit && occupant is Building building)
                {
                    if (building.HasAuthoredFootprint)
                        return false;
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

    // Called only for host-validated earthwork results (or their authoritative client replay).
    internal void RegisterEarthworkFootprint(MobileUnit unit, IReadOnlyList<Point> cells)
    {
        Clear(unit);
        foreach (Point cell in cells)
            if (Contains(cell)) _occupants[cell.X, cell.Y] = unit;
        _occupiedCells[unit] = cells;
    }

    public bool TryMove(MobileUnit unit, Point centerCell)
    {
        Point start = ToCell(unit.Position);
        // New registrations (spawn/disembark) only validate their destination.
        int steps = _occupiedFootprints.ContainsKey(unit)
            ? Math.Max(Math.Abs(centerCell.X - start.X), Math.Abs(centerCell.Y - start.Y))
            : 0;
        Point previous = start;
        for (int step = 1; step <= steps; step++)
        {
            Point next = new(
                start.X + (int)MathF.Round((centerCell.X - start.X) * (float)step / steps),
                start.Y + (int)MathF.Round((centerCell.Y - start.Y) * (float)step / steps));
            if (!CanPlace(unit, next) ||
                (next.X != previous.X && next.Y != previous.Y &&
                 (!CanPlace(unit, new Point(next.X, previous.Y)) || !CanPlace(unit, new Point(previous.X, next.Y)))))
                return false;
            previous = next;
        }
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
            if (!AllowsUnit(unit, cell))
                return false;
            Unit? occupant = _occupants[cell.X, cell.Y];
            if (occupant is not null && occupant != unit)
                return false;
            if (unit is Building && IsReservedForBuilding(cell, unit))
                return false;
        }
        if (unit is Building)
        {
            foreach (Point cell in GetClearanceCells(unit, position, rotationDegrees))
            {
                if (!Contains(cell) || GetCell(cell).IsBlocked)
                    return false;
                Unit? occupant = _occupants[cell.X, cell.Y];
                if (occupant is not null && occupant != unit)
                    return false;
            }
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
        if (unit is Building)
        {
            IReadOnlyList<Point> clearance = GetClearanceCells(unit, position, rotationDegrees);
            foreach (Point cell in clearance)
            {
                if (!_clearanceOwners.TryGetValue(cell, out HashSet<Unit>? owners))
                    _clearanceOwners[cell] = owners = [];
                owners.Add(unit);
            }
            _clearanceCells[unit] = clearance;
        }
        return true;
    }

    /// <summary>Returns every grid cell intersected by the unit's rotated local rectangle.</summary>
    public IReadOnlyList<Point> GetFootprintCells(Unit unit, Vector3 position, float rotationDegrees)
    {
        if (unit.HasAuthoredFootprint)
            return GetAuthoredFootprintCells(unit, position, rotationDegrees);

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

    /// <summary>Checks whether two footprints touch along an edge or at a corner.</summary>
    public bool AreFootprintsAdjacent(Unit first, Unit second) =>
        AreCellSetsAdjacent(GetCurrentGridFootprintCells(first), GetCurrentGridFootprintCells(second));

    public bool AreFootprintsAdjacent(
        Unit first, Vector3 firstPosition, float firstRotationDegrees,
        Unit second, Vector3 secondPosition, float secondRotationDegrees)
    {
        IReadOnlyList<Point> firstCells = GetGridFootprintCells(
            first, firstPosition, firstRotationDegrees);
        IReadOnlyList<Point> secondCells = GetGridFootprintCells(
            second, secondPosition, secondRotationDegrees);
        return AreCellSetsAdjacent(firstCells, secondCells);
    }

    private IReadOnlyList<Point> GetCurrentGridFootprintCells(Unit unit)
    {
        if (_occupiedCells.TryGetValue(unit, out IReadOnlyList<Point>? cells))
            return cells;
        if (_occupiedFootprints.TryGetValue(unit, out Rectangle footprint))
            return EnumerateCells(footprint);
        return GetGridFootprintCells(unit, unit.Position, GetYawDegrees(unit.Transform));
    }

    private IReadOnlyList<Point> GetGridFootprintCells(
        Unit unit, Vector3 position, float rotationDegrees)
    {
        if (unit is not MobileUnit)
            return GetFootprintCells(unit, position, rotationDegrees);

        Point centerCell = ToCell(position);
        int footprintWidth = unit.Width;
        int footprintHeight = unit.Length;
        float radians = MathHelper.ToRadians(rotationDegrees);
        if (MathF.Abs(MathF.Sin(radians)) > MathF.Abs(MathF.Cos(radians)))
            (footprintWidth, footprintHeight) = (footprintHeight, footprintWidth);

        int left = centerCell.X - (footprintWidth - 1) / 2;
        int top = centerCell.Y - (footprintHeight - 1) / 2;
        return EnumerateCells(new Rectangle(left, top, footprintWidth, footprintHeight));
    }

    private static IReadOnlyList<Point> EnumerateCells(Rectangle footprint)
    {
        List<Point> cells = new(footprint.Width * footprint.Height);
        for (int y = footprint.Top; y < footprint.Bottom; y++)
            for (int x = footprint.Left; x < footprint.Right; x++)
                cells.Add(new Point(x, y));
        return cells;
    }

    private static bool AreCellSetsAdjacent(
        IReadOnlyList<Point> firstCells,
        IReadOnlyList<Point> secondFootprint)
    {
        HashSet<Point> secondCells = secondFootprint.ToHashSet();

        // Freely rotated buildings conservatively register every touched cell.
        // A mobile footprint may therefore share such a cell while its real
        // rectangle merely touches the building edge. CanPlace has already
        // ruled out a positive-area collision for every approach candidate.
        if (firstCells.Any(secondCells.Contains))
            return true;

        foreach (Point cell in firstCells)
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if ((x != 0 || y != 0) && secondCells.Contains(cell + new Point(x, y)))
                        return true;

        return false;
    }

    /// <summary>Returns cells which must remain free of building footprints but remain traversable.</summary>
    public IReadOnlyList<Point> GetClearanceCells(Unit unit, Vector3 position, float rotationDegrees) =>
        unit.HasAuthoredClearance
            ? GetAuthoredRegionCells(unit.ClearanceRegions, unit is MobileUnit, position, rotationDegrees)
            : Array.Empty<Point>();

    private IReadOnlyList<Point> GetAuthoredFootprintCells(Unit unit, Vector3 position, float rotationDegrees)
    {
        return GetAuthoredRegionCells(unit.FootprintRegions, unit is MobileUnit, position, rotationDegrees);
    }

    private IReadOnlyList<Point> GetAuthoredRegionCells(
        IReadOnlyList<BoundingBox> regions,
        bool snapRotation,
        Vector3 position,
        float rotationDegrees)
    {
        float yawDegrees = snapRotation
            ? MathF.Round(rotationDegrees / 90.0f) * 90.0f
            : rotationDegrees;
        Matrix rotation = Matrix.CreateRotationY(MathHelper.ToRadians(yawDegrees));
        Vector3 right3 = Vector3.TransformNormal(Vector3.Right, rotation);
        Vector3 forward3 = Vector3.TransformNormal(Vector3.Forward, rotation);
        Vector2 right = new(right3.X, right3.Z);
        Vector2 forward = new(forward3.X, forward3.Z);
        HashSet<Point> cells = [];
        foreach (BoundingBox region in regions)
        {
            Vector3 localCenter = (region.Min + region.Max) * 0.5f;
            Vector3 worldCenter = position + Vector3.TransformNormal(localCenter, rotation);
            Vector2 center = new(worldCenter.X / CellSize, worldCenter.Z / CellSize);
            float halfWidth = (region.Max.X - region.Min.X) * 0.5f / CellSize;
            float halfLength = (region.Max.Z - region.Min.Z) * 0.5f / CellSize;
            float extentX = MathF.Abs(right.X) * halfWidth + MathF.Abs(forward.X) * halfLength;
            float extentY = MathF.Abs(right.Y) * halfWidth + MathF.Abs(forward.Y) * halfLength;
            for (int y = (int)MathF.Floor(center.Y - extentY) - 1; y <= (int)MathF.Floor(center.Y + extentY) + 1; y++)
                for (int x = (int)MathF.Floor(center.X - extentX) - 1; x <= (int)MathF.Floor(center.X + extentX) + 1; x++)
                    if (IntersectsCell(center, right, forward, halfWidth, halfLength, x, y))
                        cells.Add(new Point(x, y));
        }
        return cells.ToArray();
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
        Point start = ToCell(unit.Position);
        // New registrations (spawn/disembark) only validate their destination.
        int steps = _occupiedFootprints.ContainsKey(unit)
            ? Math.Max(Math.Abs(centerCell.X - start.X), Math.Abs(centerCell.Y - start.Y))
            : 0;
        Point previous = start;
        for (int step = 1; step <= steps; step++)
        {
            Point next = new(
                start.X + (int)MathF.Round((centerCell.X - start.X) * (float)step / steps),
                start.Y + (int)MathF.Round((centerCell.Y - start.Y) * (float)step / steps));
            if (!CanPlace(unit, next) ||
                (next.X != previous.X && next.Y != previous.Y &&
                 (!CanPlace(unit, new Point(next.X, previous.Y)) || !CanPlace(unit, new Point(previous.X, next.Y)))))
                return false;
            previous = next;
        }
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

    /// <summary>Applies a host position and keeps the client's registered footprint with it.</summary>
    public bool TryApplyAuthoritativeTransform(MobileUnit unit, Matrix authoritativeTransform)
    {
        Matrix previousTransform = unit.Transform;
        Point previousCell = ToCell(unit.Position);
        bool wasRegistered = _occupiedFootprints.ContainsKey(unit) || _occupiedCells.ContainsKey(unit);
        Clear(unit);
        unit.SetTransform(authoritativeTransform);
        if (!wasRegistered)
            return true;

        Point authoritativeCell = ToCell(unit.Position);
        if (CanPlace(unit, authoritativeCell))
        {
            Occupy(unit, authoritativeCell);
            return true;
        }

        unit.SetTransform(previousTransform);
        if (wasRegistered && CanPlace(unit, previousCell))
            Occupy(unit, previousCell);
        return false;
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
            (int)MathF.Floor(position.X / CellSize),
            (int)MathF.Floor(position.Z / CellSize));
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
        const float epsilon = 0.00001f;
        Vector2 delta = new(cellX + 0.5f - rectangleCenter.X, cellY + 0.5f - rectangleCenter.Y);
        // Separating-axis test: the rectangle's local axes and the cell's
        // world X/Y axes are sufficient for two rectangles in the plane.
        // Merely touching a cell edge has zero area and must not reserve that
        // neighbouring cell; otherwise an axis-aligned 1x1 footprint becomes
        // an artificial 3x3 footprint.
        if (MathF.Abs(Vector2.Dot(delta, right)) >=
            halfWidth + 0.5f * (MathF.Abs(right.X) + MathF.Abs(right.Y)) - epsilon)
            return false;
        if (MathF.Abs(Vector2.Dot(delta, forward)) >=
            halfLength + 0.5f * (MathF.Abs(forward.X) + MathF.Abs(forward.Y)) - epsilon)
            return false;
        if (MathF.Abs(delta.X) >=
            0.5f + halfWidth * MathF.Abs(right.X) + halfLength * MathF.Abs(forward.X) - epsilon)
            return false;
        return MathF.Abs(delta.Y) <
            0.5f + halfWidth * MathF.Abs(right.Y) + halfLength * MathF.Abs(forward.Y) - epsilon;
    }
}

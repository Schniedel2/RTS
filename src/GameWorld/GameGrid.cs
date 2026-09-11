using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace RTS;

public class GameGrid
{
    private readonly Unit?[,] _occupants;

    public int Width { get; }
    public int Height { get; }
    public int CellSize { get; }
    private readonly Dictionary<Unit, Point> _positions = [];

    public GameGrid(int width, int height, int cellSize)
    {
        Width = width;
        Height = height;
        CellSize = cellSize;
        _occupants = new Unit[width, height];
    }

    private void Clear(Unit unit)
    {
        if (!_positions.TryGetValue(unit, out Point centerCell))
            return;

        GetFootprintBounds(
            unit,
            centerCell,
            out int left,
            out int top,
            out int right,
            out int bottom);

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (_occupants[x, y] == unit)
                    _occupants[x, y] = null;
            }
        }

        _positions.Remove(unit);
    }

    public bool CanPlace(Unit unit, Point centerCell)
    {
        GetFootprintBounds(unit, centerCell, out int left, out int top, out int right, out int bottom);

        if (left < 0 || top < 0 || right >= Width || bottom >= Height)
            return false;

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                Unit? occupant = _occupants[x, y];

                if (occupant != null && occupant != unit)
                    return false;
            }
        }

        return true;
    }

    public bool TryMove(MobileUnit unit, Point centerCell)
    {
        if (_positions.TryGetValue(unit, out Point oldCell) &&
            oldCell == centerCell)
        {
            return true;
        }

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
        GetFootprintBounds(
            unit,
            centerCell,
            out int left,
            out int top,
            out int right,
            out int bottom);

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
                _occupants[x, y] = unit;
        }

        _positions[unit] = centerCell;
    }

    private static void GetFootprintBounds(
        Unit unit,
        Point centerCell,
        out int left,
        out int top,
        out int right,
        out int bottom)
    {
        left = centerCell.X - (unit.Width - 1) / 2;
        top = centerCell.Y - (unit.Length - 1) / 2;
        right = left + unit.Width - 1;
        bottom = top + unit.Length - 1;
    }
}

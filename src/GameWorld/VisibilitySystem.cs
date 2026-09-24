using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public enum VisibilityState : byte
{
    Unexplored,
    Explored,
    Visible
}

[Flags]
public enum CellVisibility : byte
{
    None = 0,
    Explored = 1 << 0,
    Visible = 1 << 1,
    ExploredByAlly = 1 << 2,
    VisibleByAlly = 1 << 3
}

public enum IntelligenceContactSource
{
    OwnVision,
    AlliedVision,
    Radar,
    EnemyNetwork,
    LastKnown,
    Deception
}

public sealed record IntelligenceContact(
    Guid? UnitId,
    Point Cell,
    IntelligenceContactSource Source,
    string Classification,
    double ObservedAt,
    float Confidence);

public sealed class VisibilityGrid
{
    private readonly VisibilityState[] _cells;
    private readonly HashSet<int> _visibleCells = [];
    public int Width { get; }
    public int Height { get; }

    public VisibilityGrid(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        _cells = new VisibilityState[width * height];
    }

    public VisibilityState this[Point cell] => Contains(cell) ? _cells[cell.Y * Width + cell.X] : VisibilityState.Unexplored;

    public void BeginUpdate()
    {
        foreach (int index in _visibleCells)
            _cells[index] = VisibilityState.Explored;
        _visibleCells.Clear();
    }

    public void Reveal(Point center, int radius)
    {
        int radiusSquared = radius * radius;
        for (int y = Math.Max(0, center.Y - radius); y <= Math.Min(Height - 1, center.Y + radius); y++)
            for (int x = Math.Max(0, center.X - radius); x <= Math.Min(Width - 1, center.X + radius); x++)
                if ((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y) <= radiusSquared)
                {
                    int index = y * Width + x;
                    _cells[index] = VisibilityState.Visible;
                    _visibleCells.Add(index);
                }
    }

    public void Reset()
    {
        Array.Clear(_cells);
        _visibleCells.Clear();
    }

    private bool Contains(Point cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;
}

/// <summary>Local deterministic fog-of-war state. The full replicated world remains untouched.</summary>
public sealed class VisibilitySystem
{
    private readonly GameWorld _world;
    private readonly Dictionary<Guid, VisibilityGrid> _grids = [];
    private readonly Dictionary<Guid, IReadOnlyList<Guid>> _alliedArmyIds = [];

    public VisibilitySystem(GameWorld world) => _world = world;

    public VisibilityGrid GetGrid(Guid armyId)
    {
        if (!_grids.TryGetValue(armyId, out VisibilityGrid? grid))
            _grids.Add(armyId, grid = new VisibilityGrid(_world.GameGrid.Width, _world.GameGrid.Height));
        return grid;
    }

    public void Reset()
    {
        _grids.Clear();
        _alliedArmyIds.Clear();
    }

    public void Update()
    {
        RefreshAlliances();
        foreach (VisibilityGrid grid in _grids.Values) grid.BeginUpdate();
        foreach (Unit unit in _world.Units.Units)
        {
            if (unit.ArmyId is not Guid armyId || unit.IsEmbarked || unit.IsDying || unit.GetSightRange() <= 0) continue;
            GetGrid(armyId).Reveal(_world.GameGrid.ToCell(unit.Position), unit.GetSightRange());
        }
    }

    public CellVisibility GetVisibility(Guid viewerArmyId, Point cell)
        => GetVisibility(viewerArmyId, cell, GetAlliedArmyIds(viewerArmyId));

    private CellVisibility GetVisibility(Guid viewerArmyId, Point cell, IReadOnlyList<Guid> alliedArmyIds)
    {
        CellVisibility result = ToOwnFlags(GetGrid(viewerArmyId)[cell]);
        foreach (Guid allyArmyId in alliedArmyIds)
        {
            VisibilityState state = GetGrid(allyArmyId)[cell];
            if (state >= VisibilityState.Explored) result |= CellVisibility.ExploredByAlly;
            if (state == VisibilityState.Visible) result |= CellVisibility.VisibleByAlly;
        }
        return result;
    }

    public bool IsUnitVisible(Guid viewerArmyId, Unit unit, bool forMinimap = false)
    {
        if (unit.ArmyId is not Guid targetArmyId || targetArmyId == viewerArmyId || AreAllies(viewerArmyId, targetArmyId))
            return true;

        CellVisibility visibility = GetVisibility(viewerArmyId, _world.GameGrid.ToCell(unit.Position));
        if (visibility.HasFlag(CellVisibility.Visible)) return true;
        Army? viewer = Globals.Game.Armies.Find(viewerArmyId);
        return forMinimap
            ? viewer?.Intelligence.ShareVisibleMinimap == true && visibility.HasFlag(CellVisibility.VisibleByAlly)
            : viewer?.Intelligence.ShareWorldVision == true && visibility.HasFlag(CellVisibility.VisibleByAlly);
    }

    public VisibilityState GetDisplayedTerrainVisibility(Guid viewerArmyId, Point cell, bool forMinimap)
        => GetDisplayedTerrainVisibility(viewerArmyId, cell, forMinimap, GetAlliedArmyIds(viewerArmyId));

    public VisibilityState GetDisplayedTerrainVisibility(
        Guid viewerArmyId,
        Point cell,
        bool forMinimap,
        IReadOnlyList<Guid> alliedArmyIds)
    {
        CellVisibility visibility = GetVisibility(viewerArmyId, cell, alliedArmyIds);
        if (visibility.HasFlag(CellVisibility.Visible)) return VisibilityState.Visible;
        if (visibility.HasFlag(CellVisibility.Explored)) return VisibilityState.Explored;

        Army? viewer = Globals.Game.Armies.Find(viewerArmyId);
        if (forMinimap && viewer?.Intelligence.ShareVisibleMinimap == true && visibility.HasFlag(CellVisibility.VisibleByAlly))
            return VisibilityState.Visible;
        if (forMinimap && viewer?.Intelligence.ShareExploredMinimap == true && visibility.HasFlag(CellVisibility.ExploredByAlly))
            return VisibilityState.Explored;
        if (!forMinimap && viewer?.Intelligence.ShareWorldVision == true && visibility.HasFlag(CellVisibility.VisibleByAlly))
            return VisibilityState.Visible;
        return VisibilityState.Unexplored;
    }

    public bool IsUnitVisibleToLocalPlayer(Unit unit, bool forMinimap = false)
    {
        Player? local = Globals.Game.Players.FirstOrDefault(player => player.Id == Globals.Game.Network.LocalPeerId);
        return local is null || IsUnitVisible(local.ArmyId, unit, forMinimap);
    }

    public IReadOnlyList<Guid> GetAlliedArmyIds(Guid armyId) =>
        _alliedArmyIds.TryGetValue(armyId, out IReadOnlyList<Guid>? allies) ? allies : [];

    private void RefreshAlliances()
    {
        _alliedArmyIds.Clear();
        foreach (Army army in Globals.Game.Armies.Armies)
            _alliedArmyIds[army.Id] = GetAlliedArmies(army.Id).Select(ally => ally.Id).ToArray();
    }

    private IEnumerable<Army> GetAlliedArmies(Guid armyId)
    {
        Army? army = Globals.Game.Armies.Find(armyId);
        if (army?.TeamId is not Guid teamId) return [];
        return Globals.Game.Armies.Armies.Where(candidate => candidate.Id != armyId && candidate.TeamId == teamId);
    }

    private static bool AreAllies(Guid first, Guid second)
    {
        Army? a = Globals.Game.Armies.Find(first);
        Army? b = Globals.Game.Armies.Find(second);
        return a?.TeamId is Guid team && b?.TeamId == team;
    }

    private static CellVisibility ToOwnFlags(VisibilityState state) => state switch
    {
        VisibilityState.Visible => CellVisibility.Explored | CellVisibility.Visible,
        VisibilityState.Explored => CellVisibility.Explored,
        _ => CellVisibility.None
    };
}

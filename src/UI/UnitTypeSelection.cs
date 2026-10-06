using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace RTS;

public sealed class UnitTypeSelection
{
    public TimeSpan DoubleClickWindow { get; set; } = TimeSpan.FromMilliseconds(300);
    public float MouseTolerancePixels { get; set; } = 6;
    public static float Radius { get; set; } = 25;
    private Guid? _lastUnit;
    private Point _lastPosition;
    private double _lastTime;

    public bool Click(Unit? target, Point screen, double now)
    {
        if (target is null || !double.IsFinite(now)) { Reset(); return false; }
        double elapsed = now - _lastTime;
        double dx = (double)screen.X - _lastPosition.X, dy = (double)screen.Y - _lastPosition.Y;
        bool doubled = _lastUnit == target.UnitId && elapsed >= 0 && elapsed <= DoubleClickWindow.TotalSeconds &&
            dx * dx + dy * dy <= MouseTolerancePixels * MouseTolerancePixels;
        _lastUnit = doubled ? null : target.UnitId;
        _lastPosition = screen;
        _lastTime = now;
        return doubled;
    }

    public void Reset() => _lastUnit = null;

    public static bool IsSelectionIntent(Unit? target, IReadOnlyList<Unit> selected, Guid armyId,
        bool firstSelection, bool shift, bool hasExplicitAction) => !hasExplicitAction &&
        (firstSelection || target is not null && target.ArmyId == armyId && (shift || selected.Contains(target)));

    public static List<Unit> GetNearby(Unit target, IEnumerable<Unit> candidates, Guid armyId, Func<Unit, bool> visible)
    {
        if (target.ArmyId != armyId || !target.IsSelectable || !visible(target) || string.IsNullOrWhiteSpace(target.GameplayTypeId)) return [];
        float radius = Math.Max(0, Radius);
        return candidates.Where(unit => unit.ArmyId == armyId && unit.IsSelectable && visible(unit) &&
            unit.GameplayTypeId == target.GameplayTypeId &&
            Vector2.DistanceSquared(new(unit.Position.X, unit.Position.Z), new(target.Position.X, target.Position.Z)) <= radius * radius)
            .DistinctBy(unit => unit.UnitId).ToList();
    }

    public static List<Unit> Merge(IEnumerable<Unit> existing, IEnumerable<Unit> nearby, Guid armyId, Func<Unit, bool> visible) =>
        existing.Concat(nearby).Where(unit => unit.ArmyId == armyId && unit.IsSelectable && visible(unit))
            .DistinctBy(unit => unit.UnitId).ToList();
}

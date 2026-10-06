using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace RTS;

public readonly record struct SelectionGroupKey(int Number, bool Save, bool CenterCamera);

/// <summary>Edge-triggered group input using real time, independent of simulation speed.</summary>
public sealed class SelectionGroupHotkeys
{
    public TimeSpan DoubleTapWindow { get; set; } = TimeSpan.FromMilliseconds(300);
    private KeyboardState _previous;
    private int? _lastNumber;
    private double _lastPress;

    public SelectionGroupKey? Update(KeyboardState keyboard, double nowSeconds, bool enabled)
    {
        int? pressed = null;
        int count = 0;
        for (int number = 0; number <= 9; number++)
        {
            Keys key = Keys.D0 + number;
            if (keyboard.IsKeyDown(key) && !_previous.IsKeyDown(key))
            { pressed = number; count++; }
        }
        _previous = keyboard;
        bool control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        if (!enabled || count > 1 || !double.IsFinite(nowSeconds))
        { _lastNumber = null; return null; }
        if (control) _lastNumber = null;
        if (pressed is not int value) return null;
        if (control) return new(value, true, false);
        double elapsed = nowSeconds - _lastPress;
        bool center = _lastNumber == value && elapsed >= 0 && elapsed <= DoubleTapWindow.TotalSeconds;
        _lastNumber = center ? null : value;
        _lastPress = nowSeconds;
        return new(value, false, center);
    }

    public void Reset()
    {
        _lastNumber = null;
        // Keep sampled keys: a held key must not become a new press after a reset.
    }

    public static List<Unit> GetLiveMembers(GameWorld world, IEnumerable<Unit> members)
    {
        List<Unit> result = [];
        HashSet<Guid> seen = [];
        foreach (Unit unit in members)
            if (!unit.IsDying && ReferenceEquals(world.Units.FindById(unit.UnitId), unit) && seen.Add(unit.UnitId))
                result.Add(unit);
        return result;
    }

    public static bool TryGetCenter(GameWorld world, IEnumerable<Unit> members, out Vector3 center)
    {
        Vector3 sum = Vector3.Zero;
        HashSet<Guid> positions = [];
        foreach (Unit member in GetLiveMembers(world, members))
        {
            Unit target = member;
            if (member.IsEmbarked)
            {
                if (member.ContainerUnitId is not Guid id || world.Units.FindById(id) is not Unit container || container.IsDying)
                    continue;
                target = container;
            }
            Vector3 point = target.Position;
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) continue;
            if (positions.Add(target.UnitId)) sum += point;
        }
        center = positions.Count == 0 ? Vector3.Zero : sum / positions.Count;
        return positions.Count > 0;
    }
}

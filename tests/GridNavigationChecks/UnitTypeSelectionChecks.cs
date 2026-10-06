using System;
using System.Linq;
using Microsoft.Xna.Framework;
using RTS;

internal static class UnitTypeSelectionChecks
{
    private sealed class TypedUnit : MobileUnit
    {
        private readonly string _type;
        public bool Dead;
        public override bool IsDying => Dead;
        public override string GameplayTypeId => _type;
        public TypedUnit(string type, Vector3 point, Guid army) : base(point, Guid.NewGuid())
        { _type = type; SetArmy(army); }
    }
    public static int Run()
    {
        int checks = 0;
        void Check(bool test, string message) { if (!test) throw new Exception("Type selection: " + message); checks++; }
        Guid army = Guid.NewGuid(), enemy = Guid.NewGuid();
        TypedUnit first = new("gunner", new(10, 0, 10), army);
        TypedUnit close = new("gunner", new(20, 0, 10), army);
        TypedUnit boundary = new("gunner", new(35, 50, 10), army);
        TypedUnit far = new("gunner", new(35.1f, 0, 10), army);
        TypedUnit otherType = new("engineer", new(10, 0, 10), army);
        TypedUnit foreign = new("gunner", new(10, 0, 10), enemy);
        TypedUnit hidden = new("gunner", new(10, 0, 10), army);
        TypedUnit dead = new("gunner", new(10, 0, 10), army) { Dead = true };
        TypedUnit embarked = new("gunner", new(10, 0, 10), army); embarked.Embark(first.UnitId);
        Unit[] all = { first, close, boundary, far, otherType, foreign, hidden, dead, embarked, close };
        bool Visible(Unit u) => u != hidden;
        var matches = UnitTypeSelection.GetNearby(first, all, army, Visible);
        Check(matches.Count == 3 && matches.Contains(first) && matches.Contains(close) && matches.Contains(boundary), "same own type within inclusive X/Z radius");
        Check(!matches.Contains(far) && !matches.Contains(otherType), "outside and other type excluded");
        Check(!matches.Contains(foreign) && !matches.Contains(hidden) && !matches.Contains(dead) && !matches.Contains(embarked), "ownership and eligibility filters");
        Check(UnitTypeSelection.GetNearby(foreign, all, army, Visible).Count == 0, "foreign debug target cannot expand to own group");
        Check(UnitTypeSelection.GetNearby(hidden, all, army, Visible).Count == 0, "hidden target cannot expand");
        var merged = UnitTypeSelection.Merge(new Unit[] { otherType, foreign, close }, matches, army, Visible);
        Check(merged.Count == 4 && merged.Contains(otherType) && !merged.Contains(foreign), "shift preserves own types without duplicates or foreign debug members");
        Check(UnitTypeSelection.IsSelectionIntent(first, Array.Empty<Unit>(), army, true, false, false), "initial click selects");
        Check(UnitTypeSelection.IsSelectionIntent(first, new[] { first }, army, false, false, false), "second click on selected own unit selects instead of commanding");
        Check(!UnitTypeSelection.IsSelectionIntent(close, new[] { first }, army, false, false, false), "new target retains context command intent");
        Check(UnitTypeSelection.IsSelectionIntent(close, new[] { first }, army, false, true, false), "shift starts additive selection");
        Check(!UnitTypeSelection.IsSelectionIntent(first, new[] { first }, army, false, true, true), "explicit target action always commands");
        Check(!UnitTypeSelection.IsSelectionIntent(foreign, new[] { first }, army, false, true, false), "shift enemy click retains command intent");
        UnitTypeSelection clicks = new();
        Point point = new(100, 100);
        Check(!clicks.Click(first, point, 0), "first click not double");
        Check(clicks.Click(first, new(103, 102), 0.2), "same target tolerates small mouse movement");
        Check(!clicks.Click(first, point, 0.25), "third click starts new pair");
        Check(!clicks.Click(close, point, 0.26), "different unit not double");
        Check(!clicks.Click(first, point, 0.27), "different target interrupted original pair");
        Check(!clicks.Click(first, point, 0.7), "late second click not double");
        Check(!clicks.Click(first, new(110, 100), 0.8), "large mouse movement not double");
        clicks.Reset();
        Check(!clicks.Click(first, point, 0.9), "drag/HUD/disabled-input reset cancels pending click");
        Check(!clicks.Click(null, point, 0.91) && !clicks.Click(first, point, 0.92), "empty click resets history");
        clicks.DoubleClickWindow = TimeSpan.FromMilliseconds(50);
        Check(!clicks.Click(first, point, 1.1), "configurable time window respected");
        Check(clicks.Click(first, point, 1.12), "short window can still match");
        return checks;
    }
}

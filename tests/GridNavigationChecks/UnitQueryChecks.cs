using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using RTS;

internal static class UnitQueryChecks
{
    private static readonly MethodInfo SetArmyMethod = typeof(Unit).GetMethod("SetArmy", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static IList<Unit> Membership(UnitHandler handler) => (IList<Unit>)typeof(UnitHandler)
        .GetField("_units", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!;
    private sealed class ProbeUnit(Guid id) : MobileUnit(Vector3.Zero, id)
    {
        private bool _dying;
        public bool Removed { get; set; }
        public override bool IsDying => _dying;
        public override bool IsReadyForRemoval => Removed;
        public override bool BeginDeathSequence() { _dying = true; return true; }
        public override void Update(GameTime gameTime) { }
        public void ChangeArmy(Guid? id) => SetArmyMethod.Invoke(this, [id]);
    }
    public static string MeasurementReport { get; private set; } = string.Empty;
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        bool previousSpectator = Globals.IsSpectator;
        bool previousFog = Globals.FogOfWarEnabled;
        try
        {
            typeof(Globals).GetProperty(nameof(Globals.IsSpectator))!.SetValue(null, true);
            Globals.FogOfWarEnabled = true;
            var visibility = new VisibilitySystem((GameWorld)RuntimeHelpers.GetUninitializedObject(typeof(GameWorld)));
            var observed = new ProbeUnit(Guid.NewGuid());
            Check(visibility.IsUnitVisible(Guid.NewGuid(), observed), "Spectator sees foreign/neutral units despite enabled fog");
            Check(visibility.GetDisplayedTerrainVisibility(Guid.NewGuid(), Point.Zero, true, Array.Empty<Guid>()) == VisibilityState.Visible,
                "Spectator minimap reveals unexplored cells");
            Check(visibility.IsTerrainExploredToLocalPlayer(Point.Zero) && visibility.IsTerrainCurrentlyVisible(Guid.NewGuid(), Point.Zero),
                "Spectator reveals terrain and transient effects");
            Check(PlayerHandler.ControllableUnits([observed]).Count == 0, "Spectator cannot issue UI orders even to owned units");
            Check(Globals.FogOfWarEnabled, "Spectator preserves normal fog setting");
        }
        finally { typeof(Globals).GetProperty(nameof(Globals.IsSpectator))!.SetValue(null, previousSpectator); Globals.FogOfWarEnabled = previousFog; }
        Guid firstArmy = Guid.NewGuid(), secondArmy = Guid.NewGuid();
        var handler = new UnitHandler(); var membership = Membership(handler);
        Check(handler.GetSnapshot().Count == 0 && handler.FindById(Guid.NewGuid()) is null, "Empty indexed handler has no units");
        var first = new ProbeUnit(Guid.NewGuid()); first.ChangeArmy(firstArmy); membership.Add(first);
        var second = new ProbeUnit(Guid.NewGuid()); second.ChangeArmy(secondArmy); membership.Add(second);
        IReadOnlyList<Unit> snapshot = handler.GetSnapshot();
        Check(ReferenceEquals(snapshot, handler.Units) && ReferenceEquals(snapshot, handler.GetSnapshot()), "Unchanged membership reuses snapshot across queries");
        Check(handler.FindById(first.UnitId) == first && handler.FindMobileUnitById(second.UnitId) == second,
            "ID index resolves actual instances and mobile lookups");
        Check(snapshot is not Unit[] && snapshot is not List<Unit>, "Snapshot does not expose a mutable array or live list");
        try { ((IList<Unit>)snapshot)[0] = second; throw new Exception("Snapshot mutation accepted"); }
        catch (NotSupportedException) { Check(true, "Snapshot rejects mutation"); }
        var firstMembers = handler.GetArmyUnits(firstArmy); var secondMembers = handler.GetArmyUnits(secondArmy);
        Check(firstMembers.SequenceEqual([first]) && secondMembers.SequenceEqual([second]), "Army indices separate ownership");
        Check(ReferenceEquals(firstMembers, handler.GetArmyUnits(firstArmy)), "Army membership reuses read-only snapshot");
        long revision = handler.MembershipRevision; first.ChangeArmy(secondArmy);
        Check(handler.MembershipRevision > revision && handler.GetArmyUnits(firstArmy).Count == 0 &&
            handler.GetArmyUnits(secondArmy).SequenceEqual([first, second]), "Army transfer updates both indices immediately, preserving world order");
        Check(firstMembers.Count == 1 && secondMembers.Count == 1 && snapshot.Count == 2,
            "Old snapshots retain membership through ownership changes");
        revision = handler.MembershipRevision; first.ChangeArmy(secondArmy);
        Check(revision == handler.MembershipRevision, "Unchanged owner does not invalidate caches");
        first.ChangeArmy(null);
        Check(handler.GetArmyUnits(secondArmy).SequenceEqual([second]) && handler.FindById(first.UnitId) == first,
            "Neutralization removes army membership while retaining ID membership");
        first.ChangeArmy(firstArmy);
        var untouchedArmySnapshot = handler.GetArmyUnits(secondArmy);
        var third = new ProbeUnit(Guid.NewGuid()); third.ChangeArmy(firstArmy); membership.Insert(0, third);
        Check(handler.GetSnapshot().SequenceEqual([third, first, second]) && handler.GetArmyUnits(firstArmy).SequenceEqual([third, first]),
            "Inserted unit appears in ordered world and army snapshots");
        Check(snapshot.Count == 2, "Snapshot remains stable after spawn");
        Check(ReferenceEquals(untouchedArmySnapshot, handler.GetArmyUnits(secondArmy)),
            "Spawn in another army does not copy unaffected army membership");
        int iterated = 0;
        foreach (Unit unit in handler.GetSnapshot())
        {
            iterated++;
            if (unit == third) { membership.Remove(second); membership.Add(second); }
        }
        Check(iterated == 3 && handler.Count == 3, "Removal and insertion during snapshot iteration are safe");
        var replacement = new ProbeUnit(third.UnitId); replacement.ChangeArmy(secondArmy); membership[0] = replacement;
        Check(handler.FindById(third.UnitId) == replacement && handler.GetArmyUnits(firstArmy).SequenceEqual([first]),
            "Atomic same-ID replacement updates identity and army indices");
        revision = handler.MembershipRevision; third.ChangeArmy(secondArmy);
        Check(revision == handler.MembershipRevision, "Detached unit cannot invalidate its former handler");
        try { membership.Add(new ProbeUnit(first.UnitId)); throw new Exception("Duplicate ID accepted"); }
        catch (InvalidOperationException) { Check(handler.Count == 3 && handler.FindById(first.UnitId) == first, "Duplicate registration fails atomically"); }
        membership.Remove(replacement);
        Check(handler.FindById(replacement.UnitId) is null && handler.GetArmyUnits(secondArmy).SequenceEqual([second]),
            "Removal updates ID and army membership");
        var savedWorld = Globals.World;
        try
        {
            var world = (GameWorld)RuntimeHelpers.GetUninitializedObject(typeof(GameWorld));
            typeof(GameWorld).GetField("<GameGrid>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world, new GameGrid(8, 8, 1));
            Globals.World = world;
            Check(handler.Destroy(first.UnitId) && first.IsDying && handler.FindById(first.UnitId) == first && handler.GetArmyUnits(firstArmy).Contains(first),
                "Dying wreck stays indexed until animation removal; callers keep life-state filters");
            Check(handler.Destroy(first.UnitId) && handler.Count == 2, "Repeated destroy retains dying wreck exactly once");
            first.Removed = true; handler.Update(new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
            Check(handler.FindById(first.UnitId) is null && handler.GetArmyUnits(firstArmy).Count == 0 && handler.Count == 1,
                "End of death animation removes wreck from all indices");
            var beforeClear = handler.GetSnapshot();
            typeof(UnitHandler).GetMethod("ClearForNetworkSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(handler, null);
            Check(handler.Count == 0 && handler.FindById(second.UnitId) is null && handler.GetArmyUnits(secondArmy).Count == 0 && beforeClear.Count == 1,
                "Network snapshot reset clears all indices while old snapshots remain iterable");
            membership.Add(second); handler.ClearForMatchStart();
            Check(handler.Count == 0 && handler.FindById(second.UnitId) is null, "Match reset removes re-registered unit from index");
            revision = handler.MembershipRevision; second.ChangeArmy(firstArmy);
            Check(handler.MembershipRevision == revision, "Reset unsubscribes ownership notifications");
        }
        finally { Globals.World = savedWorld; }
        var otherHandler = new UnitHandler(); Membership(otherHandler).Add(second);
        Check(otherHandler.FindById(second.UnitId) == second && handler.FindById(second.UnitId) is null, "Indices belong to their world, without global cross-contamination");
        Check(otherHandler.GetArmyUnits(Guid.NewGuid()).Count == 0, "Unknown army has an empty stable snapshot");

        const int count = 512, frames = 200, lookups = 16;
        var measured = new UnitHandler(); var measuredList = Membership(measured); var legacy = new List<Unit>();
        for (int i = 0; i < count; i++)
        {
            var unit = new ProbeUnit(new Guid(i + 1, 0, 0, new byte[8]));
            unit.ChangeArmy(i % 2 == 0 ? firstArmy : secondArmy); measuredList.Add(unit); legacy.Add(unit);
        }
        long Legacy()
        {
            long sum = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                sum += legacy.ToArray().Length;
                for (int i = 0; i < lookups; i++)
                {
                    Guid id = legacy[(frame * lookups + i) % count].UnitId;
                    if (legacy.ToArray().FirstOrDefault(u => u.UnitId == id) is not null) sum++;
                }
                sum += legacy.ToArray().Count(u => u.ArmyId == firstArmy);
            }
            return sum;
        }
        long Indexed()
        {
            long sum = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                sum += measured.GetSnapshot().Count;
                for (int i = 0; i < lookups; i++)
                    if (measured.FindById(legacy[(frame * lookups + i) % count].UnitId) is not null) sum++;
                sum += measured.GetArmyUnits(firstArmy).Count;
            }
            return sum;
        }
        // Warm both complete workloads equally; measure thread allocations and elapsed time, not FPS.
        Legacy(); Indexed();
        var watch = new Stopwatch(); long before = GC.GetAllocatedBytesForCurrentThread(); watch.Start();
        long oldResult = Legacy(); watch.Stop(); long oldBytes = GC.GetAllocatedBytesForCurrentThread() - before; double oldMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart(); before = GC.GetAllocatedBytesForCurrentThread(); long newResult = Indexed();
        watch.Stop(); long newBytes = GC.GetAllocatedBytesForCurrentThread() - before; double newMs = watch.Elapsed.TotalMilliseconds;
        Check(oldResult == newResult, "Query benchmark preserves identical result across old and new algorithms");
        Check(oldBytes > 10_000_000 && newBytes < oldBytes / 100, "Cached snapshots and ID/army indices reduce allocations by more than 99 percent in identical stable-membership workload");
        MeasurementReport = $"# Unit-Abfragen: Vergleichsmessung\n\nUTC: {DateTime.UtcNow:O}\n\nRuntime: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}.\n\nSzenario: {count} reguläre grafikfreie Units, zwei Armies, {frames} Auswertungen; je Auswertung ein Welt-Snapshot, {lookups} ID-Abfragen und eine Army-Abfrage. Gleiche Reihenfolge, IDs und Ergebnis-Prüfsumme; beide vollständigen Abläufe vorher aufgewärmt. Keine Mitgliedschaftsänderungen innerhalb des Messfensters.\n\n| Verfahren | Allokationen auf dem Aufrufthread | Laufzeit | Ergebnis |\n|---|---:|---:|---:|\n| Bisher: ToArray je Snapshot/ID-/Army-Abfrage | {oldBytes:N0} Bytes | {oldMs:F3} ms | {oldResult} |\n| Jetzt: gecachter Snapshot, ID-Index, Army-Snapshot | {newBytes:N0} Bytes | {newMs:F3} ms | {newResult} |\n\nDas bisherige Verfahren wird im selben Prozess nachgebildet; kein Vergleich unterschiedlicher Rechner oder Spielstände. Snapshot-Aufbau und Indexpflege bei Spawn/Army-Wechsel sind nicht kostenlos und liegen außerhalb dieser stationären Messung. Sichere Mitgliedschaft, Wechsel und Lebenszyklus werden zusätzlich durch Verhaltenstests geprüft. Zeitwerte sind Einzelmessungen ohne Echtzeitgarantie; dies ist kein FPS- oder vollständiger KI-Schlachtbenchmark.\n";
        return checks;
    }
}

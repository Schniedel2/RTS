using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RTS;

/// <summary>Opt-in, inclusive game-thread measurements. Nested rows must not be added together.</summary>
public static class PerformanceMeasurements
{
    private static readonly Dictionary<string, Measurement> Rows = new(StringComparer.Ordinal);
    private static int _generation;
    public static bool Enabled { get; set; }

    public sealed class Measurement
    {
        public long Calls { get; internal set; }
        public double TotalMilliseconds { get; internal set; }
        public double MaximumMilliseconds { get; internal set; }
        public long AllocatedBytes { get; internal set; }
        public long MaximumAllocatedBytes { get; internal set; }
    }

    public static Scope Measure(string name)
    {
        if (!Enabled) return default;
        if (!Rows.TryGetValue(name, out Measurement? row))
            Rows.Add(name, row = new Measurement());
        return new Scope(row, _generation);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly Measurement? _row;
        private readonly long _started;
        private readonly long _bytes;
        private readonly int _generation;
        private readonly int _thread;
        internal Scope(Measurement row, int generation)
        {
            _row = row;
            _generation = generation;
            _thread = Environment.CurrentManagedThreadId;
            _bytes = GC.GetAllocatedBytesForCurrentThread();
            _started = Stopwatch.GetTimestamp();
        }
        public void Dispose()
        {
            if (_row is null || _generation != PerformanceMeasurements._generation) return;
            if (_thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Performance scopes must remain on the game thread.");
            double elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - _bytes;
            _row.Calls++;
            _row.TotalMilliseconds += elapsed;
            _row.MaximumMilliseconds = Math.Max(_row.MaximumMilliseconds, elapsed);
            _row.AllocatedBytes += bytes;
            _row.MaximumAllocatedBytes = Math.Max(_row.MaximumAllocatedBytes, bytes);
        }
    }

    public static void Reset()
    {
        Rows.Clear();
        _generation++;
    }

    public static string Report()
    {
        var result = new StringBuilder();
        result.AppendLine("Inclusive game-thread measurements; nested rows overlap. Allocations are bytes, not retained memory.");
        result.AppendLine("Scope | Calls | Total ms | Mean ms | Max ms | Allocated bytes | Max bytes/call");
        foreach (var pair in Rows.OrderByDescending(pair => pair.Value.MaximumMilliseconds)
                     .ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Measurement row = pair.Value;
            result.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{pair.Key} | {row.Calls} | {row.TotalMilliseconds:F3} | {(row.Calls == 0 ? 0 : row.TotalMilliseconds / row.Calls):F3} | {row.MaximumMilliseconds:F3} | {row.AllocatedBytes} | {row.MaximumAllocatedBytes}"));
        }
        return result.ToString();
    }
}

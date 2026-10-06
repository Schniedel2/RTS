using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Text.Json;
namespace RTS;

/// <summary>Opt-in bounded samples; mutation is game-thread only except wire byte counters.</summary>
public sealed class TimingSamples
{
    private readonly List<double> _values = [];
    public long Count { get; private set; }
    public double Total { get; private set; }
    public double Maximum { get; private set; }
    public void Add(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        Count++; Total += milliseconds; Maximum = Math.Max(Maximum, milliseconds);
        if (_values.Count < 100000) _values.Add(milliseconds);
    }
    public object Snapshot()
    {
        var ordered = _values.Order().ToArray();
        double Percentile(double p) => ordered.Length == 0 ? 0 : ordered[(int)Math.Ceiling(p * ordered.Length) - 1];
        return new { Count, Mean = Count == 0 ? 0 : Total / Count, P95 = Percentile(.95), P99 = Percentile(.99), Maximum, SampleCount = ordered.Length };
    }
}
public sealed class ClientRunDiagnostics
{
    private long _sent, _received;
    private readonly Dictionary<Guid, long> _requests = [];
    private readonly Dictionary<(Guid Army, long Generation, long Sequence), long> _heartbeats = [];
    public TimingSamples HeartbeatRoundTrip { get; } = new();
    public void Heartbeat(Guid army, long generation, long sequence)
    {
        if (_heartbeats.Count >= 512) _heartbeats.Remove(_heartbeats.Keys.First());
        _heartbeats.TryAdd((army, generation, sequence), Stopwatch.GetTimestamp());
    }
    public void HeartbeatReply(Guid army, long generation, long sequence)
    { if (_heartbeats.Remove((army,generation,sequence),out long started)) HeartbeatRoundTrip.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    public TimingSamples FrameWork { get; } = new();
    public TimingSamples FrameInterval { get; } = new();
    public TimingSamples Decisions { get; } = new();
    public TimingSamples Replies { get; } = new();
    public int MaximumInbox { get; private set; }
    public int MaximumControllers { get; private set; }
    public int Snapshots { get; set; }
    public int MatchStarts { get; set; }
    public int Rejections { get; private set; }
    public long RequestCount { get; private set; }
    public void Wire(bool sent, int bytes) { if (sent) Interlocked.Add(ref _sent, bytes); else Interlocked.Add(ref _received, bytes); }
    public void Request(Guid id)
    {
        if (_requests.ContainsKey(id)) return;
        if (_requests.Count >= 4096) _requests.Remove(_requests.Keys.First());
        _requests.Add(id, Stopwatch.GetTimestamp());
        RequestCount++;
    }
    public void Reply(Guid id, bool rejected)
    {
        if (!_requests.Remove(id, out long started)) return;
        Replies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        if (rejected) Rejections++;
    }
    public void Frame(double work, double interval, int inbox, int controllers)
    {
        FrameWork.Add(work); FrameInterval.Add(interval);
        MaximumInbox = Math.Max(MaximumInbox, inbox); MaximumControllers = Math.Max(MaximumControllers, controllers);
    }
    public object Snapshot() => new { frameWorkMs = FrameWork.Snapshot(), frameIntervalMs = FrameInterval.Snapshot(),
        decisionsMs = Decisions.Snapshot(), requestReplyMs = Replies.Snapshot(), heartbeatRoundTripMs = HeartbeatRoundTrip.Snapshot(), MaximumInbox, MaximumControllers,
        Snapshots, MatchStarts, Rejections, RequestCount, sentBytes = Interlocked.Read(ref _sent), receivedBytes = Interlocked.Read(ref _received),
        scopes = PerformanceMeasurements.Snapshot() };
    public void Save(string file)
    {
        using var process = Process.GetCurrentProcess();
        string target = Path.GetFullPath(file); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { cpuSeconds = process.TotalProcessorTime.TotalSeconds,
            workingSetBytes = process.WorkingSet64, metrics = Snapshot() }));
        File.Move(temporary, target, overwrite: true);
    }
}

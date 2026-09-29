using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RTS.Network;

/// <summary>Opt-in deterministic state comparison. All captures happen on the game thread.</summary>
public sealed class NetworkSyncDiagnostics
{
    private readonly NetworkHandler _network;
    private readonly Func<SessionSnapshot> _capture;
    private readonly Action<string> _write;
    private bool _enabled;
    private float _intervalSeconds = 5.0f;
    private double _elapsed;
    private long _sequence;
    private int _reports;
    private int _mismatches;

    public bool Enabled => _enabled;
    public float IntervalSeconds => _intervalSeconds;

    public NetworkSyncDiagnostics(NetworkHandler network, NetworkInput input,
        Func<SessionSnapshot> capture, Action<string> write)
    {
        _network = network;
        _capture = capture;
        _write = write;
        input.MessageReceived += HandleMessage;
    }

    public void Start(float intervalSeconds = 5.0f)
    {
        _network.AssertGameThread();
        if (!_network.IsHost)
        {
            _write("Network sync diagnostics can only be started by the host.");
            return;
        }
        _intervalSeconds = Math.Clamp(intervalSeconds, 0.5f, 300.0f);
        _enabled = true;
        _elapsed = _intervalSeconds;
        _reports = 0;
        _mismatches = 0;
        BroadcastControl(true);
        _write($"Network sync diagnostics started ({_intervalSeconds:0.##} s interval).");
    }

    public void Stop()
    {
        _network.AssertGameThread();
        if (!_network.IsHost)
        {
            _write("Network sync diagnostics can only be stopped by the host.");
            return;
        }
        _enabled = false;
        _elapsed = 0;
        BroadcastControl(false);
        _write("Network sync diagnostics stopped.");
    }

    public void CheckNow()
    {
        _network.AssertGameThread();
        if (!_network.IsHost)
        {
            _write("A network sync check can only be requested by the host.");
            return;
        }
        SendProbe();
    }

    public string GetStatusText() =>
        $"Network sync diagnostics: {(_enabled ? "enabled" : "disabled")}, " +
        $"interval={_intervalSeconds:0.##}s, checks={_sequence}, reports={_reports}, mismatches={_mismatches}.";

    public void Update(GameTime gameTime)
    {
        if (!_enabled || !_network.IsHost) return;
        _elapsed += Math.Max(0, gameTime.ElapsedGameTime.TotalSeconds);
        if (_elapsed < _intervalSeconds) return;
        _elapsed %= _intervalSeconds;
        SendProbe();
    }

    private void BroadcastControl(bool enabled)
    {
        var command = new NetworkMessage(NetworkMessageType.SyncDiagnosticsControlCommand,
            _network.LocalPeerId, SyncDiagnosticsEnabled: enabled,
            SyncDiagnosticsIntervalSeconds: _intervalSeconds);
        _network.ApplyLocalCommand(command);
        _ = _network.BroadcastAsync(command);
    }

    private void SendProbe()
    {
        SyncDiagnosticDigest digest = CreateDigest(_capture(), ++_sequence);
        var probe = new NetworkMessage(NetworkMessageType.SyncDiagnosticsProbeCommand,
            _network.LocalPeerId, SyncDiagnosticDigest: digest);
        _ = _network.BroadcastAsync(probe);
        if (_network.Members.Count == 0)
            _write($"[SYNC #{_sequence}] No connected clients to compare.");
    }

    private void HandleMessage(NetworkMessage message)
    {
        if (message.Type == NetworkMessageType.MemberJoined && _network.IsHost && _enabled)
        {
            // A peer may join after diagnostics were enabled. Repeating the
            // idempotent control message also brings that peer into the run.
            BroadcastControl(true);
            return;
        }

        if (message.Type == NetworkMessageType.SyncDiagnosticsControlCommand &&
            message.SyncDiagnosticsEnabled is bool enabled)
        {
            _enabled = enabled;
            if (message.SyncDiagnosticsIntervalSeconds >= 0.5f)
                _intervalSeconds = message.SyncDiagnosticsIntervalSeconds;
            if (!_network.IsHost)
                _write($"Network sync diagnostics {(enabled ? "enabled" : "disabled")} by host.");
            return;
        }

        if (message.Type == NetworkMessageType.SyncDiagnosticsProbeCommand && !_network.IsHost &&
            message.SyncDiagnosticDigest is SyncDiagnosticDigest expected)
        {
            SyncDiagnosticDigest actual = CreateDigest(_capture(), expected.Sequence);
            string[] differences = FindDifferences(expected, actual).Take(16).ToArray();
            var outgoingReport = new SyncDiagnosticReport(expected.Sequence, differences.Length == 0,
                differences, actual.Categories);
            _ = _network.SendToHostAsync(new NetworkMessage(
                NetworkMessageType.SyncDiagnosticsReportRequest, _network.LocalPeerId,
                SyncDiagnosticReport: outgoingReport));
            return;
        }

        if (message.Type == NetworkMessageType.SyncDiagnosticsReportRequest && _network.IsHost &&
            message.SyncDiagnosticReport is SyncDiagnosticReport report)
        {
            _reports++;
            string peer = _network.GetPeerDisplayName(message.SenderId);
            if (report.Matches)
                _write($"[SYNC #{report.Sequence}] {peer}: OK");
            else
            {
                _mismatches++;
                _write($"[SYNC #{report.Sequence}] {peer}: DIFFERENT - {string.Join(", ", report.Differences)}");
            }
        }
    }

    internal static SyncDiagnosticDigest CreateDigest(SessionSnapshot snapshot, long sequence)
    {
        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(items, "world:terrain", new { snapshot.World.Width, snapshot.World.Height,
            snapshot.World.TileMap, snapshot.World.HeightMap });
        Add(items, "world:markers", snapshot.World.GameplayMarkers ?? []);
        Add(items, "world:tiberium", snapshot.World.TiberiumCells ?? []);
        Add(items, "world:objects", snapshot.World.MapObjects ?? []);
        foreach (ArmySnapshot army in snapshot.Armies.OrderBy(value => value.Id))
            Add(items, $"army:{army.Id:N}", army);
        foreach (RuntimeUnitSnapshot unit in snapshot.Units.OrderBy(value => value.UnitId))
            Add(items, $"unit:{unit.UnitId:N}", unit);
        foreach (VisibilitySnapshot visibility in snapshot.Visibility.OrderBy(value => value.ArmyId))
            Add(items, $"visibility:{visibility.ArmyId:N}", visibility);

        var categories = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["world"] = HashGroup(items, "world:"),
            ["armies"] = HashGroup(items, "army:"),
            ["units"] = HashGroup(items, "unit:"),
            ["visibility"] = HashGroup(items, "visibility:")
        };
        return new SyncDiagnosticDigest(sequence, snapshot.HostTime, categories, items);
    }

    internal static IEnumerable<string> FindDifferences(SyncDiagnosticDigest expected,
        SyncDiagnosticDigest actual)
    {
        foreach (string category in expected.Categories.Keys.Union(actual.Categories.Keys).Order())
            if (expected.Categories.GetValueOrDefault(category) != actual.Categories.GetValueOrDefault(category))
                yield return category;
        foreach (string key in expected.Items.Keys.Union(actual.Items.Keys).Order())
            if (expected.Items.GetValueOrDefault(key) != actual.Items.GetValueOrDefault(key))
                yield return key;
    }

    private static void Add(Dictionary<string, string> items, string key, object value) =>
        items[key] = Hash(JsonSerializer.SerializeToUtf8Bytes(value, NetworkJson.Options));

    private static string HashGroup(Dictionary<string, string> items, string prefix)
    {
        string value = string.Join('|', items.Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(item => item.Key).Select(item => $"{item.Key}={item.Value}"));
        return Hash(Encoding.UTF8.GetBytes(value));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16];
}

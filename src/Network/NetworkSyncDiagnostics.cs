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
                _write($"[SYNC #{report.Sequence}] {peer}: DIFFERENT");
                foreach (string difference in report.Differences)
                    foreach (string line in difference.Split('\n'))
                        _write($"  {line}");
            }
        }
    }

    internal static SyncDiagnosticDigest CreateDigest(SessionSnapshot snapshot, long sequence)
    {
        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        var details = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(items, "world:terrain", new { snapshot.World.Width, snapshot.World.Height,
            snapshot.World.TileMap, snapshot.World.HeightMap });
        Add(items, "world:markers", (snapshot.World.GameplayMarkers ?? [])
            .OrderBy(marker => marker.Id).Select(marker => new
            {
                marker.Id, marker.Name, marker.Type, marker.X, marker.Y, marker.Z,
                marker.RotationDegrees, marker.Shape, marker.Width, marker.Height,
                marker.PlayerSlot, marker.TeamId,
                Tags = marker.Tags.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                Properties = marker.Properties.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new { item.Key, item.Value }).ToArray(),
                marker.PathPoints
            }).ToArray());
        // CreatedAt and Amount continuously advance from the synchronized host clock.
        // They cannot be sampled at precisely the same instant on two processes.
        Add(items, "world:tiberium", (snapshot.World.TiberiumCells ?? [])
            .OrderBy(cell => cell.CellX).ThenBy(cell => cell.CellZ).Select(cell => new
            {
                cell.CellX, cell.CellZ, cell.RotationYRadians, cell.SubType,
                cell.GrowthFactor, cell.MaxSize, cell.RenderSizeFactor
            }).ToArray());
        Add(items, "world:objects", (snapshot.World.MapObjects ?? [])
            .OrderBy(value => value.Id).ToArray());
        foreach (ArmySnapshot army in snapshot.Armies.OrderBy(value => value.Id))
            Add(items, $"army:{army.Id:N}", army);
        foreach (RuntimeUnitSnapshot unit in snapshot.Units.OrderBy(value => value.UnitId))
        {
            string prefix = $"unit:{unit.UnitId:N}";
            string identityKey = $"{prefix}:identity";
            var identity = new
                { unit.TypeId, unit.UnitId, unit.CreatorPlayerId, unit.ArmyId, unit.PurchasePrice };
            Add(items, identityKey, identity);
            details[identityKey] = $"type={unit.TypeId} creator={Short(unit.CreatorPlayerId)} " +
                $"army={Short(unit.ArmyId)} price={unit.PurchasePrice}";
            Add(items, $"{prefix}:health", unit.HitPoints);
            Add(items, $"{prefix}:behavior", unit.Behavior);
            Add(items, $"{prefix}:occupants", unit.Occupants
                .OrderBy(value => value.UnitId).ToArray());
            if (unit.HarvestPhase is not null)
                Add(items, $"{prefix}:harvester", new
                    { unit.HarvestPhase, Cargo = MathF.Round(unit.CargoAmount, 1) });
        }
        var categories = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["world"] = HashGroup(items, "world:"),
            ["armies"] = HashGroup(items, "army:"),
            ["units"] = HashGroup(items, "unit:")
        };
        Dictionary<Guid, SyncDiagnosticPose> poses = snapshot.Units.ToDictionary(unit => unit.UnitId,
            unit => new SyncDiagnosticPose(unit.X, unit.Y, unit.Z, unit.RotationDegrees));
        Dictionary<Guid, byte[]> explored = snapshot.Visibility.ToDictionary(value => value.ArmyId,
            value => value.Cells.Select(cell => cell == (byte)VisibilityState.Unexplored ? (byte)0 : (byte)1).ToArray());
        return new SyncDiagnosticDigest(sequence, snapshot.HostTime, categories, items, poses, details, explored);
    }

    internal static IEnumerable<string> FindDifferences(SyncDiagnosticDigest expected,
        SyncDiagnosticDigest actual)
    {
        foreach (string category in expected.Categories.Keys.Union(actual.Categories.Keys).Order())
            if (expected.Categories.GetValueOrDefault(category) != actual.Categories.GetValueOrDefault(category))
                yield return category;
        foreach (string key in expected.Items.Keys.Union(actual.Items.Keys).Order())
        {
            bool onHost = expected.Items.ContainsKey(key);
            bool onClient = actual.Items.ContainsKey(key);
            if (!onClient) yield return $"client-missing:{key}";
            else if (!onHost) yield return $"client-only:{key}";
            else if (expected.Items[key] != actual.Items[key])
            {
                if (key.EndsWith(":identity", StringComparison.Ordinal) &&
                    expected.Details?.GetValueOrDefault(key) is string hostIdentity &&
                    actual.Details?.GetValueOrDefault(key) is string clientIdentity)
                    yield return $"{key}\nhost: {hostIdentity}\nclient: {clientIdentity}";
                else yield return key;
            }
        }
        IReadOnlyDictionary<Guid, SyncDiagnosticPose> expectedPoses = expected.UnitPoses ??
            new Dictionary<Guid, SyncDiagnosticPose>();
        IReadOnlyDictionary<Guid, SyncDiagnosticPose> actualPoses = actual.UnitPoses ??
            new Dictionary<Guid, SyncDiagnosticPose>();
        foreach (Guid id in expectedPoses.Keys.Intersect(actualPoses.Keys).Order())
        {
            SyncDiagnosticPose first = expectedPoses[id];
            SyncDiagnosticPose second = actualPoses[id];
            float dx = first.X - second.X;
            float dy = first.Y - second.Y;
            float dz = first.Z - second.Z;
            float yaw = MathF.Abs(MathHelper.WrapAngle(MathHelper.ToRadians(
                first.YawDegrees - second.YawDegrees)));
            // Normal replication delay is expected. Report only a displacement
            // large enough to indicate a stuck or divergent replica.
            if (dx * dx + dy * dy + dz * dz > 9.0f || yaw > MathHelper.ToRadians(60))
                yield return $"movement:{id:N}";
        }
        IReadOnlyDictionary<Guid, byte[]> expectedVisibility = expected.ExploredVisibility ??
            new Dictionary<Guid, byte[]>();
        IReadOnlyDictionary<Guid, byte[]> actualVisibility = actual.ExploredVisibility ??
            new Dictionary<Guid, byte[]>();
        foreach (Guid armyId in expectedVisibility.Keys.Union(actualVisibility.Keys).Order())
        {
            if (!expectedVisibility.TryGetValue(armyId, out byte[]? hostCells))
            {
                yield return $"visibility:client-only-army:{armyId:N}";
                continue;
            }
            if (!actualVisibility.TryGetValue(armyId, out byte[]? clientCells))
            {
                yield return $"visibility:client-missing-army:{armyId:N}";
                continue;
            }
            int different = Math.Abs(hostCells.Length - clientCells.Length);
            for (int index = 0; index < Math.Min(hostCells.Length, clientCells.Length); index++)
                if (hostCells[index] != clientCells[index]) different++;
            // The currently visible rim moves with replicated units and reaches
            // the client later. A small explored-cell fringe is expected.
            if (different > 32)
                yield return $"visibility:{armyId:N} ({different} explored cells differ)";
        }
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

    private static string Short(Guid? id) => id is Guid value ? value.ToString("N")[..8] : "none";
}

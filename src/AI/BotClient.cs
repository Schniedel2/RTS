using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Xna.Framework;
using RTS.Network;

namespace RTS;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BotClientConfig(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string ServerAddress,
    [property: JsonRequired] int Port,
    [property: JsonRequired] string DisplayName,
    string? ProposedProfileId = null, int MaximumArmies = 1, bool Reconnect = true, string? DiagnosticsPath = null)
{
    public static BotClientConfig Load(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{path}: $ must be an object.");
            if (document.RootElement.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() > 1))
                throw new InvalidDataException($"{path}: duplicate field.");
            var value = JsonSerializer.Deserialize<BotClientConfig>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                ?? throw new InvalidDataException($"{path}: $ must not be null.");
            value.Validate(path);
            return value;
        }
        catch (JsonException error) { throw new InvalidDataException($"{path}: {error.Path}: {error.Message}", error); }
    }
    public void Validate(string source)
    {
        if (SchemaVersion != 1) throw new InvalidDataException($"{source}: $.schemaVersion must be 1.");
        if (string.IsNullOrWhiteSpace(ServerAddress) || ServerAddress.Length > 253) throw new InvalidDataException($"{source}: $.serverAddress is invalid.");
        if (Port is < 1 or > 65535) throw new InvalidDataException($"{source}: $.port must be in 1..65535.");
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 32 || DisplayName != DisplayName.Trim()) throw new InvalidDataException($"{source}: $.displayName must contain 1..32 characters without surrounding whitespace.");
        if (MaximumArmies is < 1 or > 32) throw new InvalidDataException($"{source}: $.maximumArmies must be in 1..32.");
        if (DiagnosticsPath is not null && string.IsNullOrWhiteSpace(DiagnosticsPath)) throw new InvalidDataException($"{source}: $.diagnosticsPath must be a nonempty file path.");
        if (ProposedProfileId is not null && !Enum.GetValues<AIStrategyProfileType>().Any(t => AIProfileCatalog.Id(t) == ProposedProfileId))
            throw new InvalidDataException($"{source}: $.proposedProfileId is unknown.");
    }
}

/// <summary>Windowless client entry point. Transport is asynchronous; all world/controller updates stay on this thread.</summary>
public static class BotClient
{
    public static int Run(string path)
    {
        try
        {
            var config = BotClientConfig.Load(Path.GetFullPath(path));
            Globals.MeshHandler = new MeshHandler();
            Globals.MeshHandler.LoadMeshes(Globals.ModelsDirectory, loadTextures: false);
            var diagnostics = config.DiagnosticsPath is null ? null : new ClientRunDiagnostics();
            if (diagnostics is not null) { PerformanceMeasurements.Enabled = true; PerformanceMeasurements.Reset(); }
            using var network = new NetworkHandler(config.DisplayName) { BotOffer = new(config.MaximumArmies, config.ProposedProfileId), RunDiagnostics = diagnostics };
            network.Diagnostic += text => System.Console.WriteLine("[Network] " + text);
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            System.Console.CancelKeyPress += cancel;
            try
            {
                GameWorld? world = null;
                NetworkInput? input = null;
                using var runtime = new RemoteAIRuntime(config.MaximumArmies);
                network.MessageReceived += message => {
                    if (diagnostics is not null && message.Type == NetworkMessageType.StartMultiplayerGameCommand) diagnostics.MatchStarts++;
                    if (message.Type == NetworkMessageType.SessionSnapshot && message.SessionSnapshot is { } snapshot)
                    {
                        if (diagnostics is not null) diagnostics.Snapshots++;
                        runtime.Dispose();
                        input?.Dispose();
                        world = new GameWorld(snapshot.World.Width, snapshot.World.Height, 1, graphicsEnabled: false);
                        Globals.World = world;
                        input = new NetworkInput(network, world, world.SimulationArmies,
                            new SessionStateService(world, world.SimulationArmies), subscribe: false);
                        System.Console.WriteLine($"Snapshot: {snapshot.World.Width}x{snapshot.World.Height}, units={snapshot.Units.Length}");
                    }
                    input?.OnMessageReceived(message);
                };
                network.AIControllerAssignmentChanged += assignment => {
                    if (assignment.ControllerPeerId == network.LocalPeerId)
                        System.Console.WriteLine($"Assigned army={assignment.ArmyId} generation={assignment.Generation} profile={AIProfileCatalog.Id(assignment.Profile.Type)} seed={assignment.Profile.Seed} version={assignment.ProfileVersion} fingerprint={assignment.ProfileFingerprint}");
                };
                var clock = Stopwatch.StartNew();
                double previous = 0, nextConnect = 0, nextReport = 5;
                NetworkConnectionStatus? lastStatus = null;
                while (!cancellation.IsCancellationRequested)
                {
                    double now = clock.Elapsed.TotalSeconds;
                    if ((network.Status is NetworkConnectionStatus.Disconnected or NetworkConnectionStatus.Faulted) && now >= nextConnect)
                    {
                        if (!config.Reconnect && lastStatus is not null) return 2;
                        System.Console.WriteLine($"Connecting to {config.ServerAddress}:{config.Port} as {config.DisplayName}; capacity={config.MaximumArmies}, proposed profile={config.ProposedProfileId ?? "host default"}");
                        network.JoinSessionAsync(config.ServerAddress, config.Port, cancellation.Token).GetAwaiter().GetResult();
                        nextConnect = now + 3;
                    }
                    long frameStarted = Stopwatch.GetTimestamp();
                    network.Update();
                    var time = new GameTime(TimeSpan.FromSeconds(now), TimeSpan.FromSeconds(Math.Min(0.1, now - previous)));
                    double interval = (now - previous) * 1000;
                    previous = now;
                    if (world is not null)
                    {
                        if (network.Status == NetworkConnectionStatus.Connected) world.Update(time);
                        long decisionStarted = Stopwatch.GetTimestamp();
                        runtime.Update(time, world, network);
                        if (now >= 5) diagnostics?.Decisions.Add(Stopwatch.GetElapsedTime(decisionStarted).TotalMilliseconds);
                    }
                    if (network.Status != lastStatus) { System.Console.WriteLine($"Status: {network.Status}; controllers={runtime.Players.Count}"); lastStatus = network.Status; }
                    if (now >= 5) diagnostics?.Frame(Stopwatch.GetElapsedTime(frameStarted).TotalMilliseconds, interval, network.PendingMessages, runtime.Players.Count);
                    if (diagnostics is not null && now >= nextReport)
                    { diagnostics.Save(config.DiagnosticsPath!); nextReport = now + 5; }
                    Thread.Sleep(16);
                }
                return 0;
            }
            finally { System.Console.CancelKeyPress -= cancel; if (diagnostics is not null) diagnostics.Save(config.DiagnosticsPath!); }
        }
        catch (Exception error) { System.Console.Error.WriteLine("Bot client: " + error); return 1; }
    }
}

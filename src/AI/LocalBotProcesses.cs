using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json;
using RTS.Network;

namespace RTS;

/// <summary>Host-owned test bot processes. Start/poll/assignment run on the game thread.</summary>
public sealed class LocalBotProcesses(NetworkHandler network, Action<string> log) : IDisposable
{
    private sealed record Bot(Process Process, AIPlayer AI, string Name, string ConfigPath, long Session, double Started)
    {
        public bool Assigned;
    }
    private readonly Dictionary<Guid, Bot> _bots = [];
    private readonly ConcurrentQueue<string> _output = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    public int Count => _bots.Count;

    public void Start(AIPlayer ai, string? configPath = null)
    {
        network.AssertGameThread();
        if (!network.IsHost || network.HostingPort is not int port) throw new InvalidOperationException("Start a host session first.");
        if (_bots.ContainsKey(ai.Id)) throw new InvalidOperationException("A local bot already runs for this AI. Use bot-client-stop first.");
        var config = configPath is null ? new BotClientConfig(1, "127.0.0.1", port, "Bot") :
            BotClientConfig.Load(Path.IsPathRooted(configPath) ? configPath : Path.Combine(AppContext.BaseDirectory, configPath));
        string name = "RTS-Bot-" + ai.Id.ToString("N")[..12];
        // Local launch always connects to this host and uses a unique peer name.
        config = config with { ServerAddress = "127.0.0.1", Port = port, DisplayName = name, Reconnect = false };
        config.Validate("Local bot");
        string directory = Path.Combine(Path.GetTempPath(), "RTS-LocalBots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, JsonSerializer.Serialize(config, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var info = new ProcessStartInfo("dotnet") {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(typeof(BotClient).Assembly.Location);
        info.ArgumentList.Add("--bot-client"); info.ArgumentList.Add(file);
        var process = new Process { StartInfo = info };
        void Output(object sender, DataReceivedEventArgs args) { if (!string.IsNullOrEmpty(args.Data) && _output.Count < 128) _output.Enqueue($"[Bot {ai.Name}] {args.Data}"); }
        process.OutputDataReceived += Output; process.ErrorDataReceived += Output;
        bool started = false;
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Could not start bot process.");
            started = true;
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            _bots.Add(ai.Id, new(process, ai, name, file, network.SessionGeneration, _clock.Elapsed.TotalSeconds));
            log($"Starting bot for {ai.Name} (PID {process.Id}); assignment follows after join.");
        }
        catch { if (started && !process.HasExited) process.Kill(entireProcessTree: true); process.Dispose(); File.Delete(file); throw; }
    }
    public void Update()
    {
        network.AssertGameThread();
        for (int i = 0; i < 32 && _output.TryDequeue(out string? text); i++) log(text);
        foreach (var bot in _bots.Values.ToArray())
        {
            if (!network.IsHost || network.SessionGeneration != bot.Session) { Stop(bot.AI.Id); continue; }
            if (bot.Process.HasExited) { log($"Bot for {bot.AI.Name} exited ({bot.Process.ExitCode})."); Stop(bot.AI.Id); continue; }
            if (bot.Assigned) continue;
            var peer = network.Members.FirstOrDefault(p => p.DisplayName == bot.Name);
            if (peer is null)
            {
                if (_clock.Elapsed.TotalSeconds - bot.Started > 30) { log($"Bot for {bot.AI.Name}: join timed out."); Stop(bot.AI.Id); }
                continue;
            }
            var profile = network.AIControllers.Find(bot.AI.Player.ArmyId)?.Profile ?? bot.AI.Controller.StrategyProfile
                ?? AIStrategyProfile.Create(0, bot.AI.Player.ArmyId);
            try
            {
                var assignment = network.AssignAIController(bot.AI.Player.ArmyId, bot.AI.Id, peer.Id, profile);
                bot.AI.Controller.BeginMatch(0, bot.AI.Player.ArmyId, assignment.Profile);
                bot.AI.SetStatus(AIPlayerStatus.Active);
                bot.Assigned = true;
                log($"Bot now controls {bot.AI.Name}; generation={assignment.Generation}.");
            }
            catch (ArgumentException error) { log("Bot assignment failed: " + error.Message); Stop(bot.AI.Id); }
        }
    }
    public bool Stop(Guid actor)
    {
        network.AssertGameThread();
        if (!_bots.Remove(actor, out var bot)) return false;
        try { if (!bot.Process.HasExited) bot.Process.Kill(entireProcessTree: true); }
        finally { bot.Process.Dispose(); File.Delete(bot.ConfigPath); }
        log($"Stopped bot for {bot.AI.Name}; host fallback follows through normal disconnect handling.");
        return true;
    }
    public void Dispose() { foreach (Guid actor in _bots.Keys.ToArray()) Stop(actor); }
}

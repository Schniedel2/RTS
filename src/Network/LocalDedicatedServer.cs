using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;

namespace RTS;

/// <summary>Owns local headless processes; reusable by console and a future lobby. Poll on the game thread.</summary>
public sealed class LocalDedicatedServer(Action<string> log, string? executableAssembly = null) : IDisposable
{
    private Process? _server;
    private readonly List<Process> _bots = [];
    private readonly ConcurrentQueue<string> _output = new();
    private string? _directory;
    private DateTime _started;
    private readonly object _logSync = new();
    public string? LogPath { get; private set; }
    private string? _lastError;
    private void RecordOutput(string? line, bool error)
    {
        if (line is null) return;
        lock (_logSync)
        {
            if (error) _lastError = line;
            if (LogPath is string path)
                try { File.AppendAllText(path, (error ? "[error] " : "") + line + Environment.NewLine); }
                catch (IOException) { } // Diagnostics must not terminate the child reader.
        }
        Enqueue(line);
    }
    private bool _ready;
    public bool IsRunning => _server is { HasExited: false };
    public int Port { get; private set; }
    public string? ReadStatus()
    {
        if (_directory is null) return null;
        try
        {
            // The writer atomically replaces this file. Allow renaming while our handle is open.
            using var stream = new FileStream(Path.Combine(_directory, "status.json"), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
    public event Action<int>? Ready;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void Start(DedicatedServerConfig config)
    {
        if (_server is not null) throw new InvalidOperationException("Stop the existing server first.");
        config.Validate();
        _directory = Path.Combine(Path.GetTempPath(), "RTS-LocalServers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        config = config with { ControlFile = Path.Combine(_directory, "control.txt"), StatusPath = Path.Combine(_directory, "status.json") };
        File.WriteAllText(config.ControlFile, "");
        Port = config.Port;
        string logs = Path.Combine(Path.GetTempPath(), "RTS-ServerLogs");
        Directory.CreateDirectory(logs);
        LogPath = Path.Combine(logs, Path.GetFileName(_directory) + ".log");
        _lastError = null;
        File.WriteAllText(LogPath, "Server start " + DateTime.UtcNow.ToString("O") + Environment.NewLine);
        log("Server log: " + LogPath);
        _started = DateTime.UtcNow;
        _ready = false;
        try { _server = Launch("--dedicated-server", config, "server.json"); }
        catch { Directory.Delete(_directory, true); _directory = null; throw; }
        log(config.AutoSelectPort ? "Server starting with automatic port selection..." : $"Server starting on port {Port}...");
    }

    private Process Launch(string mode, object config, string file)
    {
        string path = Path.Combine(_directory!, file);
        File.WriteAllText(path, JsonSerializer.Serialize(config, config.GetType(), Json));
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(executableAssembly ?? typeof(DedicatedServer).Assembly.Location);
        info.ArgumentList.Add(mode); info.ArgumentList.Add(path);
        var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => RecordOutput(e.Data, error: false);
        process.ErrorDataReceived += (_, e) => RecordOutput(e.Data, error: true);
        try { process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); return process; }
        catch { process.Dispose(); throw; }
    }
    private void Enqueue(string? line) { if (line is not null && _output.Count < 128) _output.Enqueue(line); }
    public void Admin(string command)
    {
        if (!IsRunning || !_ready) throw new InvalidOperationException("Server is not ready.");
        if (command.Contains('\n') || command.Contains('\r')) throw new ArgumentException("One admin command per call.");
        File.AppendAllText(Path.Combine(_directory!, "control.txt"), command + Environment.NewLine);
    }
    public void StartBot(string aiName)
    {
        if (!IsRunning || !_ready) throw new InvalidOperationException("Server is not ready.");
        if (string.IsNullOrWhiteSpace(aiName) || aiName.Contains(' ') || aiName.Contains('\n') || aiName.Contains('\r')) throw new ArgumentException("Use one AI name.");
        // Validate the slot before starting a process. Assignments use the server's existing capacity/authority checks.
        using var status = JsonDocument.Parse(ReadStatus() ?? throw new InvalidOperationException("Server status is temporarily unavailable. Try again."));
        bool found = false;
        foreach (var player in status.RootElement.GetProperty("players").EnumerateArray())
            found |= player.GetProperty("isAI").GetBoolean() && player.GetProperty("Name").GetString()!.Equals(aiName, StringComparison.OrdinalIgnoreCase);
        if (!found) throw new ArgumentException("Unknown AI slot. Use server-status.");
        string peer = "LocalBot-" + Guid.NewGuid().ToString("N")[..8];
        _bots.Add(Launch("--bot-client", new BotClientConfig(1, "127.0.0.1", Port, peer, Reconnect: false), peer + ".json"));
        _pending.Add((aiName, peer, DateTime.UtcNow));
    }
    private readonly List<(string AI, string Peer, DateTime Started)> _pending = [];
    public void Update()
    {
        for (int i = 0; i < 32 && _output.TryDequeue(out var line); i++) log("[server] " + line);
        if (_server is null) return;
        if (_server.HasExited)
        {
            // WaitForExit also drains redirected output after the process has exited.
            _server.WaitForExit();
            while (_output.TryDequeue(out var remaining)) log("[server] " + remaining);
            log($"Server exited ({_server.ExitCode}). Last error: {_lastError ?? "none"}. Log: {LogPath}");
            Dispose(); return;
        }
        string? statusJson = ReadStatus();
        if (statusJson is not null)
        {
            try
            {
                using var status = JsonDocument.Parse(statusJson);
                if (!_ready) { Port = status.RootElement.GetProperty("port").GetInt32(); _ready = true; log($"Server ready: localhost:{Port}. Use game-start after players join."); Ready?.Invoke(Port); }
                foreach (var pending in _pending.ToArray())
                {
                    bool joined = false;
                    foreach (var peer in status.RootElement.GetProperty("peers").EnumerateArray())
                        joined |= peer.GetProperty("DisplayName").GetString() == pending.Peer;
                    if (joined && status.RootElement.GetProperty("IsMatchStarted").GetBoolean())
                    { Admin($"assign {pending.AI} {pending.Peer}"); _pending.Remove(pending); }
                    else if (DateTime.UtcNow - pending.Started > TimeSpan.FromMinutes(2))
                    { log("Bot assignment timed out. Start the match before starting bots."); _pending.Remove(pending); }
                }
            }
            catch (IOException) { } // Atomic status replacement can briefly hold the file on Windows.
        }
        if (!_ready && DateTime.UtcNow - _started > TimeSpan.FromSeconds(30)) { log("Server startup timed out."); Dispose(); }
    }
    public async Task StopAsync()
    {
        if (_server is null) return;
        if (IsRunning && _ready)
        {
            Admin("stop");
            await Task.WhenAny(_server.WaitForExitAsync(), Task.Delay(3000));
        }
        Dispose();
    }
    public void Dispose()
    {
        foreach (var bot in _bots) { if (!bot.HasExited) bot.Kill(entireProcessTree: true); bot.Dispose(); }
        _bots.Clear(); _pending.Clear();
        if (_server is not null) { if (!_server.HasExited) _server.Kill(entireProcessTree: true); _server.Dispose(); _server = null; }
        if (_directory is not null) { try { Directory.Delete(_directory, true); } catch (IOException) { } _directory = null; }
        _ready = false;
    }
}

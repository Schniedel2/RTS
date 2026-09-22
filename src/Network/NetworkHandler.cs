using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RTS.Network;

public sealed class NetworkHandler : IDisposable
{
    public const int SessionPortStart = 27000;
    public const int SessionPortEnd = 27010;
    private readonly ConcurrentQueue<NetworkMessage> _receivedMessages = new();
    private readonly ConcurrentDictionary<Guid, NetworkPeer> _members = new();
    private readonly ConcurrentDictionary<Guid, string> _peerDisplayNames = new();
    private readonly object _memberLock = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private TcpListener? _listener;
    private TcpClient? _serverConnection;
    private CancellationTokenSource? _cancellation;
    private readonly NetworkDiscovery _discovery = new();
    private bool _sessionAccepted;
    private Func<NetworkMessage>? _worldDataProvider;
    private Func<IEnumerable<NetworkMessage>>? _playerDataProvider;
    private Func<double>? _hostTimeProvider;
    // Independent of GameTime/Update ticks so it works from the background network read loop too.
    private readonly Stopwatch _localClock = Stopwatch.StartNew();
    private double _hostTimeOffset;

    public Guid LocalPeerId { get; } = Guid.NewGuid();
    public string DisplayName { get; set; }
    public string SessionName { get; private set; } = "";
    public Guid? SessionId { get; private set; }
    public bool IsHost { get; private set; }
    public bool IsConnected => IsHost || (_sessionAccepted && _serverConnection?.Connected == true);
    public IReadOnlyCollection<NetworkPeer> Members => _members.Values.ToArray();
    public IReadOnlyDictionary<Guid, string> PeerDisplayNames => _peerDisplayNames;
    /// <summary>
    /// Host: its own authoritative simulation time. Clients: that same time frame, estimated
    /// from the offset captured once at JoinAccepted. Use this (not GameTime.TotalGameTime) for
    /// any timestamp that is compared against a value coming from the host, e.g. TiberiumCell.CreatedAt.
    /// </summary>
    public double EstimatedHostTime => IsHost
        ? _hostTimeProvider?.Invoke() ?? 0.0
        : _localClock.Elapsed.TotalSeconds + _hostTimeOffset;

    public event Action<NetworkMessage>? MessageReceived;

    public void SetHostTimeProvider(Func<double> hostTimeProvider)
    {
        _hostTimeProvider = hostTimeProvider ?? throw new ArgumentNullException(nameof(hostTimeProvider));
    }

    public void SetWorldDataProvider(Func<NetworkMessage> worldDataProvider)
    {
        _worldDataProvider = worldDataProvider ?? throw new ArgumentNullException(nameof(worldDataProvider));
    }

    public void SetPlayerDataProvider(Func<IEnumerable<NetworkMessage>> playerDataProvider)
    {
        _playerDataProvider = playerDataProvider ?? throw new ArgumentNullException(nameof(playerDataProvider));
    }

    public Task RequestWorldDataAsync(CancellationToken cancellationToken = default)
    {
        if (IsHost)
            return Task.CompletedTask;

        return SendToServerAsync(
            NetworkCommands.CreateWorldDataRequest(LocalPeerId),
            cancellationToken);
    }

    public NetworkHandler() : this(CreateTestDisplayName())
    {
    }

    public NetworkHandler(string displayName)
    {
        DisplayName = ValidateDisplayName(displayName);
        _peerDisplayNames[LocalPeerId] = DisplayName;
    }

    public string GetPeerDisplayName(Guid peerId)
    {
        return _peerDisplayNames.TryGetValue(peerId, out string? displayName)
            ? displayName
            : peerId.ToString();
    }

    private static string CreateTestDisplayName()
    {
        string[] TestNames =
        {
            "BlueFalcon",
            "IronWolf",
            "RedFox",
            "SilverHawk",
            "NightRider",
            "StormRunner",
            "GoldenBear",
            "ShadowTiger"
        };
        string name = TestNames[Random.Shared.Next(TestNames.Length)];
        int number = Random.Shared.Next(0, 100);
        return $"{name}{number:00}";
    }

    public async Task<int> CreateSessionAsync(string sessionName, CancellationToken cancellationToken = default)
    {
        Disconnect();

        SessionName = ValidateDisplayName(sessionName);
        SessionId = Guid.NewGuid();
        IsHost = true;
        _sessionAccepted = true;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        int port = StartSessionListener();
        _discovery.StartAdvertising(SessionName, DisplayName, port);
        _ = AcceptClientsAsync(_cancellation.Token);
        await Task.CompletedTask;
        return port;
    }

    public async Task JoinSessionAsync(string sessionName, CancellationToken cancellationToken = default)
    {
        Disconnect();

        SessionInfo session = (await _discovery.DiscoverAsync(TimeSpan.FromSeconds(2), cancellationToken))
            .FirstOrDefault(candidate => string.Equals(candidate.SessionName, sessionName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Session not found: {sessionName}");

        await JoinSessionAsync(session.Address, session.Port, cancellationToken);
    }

    private async Task JoinSessionAsync(string hostAddress, int port, CancellationToken cancellationToken)
    {
        DisplayName = ValidateDisplayName(DisplayName);
        _serverConnection = new TcpClient();
        _sessionAccepted = false;
        await _serverConnection.ConnectAsync(hostAddress, port, cancellationToken);
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = ReadMessagesAsync(_serverConnection, _cancellation.Token);

        await SendAsync(_serverConnection, new NetworkMessage(
            NetworkMessageType.JoinSession,
            LocalPeerId,
            DisplayName: DisplayName), cancellationToken);
    }

    public Task SendCommandToHostAsync(string command, string[] arguments, CancellationToken cancellationToken = default)
    {
        NetworkMessage message = NetworkCommands.CreateCommandToHost(
            LocalPeerId,
            command,
            arguments);

        if (IsHost)
        {
            _receivedMessages.Enqueue(message);
            return Task.CompletedTask;
        }

        return SendToServerAsync(message, cancellationToken);
    }

    public Task SendCommandToMemberAsync(Guid memberId, string command, string[] arguments, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            NetworkCommands.CreateCommandToMember(
                LocalPeerId,
                memberId,
                command,
                arguments),
            cancellationToken);
    }

    public Task SendSpawnRequestAsync(
        string unitTypeId,
        float x,
        float y,
        float z,
        CancellationToken cancellationToken = default)
    {
        return SendToHostAsync(
            NetworkCommands.CreateSpawnRequest(
                LocalPeerId,
                unitTypeId,
                x,
                y,
                z),
            cancellationToken);
    }

    public Task SendToHostAsync(NetworkMessage message, CancellationToken cancellationToken = default)
    {
        if (IsHost)
        {
            EnqueueLocalMessage(message);
            return Task.CompletedTask;
        }

        return SendToServerAsync(message, cancellationToken);
    }

    public void EnqueueLocalMessage(NetworkMessage message)
    {
        _receivedMessages.Enqueue(message);
    }

    public Task<IReadOnlyList<SessionInfo>> DiscoverSessionsAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return _discovery.DiscoverAsync(timeout, cancellationToken);
    }

    public Task SendCommandToAllAsync(string command, string[] arguments, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            NetworkCommands.CreateCommandToAll(
                LocalPeerId,
                command,
                arguments),
            cancellationToken);
    }

    public void Update()
    {
        while (_receivedMessages.TryDequeue(out NetworkMessage? message))
        {
            MessageReceived?.Invoke(message);
        }
    }

    public void Disconnect()
    {
        _cancellation?.Cancel();
        _listener?.Stop();
        _serverConnection?.Dispose();
        _listener = null;
        _serverConnection = null;
        _sessionAccepted = false;
        _discovery.Stop();
        _members.Clear();
        SessionId = null;
        IsHost = false;
        SessionName = "";
    }

    public void Dispose() => Disconnect();

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = ReadMessagesAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }
        }
    }

    private async Task ReadMessagesAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using NetworkStream stream = client.GetStream();
        using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;

                NetworkMessage? message = JsonSerializer.Deserialize<NetworkMessage>(line, _jsonOptions);
                if (message is null)
                    continue;

                if (IsHost)
                {
                    await HandleHostMessageAsync(client, message, cancellationToken);
                }
                else
                {
                    if (message.Type == NetworkMessageType.JoinAccepted)
                    {
                        _sessionAccepted = true;
                        SessionId = message.SessionId;
                        if (message.DisplayName is not null)
                            _peerDisplayNames[message.SenderId] = message.DisplayName;
                        _hostTimeOffset = message.ServerTime - _localClock.Elapsed.TotalSeconds;
                    }

                    if (message.Type == NetworkMessageType.JoinRejected)
                    {
                        _sessionAccepted = false;
                        _serverConnection?.Dispose();
                    }

                    if (message.Type == NetworkMessageType.MemberJoined &&
                        message.DisplayName is not null)
                    {
                        _peerDisplayNames[message.SenderId] = message.DisplayName;
                    }

                    if (message.Type == NetworkMessageType.MemberLeft)
                        _peerDisplayNames.TryRemove(message.SenderId, out _);

                    _receivedMessages.Enqueue(message);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            NetworkPeer? disconnectedPeer = _members.Values.FirstOrDefault(peer => peer.Client == client);
            if (disconnectedPeer is not null && _members.TryRemove(disconnectedPeer.Id, out _))
            {
                    _peerDisplayNames.TryRemove(disconnectedPeer.Id, out _);
                    _receivedMessages.Enqueue(new NetworkMessage(NetworkMessageType.MemberLeft, disconnectedPeer.Id));
            }

            client.Dispose();
        }
    }

    private async Task HandleHostMessageAsync(TcpClient client, NetworkMessage message, CancellationToken cancellationToken)
    {
        if (message.Type == NetworkMessageType.JoinSession)
        {
            string displayName = message.DisplayName?.Trim() ?? "";
            if (displayName.Length == 0)
            {
                await RejectJoinAsync(client, "A display name is required.", cancellationToken);
                return;
            }

            NetworkPeer peer;
            lock (_memberLock)
            {
                if (string.Equals(displayName, DisplayName, StringComparison.OrdinalIgnoreCase) ||
                    _members.Values.Any(member => string.Equals(member.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)))
                {
                    peer = null!;
                }
                else
                {
                    peer = new NetworkPeer(message.SenderId, displayName, client);
                    _members[peer.Id] = peer;
                    _peerDisplayNames[peer.Id] = peer.DisplayName;
                }
            }

            if (peer is null)
            {
                await RejectJoinAsync(client, $"The display name '{displayName}' is already in use.", cancellationToken);
                return;
            }
                        _peerDisplayNames.Clear();
                        _peerDisplayNames[LocalPeerId] = DisplayName;

            await SendAsync(client, new NetworkMessage(
                NetworkMessageType.JoinAccepted,
                LocalPeerId,
                SessionId: SessionId,
                DisplayName: DisplayName,
                ServerTime: _hostTimeProvider?.Invoke() ?? 0.0), cancellationToken);

            if (_worldDataProvider is not null)
                await SendAsync(client, _worldDataProvider(), cancellationToken);

            if (_playerDataProvider is not null)
            {
                foreach (NetworkMessage playerData in _playerDataProvider())
                    await SendAsync(client, playerData, cancellationToken);
            }

            foreach (NetworkPeer member in _members.Values.Where(member => member.Id != peer.Id))
            {
                await SendAsync(client, new NetworkMessage(
                    NetworkMessageType.MemberJoined,
                    member.Id,
                    DisplayName: member.DisplayName), cancellationToken);
            }

            NetworkMessage memberJoined = new(
                NetworkMessageType.MemberJoined,
                peer.Id,
                DisplayName: peer.DisplayName);
            _receivedMessages.Enqueue(memberJoined);
            await BroadcastAsync(memberJoined, cancellationToken);

            NetworkMessage joinedMessage = NetworkCommands.CreateTextMessage(
                LocalPeerId,
                $"{peer.DisplayName} joined");
            _receivedMessages.Enqueue(joinedMessage);
            await BroadcastAsync(joinedMessage, cancellationToken);
            return;
        }

        if (!_members.ContainsKey(message.SenderId))
            return;

        if (message.Type == NetworkMessageType.RequestWorldData)
        {
            if (_worldDataProvider is not null)
                await SendAsync(client, _worldDataProvider(), cancellationToken);

            return;
        }

        switch (message.Type)
        {
            case NetworkMessageType.CommandToHost:
                _receivedMessages.Enqueue(message);
                break;
            case NetworkMessageType.CommandToMember:
            case NetworkMessageType.CommandToAll:
                await BroadcastAsync(message, cancellationToken);
                break;
            default:
                _receivedMessages.Enqueue(message);
                break;
        }
    }

    private async Task RejectJoinAsync(TcpClient client, string error, CancellationToken cancellationToken)
    {
        await SendAsync(client, new NetworkMessage(
            NetworkMessageType.JoinRejected,
            LocalPeerId,
            Error: error), cancellationToken);
        client.Dispose();
    }

    private static string ValidateDisplayName(string displayName)
    {
        string normalizedName = displayName.Trim();
        if (normalizedName.Length == 0)
            throw new ArgumentException("Display name cannot be empty.", nameof(displayName));

        if (normalizedName.Length > 32)
            throw new ArgumentException("Display name cannot exceed 32 characters.", nameof(displayName));

        return normalizedName;
    }

    private int StartSessionListener()
    {
        for (int port = SessionPortStart; port <= SessionPortEnd; port++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                return port;
            }
            catch (SocketException)
            {
                _listener?.Stop();
                _listener = null;
            }
        }

        throw new InvalidOperationException($"No free session port in range {SessionPortStart}-{SessionPortEnd}.");
    }

    private async Task SendCommandAsync(NetworkMessage message, CancellationToken cancellationToken)
    {
        if (IsHost)
        {
            if (message.Type == NetworkMessageType.CommandToAll)
                _receivedMessages.Enqueue(message);

            await BroadcastAsync(message, cancellationToken);
            return;
        }

        if (message.Type == NetworkMessageType.CommandToAll)
            _receivedMessages.Enqueue(message);

        await SendToServerAsync(message, cancellationToken);
    }

    private async Task SendToServerAsync(NetworkMessage message, CancellationToken cancellationToken)
    {
        if (_serverConnection is null)
            throw new InvalidOperationException("No session connection exists.");

        await SendAsync(_serverConnection, message, cancellationToken);
    }

    public async Task BroadcastAsync(NetworkMessage message, CancellationToken cancellationToken = default)
    {
        IEnumerable<NetworkPeer> recipients = message.Type == NetworkMessageType.CommandToMember
            ? _members.Values.Where(peer => peer.Id == message.TargetId)
            : _members.Values.Where(peer => peer.Id != message.SenderId);

        foreach (NetworkPeer peer in recipients)
        {
            await SendAsync(peer.Client, message, cancellationToken);
        }
    }

    private async Task SendAsync(TcpClient client, NetworkMessage message, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(message, _jsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
        await client.GetStream().WriteAsync(bytes, cancellationToken);
    }
}
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

public enum NetworkConnectionStatus { Disconnected, Hosting, Connecting, Synchronizing, Connected, Faulted }

public sealed class NetworkHandler : IDisposable
{
    public const int SessionPortStart = 27000;
    public const int SessionPortEnd = 27010;
    public const int ProtocolVersion = 6;
    public const int MaximumMessagesPerUpdate = 128;
    public const int MaximumPendingMessages = 8192;
    private readonly NetworkInbox<Inbound> _receivedMessages = new(input => input.Message, input => (input.Generation, input.Client));
    private readonly ConcurrentDictionary<TcpClient, NetworkConnection> _connections = new();
    private sealed record Inbound(long Generation, TcpClient? Client, NetworkMessage? Message,
        bool Closed = false, string? Error = null);
    private long _generation;
    private int _gameThreadId;
    private Guid? _hostPeerId;
    private Func<NetworkMessage>? _sessionSnapshotProvider;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<NetworkMessage, LocalRequestReceipt> _localRequests = new();
    private readonly Dictionary<Guid, LocalRequestReceipt> _localRejections = [];
    private readonly Dictionary<Guid, Func<NetworkMessage, bool>> _localAIPolicies = [];
    private readonly Dictionary<(Guid Actor, NetworkMessageType Type, Guid? Producer, string? Product), LocalRequestReceipt> _pendingAIPurchases = [];
    private long _receiptGeneration = -1;
    private readonly Dictionary<Guid, Func<NetworkMessage, bool>> _localAIOrderRouters = [];
    internal void SetLocalAIOrderRouter(Guid actorId, Func<NetworkMessage, bool>? router)
    {
        if (router is null) _localAIOrderRouters.Remove(actorId);
        else _localAIOrderRouters[actorId] = router;
    }
    internal bool RouteLocalAIOrder(NetworkMessage request) => IsHost &&
        _localAIOrderRouters.TryGetValue(request.SenderId, out var router) && router(request);
    internal void SetLocalAIRequestPolicy(Guid actorId, Func<NetworkMessage, bool>? policy)
    {
        if (policy is null) _localAIPolicies.Remove(actorId);
        else _localAIPolicies[actorId] = policy;
    }
    internal bool AllowLocalAIRequest(NetworkMessage request) =>
        !IsHost || !_localAIPolicies.TryGetValue(request.SenderId, out var policy) || policy(request);
    internal LocalRequestReceipt? TrackLocalRequest(NetworkMessage request)
    {
        if (!IsHost) return null;
        if (_receiptGeneration != SessionGeneration)
        {
            _pendingAIPurchases.Clear(); _localRejections.Clear();
            _receiptGeneration = SessionGeneration;
        }
        bool purchase = _localAIPolicies.ContainsKey(request.SenderId) &&
            request.Type is NetworkMessageType.TrainUnitRequest or NetworkMessageType.ResearchRequest or NetworkMessageType.BuildRequest;
        var key = LocalPurchaseKey(request);
        if (purchase && _pendingAIPurchases.TryGetValue(key, out var pending) &&
            pending.State == LocalRequestState.Pending) return pending;
        var receipt = _localRequests.GetValue(request, message => new(message, SessionGeneration));
        if (purchase) _pendingAIPurchases[key] = receipt;
        return receipt;
    }
    public LocalRequestReceipt? GetLastLocalRejection(Guid actorId) =>
        _localRejections.TryGetValue(actorId, out LocalRequestReceipt? receipt) && receipt.Generation == SessionGeneration
            && receipt.State == LocalRequestState.Rejected
            ? receipt : null;
    internal void ResolveLocalRequest(NetworkMessage request, bool accepted, string? reason = null, AIOrderFailure failure = AIOrderFailure.Validation)
    {
        if (!_localRequests.TryGetValue(request, out LocalRequestReceipt? receipt)) return;
        receipt.State = receipt.Generation != SessionGeneration ? LocalRequestState.Abandoned
            : accepted ? LocalRequestState.Accepted : LocalRequestState.Rejected;
        receipt.Reason = reason;
        receipt.Result.Set(receipt.State == LocalRequestState.Accepted ? AIOrderStatus.Accepted
            : receipt.State == LocalRequestState.Abandoned ? AIOrderStatus.Failed : AIOrderStatus.Rejected,
            receipt.State == LocalRequestState.Accepted ? AIOrderFailure.None :
            receipt.State == LocalRequestState.Abandoned ? AIOrderFailure.SessionChanged : failure, reason);
        var key = LocalPurchaseKey(request);
        if (_pendingAIPurchases.TryGetValue(key, out var pending) && ReferenceEquals(receipt, pending))
            _pendingAIPurchases.Remove(key);
        if (receipt.State == LocalRequestState.Rejected) _localRejections[request.SenderId] = receipt;
    }
    internal void AbandonLocalRequest(NetworkMessage request, string reason)
    {
        if (_localRequests.TryGetValue(request, out var abandoned))
            abandoned.Result.Set(AIOrderStatus.Failed, abandoned.Generation != SessionGeneration
                ? AIOrderFailure.SessionChanged : AIOrderFailure.Cancelled, reason);
        ResolveLocalRequest(request, false, reason);
        if (_localRequests.TryGetValue(request, out var receipt)) receipt.State = LocalRequestState.Abandoned;
    }
    private static (Guid Actor, NetworkMessageType Type, Guid? Producer, string? Product) LocalPurchaseKey(NetworkMessage request) =>
        request.Type == NetworkMessageType.BuildRequest
            ? (request.SenderId, request.Type, null, $"{request.UnitTypeId}:{request.X:R}:{request.Z:R}:{request.TargetAngleY:R}")
            : (request.SenderId, request.Type, request.UnitId, request.UnitTypeId);
    public long SessionGeneration => Volatile.Read(ref _generation);
    public NetworkConnectionStatus Status { get; private set; } = NetworkConnectionStatus.Disconnected;
    public string? LastError { get; private set; }
    public int PendingMessages => _receivedMessages.Count;
    public event Action<string>? Diagnostic;
    public void SetSessionSnapshotProvider(Func<NetworkMessage> provider) => _sessionSnapshotProvider = provider;
    public void AssertGameThread()
    {
        if (_gameThreadId != 0 && _gameThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Network gameplay processing must run on the game thread.");
    }

    private readonly ConcurrentDictionary<Guid, NetworkPeer> _members = new();
    private readonly ConcurrentDictionary<Guid, string> _peerDisplayNames = new();
    private readonly object _memberLock = new();
    private readonly JsonSerializerOptions _jsonOptions = NetworkJson.Options;
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
        Status = NetworkConnectionStatus.Hosting;
        _sessionAccepted = true;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        int port = StartSessionListener();
        _discovery.StartAdvertising(SessionName, DisplayName, port);
        _ = AcceptClientsAsync(_listener!, SessionGeneration, _cancellation.Token);
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

    public async Task JoinSessionAsync(string hostAddress, int port, CancellationToken cancellationToken = default)
    {
        Disconnect();
        DisplayName = ValidateDisplayName(DisplayName);
        Status = NetworkConnectionStatus.Connecting;
        long generation = SessionGeneration;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var connection = new TcpClient();
        _serverConnection = connection;
        try
        {
            await connection.ConnectAsync(hostAddress, port, _cancellation.Token);
            if (generation != SessionGeneration) { connection.Dispose(); return; }
            StartConnection(connection, generation, _cancellation.Token);
            await SendAsync(connection, new NetworkMessage(NetworkMessageType.JoinSession, LocalPeerId,
                DisplayName: DisplayName, ProtocolVersion: ProtocolVersion), cancellationToken);
        }
        catch (Exception error) when (error is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            connection.Dispose();
            EnqueueInbound(new(generation, connection, null, Closed: true, Error: error.Message));
        }
    }

    public Task SendCommandToHostAsync(string command, string[] arguments, CancellationToken cancellationToken = default)
    {
        NetworkMessage message = NetworkCommands.CreateCommandToHost(
            LocalPeerId,
            command,
            arguments);

        if (!AcceptComplexCommand(message)) return Task.CompletedTask;
        if (IsHost)
        {
            EnqueueLocalMessage(message);
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

    public void ApplyLocalCommand(NetworkMessage message)
    {
        AssertGameThread();
        if (AcceptComplexCommand(message)) MessageReceived?.Invoke(message);
    }

    private bool AcceptComplexCommand(NetworkMessage message)
    {
        if (ComplexCommandPayloads.TryValidate(message, out string error)) return true;
        ReportError(error);
        return false;
    }

    public void EnqueueLocalMessage(NetworkMessage message)
    {
        if (AcceptComplexCommand(message)) EnqueueInbound(new(SessionGeneration, null, message));
        else ResolveLocalRequest(message, false, "Local host admission rejected an invalid command payload.");
    }

    private void EnqueueInbound(Inbound input)
    {
        if (input.Generation != SessionGeneration) return;
        // Commands are never silently dropped. An overloaded remote connection
        // is closed and reported instead of allowing an unbounded memory backlog.
        if (!input.Closed && input.Client is not null && PendingMessages >= MaximumPendingMessages)
        {
            if (_connections.TryGetValue(input.Client, out NetworkConnection? connection)) connection.Dispose();
            return;
        }
        _receivedMessages.Enqueue(input);
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
        if (_gameThreadId == 0) _gameThreadId = Environment.CurrentManagedThreadId;
        AssertGameThread();
        long started = Stopwatch.GetTimestamp();
        for (int count = 0; count < MaximumMessagesPerUpdate; count++)
        {
            if (!_receivedMessages.TryDequeue(out Inbound? input)) break;
            if (input.Generation != SessionGeneration) continue;
            if (input.Closed) HandleClosed(input);
            else if (input.Message is NetworkMessage message)
            {
                if (input.Client is null) MessageReceived?.Invoke(message);
                else if (IsHost) HandleHostMessage(input.Client, message);
                else if (ReferenceEquals(input.Client, _serverConnection)) HandleClientMessage(message);
            }
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 4) break;
        }
    }

    public void Disconnect()
    {
        Interlocked.Increment(ref _generation);
        _cancellation?.Cancel();
        _listener?.Stop();
        foreach (NetworkConnection connection in _connections.Values) connection.Dispose();
        _connections.Clear();
        _serverConnection?.Dispose();
        _listener = null;
        _serverConnection = null;
        _sessionAccepted = false;
        _hostPeerId = null;
        _discovery.Stop();
        _members.Clear();
        _peerDisplayNames.Clear();
        _peerDisplayNames[LocalPeerId] = DisplayName;
        // Concurrent old-session readers may enqueue after this drain. Generation
        // tags also reject those entries when they are eventually consumed.
        while (_receivedMessages.TryDequeue(out _)) { }
        SessionId = null;
        IsHost = false;
        SessionName = "";
        Status = NetworkConnectionStatus.Disconnected;
        LastError = null;
    }

    public void Dispose() => Disconnect();

    private void StartConnection(TcpClient client, long generation, CancellationToken token)
    {
        if (generation != SessionGeneration) { client.Dispose(); return; }
        var connection = new NetworkConnection(client, token,
            message => EnqueueInbound(new(generation, client, message)),
            error => EnqueueInbound(new(generation, client, null, Closed: true, Error: error)));
        if (!_connections.TryAdd(client, connection)) connection.Dispose();
    }

    private async Task AcceptClientsAsync(TcpListener listener, long generation, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(token);
                StartConnection(client, generation, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is SocketException or ObjectDisposedException)
        {
            if (!token.IsCancellationRequested)
                EnqueueInbound(new(generation, null, null, Closed: true, Error: error.Message));
        }
    }

    private void HandleClosed(Inbound input)
    {
        if (input.Client is not null && _connections.TryRemove(input.Client, out NetworkConnection? transport))
            transport.Dispose();
        NetworkPeer? peer = _members.Values.FirstOrDefault(member => member.Client == input.Client);
        if (peer is not null && _members.TryRemove(peer.Id, out _))
        {
            _peerDisplayNames.TryRemove(peer.Id, out _);
            var left = new NetworkMessage(NetworkMessageType.MemberLeft, peer.Id);
            MessageReceived?.Invoke(left);
            _ = BroadcastAsync(left);
        }
        if (ReferenceEquals(input.Client, _serverConnection) && input.Client is not null)
        {
            _sessionAccepted = false;
            _serverConnection = null;
            Status = NetworkConnectionStatus.Faulted;
            SessionId = null;
            Interlocked.Increment(ref _generation); // Pending commands from this server are no longer current.
        }
        if (input.Error is not null) ReportError(input.Error);
    }

    public void ReportError(string error)
    {
        LastError = error;
        Diagnostic?.Invoke(error);
    }

    private void HandleClientMessage(NetworkMessage message)
    {
        if (message.Type == NetworkMessageType.JoinAccepted)
        {
            if (message.ProtocolVersion != ProtocolVersion)
            {
                ReportError($"Incompatible network protocol: client {ProtocolVersion}, host {message.ProtocolVersion}. Host and clients must use the same build.");
                _connections.GetValueOrDefault(_serverConnection!)?.Dispose();
                return;
            }
            _hostPeerId = message.SenderId;
            _sessionAccepted = true;
            SessionId = message.SessionId;
            Status = NetworkConnectionStatus.Synchronizing;
            if (message.DisplayName is not null) _peerDisplayNames[message.SenderId] = message.DisplayName;
            _hostTimeOffset = message.ServerTime - _localClock.Elapsed.TotalSeconds;
        }
        else if (message.Type == NetworkMessageType.JoinRejected)
        {
            _sessionAccepted = false;
            Status = NetworkConnectionStatus.Faulted;
            ReportError(message.Error ?? "Join rejected.");
        }
        else if (message.Type == NetworkMessageType.SessionReady)
        {
            Status = NetworkConnectionStatus.Connected;
        }
        if (message.Type == NetworkMessageType.MemberJoined && message.DisplayName is not null)
            _peerDisplayNames[message.SenderId] = message.DisplayName;
        if (message.Type == NetworkMessageType.MemberLeft) _peerDisplayNames.TryRemove(message.SenderId, out _);
        MessageReceived?.Invoke(message);
    }

    private void HandleHostMessage(TcpClient client, NetworkMessage message)
    {
        AssertGameThread();
        if (message.Type == NetworkMessageType.JoinSession)
        {
            string name = message.DisplayName?.Trim() ?? "";
            if (message.ProtocolVersion != ProtocolVersion)
            {
                RejectJoin(client, $"Incompatible network protocol: host {ProtocolVersion}, client {message.ProtocolVersion}. Use the same build.");
                return;
            }
            if (name.Length is < 1 or > 32 ||
                message.SenderId == Guid.Empty || message.SenderId == LocalPeerId ||
                _members.ContainsKey(message.SenderId) || _members.Values.Any(peer => peer.Client == client) ||
                string.Equals(name, DisplayName, StringComparison.OrdinalIgnoreCase) ||
                _members.Values.Any(peer => string.Equals(peer.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
            {
                RejectJoin(client, "Incompatible build, invalid identity or duplicate player name.");
                return;
            }
            try
            {
                // Capture and encode every initial message on the game thread,
                // before publishing this peer to the regular broadcast recipients.
                var initial = new List<NetworkMessage>
                {
                    new(NetworkMessageType.JoinAccepted, LocalPeerId, SessionId: SessionId,
                        DisplayName: DisplayName, ServerTime: _hostTimeProvider?.Invoke() ?? 0,
                        ProtocolVersion: ProtocolVersion)
                };
                if (_playerDataProvider is not null) initial.AddRange(_playerDataProvider());
                if (_sessionSnapshotProvider is not null) initial.Add(_sessionSnapshotProvider());
                else if (_worldDataProvider is not null) initial.Add(_worldDataProvider());
                initial.AddRange(_members.Values.Select(peer => new NetworkMessage(NetworkMessageType.MemberJoined,
                    peer.Id, DisplayName: peer.DisplayName)));
                initial.Add(new(NetworkMessageType.SessionReady, LocalPeerId));
                byte[][] frames = initial.Select(NetworkConnection.Encode).ToArray();
                if (!_connections.TryGetValue(client, out NetworkConnection? connection)) return;
                foreach (byte[] frame in frames) if (!connection.TrySend(frame)) return;
                _members[message.SenderId] = new NetworkPeer(message.SenderId, name, client);
                _peerDisplayNames[message.SenderId] = name;
                var joined = new NetworkMessage(NetworkMessageType.MemberJoined, message.SenderId, DisplayName: name);
                MessageReceived?.Invoke(joined);
                _ = BroadcastAsync(joined);
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or JsonException or NotSupportedException)
            {
                ReportError($"Could not capture the joining player's state: {error.Message}");
                RejectJoin(client, "The host could not create a consistent game snapshot.");
            }
            return;
        }
        NetworkPeer? sender = _members.Values.FirstOrDefault(peer => ReferenceEquals(peer.Client, client));
        if (sender is null || sender.Id != message.SenderId) return;
        if (message.Type == NetworkMessageType.RequestWorldData)
        {
            if (_sessionSnapshotProvider is not null) _ = SendAsync(client, _sessionSnapshotProvider());
            else if (_worldDataProvider is not null) _ = SendAsync(client, _worldDataProvider());
            return;
        }
        if (message.Type is NetworkMessageType.CommandToMember or NetworkMessageType.CommandToAll)
        {
            _ = BroadcastAsync(message);
            if (message.Type == NetworkMessageType.CommandToAll) MessageReceived?.Invoke(message);
            return;
        }
        // Clients issue requests, never confirmations. Actor identity comes from the connection.
        if (message.Type.ToString().EndsWith("Request", StringComparison.Ordinal) ||
            message.Type is NetworkMessageType.CommandToHost or NetworkMessageType.RequestPlayerUpdate or NetworkMessageType.NotifyUnitsSelected)
            MessageReceived?.Invoke(message with { PlayerId = sender.Id });
    }

    private void RejectJoin(TcpClient client, string error)
    {
        _ = SendAsync(client, new NetworkMessage(NetworkMessageType.JoinRejected, LocalPeerId, Error: error));
        if (_connections.TryGetValue(client, out NetworkConnection? connection)) connection.FinishSending();
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
                EnqueueLocalMessage(message);

            await BroadcastAsync(message, cancellationToken);
            return;
        }

        if (message.Type == NetworkMessageType.CommandToAll)
            EnqueueLocalMessage(message);

        await SendToServerAsync(message, cancellationToken);
    }

    private async Task SendToServerAsync(NetworkMessage message, CancellationToken cancellationToken)
    {
        if (_serverConnection is null)
            throw new InvalidOperationException("No session connection exists.");

        await SendAsync(_serverConnection, message, cancellationToken);
    }

    /// <summary>Completes when queued, not when a remote peer has received the message.</summary>
    public Task BroadcastAsync(NetworkMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] frame = NetworkConnection.Encode(message);
        IEnumerable<NetworkPeer> recipients = message.Type == NetworkMessageType.CommandToMember
            ? _members.Values.Where(peer => peer.Id == message.TargetId)
            : _members.Values.Where(peer => peer.Id != message.SenderId);
        foreach (NetworkPeer peer in recipients)
            if (_connections.TryGetValue(peer.Client, out NetworkConnection? connection)) connection.TrySend(frame);
        return Task.CompletedTask;
    }

    private Task SendAsync(TcpClient client, NetworkMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_connections.TryGetValue(client, out NetworkConnection? connection))
            connection.TrySend(NetworkConnection.Encode(message));
        return Task.CompletedTask;
    }
}

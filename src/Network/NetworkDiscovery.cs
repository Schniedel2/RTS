using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RTS.Network;

public sealed class NetworkDiscovery : IDisposable
{
    public const int DiscoveryPort = 27011;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private UdpClient? _listener;
    private CancellationTokenSource? _cancellation;

    public void StartAdvertising(string sessionName, string hostName, int port)
    {
        Stop();
        _cancellation = new CancellationTokenSource();
        _listener = CreateListener();
        _ = ListenForDiscoveryAsync(sessionName, hostName, port, _cancellation.Token);
    }

    public async Task<IReadOnlyList<SessionInfo>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using UdpClient client = new();
        client.EnableBroadcast = true;

        byte[] request = Serialize(new DiscoveryMessage(DiscoveryMessageType.Discover));
        await client.SendAsync(request, request.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

        DateTime deadline = DateTime.UtcNow + timeout;
        Dictionary<string, SessionInfo> sessions = new(StringComparer.OrdinalIgnoreCase);

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            int waitMilliseconds = Math.Max(1, (int)Math.Min(remaining.TotalMilliseconds, 250));
            Task<UdpReceiveResult> receiveTask = client.ReceiveAsync(cancellationToken).AsTask();
            Task completedTask = await Task.WhenAny(receiveTask, Task.Delay(waitMilliseconds, cancellationToken));
            if (completedTask != receiveTask)
                continue;

            DiscoveryMessage? response = Deserialize(await receiveTask);
            if (response?.Type != DiscoveryMessageType.Advertise ||
                string.IsNullOrWhiteSpace(response.SessionName) ||
                string.IsNullOrWhiteSpace(response.HostName) ||
                response.Port <= 0)
                continue;

            string address = ((IPEndPoint)(await receiveTask).RemoteEndPoint).Address.ToString();
            SessionInfo session = new(response.SessionName, response.HostName, address, response.Port);
            sessions[$"{session.SessionName}\u0000{session.Address}"] = session;
        }

        return sessions.Values.OrderBy(session => session.SessionName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _listener?.Dispose();
        _cancellation = null;
        _listener = null;
    }

    public void Dispose() => Stop();

    private async Task ListenForDiscoveryAsync(string sessionName, string hostName, int port, CancellationToken cancellationToken)
    {
        if (_listener is null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult request = await _listener.ReceiveAsync(cancellationToken);
                DiscoveryMessage? message = Deserialize(request.Buffer);
                if (message?.Type != DiscoveryMessageType.Discover)
                    continue;

                byte[] response = Serialize(new DiscoveryMessage(
                    DiscoveryMessageType.Advertise,
                    sessionName,
                    hostName,
                    port));
                await _listener.SendAsync(response, response.Length, request.RemoteEndPoint);
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

    private static UdpClient CreateListener()
    {
        UdpClient client = new(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        return client;
    }

    private byte[] Serialize(DiscoveryMessage message)
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, _jsonOptions));
    }

    private DiscoveryMessage? Deserialize(UdpReceiveResult result)
    {
        return Deserialize(result.Buffer);
    }

    private DiscoveryMessage? Deserialize(byte[] data)
    {
        try
        {
            return JsonSerializer.Deserialize<DiscoveryMessage>(data, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
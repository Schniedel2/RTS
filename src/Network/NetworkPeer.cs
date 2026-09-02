using System;
using System.Net.Sockets;

namespace RTS.Network;

public sealed class NetworkPeer
{
    internal NetworkPeer(Guid id, string displayName, TcpClient client)
    {
        Id = id;
        DisplayName = displayName;
        Client = client;
    }

    public Guid Id { get; }
    public string DisplayName { get; }
    internal TcpClient Client { get; }
}
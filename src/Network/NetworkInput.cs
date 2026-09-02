using System;

namespace RTS.Network;

public sealed class NetworkInput
{
    public NetworkInput(NetworkHandler networkHandler)
    {
        networkHandler.MessageReceived += OnMessageReceived;
    }

    public event Action<NetworkMessage>? MessageReceived;

    private void OnMessageReceived(NetworkMessage message)
    {
        MessageReceived?.Invoke(message);
    }
}
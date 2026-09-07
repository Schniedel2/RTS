using System;
using System.Threading;
using System.Threading.Tasks;
using RTS.Network;

namespace RTS;

public sealed class Player
{
    public Guid Id { get; }
    public string Name { get; private set; }
    public int TeamId { get; private set; }

    public Player(Guid id, string name, int teamId = 0)
    {
        Id = id;
        Name = name;
        TeamId = teamId;
    }

    public void SetRequestedData(string name, int teamId)
    {
        Name = name;
        TeamId = teamId;
    }

    public Task RequestUpdateAsync(
        NetworkClient networkClient,
        CancellationToken cancellationToken = default)
    {
        return networkClient.RequestPlayerUpdateAsync(this, cancellationToken);
    }
}

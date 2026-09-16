using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using RTS.Network;

namespace RTS;

public sealed class Player
{
    public Guid Id { get; }
    public string Name { get; private set; }
    public int TeamId { get; private set; }
    /// <summary>Initial armies use the player ID, so every peer derives the same ID.</summary>
    public Guid ArmyId { get; private set; }
    public PlayerSkin Skin { get; private set; }

    public Player(Guid id, string name, int teamId = 0, PlayerSkin skin = PlayerSkin.Green, Guid? armyId = null)
    {
        Id = id;
        Name = name;
        TeamId = teamId;
        Skin = skin;
        ArmyId = armyId ?? id;
    }

    public void SetRequestedData(string name, int teamId, PlayerSkin skin)
    {
        Name = name;
        TeamId = teamId;
        Skin = skin;
    }

    internal void SetArmy(Guid armyId) => ArmyId = armyId;

    public Task RequestUpdateAsync(
        NetworkClient networkClient,
        CancellationToken cancellationToken = default)
    {
        return networkClient.RequestPlayerUpdateAsync(this, cancellationToken);
    }

    /// <summary>Asks the host for a skin; the host confirms it through the regular player update.</summary>
    public Task RequestSkinAsync(
        NetworkClient networkClient,
        PlayerSkin skin,
        CancellationToken cancellationToken = default)
    {
        Skin = skin;
        return RequestUpdateAsync(networkClient, cancellationToken);
    }
}

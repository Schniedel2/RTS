using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using RTS.Network;

namespace RTS;

public sealed class Player
{
    /// <summary>Colors the host hands out when a requested color is unavailable.</summary>
    public static readonly Color[] ColorPalette =
    [
        Color.Red,
        Color.RoyalBlue,
        Color.LimeGreen,
        Color.Yellow,
        Color.Orange,
        Color.MediumPurple,
        Color.Cyan,
        Color.White
    ];

    public Guid Id { get; }
    public string Name { get; private set; }
    public int TeamId { get; private set; }
    public Color Color { get; private set; }

    public Player(Guid id, string name, int teamId = 0, Color? color = null)
    {
        Id = id;
        Name = name;
        TeamId = teamId;
        Color = color ?? ColorPalette[0];
    }

    public static Color ColorFromPacked(uint packedValue)
    {
        return new Color { PackedValue = packedValue };
    }

    public void SetRequestedData(string name, int teamId, Color color)
    {
        Name = name;
        TeamId = teamId;
        Color = color;
    }

    public Task RequestUpdateAsync(
        NetworkClient networkClient,
        CancellationToken cancellationToken = default)
    {
        return networkClient.RequestPlayerUpdateAsync(this, cancellationToken);
    }

    /// <summary>Asks the host for a color; the host confirms it through the regular player update.</summary>
    public Task RequestColorAsync(
        NetworkClient networkClient,
        Color color,
        CancellationToken cancellationToken = default)
    {
        Color = color;
        return RequestUpdateAsync(networkClient, cancellationToken);
    }
}

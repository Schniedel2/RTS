using Microsoft.Xna.Framework;
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
        HandleNetworkMessage(message);
    }

    private void HandleNetworkMessage(NetworkMessage message)
    {
        if (message.Type == NetworkMessageType.JoinRejected)
        {
            Globals.Console.Print($"Session join rejected: {message.Error ?? "Unknown reason"}");
            return;
        }

        if (message.Type == NetworkMessageType.JoinAccepted)
        {
            Globals.Console.Print($"Joined session as {Globals.Game.Network.DisplayName}.");
            return;
        }

        if (message.Type == NetworkMessageType.TextMessage)
        {
            if (message.TargetId is not null &&
                message.TargetId != Globals.Game.Network.LocalPeerId)
                return;

            if (!string.IsNullOrWhiteSpace(message.Text))
                Globals.Console.Print(message.Text);

            return;
        }

        if (message.Type == NetworkMessageType.SpawnCommand)
        {
            Guid unitId = message.UnitId ?? Guid.NewGuid();
            if (message.PlayerId is Guid playerId && message.UnitTypeId is not null)
                SpawnLocally(message.UnitTypeId, playerId, unitId, message.X, message.Y, message.Z);

            return;
        }

        if (message.Type == NetworkMessageType.GotoCommand)
        {
            ExecuteGoto(message);
            return;
        }

        if (message.Type == NetworkMessageType.ToolActionCommand)
        {
            ExecuteToolAction(message);
            return;
        }
    }

    private void SpawnLocally(string unitTypeId, Guid playerId, Guid unitId, float x, float y, float z)
    {
        Vector3 target = new(x, z, y);
        Globals.World.Markers.ShowGotoMarker(target);

        Globals.World.Units.SpawnUnit(unitTypeId, target, unitId);

        string playerName = Globals.Game.Network.GetPeerDisplayName(playerId);
        Globals.Console.Print($"Spawned {unitTypeId} for player {playerName}.");
    }

    private void ExecuteGoto(NetworkMessage message)
    {
        GotoCommand command = new(new Vector2(message.X, message.Z));

        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
        {
            Unit? unit = Globals.World.Units.FindById(unitId);
            unit?.TryReceiveGotoCommand(Globals.World, command);
        }

        Globals.Game.World.Markers.ShowGotoMarker(new Vector3(message.X, message.Y, message.Z));
    }

    private void ExecuteToolAction(NetworkMessage message)
    {
        switch (message.Action)
        {
            case UnitActionType.RaiseTerrain:
                // Handle RaiseTerrain action
                TerrainHelper.RaiseTerrain(Globals.World.Terrain, message.X, message.Z, message.ToolShape ?? ToolShape.Circle, message.ToolSize, 0.5f);
                break;
            case UnitActionType.FlattenTerrain:
                TerrainHelper.FlattenTerrain(Globals.World.Terrain, message.X, message.Z, message.Y, message.ToolShape ?? ToolShape.Circle, message.ToolSize, 0.5f);
                // Handle FlattenTerrain action
                break;
            case UnitActionType.SmoothTerrain:
                // Handle SmoothTerrain action
                TerrainHelper.SmoothTerrain(Globals.World.Terrain, message.X, message.Z, message.ToolShape ?? ToolShape.Circle, message.ToolSize, 0.5f);
                break;
            case UnitActionType.LowerTerrain:
                TerrainHelper.RaiseTerrain(Globals.World.Terrain, message.X, message.Z, message.ToolShape ?? ToolShape.Circle, message.ToolSize, -0.5f);
                break;
            case UnitActionType.SetTerrainTile:
                if (message.TerrainTile is not { } tile)
                    break;
                TerrainHelper.SetTile(Globals.World.Terrain, message.X, message.Z, message.ToolShape ?? ToolShape.Circle, message.ToolSize, tile);
                break;
            default:
                // Handle other actions or do nothing
                break;
        }
    }
    
}
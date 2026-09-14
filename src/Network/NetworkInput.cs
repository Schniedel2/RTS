using Microsoft.Xna.Framework;
using System;
using System.Linq;

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
        if (Globals.Debug_ShowNetworkMessages)
            Globals.Console.Print(FormatDebugMessage(message));

        MessageReceived?.Invoke(message);
        HandleNetworkMessage(message);
    }

    private static string FormatDebugMessage(NetworkMessage message)
    {
        string unitIds = message.UnitIds is { Length: > 0 }
            ? $" units={string.Join(',', message.UnitIds.Select(id => id.ToString("N")[..8]))}"
            : "";
        string unitId = message.UnitId is Guid id ? $" unit={id.ToString("N")[..8]}" : "";
        string targetId = message.TargetId is Guid target ? $" target={target.ToString("N")[..8]}" : "";
        string position = message.X != 0.0f || message.Y != 0.0f || message.Z != 0.0f
            ? $" pos=({message.X:0.0},{message.Y:0.0},{message.Z:0.0})"
            : "";
        return $"[NET] {message.Type} from={message.SenderId.ToString("N")[..8]}{unitId}{unitIds}{targetId}{position}";
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
            _ = Globals.Game.Players[0].RequestUpdateAsync(Globals.Game.NetworkClient);
            return;
        }

        if (message.Type == NetworkMessageType.PlayerUpdate &&
            message.PlayerId is Guid updatedPlayerId &&
            !string.IsNullOrWhiteSpace(message.DisplayName))
        {
            Globals.Game.UpdatePlayer(
                updatedPlayerId,
                message.DisplayName,
                message.TeamId,
                (PlayerSkin)(message.PlayerSkin ?? (int)PlayerSkin.Green));
            return;
        }

        if (message.Type == NetworkMessageType.MemberLeft)
        {
            Globals.Game.RemovePlayer(message.SenderId);
            return;
        }

        if (message.Type == NetworkMessageType.WorldData && message.WorldData is not null)
        {
            Globals.World.Terrain.ApplyWorldData(message.WorldData);
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
                SpawnUnitLocally(message.UnitTypeId, playerId, unitId, message.X, message.Y, message.Z);

            return;
        }

        if (message.Type == NetworkMessageType.BuildCommand)
        {
            Guid unitId = message.UnitId ?? Guid.NewGuid();
            if (message.PlayerId is Guid playerId && message.UnitTypeId is not null)
                SpawnBuildingLocally(message.UnitTypeId, playerId, unitId, message.X, message.Y, message.Z);

            return;
        }

        if (message.Type == NetworkMessageType.GotoCommand)
        {
            ExecuteGoto(message);
            return;
        }

        if (message.Type == NetworkMessageType.StopCommand)
        {
            foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
                Globals.World.Units.FindById(unitId)?.Stop();
            return;
        }

        if (message.Type == NetworkMessageType.AttackCommand)
        {
            ExecuteAttack(message);
            return;
        }

        if (message.Type == NetworkMessageType.AttackTargetCommand)
        {
            ExecuteAttackTarget(message);
            return;
        }

        if (message.Type == NetworkMessageType.AttackGroundCommand)
        {
            ExecuteAttackGround(message);
            return;
        }

        if (message.Type == NetworkMessageType.FollowCommand)
        {
            ExecuteFollow(message);
            return;
        }

        if (message.Type == NetworkMessageType.TemporaryTargetCommand)
        {
            ExecuteTemporaryTarget(message);
            return;
        }

        if (message.Type == NetworkMessageType.UnitHitCommand)
        {
            ApplyHit(message);
            return;
        }

        if (message.Type == NetworkMessageType.DestroyUnitCommand)
        {
            DestroyUnit(message);
            return;
        }

        if (message.Type == NetworkMessageType.BuildConstructionCommand)
        {
            ExecuteBuildConstruction(message);
            return;
        }

        if (message.Type == NetworkMessageType.UnitStateCommand)
        {
            ApplyUnitState(message);
            return;
        }

        if (message.Type == NetworkMessageType.ToolActionCommand)
        {
            ExecuteToolAction(message);
            return;
        }
    }

    private void SpawnUnitLocally(string unitTypeId, Guid playerId, Guid unitId, float x, float y, float z)
    {
        Vector3 target = new(x, y, z);
        Globals.World.Units.SpawnUnit(unitTypeId, target, unitId, playerId);

        string playerName = Globals.Game.Network.GetPeerDisplayName(playerId);
        Globals.Console.Print($"Spawned {unitTypeId} for player {playerName}.");
    }

    private void SpawnBuildingLocally(string buildingTypeId, Guid playerId, Guid unitId, float x, float y, float z)
    {
        Vector3 target = new(x, y, z);
        Globals.World.Units.SpawnBuilding(buildingTypeId, target, unitId, playerId);

        string playerName = Globals.Game.Network.GetPeerDisplayName(playerId);
        Globals.Console.Print($"Spawned {buildingTypeId} for player {playerName}.");
    }

    private void ExecuteGoto(NetworkMessage message)
    {
        GotoCommand command = new(new Vector2(message.X, message.Z));

        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
        {
            MobileUnit? unit = Globals.World.Units.FindMobileUnitById(unitId);
            unit?.ClearFollowUnit();
            unit?.TryReceiveGotoCommand(Globals.World, command);
        }

        Globals.Game.World.Markers.ShowGotoMarker(new Vector3(message.X, message.Y, message.Z));
    }

    private void ExecuteAttack(NetworkMessage message)
    {
        Vector3 target = new(message.X, message.Y, message.Z);
        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
        {
            if (Globals.World.Units.FindById(unitId) is not Unit attacker)
                continue;

            attacker.PlayShotEffects();
            if (!attacker.TryGetMuzzleWorldPosition(out Vector3 start))
                start = attacker.Position + Vector3.Up * (attacker.Height * 0.75f);
            Globals.World.Projectiles.Fire(start, target);
        }
    }

    private void ExecuteAttackTarget(NetworkMessage message)
    {
        if (message.TargetId is not Guid targetId)
            return;
        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
            if (Globals.World.Units.FindById(unitId) is Unit unit)
                unit.SetAttackTarget(targetId);
    }

    private void ExecuteAttackGround(NetworkMessage message)
    {
        Vector3 target = new(message.X, message.Y, message.Z);
        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
            if (Globals.World.Units.FindById(unitId) is Unit unit)
                unit.SetAttackGroundTarget(target);
    }

    private void ExecuteFollow(NetworkMessage message)
    {
        if (message.TargetId is not Guid targetId)
            return;
        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
            if (Globals.World.Units.FindById(unitId) is Unit unit)
                unit.SetFollowUnit(targetId);
    }

    private void ExecuteTemporaryTarget(NetworkMessage message)
    {
        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
        {
            Unit? unit = Globals.World.Units.FindById(unitId);
            if (unit is null)
                continue;

            if (message.TargetId is Guid targetId)
                unit.SetTemporaryTarget(targetId);
            else
                unit.ClearTemporaryTarget();
        }
    }

    private void ApplyHit(NetworkMessage message)
    {
        if (message.UnitId is not Guid unitId ||
            Globals.World.Units.FindById(unitId) is not Unit unit)
            return;

        unit.ApplyHitPoints(message.HitPoints);
    }

    private void DestroyUnit(NetworkMessage message)
    {
        if (message.UnitId is not Guid unitId ||
            Globals.World.Units.FindById(unitId) is not Unit unit)
            return;

        Globals.World.Particles.EmitExplosion(unit.Position + Vector3.Up);
        Globals.World.Units.Destroy(unitId);
    }

    private void ExecuteBuildConstruction(NetworkMessage message)
    {
        if (message.ConstructionSiteId is not Guid constructionSiteId ||
            Globals.World.Units.FindById(constructionSiteId) is not Building constructionSite)
            return;

        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
        {
            Unit? unit = Globals.World.Units.FindById(unitId);
            unit?.ClearFollowUnit();
            Unit? genericUnit = unit as Unit;
            (genericUnit as MobileUnit)?.TryReceiveBuildConstructionCommand(Globals.World, constructionSite);
        }
    }

    private void ApplyUnitState(NetworkMessage message)
    {
        if (message.UnitState is not { } state)
            return;

        Unit? unit = Globals.World.Units.FindById(state.UnitId);
        unit?.ApplyState(state);
    }

    private void ExecuteToolAction(NetworkMessage message)
    {
        if (message.Action is null)
            return;

        switch (message.Action.Type)
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

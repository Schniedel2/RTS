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

        if (message.Type == NetworkMessageType.NotifyUnitsSelected && message.PlayerId is Guid selectedPlayerId)
        {
            if (selectedPlayerId != Globals.Game.Network.LocalPeerId)
                Globals.Game.RemoteSelections.SetSelection(selectedPlayerId, message.UnitIds ?? Array.Empty<Guid>());
            return;
        }

        if (message.Type is NetworkMessageType.GrantArmyControlCommand or NetworkMessageType.RevokeArmyControlCommand)
        {
            ApplyArmyControl(message, message.Type == NetworkMessageType.GrantArmyControlCommand);
            return;
        }

        if (message.Type == NetworkMessageType.TransferUnitCommand)
        {
            if (message.UnitId is Guid unitId && message.ArmyId is Guid armyId && armyId != Guid.Empty)
            {
                Unit? transferredUnit = Globals.World.Units.FindById(unitId);
                transferredUnit?.SetArmy(armyId);
                if (transferredUnit?.Occupancy is OccupancyComponent occupancy)
                    foreach (OccupantAssignment occupant in occupancy.Occupants)
                        Globals.World.Units.FindById(occupant.UnitId)?.SetArmy(armyId);
            }
            return;
        }

        if (message.Type == NetworkMessageType.MergeArmiesCommand)
        {
            ApplyArmyMerge(message);
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
            {
                if (message.SpawnSourceBuildingId is Guid sourceBuildingId)
                {
                    SpawnProducedUnitLocally(
                        message.UnitTypeId,
                        playerId,
                        unitId,
                        sourceBuildingId,
                        message.ArmyId,
                        new Vector3(message.X, message.Y, message.Z),
                        new Vector3(message.ExitX, message.ExitY, message.ExitZ),
                        message.TargetAngleY,
                        message.DriverUnitId);
                }
                else
                {
                    SpawnUnitLocally(
                        message.UnitTypeId,
                        playerId,
                        unitId,
                        message.X,
                        message.Y,
                        message.Z,
                        message.TargetAngleY,
                        message.DriverUnitId);
                }
            }

            return;
        }

        if (message.Type == NetworkMessageType.TrainUnitCommand)
        {
            if (message.UnitId is Guid buildingId &&
                message.ProductionOrderId is Guid orderId &&
                message.PlayerId is Guid playerId &&
                message.UnitTypeId is not null &&
                Globals.World.Units.FindById(buildingId) is Building building)
            {
                building.TryQueueProduction(
                    orderId,
                    message.UnitTypeId,
                    playerId,
                    message.ProductionSeconds);
            }
            return;
        }

        if (message.Type == NetworkMessageType.BuildCommand)
        {
            Guid unitId = message.UnitId ?? Guid.NewGuid();
            if (message.PlayerId is Guid playerId && message.UnitTypeId is not null)
                SpawnBuildingLocally(message.UnitTypeId, playerId, unitId, message.X, message.Y, message.Z, message.TargetAngleY);

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

        if (message.Type == NetworkMessageType.BulletImpactCommand)
        {
            ExecuteBulletImpact(message);
            return;
        }

        if (message.Type == NetworkMessageType.ProjectileSpawnCommand)
        {
            ExecuteProjectileSpawn(message);
            return;
        }

        if (message.Type == NetworkMessageType.ProjectileImpactCommand)
        {
            ExecuteProjectileImpact(message);
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

        if (message.Type == NetworkMessageType.EnterUnitCommand)
        {
            ExecuteEnterUnit(message);
            return;
        }

        if (message.Type == NetworkMessageType.EmbarkUnitCommand)
        {
            if (message.UnitId is Guid occupantId && message.TargetId is Guid containerId)
                Globals.World.Units.EmbarkUnit(occupantId, containerId, message.OccupantRole);
            return;
        }

        if (message.Type == NetworkMessageType.LeaveContainerCommand)
        {
            if (message.UnitId is Guid containerId && message.TargetId is Guid occupantId)
            {
                Globals.World.Units.DisembarkUnit(
                    containerId,
                    occupantId,
                    new Vector3(message.X, message.Y, message.Z));
            }
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

    private void SpawnUnitLocally(
        string unitTypeId,
        Guid playerId,
        Guid unitId,
        float x,
        float y,
        float z,
        float targetAngleY,
        Guid? driverUnitId)
    {
        Vector3 target = new(x, y, z);
        Globals.World.Units.SpawnUnit(
            unitTypeId,
            target,
            targetAngleY,
            unitId,
            playerId,
            driverUnitId);

        string playerName = Globals.Game.Network.GetPeerDisplayName(playerId);
        Globals.Console.Print($"Spawned {unitTypeId} for player {playerName}.");
    }

    private void SpawnProducedUnitLocally(
        string unitTypeId,
        Guid playerId,
        Guid unitId,
        Guid sourceBuildingId,
        Guid? armyId,
        Vector3 spawnPosition,
        Vector3 exitPosition,
        float targetAngleY,
        Guid? driverUnitId)
    {
        MobileUnit? unit = Globals.World.Units.SpawnUnitFromBuilding(
            unitTypeId,
            spawnPosition,
            exitPosition,
            targetAngleY,
            unitId,
            playerId,
            armyId,
            sourceBuildingId,
            driverUnitId);
        if (unit is null)
            return;

        string playerName = Globals.Game.Network.GetPeerDisplayName(playerId);
        Globals.Console.Print($"Produced {unitTypeId} for player {playerName}.");
    }

    private static void ExecuteEnterUnit(NetworkMessage message)
    {
        if (message.UnitId is not Guid occupantId ||
            message.TargetId is not Guid containerId ||
            Globals.World.Units.FindById(occupantId) is not MobileUnit occupant ||
            Globals.World.Units.FindById(containerId) is not Unit container ||
            container.Occupancy is not OccupancyComponent occupancy)
        {
            return;
        }

        if (!occupancy.TryReserve(occupant, message.OccupantRole, out _))
            return;

        if (!occupant.TryReceiveEnterUnitCommand(Globals.World, container))
            occupancy.ClearReservation(occupant.UnitId);
    }

    private void SpawnBuildingLocally(string buildingTypeId, Guid playerId, Guid unitId, float x, float y, float z, float targetAngleY)
    {
        Vector3 target = new(x, y, z);
        Globals.World.Units.SpawnBuilding(buildingTypeId, target, targetAngleY, unitId, playerId);

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

    private static void ApplyArmyControl(NetworkMessage message, bool grant)
    {
        if (message.ArmyId is not Guid armyId || message.PlayerId is not Guid ownerId || message.TargetId is not Guid recipientId)
            return;
        if (grant)
            Globals.Game.Armies.GrantCommandUnits(armyId, ownerId, recipientId);
        else
            Globals.Game.Armies.RevokeCommandUnits(armyId, ownerId, recipientId);
    }

    private static void ApplyArmyMerge(NetworkMessage message)
    {
        if (message.ArmyId is not Guid firstArmyId || message.SecondaryArmyId is not Guid secondArmyId || message.TargetId is not Guid mergedArmyId)
            return;
        Army? merged = Globals.Game.Armies.Merge(firstArmyId, secondArmyId, mergedArmyId);
        if (merged is null)
            return;
        foreach (Player player in Globals.Game.Players.Where(player => player.ArmyId is var armyId && (armyId == firstArmyId || armyId == secondArmyId)))
            player.SetArmy(merged.Id);
        foreach (Unit unit in Globals.World.Units.Units.Where(unit => unit.ArmyId == firstArmyId || unit.ArmyId == secondArmyId))
            unit.SetArmy(merged.Id);
    }

    private void ExecuteAttack(NetworkMessage message)
    {
        Vector3 target = new(message.X, message.Y, message.Z);
        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
        {
            if (Globals.World.Units.FindById(unitId) is not Unit attacker)
                continue;

            // The exact launch pitch of a rocket is contained in the
            // following ProjectileSpawnCommand. Defer its local firing effect
            // until that vector has been applied to the unit.
            if (attacker.ProjectileKind == ProjectileKind.Rocket)
                continue;

            attacker.PlayShotEffects();
            if (attacker.UsesHitscanWeapon)
                continue;
            if (!attacker.TryGetProjectileLaunchWorldTransform(out Matrix launchTransform))
                launchTransform = Matrix.CreateTranslation(
                    attacker.Position + Vector3.Up * (attacker.Height * 0.75f));
            Vector3 start = launchTransform.Translation;
            if (start == Vector3.Zero)
                start = attacker.Position + Vector3.Up * (attacker.Height * 0.75f);
            Globals.World.Projectiles.Fire(
                start,
                target,
                attacker.ProjectileKind,
                attacker.ProjectileSpeed);
        }
    }

    private static void ExecuteBulletImpact(NetworkMessage message)
    {
        Globals.World.Particles.EmitBulletImpact(new Vector3(message.X, message.Y, message.Z));
    }

    private static void ExecuteProjectileSpawn(NetworkMessage message)
    {
        if (message.ProjectileId is not Guid projectileId ||
            message.ProjectileKind is not ProjectileKind kind)
            return;

        Vector3 velocity = new(
            message.VelocityX,
            message.VelocityY,
            message.VelocityZ);

        // This is the exact launch vector calculated by the host, including
        // any ballistic elevation correction. Make its pitch available to the
        // firing unit before this frame is drawn.
        if (message.UnitId is Guid attackerId &&
            Globals.World.Units.FindById(attackerId) is Unit attacker)
        {
            attacker.SetProjectileLaunchVelocity(velocity);
            if (kind == ProjectileKind.Rocket)
                attacker.PlayShotEffects();
        }

        Globals.World.Projectiles.SpawnReplicated(
            projectileId,
            new Vector3(message.X, message.Y, message.Z),
            velocity,
            kind);
    }

    private static void ExecuteProjectileImpact(NetworkMessage message)
    {
        if (message.ProjectileId is not Guid projectileId)
            return;
        Globals.World.Projectiles.ApplyAuthoritativeImpact(
            projectileId,
            new Vector3(message.X, message.Y, message.Z));
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

        if (unit.HasDeathExplosion)
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

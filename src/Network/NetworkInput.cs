using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace RTS.Network;

public sealed class NetworkInput : IDisposable
{
    private readonly NetworkHandler _network;
    private readonly GameWorld? _world;
    private readonly ArmyHandler? _armies;
    private readonly SessionStateService? _sessionState;
    private GameWorld World => _world ?? Globals.World;
    private ArmyHandler Armies => _armies ?? Globals.Game.Armies;
    public NetworkInput(NetworkHandler networkHandler, GameWorld? world = null, ArmyHandler? armies = null,
        SessionStateService? sessionState = null)
    {
        _network = networkHandler;
        _world = world;
        _armies = armies;
        _sessionState = sessionState;
        if (world is { GraphicsEnabled: false } && armies is not null)
            world.ConfigureSimulation(armies, networkHandler);
        networkHandler.MessageReceived += OnMessageReceived;
    }

    public void Dispose() => _network.MessageReceived -= OnMessageReceived;

    public event Action<NetworkMessage>? MessageReceived;

    private void OnMessageReceived(NetworkMessage message)
    {
        if (!ComplexCommandPayloads.TryValidate(message, out _)) return;
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
            if (World.GraphicsEnabled)
            {
                Globals.Console.Print($"Joined session as {_network.DisplayName}.");
                _ = Globals.Game.Players[0].RequestUpdateAsync(Globals.Game.NetworkClient);
            }
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
            if (World.GraphicsEnabled) Globals.Game.RemovePlayer(message.SenderId);
            return;
        }

        if (message.Type == NetworkMessageType.SessionSnapshot &&
            message.SessionSnapshot is SessionSnapshot snapshot)
        {
            if (World.GraphicsEnabled) Globals.Game.ApplySessionSnapshot(snapshot);
            else (_sessionState ?? new SessionStateService(World, Armies)).Apply(snapshot);
            return;
        }

        if (message.Type == NetworkMessageType.SessionReady)
            return;

        if (message.Type == NetworkMessageType.SatelliteReconCommand)
        {
            if (_network.IsHost && message.SenderId != _network.LocalPeerId) return;
            if (message.ArmyId is Guid id && Armies.Find(id) is Army army &&
                message.SatelliteRecon is SatelliteReconState state && SatelliteRecon.Valid(state))
            {
                bool wasActive = army.SatelliteRecon.ActiveSeconds > 0;
                army.SatelliteRecon = state;
                if (wasActive != (state.ActiveSeconds > 0)) World.Visibility.Update();
            }
            return;
        }

        if (message.Type == NetworkMessageType.ExploredVisibilityCommand)
        {
            if (!_network.IsHost)
                World.Visibility.ApplyAuthoritativeExplored(message.ExploredVisibility);
            return;
        }

        if (message.Type == NetworkMessageType.NotifyUnitsSelected && message.PlayerId is Guid selectedPlayerId)
        {
            if (selectedPlayerId != _network.LocalPeerId)
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
                Unit? transferredUnit = World.Units.FindById(unitId);
                transferredUnit?.SetArmy(armyId);
                if (transferredUnit?.Occupancy is OccupancyComponent occupancy)
                    foreach (OccupantAssignment occupant in occupancy.Occupants)
                        World.Units.FindById(occupant.UnitId)?.SetArmy(armyId);
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
            World.ApplyWorldData(message.WorldData);
            return;
        }

        if (message.Type == NetworkMessageType.TextMessage)
        {
            if (message.TargetId is not null &&
                message.TargetId != _network.LocalPeerId)
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
                        message.DriverUnitId,
                        message.RallyPoint);
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

        if (message.Type is NetworkMessageType.EarthworkStartCommand or NetworkMessageType.EarthworkCellCommand or NetworkMessageType.EarthworkEndCommand)
        {
            if (_network.IsHost && message.SenderId != _network.LocalPeerId) return;
            if (message.UnitId is not Guid id || World.Units.FindById(id) is not GDIBulldozer worker) return;
            if (message.Type == NetworkMessageType.EarthworkStartCommand && message.EarthworkOrder is EarthworkOrder order)
                worker.BeginEarthwork(order);
            else if (message.Type == NetworkMessageType.EarthworkCellCommand && message.EarthworkOrderId is Guid orderId)
            {
                if (message.EarthworkCells is int[] cells)
                    worker.ApplyEarthworkDrive(World, orderId, message.EarthworkSequence, cells, new(message.X, message.Z));
                else worker.ApplyEarthworkCell(World, orderId, message.EarthworkSequence, new(message.CellX, message.CellZ));
            }
            else if (message.Type == NetworkMessageType.EarthworkEndCommand && worker.EarthworkOrder?.Id == message.EarthworkOrderId)
                worker.EndEarthwork();
            return;
        }

        if (message.Type == NetworkMessageType.TiberiumSeedCommand)
        {
            if (message.TiberiumSeed is TiberiumSeedState state)
                World.Tiberium.ApplySeed(state);
            return;
        }

        if (message.Type == NetworkMessageType.TiberiumHarvestCommand)
        {
            World.Tiberium.ApplyHarvest(new Point(message.CellX, message.CellZ),
                message.TiberiumAmount, message.ServerTime);
            return;
        }

        if (message.Type == NetworkMessageType.HarvestCommand)
        {
            if (message.UnitId is Guid harvesterId &&
                World.Units.FindById(harvesterId) is Harvester harvester &&
                message.HarvestPhase is HarvestPhase phase)
                harvester.ApplyHarvestState(phase, message.CargoAmount);
            return;
        }

        if (message.Type == NetworkMessageType.ArmyResourcesCommand)
        {
            if (message.ArmyId is Guid armyId && Armies.Find(armyId) is Army army)
                army.Resources = message.ResourceAmount;
            return;
        }

        if (message.Type == NetworkMessageType.SellBuildingCommand)
        {
            if (message.ArmyId is Guid armyId && Armies.Find(armyId) is Army army)
                army.Resources = message.ResourceAmount;
            if (message.UnitId is Guid buildingId)
                World.Units.SellBuilding(buildingId);
            return;
        }

        if (message.Type == NetworkMessageType.StartMultiplayerGameCommand)
        {
            if (World.GraphicsEnabled)
            {
                Globals.Game.MarkMatchStarted();
                Globals.Game.PrepareAIPlayersForMatch(message.MatchStartAssignments ?? []);
            }
            SessionStateService state = _sessionState ?? new SessionStateService(World, Armies);
            Vector3? localStartPosition = state.ApplyMatchStart(message, _network.LocalPeerId);
            if (World.GraphicsEnabled)
            {
                Globals.Game.ResetMatchPresentation(localStartPosition);
                Globals.Console.Print($"Multiplayer game started with {message.MatchStartAssignments?.Length ?? 0} player(s).");
            }
            return;
        }

        if (message.Type == NetworkMessageType.SetRallyPointCommand)
        {
            // A client may request a change, but may not inject its own confirmation on the host.
            if (_network.IsHost && message.SenderId != _network.LocalPeerId)
                return;
            if (message.UnitId is Guid rallyUnitId && message.RallyPoint is RallyPointState rallyPoint &&
                World.Units.FindById(rallyUnitId) is Unit rallyUnit)
            {
                rallyUnit.ApplyRallyPointState(rallyPoint);
                UnitActionContext context = rallyPoint.HasPosition
                    ? UnitActionContext.At(new Vector3(rallyPoint.X, rallyPoint.Y, rallyPoint.Z))
                    : UnitActionContext.Empty;
                rallyUnit.OnHostAction(
                    rallyPoint.HasPosition ? UnitActionType.SetRallyPoint : UnitActionType.ClearRallyPoint,
                    context);
            }
            return;
        }

        if (message.Type == NetworkMessageType.TrainUnitCommand)
        {
            if (message.UnitId is Guid buildingId &&
                message.ProductionOrderId is Guid orderId &&
                message.PlayerId is Guid playerId &&
                message.UnitTypeId is not null &&
                World.Units.FindById(buildingId) is Building building)
            {
                building.TryQueueProduction(
                    orderId,
                    message.UnitTypeId,
                    playerId,
                    message.ProductionSeconds);
            }
            if (message.ArmyId is Guid armyId && Armies.Find(armyId) is Army army)
                army.Resources = message.ResourceAmount;
            return;
        }

        if (message.Type == NetworkMessageType.ResearchCommand)
        {
            if (message.UnitId is Guid buildingId &&
                message.ProductionOrderId is Guid orderId &&
                message.PlayerId is Guid playerId &&
                message.UnitTypeId is not null &&
                World.Units.FindById(buildingId) is Building building)
            {
                building.TryQueueProduction(orderId, message.UnitTypeId, playerId,
                    message.ProductionSeconds);
            }
            if (message.ArmyId is Guid armyId && Armies.Find(armyId) is Army army)
                army.Resources = message.ResourceAmount;
            return;
        }

        if (message.Type == NetworkMessageType.ResearchCompletedCommand)
        {
            if (message.ArmyId is Guid armyId &&
                message.ProductionOrderId is Guid researchId &&
                message.UnitTypeId is string projectId &&
                ResearchProjects.TryGetGrantedPerk(projectId, out PerkType perk) &&
                Armies.Find(armyId) is Army army)
            {
                army.Perks.GrantPermanent(perk, researchId);
            }
            return;
        }

        if (message.Type == NetworkMessageType.CancelConstructionCommand)
        {
            if (message.ArmyId is Guid armyId && Armies.Find(armyId) is Army army)
                army.Resources = message.ResourceAmount;
            if (message.UnitId is Guid buildingId)
                World.Units.SellBuilding(buildingId);
            return;
        }

        if (message.Type == NetworkMessageType.BuildCommand)
        {
            Guid unitId = message.UnitId ?? Guid.NewGuid();
            if (message.PlayerId is Guid playerId && message.UnitTypeId is not null)
                SpawnBuildingLocally(message.UnitTypeId, playerId, unitId, message.X, message.Y, message.Z,
                    message.TargetAngleY, message.PurchasePrice);
            if (message.ArmyId is Guid armyId && Armies.Find(armyId) is Army army)
                army.Resources = message.ResourceAmount;

            if (message.UnitIds is { Length: > 0 })
                ExecuteBuildConstruction(message with { ConstructionSiteId = unitId });

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
                World.Units.FindById(unitId)?.OnHostAction(
                    UnitActionType.Stop, UnitActionContext.Empty);
            return;
        }

        if (message.Type == NetworkMessageType.UnitActionCommand &&
            message.UnitActionType is UnitActionType actionType)
        {
            UnitActionContext context = message.UnitActionContext ?? UnitActionContext.Empty;
            foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
                World.Units.FindById(unitId)?.OnHostAction(actionType, context);
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
                World.Units.EmbarkUnit(occupantId, containerId, message.OccupantRole);
            return;
        }

        if (message.Type == NetworkMessageType.LeaveContainerCommand)
        {
            if (message.UnitId is Guid containerId && message.TargetId is Guid occupantId)
            {
                World.Units.DisembarkUnit(
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
        Unit? spawned = World.Units.SpawnUnit(
            unitTypeId,
            target,
            targetAngleY,
            unitId,
            playerId,
            driverUnitId);
        // SpawnCommand is the console/debug spawn path. Buildings constructed
        // through gameplay use BuildCommand and retain their normal build time.
        if (spawned is Building building)
            building.AdvanceConstruction(building.RemainingBuildingPoints);

        string playerName = _network.GetPeerDisplayName(playerId);
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
        Guid? driverUnitId,
        RallyPointState? rallyPoint)
    {
        MobileUnit? unit = World.Units.SpawnUnitFromBuilding(
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

        unit.SetProductionRallyPoint(rallyPoint);
        string playerName = _network.GetPeerDisplayName(playerId);
        Globals.Console.Print($"Produced {unitTypeId} for player {playerName}.");
    }

    private void ExecuteEnterUnit(NetworkMessage message)
    {
        if (message.UnitId is not Guid occupantId ||
            message.TargetId is not Guid containerId ||
            World.Units.FindById(occupantId) is not MobileUnit occupant ||
            World.Units.FindById(containerId) is not Unit container ||
            container.Occupancy is not OccupancyComponent occupancy)
        {
            return;
        }

        if (!occupancy.TryReserve(occupant, message.OccupantRole, out _))
            return;

        if (!occupant.TryReceiveEnterUnitCommand(World, container))
            occupancy.ClearReservation(occupant.UnitId);
    }

    private void SpawnBuildingLocally(string buildingTypeId, Guid playerId, Guid unitId, float x, float y,
        float z, float targetAngleY, int purchasePrice)
    {
        // Host placement already reserves the site; repeated confirmations are idempotent.
        if (World.Units.FindById(unitId) is not null)
            return;
        Vector3 target = new(x, y, z);
        if (World.Units.SpawnBuilding(buildingTypeId, target, targetAngleY, unitId, playerId,
            purchasePrice) is null)
            return;

        string playerName = _network.GetPeerDisplayName(playerId);
        Globals.Console.Print($"Spawned {buildingTypeId} for player {playerName}.");
    }

    private void ExecuteGoto(NetworkMessage message)
    {
        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
        {
            MobileUnit? unit = World.Units.FindMobileUnitById(unitId);
            if (message.EarthworkOrderId is Guid orderId &&
                (unit is not GDIBulldozer worker || worker.EarthworkOrder?.Id != orderId)) continue;
            // The host already issued its own work movement before broadcasting it.
            if (message.EarthworkOrderId is not null && _network.IsHost) continue;
            unit?.ClearFollowUnit();
            UnitRoute? assignedRoute = message.Routes?.FirstOrDefault(candidate => candidate.UnitId == unitId);
            Point[] route = assignedRoute?.Cells ?? [];
            GotoCommand command = new(new Vector2(
                assignedRoute?.TargetX ?? message.X,
                assignedRoute?.TargetZ ?? message.Z));
            unit?.TryReceiveGotoCommand(World, command, message.AppendToQueue, route);
        }

        if (message.EarthworkOrderId is null)
            World.Markers.ShowGotoMarker(new Vector3(message.X, message.Y, message.Z));
    }

    private void ApplyArmyControl(NetworkMessage message, bool grant)
    {
        if (message.ArmyId is not Guid armyId || message.PlayerId is not Guid ownerId || message.TargetId is not Guid recipientId)
            return;
        if (grant)
            Armies.GrantCommandUnits(armyId, ownerId, recipientId);
        else
            Armies.RevokeCommandUnits(armyId, ownerId, recipientId);
    }

    private void ApplyArmyMerge(NetworkMessage message)
    {
        if (message.ArmyId is not Guid firstArmyId || message.SecondaryArmyId is not Guid secondArmyId || message.TargetId is not Guid mergedArmyId)
            return;
        Army? merged = Armies.Merge(firstArmyId, secondArmyId, mergedArmyId);
        if (merged is null)
            return;
        foreach (Player player in Globals.Game.Players.Where(player => player.ArmyId is var armyId && (armyId == firstArmyId || armyId == secondArmyId)))
            player.SetArmy(merged.Id);
        foreach (Unit unit in World.Units.Units.Where(unit => unit.ArmyId == firstArmyId || unit.ArmyId == secondArmyId))
            unit.SetArmy(merged.Id);
    }

    private void ExecuteAttack(NetworkMessage message)
    {
        Vector3 target = new(message.X, message.Y, message.Z);
        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
        {
            if (World.Units.FindById(unitId) is not Unit attacker)
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
            World.Projectiles.Fire(
                start,
                target,
                attacker.ProjectileKind,
                attacker.ProjectileSpeed);
        }
    }

    private void ExecuteBulletImpact(NetworkMessage message)
    {
        if (!World.GraphicsEnabled) return;
        World.Particles.EmitBulletImpact(new Vector3(message.X, message.Y, message.Z));
    }

    private void ExecuteProjectileSpawn(NetworkMessage message)
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
            World.Units.FindById(attackerId) is Unit attacker)
        {
            attacker.SetProjectileLaunchVelocity(velocity);
            if (kind == ProjectileKind.Rocket)
                attacker.PlayShotEffects();
        }

        World.Projectiles.SpawnReplicated(
            projectileId,
            new Vector3(message.X, message.Y, message.Z),
            velocity,
            kind);
    }

    private void ExecuteProjectileImpact(NetworkMessage message)
    {
        if (message.ProjectileId is not Guid projectileId)
            return;
        World.Projectiles.ApplyAuthoritativeImpact(
            projectileId,
            new Vector3(message.X, message.Y, message.Z));
    }

    private void ExecuteAttackTarget(NetworkMessage message)
    {
        if (message.TargetId is not Guid targetId)
            return;
        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
            if (World.Units.FindById(unitId) is Unit unit)
                unit.SetAttackTarget(targetId);
    }

    private void ExecuteAttackGround(NetworkMessage message)
    {
        Vector3 target = new(message.X, message.Y, message.Z);
        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
            if (World.Units.FindById(unitId) is Unit unit)
                unit.SetAttackGroundTarget(target);
    }

    private void ExecuteFollow(NetworkMessage message)
    {
        if (message.TargetId is not Guid targetId)
            return;
        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
            if (World.Units.FindById(unitId) is Unit unit)
                unit.SetFollowUnit(targetId);
    }

    private void ExecuteTemporaryTarget(NetworkMessage message)
    {
        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
        {
            Unit? unit = World.Units.FindById(unitId);
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
            World.Units.FindById(unitId) is not Unit unit)
            return;

        unit.ApplyHitPoints(message.HitPoints);
    }

    private void DestroyUnit(NetworkMessage message)
    {
        if (message.UnitId is not Guid unitId ||
            World.Units.FindById(unitId) is not Unit unit)
            return;

        if (World.GraphicsEnabled && unit is Building destroyedBuilding && unit.HasDeathExplosion)
            World.Decals.AddBuildingRubble(destroyedBuilding);

        if (World.GraphicsEnabled && unit.HasDeathExplosion)
        {
            if (unit is not Building)
            {
                Vector3 pos = unit.Position + Vector3.Up * Math.Max(1.0f, unit.Height * 0.5f);
                World.Particles.EmitExplosion(
                    pos,
                    unit.UsesVehicleDeathSequence
                        ? ExplosionEmissionPresets.VehicleDestruction()
                        : ExplosionEmissionPresets.TankShell());
            }
        }
        World.Units.Destroy(unitId);
    }

    private void ExecuteBuildConstruction(NetworkMessage message)
    {
        if (message.ConstructionSiteId is not Guid constructionSiteId ||
            World.Units.FindById(constructionSiteId) is not Building constructionSite)
            return;

        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
        {
            Unit? unit = World.Units.FindById(unitId);
            unit?.ClearFollowUnit();
            Unit? genericUnit = unit as Unit;
            (genericUnit as MobileUnit)?.TryReceiveBuildConstructionCommand(World, constructionSite);
        }
    }

    private void ApplyUnitState(NetworkMessage message)
    {
        if (message.UnitState is not { } state)
            return;

        Unit? unit = World.Units.FindById(state.UnitId);
        // The host owns movement simulation. Its own broadcast must never
        // rewind a unit after the host has already advanced another frame.
        if (unit is MobileUnit && _network.IsHost) return;
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
                TerrainHelper.RaiseTerrain(World.Terrain, message.X, message.Z, message.ToolShape ?? ToolShape.Circle, message.ToolSize, 0.5f);
                break;
            case UnitActionType.FlattenTerrain:
                TerrainHelper.FlattenTerrain(World.Terrain, message.X, message.Z, message.Y, message.ToolShape ?? ToolShape.Circle, message.ToolSize, 0.5f);
                // Handle FlattenTerrain action
                break;
            case UnitActionType.SharpenTerrain:
                TerrainHelper.SharpenTerrain(World.Terrain, message.X, message.Z, message.Y, message.ToolShape ?? ToolShape.Circle, message.ToolSize, 0.02f);
                // Handle SharpenTerrain action
                break;
            case UnitActionType.SmoothTerrain:
                // Handle SmoothTerrain action
                TerrainHelper.SmoothTerrain(World.Terrain, message.X, message.Z, message.ToolShape ?? ToolShape.Circle, message.ToolSize, 0.5f);
                break;
            case UnitActionType.LowerTerrain:
                TerrainHelper.RaiseTerrain(World.Terrain, message.X, message.Z, message.ToolShape ?? ToolShape.Circle, message.ToolSize, -0.5f);
                break;
            case UnitActionType.SetTerrainTile:
                if (message.TerrainTile is not { } tile)
                    break;
                TerrainHelper.SetTile(World.Terrain, message.X, message.Z, message.ToolShape ?? ToolShape.Circle, message.ToolSize, tile);
                break;
            case UnitActionType.FillTile:
                if (message.TerrainTile is not { } fillTile)
                    break;
                TerrainHelper.FillTile(World.Terrain, message.X, message.Z, fillTile);
                break;
            default:
                // Handle other actions or do nothing
                break;
        }
    }
    
}

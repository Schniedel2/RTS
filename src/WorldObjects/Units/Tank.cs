using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace RTS;

public class Tank : MobileUnit
{
    // The turret angle is local to the hull.  Keeping it this way means that a
    // rotating hull does not automatically drag the turret around in world space.
    public float ReverseSpeed { get; set; } = 1.4f;
    public float ReverseWithoutTurningDistance { get; set; } = 6.0f;
    public float TurnInPlaceDotThreshold { get; set; } = 0.5f;
    private string? _lastMovementMode;
    private MeshSet? _meshSet;
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Attack, "Attack", 1, 1),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];

    public Tank(Vector3 position, Guid unitId, IMovementProfile? movementProfile = null)
        : base(position, length: 4, width: 2, height: 2.2f, unitId, movementProfile)
    {
        MoveSpeed = 3.0f;
        RotationSpeed = 1.0f;
        HeadingSnapAngle = 0.0f;
        CanOnlyMoveForward = true;
        CanTurnInPlace = true;
        TargetAngleDegreesPerSecond = 50.0f;
    }

    protected override void MoveAlongPath(GameTime gameTime)
    {
        if (PlannedPath.Count == 0)
        {
            _lastMovementMode = null;
            return;
        }

        Point nextCell = PlannedPath[0];
        Vector3 target = new(nextCell.X + 0.5f, Position.Y, nextCell.Y + 0.5f);
        Vector3 toTarget = target - Position;
        toTarget.Y = 0.0f;

        float distanceToTarget = toTarget.Length();
        if (distanceToTarget <= WaypointArrivalRadius)
        {
            LogMovementMode("arrive", distanceToTarget, directionDot: 1.0f);
            CompleteWaypoint();
            return;
        }

        Vector3 desiredDirection = toTarget / distanceToTarget;
        Vector3 forward = GetHorizontalDirection(Vector3.Forward);
        float directionDot = Vector3.Dot(forward, desiredDirection);
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // A nearby waypoint directly behind the tank is most naturally reached
        // in reverse.  Do not rotate the hull for this small correction.
        bool canReverse = directionDot < -0.35f &&
            distanceToTarget <= ReverseWithoutTurningDistance;
        if (canReverse)
        {
            LogMovementMode("reverse", distanceToTarget, directionDot);
            TryMoveTo(Position - forward * ReverseSpeed * deltaTime);
            return;
        }

        // Start driving only once the hull is reasonably aligned.  The former
        // threshold allowed nearly sideways movement, which let the tank orbit
        // its first waypoint instead of converging on it.
        bool needsTurnInPlace = directionDot < TurnInPlaceDotThreshold;
        forward = TurnTowards(desiredDirection, gameTime);
        if (needsTurnInPlace)
        {
            LogMovementMode("turn-in-place", distanceToTarget, directionDot);
            return;
        }

        // TurnTowards only applies RotationSpeed * deltaTime, so both the body
        // rotation and the resulting forward movement stay continuous.
        float alignment = MathF.Max(0.0f, Vector3.Dot(forward, desiredDirection));
        float speed = MoveSpeed * MathHelper.Lerp(0.45f, 1.0f, alignment);
        LogMovementMode("forward", distanceToTarget, alignment);
        TryMoveTo(Position + forward * speed * deltaTime);
    }

    private void LogMovementMode(string mode, float distance, float directionDot)
    {
        if (_lastMovementMode == mode)
            return;

        _lastMovementMode = mode;
        PathDebug($"tank mode={mode} distance={distance:0.00} alignment={directionDot:0.00}");
    }

    public override void Draw(Effect effect)
    {
        MeshSet? meshSet = GetMeshSet();
        if (meshSet is null)
            return;

        meshSet.SetParameter(Mesh.TurretAngle, MathHelper.ToRadians(TargetAngleDegrees));
        meshSet.Draw(effect, GetWorldMatrix());
    }

    /// <summary>Changes this tank instance's turret without modifying shared mesh assets.</summary>
    public bool SetTurretMesh(string meshName)
    {
        if (!Globals.MeshHandler.Meshes.TryGetValue(meshName, out Mesh? turretMesh))
            return false;

        MeshSet? meshSet = GetMeshSet();
        if (meshSet is null)
            return false;

        meshSet.SetAttachment("pivot:turret", turretMesh);
        return true;
    }

    /// <summary>Changes this tank instance's barrel below its currently attached turret.</summary>
    public bool SetBarrelMesh(string meshName)
    {
        if (!Globals.MeshHandler.Meshes.TryGetValue(meshName, out Mesh? barrelMesh))
            return false;

        MeshSet? meshSet = GetMeshSet();
        return meshSet?.SetAttachmentPath("pivot:turret/pivot:barrel", barrelMesh) ?? false;
    }

    private MeshSet? GetMeshSet()
    {
        if (_meshSet is not null)
            return _meshSet;
        if (!Globals.MeshHandler.Meshes.TryGetValue("TankBody-1", out Mesh? bodyMesh) ||
            !Globals.MeshHandler.Meshes.TryGetValue("TankTurret-1", out Mesh? turretMesh) ||
            !Globals.MeshHandler.Meshes.TryGetValue("TankBarrel-1", out Mesh? barrelMesh))
            return null;

        _meshSet = new MeshSet(bodyMesh);
        _meshSet.SetAttachment("pivot:turret", turretMesh);
        _meshSet.SetAttachmentPath("pivot:turret/pivot:barrel", barrelMesh);
        return _meshSet;
    }
}

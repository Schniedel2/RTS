using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace RTS;

public class Jeep : Car
{
    // The turret angle is local to the hull.  Keeping it this way means that a
    // rotating hull does not automatically drag the turret around in world space.
    public float TurretRotation { get; private set; }
    public float ReverseWithoutTurningDistance { get; set; } = 6.0f;
    public float TurretRotationSpeed { get; set; } = 0.01f;
    public float TurnInPlaceDotThreshold { get; set; } = 0.5f;
    private string? _lastMovementMode;
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Attack, "Attack", 1, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];

    public Jeep(Vector3 position, Guid unitId, IMovementProfile? movementProfile = null)
        : base(position, unitId, movementProfile)
    {
        Length = 3;
        Width = 2;         
        Height = 2.2f;

        MoveSpeed = 3.0f;
        RotationSpeed = 1.0f;
        HeadingSnapAngle = 0.0f;
        CanOnlyMoveForward = true;
        CanTurnInPlace = true;
    }

    public override void Draw(Effect effect)
    {
        TurretRotation += 0.1f;
        if (Globals.MeshHandler.Meshes.TryGetValue("jeep", out Mesh? mesh))
        {
            mesh.SetParameter(Mesh.TurretAngle, TurretRotation);
            mesh.SetParameter(Mesh.WheelAngle, TurretRotation);
            mesh.SetParameter(Mesh.WheelAngleY, TurretRotation);
        }

        Globals.MeshHandler.DrawMesh(effect, "jeep", GetWorldMatrix());
    }
}

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
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Attack, "Attack", 1, 1),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.LeaveContainer, "Leave", 5, 1),
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
    
        TargetAngleMinimumDegrees = -120.0f;
        TargetAngleMaximumDegrees = 120.0f;

        AttackCooldown = 2.0f;

        _meshSet = new MeshSet(Globals.MeshHandler.Meshes["blue-pick-up-truck"]);
        //_meshSet.SetAttachment("pivot:turret", Globals.MeshHandler.Meshes["TankTurret-1"]);
        //_meshSet.SetAttachmentPath("pivot:turret/pivot:barrel", Globals.MeshHandler.Meshes["TankBarrel-2"]);

    }

    public override void Draw(Effect effect)
    {
        if (_meshSet is null)
            return;

        _meshSet.SetParameter(Mesh.TurretAngle, MathHelper.ToRadians(TargetAngleDegrees));
        // BBModelLoader maps every group whose name contains "wheel" to this
        // shared parameter. WheelRotationDegrees is advanced from travelled
        // distance in MobileUnit, including reverse movement.
        _meshSet.SetParameter(Mesh.WheelAngle, -MathHelper.ToRadians(WheelRotationDegrees));
        _meshSet.Draw(effect, GetVisualWorldMatrix());
    }
}

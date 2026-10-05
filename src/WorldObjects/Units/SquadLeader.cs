using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS;

public sealed class SquadLeader : Soldier
{
    public override string GameplayTypeId => "squad-leader";
    public SquadLeader(Vector3 position, Guid unitId, bool loadModel = true)
        : base(position, unitId, new GroundMovementProfile(MovementModes.Walk, 50.0f), loadModel)
    {
        if (loadModel) SetMesh("Soldier-2", deriveDimensions: true);
        SetWeapon(Weapon.Brok17);
        IsCrewMember = true;
        SightRange = 20;
    }

    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Attack, "Attack", 1, 1),
        new(UnitActionType.AssembleSquad, "Assemble squad", 4, 1, RequiresTarget: false),
        new(UnitActionType.DisbandSquad, "Disband squad", 7, 1, RequiresTarget: false),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];
}

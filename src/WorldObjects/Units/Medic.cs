using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS;

public sealed class Medic : Soldier
{
    public override string GameplayTypeId => "medic";
    public const float HealAmountPerPulse = 5.0f;
    public const float HealPulseSeconds = 1.0f;
    public const float SearchRadiusInCells = 12.0f;
    public const float HealingRadiusInCells = 2.5f;

    public Medic(Vector3 position, Guid unitId)
        : base(position, unitId, new GroundMovementProfile(MovementModes.Walk, 50.0f))
    {
        SetMesh("Soldier-2", deriveDimensions: true);
        SetWeapon(Weapon.Medikit);
        IsCrewMember = true;
    }

    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Scouting, "AI: Scouting", 5, 1),
        new(UnitActionType.MoveAway, "AI: Move away", 4, 1),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];
}

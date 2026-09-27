using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS;

/// <summary>
/// Debug building which starts the production AI's reusable goals for its army.
/// </summary>
public sealed class CommandCenter : Building
{
    public override IReadOnlyList<UnitAction> Actions => IsCompleted
        ? WithSellAction(
        [
            new(UnitActionType.AIStartReactor, "AI: Build reactor", 1, 4, RequiresTarget: false),
            new(UnitActionType.AIStartRefinery, "AI: Build refinery", 3, 4, RequiresTarget: false),
            new(UnitActionType.AIStartEconomy, "AI: Start economy", 4, 4, RequiresTarget: false),
            new(UnitActionType.AIStartScouting, "AI: Start scouting", 5, 1, RequiresTarget: false),
            new(UnitActionType.AIStopGoals, "AI: Stop goals", 7, 1, RequiresTarget: false)
        ])
        : WithSellAction([]);

    public CommandCenter(Vector3 position, Guid unitId, int purchasePrice = 0)
        : base(position, unitId, purchasePrice)
    {
        TotalBuildingPointsNeeded = 0;
        HitPoints = MaxHitPoints = 1000;
        SetMesh("building-1", deriveDimensions: true);
    }
}

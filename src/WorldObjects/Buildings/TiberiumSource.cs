using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

/// <summary>Neutral map feature that keeps seeding Tiberium growth in its radius; itself not harvestable.</summary>
public class TiberiumSource : Building
{
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();

    private const int SpreadRadius = 3;
    private const double SpreadIntervalSeconds = 2.0;
    private double _nextSpreadTime;

    public TiberiumSource(
        Vector3 position,
        Guid unitId
        ) : base(
            position,
            unitId)
    {
        SetMesh("tiberiumSource-1", deriveDimensions: true);
        TotalBuildingPointsNeeded = 0;
        HitPoints = 300;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        double now = gameTime.TotalGameTime.TotalSeconds;
        if (now < _nextSpreadTime)
            return;

        _nextSpreadTime = now + SpreadIntervalSeconds;
        Point center = Globals.World.GameGrid.ToCell(Position);
        Point target = new(
            center.X + Random.Shared.Next(-SpreadRadius, SpreadRadius + 1),
            center.Y + Random.Shared.Next(-SpreadRadius, SpreadRadius + 1));
        Globals.World.Tiberium.Seed(target, now);
    }

    public IReadOnlyList<UnitAction> GetUnitActions() =>
    [
        new(UnitActionType.Stop, "Destroy", 7, 1)
    ];
}

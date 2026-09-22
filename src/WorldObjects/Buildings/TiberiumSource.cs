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
    private Point? _pendingSeedCell;

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

    // Only invoked on the host (see NetworkHost.UpdateHostSimulation); picks a candidate cell,
    // but does not touch TiberiumHandler itself - NetworkHost rolls/broadcasts the actual result.
    public override void UpdateHost(GameTime gameTime)
    {
        base.UpdateHost(gameTime);
        double now = gameTime.TotalGameTime.TotalSeconds;
        if (now < _nextSpreadTime || _pendingSeedCell is not null)
            return;

        _nextSpreadTime = now + SpreadIntervalSeconds;
        Point center = Globals.World.GameGrid.ToCell(Position);
        _pendingSeedCell = new Point(
            center.X + Random.Shared.Next(-SpreadRadius, SpreadRadius + 1),
            center.Y + Random.Shared.Next(-SpreadRadius, SpreadRadius + 1));
    }

    /// <summary>Drained by NetworkHost once per candidate; clears the pending cell either way.</summary>
    public bool TryTakePendingSeedCell(out Point cell)
    {
        if (_pendingSeedCell is not Point pending)
        {
            cell = default;
            return false;
        }
        cell = pending;
        _pendingSeedCell = null;
        return true;
    }

    public IReadOnlyList<UnitAction> GetUnitActions() =>
    [
        new(UnitActionType.Stop, "Destroy", 7, 1)
    ];
}

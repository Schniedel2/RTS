using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class GDIBase : Building
{
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();
    private float RadarAngleDegree = 0.0f;
    private float RadarSpeedDegreePerSecond = 180.0f;
    private float RadarSpeedFactor = 0.0f;

    public GDIBase(
        Vector3 position,
        Guid unitId
        ) : base(
            position,
            unitId)
    {
        SetMesh("gdi-base", deriveDimensions: true);

        TotalBuildingPointsNeeded = 2500;
        HitPoints = 2500;
    }

    public override void Draw(Effect effect)
    {
        _meshSet?.SetParameter("pivot:radar", MathHelper.ToRadians(RadarAngleDegree));
        base.Draw(effect);
    }

    public IReadOnlyList<UnitAction> GetUnitActions()
    {
        IReadOnlyList<UnitAction> actions =
        [
            new(UnitActionType.Goto, "Cancel", 0, 1)
        ];
    
        if (IsCompleted)
        {
            actions = 
            [
                new(UnitActionType.TrainUnit, "Bulldozer", 6, 1),
                new(UnitActionType.Follow, "Sell", 6, 1),
                new(UnitActionType.Stop, "Destroy", 7, 1)
            ];
        }
        
        return actions;
    }

    public override void Update(GameTime gameTime)
    {
        if (IsCompleted)
        {
            //  todo: bei Stromausfall muss RadarSpeedFactor reduziert werden
            RadarSpeedFactor += 0.01f;
            if (RadarSpeedFactor > 1.0f)
                RadarSpeedFactor = 1.0f;

           RadarAngleDegree += (float)gameTime.ElapsedGameTime.TotalSeconds * RadarSpeedDegreePerSecond * RadarSpeedFactor;
        }
        base.Update(gameTime);
    }
}
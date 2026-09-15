using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class GDIBase : Building
{
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();
    private float RadarAngleDegree = 0.0f;

    public GDIBase(
        Vector3 position,
        Guid unitId
        ) : base(
            position,
            unitId)
    {
        Length = 2;
        Width = 4;
        Height = 4;

        TotalBuildingPointsNeeded = 2500;
        HitPoints = 2500;
    }

    public override void Draw(Effect effect)
    {
       //  scale Y by percentage
        Matrix world = GetWorldMatrix();

        Mesh mesh = Globals.MeshHandler.Meshes["gdi-base"];
        mesh.SetParameter("radar", MathHelper.ToRadians(RadarAngleDegree));
        Globals.MeshHandler.DrawMesh(effect, "gdi-base", world);
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
           RadarAngleDegree += (float)gameTime.ElapsedGameTime.TotalSeconds * 180.0f;
        base.Update(gameTime);
    }
}
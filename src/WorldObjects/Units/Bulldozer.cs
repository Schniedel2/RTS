using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class GDIBulldozer : Car
{
    public override float BuildRate => 1000.0f;    

    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.Build, "Build Base", 1, 4, "GDI-Base"),
        new(UnitActionType.Build, "Build Barracks", 2, 4, "GDI-Barracks"),
        new(UnitActionType.BuildConstruction, "Build construction site", 0, 4),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];

    public GDIBulldozer(
        Vector3 position,
        Guid unitId,        
        IMovementProfile? movementProfile = null
        ) : base(
            position,
            unitId,
            movementProfile)
    {
        MoveSpeed = 7.0f;
        RotationSpeed = 4.0f;
        WaypointArrivalRadius = 1.5f;
        CanOnlyMoveForward = true;
        CanTurnInPlace = false;

        Length = 4;
        Width = 3;
        Height = 1.5f;
    }

    public override void UpdateHost(GameTime gameTime)
    {
        if (IsBuilding)
        {
            if (CurrentConstructionSiteId.HasValue)
            {
                Unit? CurrentConstructionSite = Globals.World.Units.FindById(CurrentConstructionSiteId.Value);
                ConstructionSite? constructionSite = (CurrentConstructionSite is ConstructionSite site) ? site : null;
                if (constructionSite != null)
                {
                    constructionSite.AdvanceConstruction(BuildRate * (float)gameTime.ElapsedGameTime.TotalSeconds);
                }
            }
        }
    }
}

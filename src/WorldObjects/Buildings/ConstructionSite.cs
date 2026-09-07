using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class ConstructionSite : Building
{
    private string BuildingTypeName;
    private float ConstructionProgress;
    private float TotalBuildingPointsNeeded;
    private float RemainingBuildingPoints;
    private float BuildingPointsPerSecond;

    public ConstructionSite(
        Vector3 position,
        Guid unitId,
        string buildingTypeName,
        float totalDurationRequiredMS
        ) : base(
            position,
            unitId)
    {
        this.BuildingTypeName = buildingTypeName;
        this.TotalBuildingPointsNeeded = totalDurationRequiredMS;
        this.RemainingBuildingPoints = totalDurationRequiredMS;
        BuildingPointsPerSecond = 0;
    }

    public override void Update(GameTime gameTime)
    {
        RemainingBuildingPoints -= BuildingPointsPerSecond * (float)(gameTime.ElapsedGameTime.TotalSeconds);
    }
}
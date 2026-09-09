using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RTS;

public class ConstructionSite : Building
{
    private sealed record ConstructionSiteState(float ConstructionProgress);

    public string BuildingTypeName { get; }
    public float ConstructionProgress { get; private set; }
    public float ConstructionPercentage => ConstructionProgress / TotalBuildingPointsNeeded;
    public float TotalBuildingPointsNeeded { get; }
    public float RemainingBuildingPoints => TotalBuildingPointsNeeded - ConstructionProgress;
    public override string StateTypeId => "construction-site";

    public ConstructionSite(
        Vector3 position,
        Guid unitId,
        string buildingTypeName,
        float totalDurationRequiredMS
        ) : base(
            position,
            unitId)
    {
        BuildingTypeName = buildingTypeName;
        TotalBuildingPointsNeeded = totalDurationRequiredMS;
    }

    public bool IsCompleted => ConstructionProgress >= TotalBuildingPointsNeeded;

    public void AdvanceConstruction(float buildPoints)
    {
        if (buildPoints <= 0.0f || IsCompleted)
            return;

        float nextProgress = Math.Min(
            TotalBuildingPointsNeeded,
            ConstructionProgress + buildPoints);
        if (nextProgress == ConstructionProgress)
            return;

        ConstructionProgress = nextProgress;
        StateRevision++;
        NetworkStateDirty = true;
    }

    public override UnitState GetState()
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConstructionSiteState(ConstructionProgress));
        return new UnitState(
            UnitId,
            StateRevision,
            StateTypeId,
            StateVersion,
            payload);
    }

    public override void ApplyState(UnitState state)
    {
        if (state.UnitId != UnitId ||
            state.TypeId != StateTypeId ||
            state.Version != StateVersion ||
            state.Revision < StateRevision)
            return;

        ConstructionSiteState? payload = JsonSerializer.Deserialize<ConstructionSiteState>(state.Payload);
        if (payload is null)
            return;

        ConstructionProgress = Math.Clamp(
            payload.ConstructionProgress,
            0.0f,
            TotalBuildingPointsNeeded);
        StateRevision = state.Revision;
        NetworkStateDirty = false;
    }

    public bool IsNetworkUpdateDue(double hostTime) =>
        NetworkStateDirty || hostTime >= NextNetworkUpdateTime;

    public void MarkNetworkStateSent(double hostTime, double heartbeatInterval)
    {
        NetworkStateDirty = false;
        NextNetworkUpdateTime = hostTime + heartbeatInterval;
    }

    public override void Draw(Effect effect)
    {
        base.Draw(effect);
    }

    public void Draw2D(SpriteBatch spriteBatch, Camera camera, Viewport viewport)
    {
        Vector3 labelPosition = Position + Vector3.Up * (Height + 1.0f);
        Vector3 screenPosition = viewport.Project(
            labelPosition,
            camera.Projection,
            camera.View,
            Matrix.Identity);

        if (screenPosition.Z < 0.0f || screenPosition.Z > 1.0f)
            return;

        string text = $"{MathF.Round(ConstructionPercentage * 100.0f):0}%";
        RenderHelper.DrawTextCentered(
            spriteBatch,
            Globals._debugFont,
            text,
            new Vector2(screenPosition.X, screenPosition.Y),
            Color.LimeGreen);
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace RTS;

public class Building : Unit
{
    private sealed record BuildingState(float ConstructionProgress);

    public float ConstructionProgress { get; private set; }
    public float ConstructionPercentage => ConstructionProgress / TotalBuildingPointsNeeded;
    public float TotalBuildingPointsNeeded { get; set; }
    public float RemainingBuildingPoints => TotalBuildingPointsNeeded - ConstructionProgress;
    public override string StateTypeId => "building-state";

    public Building(
        Vector3 position,
        Guid unitId
        ) : base(
            position,
            length: 1,
            width: 1,
            height: 1,
            unitId)
    {
        TotalBuildingPointsNeeded = 10000; // Default value, can be overridden by derived classes
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
            new BuildingState(ConstructionProgress));
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

        BuildingState? payload = JsonSerializer.Deserialize<BuildingState>(state.Payload);
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
        //  scale Y by percentage
        effect.Parameters["World"].SetValue(GetWorldMatrix());
        base.Draw(effect);
    }

    public override Matrix GetWorldMatrix()
    {
        return Matrix.CreateScale(1.0f, GetConstructionScaleFactor(), 1.0f) * base.GetWorldMatrix();
    }
    public float GetConstructionScaleFactor()
    {
        return Math.Max(0.05f, ConstructionPercentage);
    }

    public override void DrawPreview(Effect effect)
    {
        float xConstructionProgress = ConstructionProgress;
        float xTotalBuildingPointsNeeded = TotalBuildingPointsNeeded;

        //  render complete building
        ConstructionProgress = TotalBuildingPointsNeeded;
        effect.Parameters["World"].SetValue(GetWorldMatrix());
        Draw(effect);

        ConstructionProgress = xConstructionProgress;
        TotalBuildingPointsNeeded = xTotalBuildingPointsNeeded;
    }

    public override void Draw2D(SpriteBatch spriteBatch, Camera camera, Viewport viewport)
    {
        if (IsCompleted)
            return;

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

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
    }
}

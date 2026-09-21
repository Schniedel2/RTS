using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace RTS;

public class Building : Unit
{
    private sealed record BuildingState(
        float ConstructionProgress,
        ProductionQueueState ProductionQueue);
    private readonly BuildingFlag _ownerFlag = new();

    public float ConstructionProgress { get; private set; }
    // Buildings without construction costs are immediately complete; avoid 0 / 0 in their world matrix.
    public float ConstructionPercentage => TotalBuildingPointsNeeded <= 0.0f
        ? 1.0f
        : ConstructionProgress / TotalBuildingPointsNeeded;
    public float TotalBuildingPointsNeeded { get; set; }
    public float RemainingBuildingPoints => TotalBuildingPointsNeeded - ConstructionProgress;
    /// <summary>Draw a local cloth flag when the building mesh exposes <c>pivot:flag</c>.</summary>
    public bool ShowOwnerFlag { get; set; } = true;
    public ProductionQueue ProductionQueue { get; } = new();
    /// <summary>Optional production bonus for each embarked Crew unit (0.25 = +25%).</summary>
    public float CrewProductionBonusPerOccupant { get; set; }
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
        Occupancy = new OccupancyComponent(
            this,
            [
                new OccupantSlot(OccupantRole.Crew, 4),
                new OccupantSlot(OccupantRole.Garrison, 4)
            ],
            OccupancyOwnershipMode.CaptureOnEntry);
        Occupancy.EntryEnabled = false;
        TotalBuildingPointsNeeded = 10000; // Default value, can be overridden by derived classes
    }

    public bool IsCompleted => ConstructionProgress >= TotalBuildingPointsNeeded;

    /// <summary>Returns this building's production time for a supported unit type.</summary>
    public virtual bool TryGetProductionDuration(string unitTypeId, out float durationSeconds)
    {
        durationSeconds = 0.0f;
        return false;
    }

    public bool TryQueueProduction(
        Guid orderId,
        string unitTypeId,
        Guid requestedByPlayerId,
        float durationSeconds)
    {
        if (!IsCompleted ||
            !ProductionQueue.Enqueue(orderId, unitTypeId, requestedByPlayerId, durationSeconds))
        {
            return false;
        }

        MarkStateDirty();
        return true;
    }

    /// <summary>Advances production on the host and returns one completed order.</summary>
    public bool UpdateProduction(float elapsedSeconds, out ProductionOrder? completedOrder)
    {
        if (ProductionQueue.ActiveOrder is null)
        {
            completedOrder = null;
            return false;
        }

        float efficiency = Occupancy?.OperationalEfficiency ?? 1.0f;
        int crew = Occupancy?.Count(OccupantRole.Crew) ?? 0;
        float crewMultiplier = 1.0f + crew * CrewProductionBonusPerOccupant;
        bool completed = ProductionQueue.Update(
            elapsedSeconds * efficiency * crewMultiplier,
            out completedOrder);
        MarkStateDirty();
        return completed;
    }

    public bool TryGetProductionSpawnPosition(out Vector3 position) =>
        TryGetPivotPosition("pivot:spawn", out position);

    public bool TryGetProductionExitPosition(out Vector3 position) =>
        TryGetPivotPosition("pivot:exit", out position);

    private bool TryGetPivotPosition(string pivotName, out Vector3 position)
    {
        if (_meshSet?.TryGetPivotWorldPosition(pivotName, GetWorldMatrix(), out position) == true)
            return true;

        position = Vector3.Zero;
        return false;
    }

    private void MarkStateDirty()
    {
        StateRevision++;
        NetworkStateDirty = true;
    }

    internal bool TryGetFlagPivotWorldTransform(out Matrix pivotWorld) =>
        _meshSet?.TryGetPivotWorldTransform("pivot:flag", GetWorldMatrix(), out pivotWorld) ??
        SetMissingFlagPivot(out pivotWorld);

    internal void DrawOwnerFlag(Effect effect, Color color)
    {
        if (ShowOwnerFlag && ArmyId is not null)
            _ownerFlag.Draw(effect, color);
    }

    public override void DrawShadow(Effect effect)
    {
        base.DrawShadow(effect);
        if (ShowOwnerFlag && ArmyId is not null)
            _ownerFlag.DrawShadow(effect);
    }

    private static bool SetMissingFlagPivot(out Matrix pivotWorld)
    {
        pivotWorld = Matrix.Identity;
        return false;
    }

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
        if (Occupancy is not null)
            Occupancy.EntryEnabled = IsCompleted;
        MarkStateDirty();
    }

    public override UnitState GetState()
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new BuildingState(ConstructionProgress, ProductionQueue.GetState()));
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
        if (Occupancy is not null)
            Occupancy.EntryEnabled = IsCompleted;
        ProductionQueue.ApplyState(payload.ProductionQueue);
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
        ProductionOrder? activeOrder = ProductionQueue.ActiveOrder;
        if (IsCompleted && activeOrder is null)
            return;

        Vector3 labelPosition = Position + Vector3.Up * (Height + 1.0f);
        Vector3 screenPosition = viewport.Project(
            labelPosition,
            camera.Projection,
            camera.View,
            Matrix.Identity);

        if (screenPosition.Z < 0.0f || screenPosition.Z > 1.0f)
            return;

        string text = IsCompleted
            ? $"{activeOrder!.UnitTypeId}: {MathF.Round(activeOrder.Progress * 100.0f):0}%"
            : $"{MathF.Round(ConstructionPercentage * 100.0f):0}%";
        RenderHelper.DrawTextCentered(
            spriteBatch,
            Globals._debugFont,
            text,
            new Vector2(screenPosition.X, screenPosition.Y),
            IsCompleted ? Color.Cyan : Color.LimeGreen);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (ShowOwnerFlag && ArmyId is not null)
            _ownerFlag.Update(this, gameTime);
    }
}

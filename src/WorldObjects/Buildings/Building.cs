using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;

namespace RTS;

public class Building : Unit
{
    private const float SellDurationSeconds = 1.0f;
    private const float CollapsePauseSeconds = 0.65f;
    private const float CollapseDurationSeconds = 1.35f;
    private const float CollapseSinkSeconds = 1.5f;
    private sealed record BuildingState(
        float ConstructionProgress,
        ProductionQueueState ProductionQueue,
        RallyPointState? RallyPoint = null,
        float StoredResources = 0.0f,
        bool IncludedUnitGranted = false);
    private readonly BuildingFlag _ownerFlag = new();
    private float _sellElapsed;
    private bool _isCollapsing;
    private float _collapseElapsed;
    private float _collapseSmokeElapsed;
    private float _collapseJitterElapsed;
    private Vector3 _collapseJitter;

    public float ConstructionProgress { get; private set; }
    // Buildings without construction costs are immediately complete; avoid 0 / 0 in their world matrix.
    public float ConstructionPercentage => TotalBuildingPointsNeeded <= 0.0f
        ? 1.0f
        : ConstructionProgress / TotalBuildingPointsNeeded;
    public float TotalBuildingPointsNeeded { get; set; }
    /// <summary>Maximum total terrain height range under the rotated footprint, in world units.</summary>
    public float MaximumTerrainHeightDifference { get; set; } = 0.5f;

    public BuildingPlacement EvaluatePlacement(GameWorld world, Vector3 position, float rotationDegrees) =>
        BuildingPlacement.Evaluate(world, this, position, rotationDegrees, MaximumTerrainHeightDifference);
    public float RemainingBuildingPoints => TotalBuildingPointsNeeded - ConstructionProgress;
    /// <summary>Draw a local cloth flag when the building mesh exposes <c>pivot:flag</c>.</summary>
    public bool ShowOwnerFlag { get; set; } = true;
    public ProductionQueue ProductionQueue { get; } = new();
    public virtual float ResourceCapacity => 0.0f;
    public float StoredResources { get; private set; }
    public float AvailableResourceCapacity => Math.Max(0.0f, ResourceCapacity - StoredResources);
    public bool IncludedUnitGranted { get; protected set; }
    public int PurchasePrice { get; }
    public int SellRefund => PurchasePrice / 2;
    public int CancelRefund => PurchasePrice;
    public bool IsSelling { get; private set; }
    public override bool IsDying => IsSelling || _isCollapsing || base.IsDying;
    public override bool HasDeathExplosion => !IsSelling && base.HasDeathExplosion;
    public override bool IsReadyForRemoval =>
        IsSelling && _sellElapsed >= SellDurationSeconds ||
        _isCollapsing && _collapseElapsed >= CollapsePauseSeconds + CollapseDurationSeconds + CollapseSinkSeconds ||
        base.IsReadyForRemoval;
    /// <summary>Optional production bonus for each embarked Crew unit (0.25 = +25%).</summary>
    public float CrewProductionBonusPerOccupant { get; set; }
    public override string StateTypeId => "building-state";

    public override string GetDebugCommandText()
    {
        if (!IsCompleted) return $"Constructing {ConstructionPercentage * 100.0f:0}%";
        if (ProductionQueue.ActiveOrder is ProductionOrder order)
            return $"Producing {order.UnitTypeId} {order.Progress * 100.0f:0}%";
        return base.GetDebugCommandText();
    }

    public Building(
        Vector3 position,
        Guid unitId,
        int purchasePrice = 0
        ) : base(
            position,
            unitId)
    {
        PurchasePrice = Math.Max(0, purchasePrice);
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

    protected IReadOnlyList<UnitAction> WithDestroyAction(IEnumerable<UnitAction> actions)
    {
        List<UnitAction> result = [.. actions];
        if (!result.Any(action => action.Type == UnitActionType.Destroy))
            result.Add(new(UnitActionType.Destroy, "Destroy", 7, 1));
        return result;
    }

    protected IReadOnlyList<UnitAction> WithSellAction(IEnumerable<UnitAction> actions)
    {
        List<UnitAction> result = [.. WithDestroyAction(actions)];
        if (!result.Any(action => action.Type == UnitActionType.SellBuilding))
            result.Add(new(UnitActionType.SellBuilding, $"Sell (+{SellRefund})", 6, 1));
        return result;
    }

    public void BeginSelling()
    {
        if (IsSelling) return;
        IsSelling = true;
        _sellElapsed = 0.0f;
        IsSelected = false;
        ProductionQueue.ApplyState(null);
        MarkStateDirty();
    }

    public override bool BeginDeathSequence()
    {
        if (IsSelling || _isCollapsing)
            return true;

        _isCollapsing = true;
        _collapseElapsed = 0.0f;
        _collapseSmokeElapsed = 0.0f;
        _collapseJitterElapsed = 0.0f;
        _collapseJitter = Vector3.Zero;
        IsSelected = false;
        ProductionQueue.ApplyState(null);
        return true;
    }

    public override int GetSightRange()
    {
        if (!IsCompleted)
            return 0;
        return SightRange;
    }

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

    public virtual Vector3 GetResourceUnloadPosition()
    {
        if (TryGetAnimatedPivotWorldTransform("pivot:unload", out Matrix pivot))
            return pivot.Translation;
        TryGetEntryWorldPosition(out Vector3 fallback);
        return fallback;
    }

    public float StoreResources(float amount)
    {
        float accepted = Math.Min(Math.Max(0.0f, amount), AvailableResourceCapacity);
        if (accepted <= 0.0f) return 0.0f;
        StoredResources += accepted;
        MarkStateDirty();
        return accepted;
    }

    private bool TryGetPivotPosition(string pivotName, out Vector3 position)
    {
        if (_meshSet?.TryGetPivotWorldPosition(pivotName, GetWorldMatrix(), out position) == true)
            return true;

        position = Vector3.Zero;
        return false;
    }

    protected void MarkStateDirty()
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
            new BuildingState(ConstructionProgress, ProductionQueue.GetState(), GetRallyPointState(),
                StoredResources, IncludedUnitGranted));
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
        if (payload.RallyPoint is RallyPointState rallyPoint)
            ApplyRallyPointState(rallyPoint);
        StoredResources = Math.Clamp(payload.StoredResources, 0.0f, ResourceCapacity);
        IncludedUnitGranted = payload.IncludedUnitGranted;
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
        float sellScale = IsSelling
            ? Math.Max(0.0f, 1.0f - _sellElapsed / SellDurationSeconds)
            : 1.0f;
        float collapseProgress = _isCollapsing
            ? MathHelper.Clamp((_collapseElapsed - CollapsePauseSeconds) / CollapseDurationSeconds, 0.0f, 1.0f)
            : 0.0f;
        float collapseScale = MathHelper.Lerp(1.0f, 0.75f, collapseProgress);
        float sinkProgress = _isCollapsing
            ? MathHelper.Clamp(
                (_collapseElapsed - CollapsePauseSeconds - CollapseDurationSeconds) / CollapseSinkSeconds,
                0.0f, 1.0f)
            : 0.0f;

        Matrix world = Matrix.Identity;

        float collapseRotateYDegree = MathHelper.Lerp(0, 25.0f, collapseProgress);
        float collapseRotateXDegree = MathHelper.Lerp(0, 15.0f, collapseProgress);
        world *= Matrix.CreateRotationX(MathHelper.ToRadians(collapseRotateXDegree));
        world *= Matrix.CreateRotationY(MathHelper.ToRadians(collapseRotateYDegree));

        world *= Matrix.CreateScale(
            1.0f,
            GetConstructionScaleFactor() * sellScale * collapseScale,
            1.0f) * base.GetWorldMatrix();

        return world * Matrix.CreateTranslation(
            _collapseJitter * (1.0f - sinkProgress) +
            Vector3.Down * Math.Max(2.0f, Height * 0.75f) * sinkProgress);
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
        if (IsSelling)
            _sellElapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_isCollapsing)
        {
            float seconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _collapseElapsed += seconds;
            _collapseJitterElapsed += seconds;
            if (_collapseJitterElapsed >= 0.05f)
            {
                _collapseJitterElapsed %= 0.05f;
                _collapseJitter = new Vector3(
                    MathHelper.Lerp(-0.2f, 0.2f, Random.Shared.NextSingle()),
                    0.0f,
                    MathHelper.Lerp(-0.2f, 0.2f, Random.Shared.NextSingle()));
            }
            UpdateCollapseSmoke(seconds);
            return;
        }
        base.Update(gameTime);
        if (ShowOwnerFlag && ArmyId is not null)
            _ownerFlag.Update(this, gameTime);
    }

    private void UpdateCollapseSmoke(float seconds)
    {
        const float interval = 0.075f;
        _collapseSmokeElapsed += seconds;
        int emissions = Math.Min(8, (int)(_collapseSmokeElapsed / interval));
        if (emissions == 0)
            return;
        _collapseSmokeElapsed -= emissions * interval;

        SmokeEmissionSettings smoke = SmokeEmissionPresets.VehicleWreck() with
        {
            ParticleCount = 2,
            Intensity = 1.9f,
            StartSize = 0.55f,
            EndSize = 2.7f,
            Lifetime = 4.0f,
            Opacity = 0.9f
        };
        float extentX = Math.Max(0.5f, Width * Globals.World.GameGrid.CellSize * 0.42f);
        float extentZ = Math.Max(0.5f, Length * Globals.World.GameGrid.CellSize * 0.42f);
        for (int index = 0; index < emissions; index++)
        {
            Vector3 position = Position + new Vector3(
                (Random.Shared.NextSingle() * 2.0f - 1.0f) * extentX,
                Math.Max(0.4f, Height * (0.15f + Random.Shared.NextSingle() * 0.55f)),
                (Random.Shared.NextSingle() * 2.0f - 1.0f) * extentZ);
            Globals.World.Particles.EmitSmoke(position, Vector3.Up, smoke);
        }
    }
}

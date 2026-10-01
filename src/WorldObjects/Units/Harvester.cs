using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public enum HarvestPhase
{
    Idle,
    DrivingToField,
    Harvesting,
    ReturningToSilo,
    Unloading
}

public sealed class Harvester : Car
{
    public override string GameplayTypeId => "harvester";
    public const float DefaultCargoCapacity = 300.0f;
    public const float HarvestRetrySeconds = 3.0f;
    public override bool CanFireWeapon => false;
    public float CargoCapacity { get; set; } = DefaultCargoCapacity;
    public float CargoAmount { get; private set; }
    public HarvestPhase HarvestPhase { get; private set; }

    public override string GetDebugCommandText() =>
        $"Harvest={HarvestPhase} cargo={CargoAmount:0.0}/{CargoCapacity:0.0} | {base.GetDebugCommandText()}";

    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Harvest, "Harvest", 3, 1),
        new(UnitActionType.ReturnToStorage, "Return & unload", 4, 1),
        new(UnitActionType.MoveAway, "AI: Move away", 4, 1),
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];

    public Harvester(Vector3 position, Guid unitId) : base(position, unitId)
    {
        Length = 4;
        Width = 3;
        Height = 2.0f;
        MoveSpeed = 5.0f;
        // Wheeled steering: change hull direction only while translating.
        CanTurnInPlace = false;
        GroundSteering = new(
            MovingTurnDegreesPerSecond: MathHelper.ToDegrees(RotationSpeed),
            StationaryTurnDegreesPerSecond: MathHelper.ToDegrees(RotationSpeed),
            TurnInPlaceThresholdDegrees: 100.0f,
            MinimumCurveSpeedFactor: 0.4f,
            AllowReverse: true,
            ReverseSpeed: 1.5f,
            ReverseStartAngleDegrees: 110.0f,
            ReverseAlignmentToleranceDegrees: 8.0f,
            ReverseMaximumDistance: 5.0f);
        HitPoints = MaxHitPoints = 900.0f;
        SetMesh("harvester-1");
    }

    internal void ApplyHarvestState(HarvestPhase phase, float cargoAmount)
    {
        // Arrival at storage is deliberately tolerant: stop at the accepted
        // approach instead of continuing to chase its exact waypoint while
        // unloading. Apply this on every peer through the HarvestCommand and
        // invalidate any movement search that was still in flight.
        if (phase is HarvestPhase.Unloading or HarvestPhase.Harvesting)
            base.Stop();
        HarvestPhase = phase;
        CargoAmount = Math.Clamp(cargoAmount, 0.0f, CargoCapacity);
    }

    public bool IsWithinHarvestReach(Vector3 resourcePosition, float cellSize)
    {
        // Approach cells may lie exactly two units from a crystal. Driving
        // finishes within its arrival radius rather than at the exact center;
        // accept that same tolerance instead of issuing the approach forever.
        float radius = 2.0f + Math.Min(WaypointArrivalRadius, cellSize * 0.35f);
        Vector2 distance = new(Position.X - resourcePosition.X, Position.Z - resourcePosition.Z);
        return distance.LengthSquared() <= radius * radius;
    }

    public override void Stop()
    {
        base.Stop();
        HarvestPhase = HarvestPhase.Idle;
    }

    public override void Draw(Effect effect)
    {
        if (_meshSet is null) return;
        _meshSet.SetParameter(Mesh.WheelAngle, -MathHelper.ToRadians(WheelRotationDegrees));
        _meshSet.Draw(effect, GetVisualWorldMatrix());
    }

    public override void Draw2D(SpriteBatch spriteBatch, Camera camera, Viewport viewport)
    {
        ResourceBarRenderer.Draw(spriteBatch, camera, viewport, this,
            CargoAmount, CargoCapacity, Color.LimeGreen);
    }
}

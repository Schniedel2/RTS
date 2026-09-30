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
        // The harvester's large 3x3 core cannot reliably follow tight grid
        // routes with the generic car turning circle. Let it align before it
        // enters the next route cell, just like the tracked bulldozer.
        CanTurnInPlace = true;
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
        HarvestPhase = phase;
        CargoAmount = Math.Clamp(cargoAmount, 0.0f, CargoCapacity);
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

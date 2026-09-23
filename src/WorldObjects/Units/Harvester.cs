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
    public const float DefaultCargoCapacity = 100.0f;
    public override bool CanFireWeapon => false;
    public float CargoCapacity { get; set; } = DefaultCargoCapacity;
    public float CargoAmount { get; private set; }
    public HarvestPhase HarvestPhase { get; private set; }

    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Harvest, "Harvest", 3, 1),
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];

    public Harvester(Vector3 position, Guid unitId) : base(position, unitId)
    {
        Length = 4;
        Width = 3;
        Height = 2.0f;
        MoveSpeed = 5.0f;
        HitPoints = MaxHitPoints = 900.0f;
        _meshSet = new MeshSet(Globals.MeshHandler.Meshes["harvester-1"]);
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
}

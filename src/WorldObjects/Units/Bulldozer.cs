using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class GDIBulldozer : Car
{
    public EarthworkOrder? EarthworkOrder { get; private set; }
    public int EarthworkSequence { get; private set; }
    private float _regularMoveSpeed, _regularArrivalRadius;
    private bool _regularTurnInPlace;

    public void BeginEarthwork(EarthworkOrder order)
    {
        if (EarthworkOrder?.Id == order.Id) return;
        if (EarthworkOrder is not null)
            EndEarthwork();
        else
            base.Stop();
        _regularMoveSpeed = MoveSpeed;
        _regularArrivalRadius = WaypointArrivalRadius;
        _regularTurnInPlace = CanTurnInPlace;
        MoveSpeed = Math.Min(MoveSpeed, 2.5f);
        WaypointArrivalRadius = 0.2f;
        CanTurnInPlace = true;
        EarthworkOrder = order;
        EarthworkSequence = 0;
    }

    public void EndEarthwork()
    {
        if (EarthworkOrder is null) return;
        EarthworkOrder = null;
        base.Stop();
        MoveSpeed = _regularMoveSpeed;
        WaypointArrivalRadius = _regularArrivalRadius;
        CanTurnInPlace = _regularTurnInPlace;
    }

    /// <summary>
    /// A normal Stop command also terminates the persistent earthwork mode and
    /// restores the bulldozer's regular movement parameters.
    /// </summary>
    public override void Stop()
    {
        if (EarthworkOrder is not null)
        {
            EndEarthwork();
            return;
        }

        base.Stop();
    }

    public bool ApplyEarthworkCell(GameWorld world, Guid orderId, int sequence, Point cell, bool refreshGraphics = true)
    {
        if (EarthworkOrder is not EarthworkOrder order || order.Id != orderId ||
            sequence <= EarthworkSequence || !order.Contains(cell)) return false;
        Earthwork.ApplyCell(world, order, cell, refreshGraphics);
        // Leveling may raise or lower the terrain underneath the worker. Snap
        // its visual/physical transform to the freshly rebuilt surface before
        // the next movement/pathfinding update.
        AlignToTerrain();
        EarthworkSequence = sequence;
        return true;
    }

    public bool ApplyEarthworkDrive(GameWorld world, Guid orderId, int sequence, int[] cells, Vector2 position, bool refreshGraphics = true)
    {
        if (EarthworkOrder is not { IsDrive: true } order || order.Id != orderId || sequence <= EarthworkSequence || cells.Length % 2 != 0) return false;
        long revision = world.Terrain.HeightRevision;
        for (int i = 0; i < cells.Length; i += 2)
            Earthwork.ApplyCell(world, order, new(cells[i], cells[i + 1]), false);
        if (refreshGraphics && cells.Length > 0)
        {
            if (world.Terrain.HeightRevision != revision) world.Terrain.BuildTerrainMesh();
            world.Terrain.UpdateTilemapTexture();
        }
        float yaw = MathHelper.ToDegrees(MathF.Atan2(-(order.DestinationX - order.StartX), -(order.DestinationZ - order.StartZ)));
        SetRotationYDegrees(yaw);
        Vector3 next = new(position.X, order.TargetHeight, position.Y);
        AdvanceWheelRotation(next);
        SetPosition(next);
        world.GameGrid.RegisterEarthworkFootprint(this, Earthwork.DriveCells(world, this, order, position));
        if (refreshGraphics) AlignToTerrain();
        EarthworkSequence = sequence;
        return true;
    }

    public override float BuildRate => 1000.0f;    

    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Scouting, "AI: Scouting", 5, 1),
        new(UnitActionType.MoveAway, "AI: Move away", 4, 1),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.Build, "Build Reaktor", 1, 4, "Reaktor"),
        new(UnitActionType.Build, "Build Base", 1, 4, "GDI-Base"),
        new(UnitActionType.Build, "Build Barracks", 2, 4, "GDI-Barracks"),
        new(UnitActionType.Build, "Build Communications Tower", 2, 4, "CommunicationsTower"),
        new(UnitActionType.Build, "Build Helipad", 2, 4, "Helipad"),
        new(UnitActionType.Build, "Build Silo", 2, 4, "Silo"),
        new(UnitActionType.Build, "Build Tiberium Refinery", 3, 4, "Tiberium-Refinery"),
        new(UnitActionType.BuildConstruction, "Build construction site", 0, 4),
        new(UnitActionType.LevelAndConcrete, "Level & concrete", 1, 4),
        new(UnitActionType.RemoveConcrete, "Remove concrete", 7, 1),
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

        _meshSet = new MeshSet(Globals.MeshHandler.Meshes["bulldozer-1"]);
    }

    public override void Draw(Effect effect)
    {
        if (_meshSet is null)
            return;

        _meshSet.SetParameter(Mesh.TurretAngle, MathHelper.ToRadians(TargetAngleDegrees));
        // BBModelLoader maps every group whose name contains "wheel" to this
        // shared parameter. WheelRotationDegrees is advanced from travelled
        // distance in MobileUnit, including reverse movement.
        _meshSet.SetParameter(Mesh.WheelAngle, -MathHelper.ToRadians(WheelRotationDegrees));
        _meshSet.Draw(effect, GetVisualWorldMatrix());
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        UpdateExhaust(gameTime, 0.1f);
    }

    public override void UpdateHost(GameTime gameTime)
    {
        if (IsBuilding)
        {
            if (TargetBuildingId.HasValue)
            {
                Unit? CurrentBuilding = Globals.World.Units.FindById(TargetBuildingId.Value);
                Building? building = (CurrentBuilding is Building site) ? site : null;
                if (building != null)
                {
                    building.AdvanceConstruction(BuildRate * (float)gameTime.ElapsedGameTime.TotalSeconds);
                }
            }
        }
    }
}

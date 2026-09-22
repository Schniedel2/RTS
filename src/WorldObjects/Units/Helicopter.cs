using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace RTS;

public enum HelicopterFlightState { Landed, TakingOff, Flying, Hovering, Landing, EmergencyLanding }
public enum HelicopterOrder { TakeOff, Land, ReturnToHelipad }

public sealed record HelicopterState(float X, float Y, float Z, float Yaw, HelicopterFlightState Flight,
    float Fuel, int Ammunition, Guid? HelipadId, float? LandingX, float? LandingY, float? LandingZ);

/// <summary>Air movement and supplies are simulated by the host; clients animate replicated state.</summary>
public class Helicopter : MobileUnit
{
    public override string StateTypeId => "helicopter";
    public HelicopterFlightState FlightState { get; private set; } = HelicopterFlightState.Landed;
    public bool IsLanded => FlightState == HelicopterFlightState.Landed;
    public float MaximumFuel { get; set; } = 120;
    public float Fuel { get; private set; } = 120;
    public int MaximumAmmunition { get; set; } = 40;
    public int Ammunition { get; private set; } = 40;
    public float FuelPerSecond { get; set; } = 1;
    public float CruiseHeight { get; set; } = 8;
    public float VerticalSpeed { get; set; } = 4;
    public float RefuelPerSecond { get; set; } = 20;
    public float ReloadPerSecond { get; set; } = 10;
    public Guid? AssignedHelipadId { get; private set; }
    public float MainRotorDegree { get; private set; }
    public float RearRotorDegree { get; private set; }
    public float MainRotorSpeed { get; set; } = 0;
    public float MainRotorSpeedMax { get; set; } = 360;
    public float RearRotorSpeed { get; set; } = 0;
    public float RearRotorSpeedMax { get; set; } = 420;
    public Vector3 RearRotorAxis { get; set; } = Vector3.Right;
    public float GroundOffset { get; private set; }
    private Vector2? _destination;
    private Vector3? _landing;
    private float _reloadFraction;
    private double _nextAuthorizedShot;
    private float _yaw;
    private Vector3 _renderPosition;
    private float _renderYaw;
    private bool _hasRenderState;
    private bool _returning;
    private bool _emergencyCrash;

    public override bool UsesHitscanWeapon => true;
    public override bool CanFireWeapon => Fuel > 0 && Ammunition > 0 && !_returning &&
        FlightState is HelicopterFlightState.Flying or HelicopterFlightState.Hovering;

    public override IReadOnlyList<UnitAction> Actions
    {
        get
        {
            List<UnitAction> actions =
            [
                new(UnitActionType.Goto, "Fly to", 0, 1),
                new(UnitActionType.TakeOff, "Take off", 0, 1),
                new(UnitActionType.Land, "Land", 5, 1),
                new(UnitActionType.ReturnToHelipad, "Return to helipad", 5, 1),
                new(UnitActionType.Attack, "Attack", 1, 1),
                new(UnitActionType.Follow, "Follow", 6, 1),
                new(UnitActionType.Stop, "Stop / hover", 7, 1)
            ];
            if (Occupancy?.Slots.Any(slot => slot.Role == OccupantRole.Passenger) == true)
                actions.Add(new(UnitActionType.LeaveContainer, "Unload", 5, 1));
            return actions;
        }
    }

    // Passenger slots are optional, ready for a later transport variant. This model has an integrated pilot.
    public Helicopter(Vector3 position, Guid unitId, int passengerCapacity = 0, string meshName = "heli-1")
        : base(position, 5, 3, 2, unitId, new GroundMovementProfile(MovementModes.All, 30))
    {
        MoveSpeed = 10;
        RotationSpeed = MathHelper.Pi;
        CanTurnInPlace = true;
        CanOnlyMoveForward = false;
        AttackRange = 18;
        AttackDamage = 12;
        AttackCooldown = 0.2f;
        Behavior = UnitBehavior.Passive;
        Occupancy = new OccupancyComponent(this,
            passengerCapacity > 0 ? new[] { new OccupantSlot(OccupantRole.Passenger, passengerCapacity) } : Array.Empty<OccupantSlot>(),
            OccupancyOwnershipMode.PreserveOwnership);
        SetMesh(meshName, deriveDimensions: true);
        BoundingBox bounds = _meshSet!.GetBounds();
        GroundOffset = -bounds.Min.Y;
        Height = Math.Max(0.1f, bounds.Max.Y - bounds.Min.Y);
    }

    public bool InitializeOnGround(GameWorld world)
    {
        Vector3 position = Position;
        position.Y = world.Terrain.GetSurfaceHeight(position.X, position.Z) + GroundOffset;
        float yaw = MathHelper.ToDegrees(MathF.Atan2(-Transform.Forward.X, -Transform.Forward.Z));
        if (!CanLandOnGround(world, position, yaw) || !world.GameGrid.TryPlace(this, position, yaw)) return false;
        SetPosition(position);
        _yaw = MathF.Atan2(-Transform.Forward.X, -Transform.Forward.Z);
        return true;
    }

    public bool CanLandOnGround(GameWorld world, Vector3 position, float yawDegrees = 0)
    {
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        foreach (Point c in world.GameGrid.GetFootprintCells(this, position, yawDegrees))
        {
            if (!world.GameGrid.Contains(c)) return false;
            GridCell data = world.GameGrid.GetCell(c);
            if (!data.HasTerrain || data.IsBlocked || data.ExcludeFromPathfinding || data.AllowedMovement == MovementModes.None ||
                world.GameGrid.GetOccupant(c) is Unit occupant && occupant != this) return false;
            foreach (Point v in Earthwork.Vertices(world.GameGrid, c))
            {
                float h = world.Terrain.GetHeight(v.X, v.Y);
                low = Math.Min(low, h); high = Math.Max(high, h);
            }
        }
        return high - low <= 0.5f;
    }

    private void ReleaseLanding(GameWorld world)
    {
        AssignedHelipadId = null;
        _landing = null;
        world.GameGrid.Remove(this);
    }

    public bool TakeOff(GameWorld world)
    {
        if (Fuel <= 0 || IsEmbarked || IsDying) return false;
        ReleaseLanding(world);
        _returning = false; _emergencyCrash = false; _destination = null;
        Occupancy!.EntryEnabled = false;
        if (IsLanded) FlightState = HelicopterFlightState.TakingOff;
        else FlightState = HelicopterFlightState.Hovering;
        return true;
    }

    public override bool TryReceiveGotoCommand(GameWorld world, GotoCommand command)
    {
        if (!ValidTarget(world, command.Target) || Fuel <= 0) return false;
        base.Stop();
        _returning = false; _emergencyCrash = false;
        if (!TakeOff(world)) return false;
        _destination = command.Target;
        return true;
    }

    private void BeginFlightIntent()
    {
        _returning = false; _emergencyCrash = false; _destination = null;
        if (!IsLanded && Fuel > 0)
        {
            ReleaseLanding(Globals.World);
            FlightState = HelicopterFlightState.Hovering;
        }
    }

    public override void SetAttackTarget(Guid targetId) { BeginFlightIntent(); base.SetAttackTarget(targetId); }
    public override void SetAttackGroundTarget(Vector3 target) { BeginFlightIntent(); base.SetAttackGroundTarget(target); }
    public override bool SetFollowUnit(Guid targetId) { BeginFlightIntent(); return base.SetFollowUnit(targetId); }

    public override void Stop()
    {
        base.Stop();
        _destination = null;
        _returning = false;
        if (!IsLanded && Fuel > 0)
        {
            ReleaseLanding(Globals.World);
            FlightState = HelicopterFlightState.Hovering;
        }
    }

    public bool RequestLanding(GameWorld world, Vector2 target, Helipad? pad = null)
    {
        if (!ValidTarget(world, target)) return false;
        if (pad is not null && pad.CanAccept(world, this))
        {
            Vector3 landing = pad.GetLandingPosition(this);
            base.Stop();
            if (IsLanded && AssignedHelipadId == pad.UnitId) return true;
            if (IsLanded && !TakeOff(world)) return false;
            ReleaseLanding(world);
            AssignedHelipadId = pad.UnitId;
            _landing = landing; _destination = new(landing.X, landing.Z);
            _returning = true;
            FlightState = HelicopterFlightState.TakingOff;
            Occupancy!.EntryEnabled = false;
            return true;
        }
        // Prefer the selected cell; then find the nearest safe alternative within twelve cells.
        Point center = world.GameGrid.ToCell(new(target.X, 0, target.Y));
        var candidates = from z in Enumerable.Range(-12, 25)
                         from x in Enumerable.Range(-12, 25)
                         orderby x * x + z * z, z, x
                         select center + new Point(x, z);
        foreach (Point c in candidates)
        {
            Vector3 landing = world.GameGrid.ToWorldPosition(c, 0);
            if (!CanLandOnGround(world, landing)) continue;
            landing.Y = world.Terrain.GetSurfaceHeight(landing.X, landing.Z) + GroundOffset;
            if (IsLanded && Vector2.Distance(new(Position.X, Position.Z), new(landing.X, landing.Z)) < 0.1f) return true;
            if (IsLanded && !TakeOff(world)) return false;
            base.Stop();
            ReleaseLanding(world);
            if (!world.GameGrid.TryPlace(this, landing, 0)) continue;
            _landing = landing; _destination = new(landing.X, landing.Z);
            _returning = true;
            FlightState = HelicopterFlightState.TakingOff;
            Occupancy!.EntryEnabled = false;
            return true;
        }
        return false;
    }

    public bool ReturnToHelipad(GameWorld world)
    {
        foreach (Helipad pad in world.Units.Units.OfType<Helipad>()
            .Where(p => p.CanAccept(world, this)).OrderBy(p => Vector3.DistanceSquared(Position, p.Position)))
            if (RequestLanding(world, new(pad.Position.X, pad.Position.Z), pad)) return true;
        return RequestLanding(world, new(Position.X, Position.Z));
    }

    private static bool ValidTarget(GameWorld world, Vector2 target) => float.IsFinite(target.X) && float.IsFinite(target.Y) &&
        target.X >= 0 && target.Y >= 0 && target.X < world.Terrain.Width - 1 && target.Y < world.Terrain.Height - 1;

    private float SafeAltitude(GameWorld world, Vector2 point)
    {
        float height = world.Terrain.GetSurfaceHeight(point.X, point.Y);
        foreach (Point c in world.GameGrid.GetFootprintCells(this, new(point.X, 0, point.Y), 0))
            if (world.GameGrid.Contains(c) && world.GameGrid.GetCell(c).HasTerrain)
                foreach (Point v in Earthwork.Vertices(world.GameGrid, c)) height = Math.Max(height, world.Terrain.GetHeight(v.X, v.Y));
        foreach (Building building in world.Units.Units.OfType<Building>())
            if (Math.Abs(building.Position.X - point.X) <= (building.Width + Width) * world.GameGrid.CellSize &&
                Math.Abs(building.Position.Z - point.Y) <= (building.Length + Length) * world.GameGrid.CellSize)
                height = Math.Max(height, building.Position.Y + building.Height);
        return height + CruiseHeight + GroundOffset;
    }

    public void SimulateFlight(GameWorld world, float seconds)
    {
        if (IsDying || IsEmbarked || seconds <= 0 || !float.IsFinite(seconds)) return;
        if (AssignedHelipadId is Guid padId &&
            (world.Units.FindById(padId) is not Helipad pad || !pad.CanAccept(world, this)))
        {
            ReleaseLanding(world);
            _returning = false;
            if (IsLanded) FlightState = HelicopterFlightState.Hovering;
        }
        if (IsLanded)
        {
            Occupancy!.EntryEnabled = true;
            if (AssignedHelipadId is Guid landedPad && world.Units.FindById(landedPad) is Helipad service && service.CanAccept(world, this))
            {
                Fuel = Math.Min(MaximumFuel, Fuel + RefuelPerSecond * seconds);
                _reloadFraction += ReloadPerSecond * seconds;
                int rounds = (int)_reloadFraction;
                Ammunition = Math.Min(MaximumAmmunition, Ammunition + rounds);
                _reloadFraction -= rounds;
            }
            if (!_returning && Fuel > 0 && (AttackTargetId is not null || AttackGroundTarget is not null || FollowUnitId is not null)) TakeOff(world);
            else { StateRevision++; return; }
        }
        Fuel = Math.Max(0, Fuel - Math.Max(0, FuelPerSecond) * seconds);
        if (!_returning && (Fuel <= MaximumFuel * 0.2f || Ammunition == 0)) ReturnToHelipad(world);
        if (Fuel <= 0 && FlightState != HelicopterFlightState.EmergencyLanding)
        {
            Vector3? padLanding = AssignedHelipadId is not null && _landing is Vector3 reserved &&
                Vector2.Distance(new(Position.X, Position.Z), new(reserved.X, reserved.Z)) < 0.1f ? reserved : null;
            if (padLanding is null) ReleaseLanding(world);
            _returning = true;
            _destination = null;
            Vector3 ground = padLanding ?? new Vector3(Position.X, world.Terrain.GetSurfaceHeight(Position.X, Position.Z) + GroundOffset, Position.Z);
            _emergencyCrash = padLanding is null && !CanLandOnGround(world, ground);
            if (!_emergencyCrash && padLanding is null) world.GameGrid.TryPlace(this, ground, 0);
            _landing = ground;
            FlightState = HelicopterFlightState.EmergencyLanding;
        }
        if (!_returning)
        {
            Vector3? aim = AttackGroundTarget;
            if ((AttackTargetId ?? FollowUnitId) is Guid targetId && world.Units.FindById(targetId) is Unit target) aim = target.Position;
            if (aim is Vector3 goal)
            {
                float range = AttackTargetId is not null || AttackGroundTarget is not null ? AttackRange * 0.85f : Math.Max(2, FollowDistance);
                _destination = Vector2.Distance(new(Position.X, Position.Z), new(goal.X, goal.Z)) > range ? new(goal.X, goal.Z) : null;
            }
        }
        Vector3 position = Position;
        if (FlightState is HelicopterFlightState.Landing or HelicopterFlightState.EmergencyLanding)
        {
            if (_landing is not Vector3 landing) { FlightState = HelicopterFlightState.Hovering; return; }
            if (AssignedHelipadId is null && !_emergencyCrash && !CanLandOnGround(world, landing))
            { ReleaseLanding(world); _returning = false; FlightState = HelicopterFlightState.Hovering; return; }
            position.Y = Math.Max(landing.Y, position.Y - VerticalSpeed * seconds);
            TurnTo(0, seconds);
            SetPosition(position);
            if (position.Y <= landing.Y + 0.001f)
            {
                FlightState = HelicopterFlightState.Landed;
                _destination = null;
                Occupancy!.EntryEnabled = true;
                if (_emergencyCrash) HitPoints = 0;
            }
        }
        else
        {
            Vector2 current = new(position.X, position.Z);
            Vector2 delta = (_destination ?? current) - current;
            float distance = delta.Length();
            Vector2 next = distance < 0.001f ? current : current + delta / distance * Math.Min(distance, MoveSpeed * seconds);
            float altitude = Math.Max(SafeAltitude(world, current), SafeAltitude(world, next));
            position.Y = MoveTowards(position.Y, altitude, VerticalSpeed * seconds);
            if (distance > 0.001f) TurnTo(MathF.Atan2(-delta.X, -delta.Y), seconds);
            if (position.Y >= altitude - 0.01f)
            {
                position.X = next.X; position.Z = next.Y;
                FlightState = distance > 0.001f ? HelicopterFlightState.Flying : HelicopterFlightState.Hovering;
                if (Vector2.Distance(next, _destination ?? next) < 0.01f)
                {
                    _destination = null;
                    if (_landing is not null) FlightState = HelicopterFlightState.Landing;
                }
            }
            SetPosition(position);
        }
        StateRevision++;
    }

    private static float MoveTowards(float value, float target, float step) => value + Math.Clamp(target - value, -step, step);
    private void TurnTo(float yaw, float seconds)
    {
        _yaw = MathHelper.WrapAngle(_yaw + Math.Clamp(MathHelper.WrapAngle(yaw - _yaw), -RotationSpeed * seconds, RotationSpeed * seconds));
        SetRotationYDegrees(MathHelper.ToDegrees(_yaw));
    }

    public bool TryAuthorizeShot(double hostTime)
    {
        if (hostTime < _nextAuthorizedShot || !TryConsumeAmmunition()) return false;
        _nextAuthorizedShot = hostTime + AttackCooldown;
        return true;
    }

    public override void PlayShotEffects()
    {
        Vector3 direction = Vector3.TransformNormal(Vector3.Forward,
            Matrix.CreateRotationY(MathHelper.ToRadians(TargetAngleDegrees)) * Transform);
        if (!TryGetMuzzleWorldPosition(out Vector3 muzzle))
            muzzle = TryGetAnimatedPivotWorldTransform("pivot:turret", out Matrix turret)
                ? turret.Translation : Position + Vector3.Up * Height * 0.5f + direction * Length * 0.5f;
        Globals.World.Particles?.EmitRifleMuzzleFlash(muzzle, direction);
    }

    public bool TryConsumeAmmunition()
    {
        if (!CanFireWeapon) return false;
        Ammunition--; StateRevision++;
        return true;
    }

    public override void Update(GameTime gameTime)
    {
        if (_hasRenderState && !Globals.Game.Network.IsHost)
        {
            float blend = 1 - MathF.Exp(-20 * (float)gameTime.ElapsedGameTime.TotalSeconds);
            _renderPosition = Vector3.Lerp(_renderPosition, Position, blend);
            _renderYaw = MathHelper.WrapAngle(_renderYaw + MathHelper.WrapAngle(_yaw - _renderYaw) * blend);
        }
        UpdateUnitVisuals(gameTime);
        if (IsLanded)
        {
            //  easing towards 0
            MainRotorSpeed = RearRotorSpeed * 0.99f;
            RearRotorSpeed = RearRotorSpeed * 0.99f;
        }
        else
        {
            //  easing towards max rotor speeds
            MainRotorSpeed += (MainRotorSpeedMax - MainRotorSpeed) * 0.99f;
            RearRotorSpeed += (RearRotorSpeedMax - RearRotorSpeed) * 0.99f;
        }
    
        float seconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        MainRotorDegree = (MainRotorDegree + seconds * MainRotorSpeed) % 360;
        RearRotorDegree = (RearRotorDegree + seconds * RearRotorSpeed) % 360;

        _meshSet?.SetPivotRotation("pivot:rotor_main", Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.ToRadians(MainRotorDegree)));
        _meshSet?.SetPivotRotation("pivot:rotor_rear", Quaternion.CreateFromAxisAngle(RearRotorAxis, MathHelper.ToRadians(RearRotorDegree)));
        _meshSet?.SetParameter(Mesh.TurretAngle, MathHelper.ToRadians(TargetAngleDegrees));
    }

    public override Matrix GetWorldMatrix() => _hasRenderState && !Globals.Game.Network.IsHost
        ? Matrix.CreateRotationY(_renderYaw) * Matrix.CreateTranslation(_renderPosition) : base.GetWorldMatrix();

    public override void Draw2D(SpriteBatch spriteBatch, Camera camera, Viewport viewport)
    {
        if (!IsSelected) return;
        Vector3 screen = viewport.Project(Position + Vector3.Up * (Height + 1), camera.Projection, camera.View, Matrix.Identity);
        if (screen.Z < 0 || screen.Z > 1) return;
        RenderHelper.DrawTextCentered(spriteBatch, Globals._debugFont,
            $"{FlightState} | Fuel {Fuel:0}/{MaximumFuel:0} | Ammo {Ammunition}/{MaximumAmmunition}",
            new(screen.X, screen.Y), Fuel <= MaximumFuel * 0.2f ? Color.Orange : Color.White);
    }

    public override UnitState GetState() => new(UnitId, StateRevision, StateTypeId, StateVersion,
        JsonSerializer.SerializeToUtf8Bytes(new HelicopterState(Position.X, Position.Y, Position.Z, _yaw, FlightState,
            Fuel, Ammunition, AssignedHelipadId, _landing?.X, _landing?.Y, _landing?.Z)));

    public override void ApplyState(UnitState state)
    {
        if (state.UnitId != UnitId || state.TypeId != StateTypeId || state.Version != StateVersion || state.Revision < StateRevision) return;
        HelicopterState? data = JsonSerializer.Deserialize<HelicopterState>(state.Payload);
        if (data is null || !float.IsFinite(data.X) || !float.IsFinite(data.Y) || !float.IsFinite(data.Z)) return;
        FlightState = data.Flight; Fuel = Math.Clamp(data.Fuel, 0, MaximumFuel); Ammunition = Math.Clamp(data.Ammunition, 0, MaximumAmmunition);
        AssignedHelipadId = data.HelipadId;
        _landing = data.LandingX is float x && data.LandingY is float y && data.LandingZ is float z ? new(x, y, z) : null;
        if (!_hasRenderState)
        {
            _renderPosition = Position;
            _renderYaw = MathF.Atan2(-Transform.Forward.X, -Transform.Forward.Z);
            _hasRenderState = true;
        }
        _yaw = data.Yaw; SetRotationYDegrees(MathHelper.ToDegrees(_yaw)); SetPosition(new(data.X, data.Y, data.Z));
        Globals.World.GameGrid.Remove(this);
        if (AssignedHelipadId is null && (IsLanded || _landing is not null))
            Globals.World.GameGrid.TryPlace(this, _landing ?? Position, IsLanded ? MathHelper.ToDegrees(_yaw) : 0);
        Occupancy!.EntryEnabled = IsLanded;
        StateRevision = state.Revision;
    }
}

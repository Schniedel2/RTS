using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public abstract class Unit : WorldObject
{
    private static readonly VertexPositionColorNormalTexture[] MeshVertices =
    [
        new(new Vector3(-0.5f, 0.0f, -0.5f), Color.SteelBlue, Vector3.Down),
        new(new Vector3(0.5f, 0.0f, -0.5f), Color.SteelBlue, Vector3.Down),
        new(new Vector3(0.5f, 0.0f, 0.5f), Color.SteelBlue, Vector3.Down),
        new(new Vector3(-0.5f, 0.0f, 0.5f), Color.SteelBlue, Vector3.Down),
        new(new Vector3(-0.5f, 1.0f, -0.5f), Color.SteelBlue, Vector3.Up),
        new(new Vector3(0.5f, 1.0f, -0.5f), Color.SteelBlue, Vector3.Up),
        new(new Vector3(0.5f, 1.0f, 0.5f), Color.SteelBlue, Vector3.Up),
        new(new Vector3(-0.5f, 1.0f, 0.5f), Color.SteelBlue, Vector3.Up),
    ];

    private static readonly int[] MeshIndices =
    [
        0, 2, 1, 0, 3, 2,
        4, 5, 6, 4, 6, 7,
        0, 1, 5, 0, 5, 4,
        1, 2, 6, 1, 6, 5,
        2, 3, 7, 2, 7, 6,
        3, 0, 4, 3, 4, 7,
    ];

    public Guid UnitId { get; }
    public float HitPoints { get; private set; }
    public float MaxHitPoints { get; }
    public Guid CreatorPlayerId { get; private set; }
    protected override VertexPositionColorNormalTexture[] Vertices => MeshVertices;
    protected override int[] Indices => MeshIndices;

    public int Length { get; protected set; }
    public int Width { get; protected set; }
    public float Height { get; protected set; }
    public bool IsSelected { get; set; }
    public GotoCommand? CurrentCommand { get; protected set; }
    public virtual IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 0)
    ];
    public override string StateTypeId => "unit";
    public float AttackRange { get; set; } = 12.0f;
    public float AttackCooldown { get; set; } = 0.75f;
    public Guid? AttackTargetId { get; private set; }
    public Vector3? AttackGroundTarget { get; private set; }
    private double _nextShotTime;

    public Unit(
        Vector3 position,
        int length,
        int width,
        float height,
        Guid unitId
        ) : base(position)
    {
        IsNetworkObject = true;
        HitPoints = MaxHitPoints = 100.0f;
        UnitId = unitId;
        Length = length;
        Width = width;
        Height = height;
    }

    internal void SetCreatorPlayer(Guid creatorPlayerId)
    {
        CreatorPlayerId = creatorPlayerId;
    }

    public virtual void ClearCommand()
    {
        CurrentCommand = null;
    }

    public virtual UnitState GetState()
    {
        return new UnitState(
            UnitId,
            Revision: 0,
            StateTypeId,
            StateVersion,
            Array.Empty<byte>());
    }

    public virtual void ApplyState(UnitState state)
    {
        // Units without specialized state intentionally have no payload to apply.
    }

    public virtual void Stop()
    {
        ClearCommand();
        AttackTargetId = null;
        AttackGroundTarget = null;
    }

    // Gameplay mutation: only the host invokes this method.
    public virtual bool OnHit(HitInfo hit)
    {
        HitPoints = Math.Max(0.0f, HitPoints - hit.Damage);
        return HitPoints <= 0.0f;
    }

    // Clients use the host-provided value for display only.
    public void ApplyHitPoints(float hitPoints)
    {
        HitPoints = Math.Clamp(hitPoints, 0.0f, MaxHitPoints);
    }

    public virtual void PlayHitEffects(HitInfo hit)
    {
    }

    public void SetAttackTarget(Guid targetId)
    {
        AttackTargetId = targetId;
        AttackGroundTarget = null;
    }

    public void SetAttackGroundTarget(Vector3 target)
    {
        AttackGroundTarget = target;
        AttackTargetId = null;
    }

    public bool TryQueueShot(double hostTime, out MobileUnit? target)
    {
        target = AttackTargetId is Guid targetId ? Globals.World.Units.FindById(targetId) : null;
        if (target is null)
        {
            AttackTargetId = null;
            return false;
        }

        Vector2 offset = new(target.Position.X - Position.X, target.Position.Z - Position.Z);
        if (offset.LengthSquared() > AttackRange * AttackRange || hostTime < _nextShotTime)
            return false;

        _nextShotTime = hostTime + AttackCooldown;
        return true;
    }

    public bool TryQueueGroundShot(double hostTime, out Vector3 target)
    {
        target = AttackGroundTarget ?? default;
        if (AttackGroundTarget is null)
            return false;

        Vector2 offset = new(target.X - Position.X, target.Z - Position.Z);
        if (offset.LengthSquared() > AttackRange * AttackRange || hostTime < _nextShotTime)
            return false;

        _nextShotTime = hostTime + AttackCooldown;
        return true;
    }

    public Rectangle GetScreenBounds(
        Matrix view,
        Matrix projection,
        Viewport viewport)
    {
        Matrix world = GetWorldMatrix();
        Point minimum = new(int.MaxValue, int.MaxValue);
        Point maximum = new(int.MinValue, int.MinValue);

        foreach (VertexPositionColorNormalTexture vertex in Vertices)
        {
            Vector3 screenPosition = viewport.Project(
                vertex.Position,
                projection,
                view,
                world);
                int screenX = (int)screenPosition.X;
                int screenY = (int)screenPosition.Y;
                minimum.X = Math.Min(minimum.X, screenX);
                minimum.Y = Math.Min(minimum.Y, screenY);
                maximum.X = Math.Max(maximum.X, screenX);
                maximum.Y = Math.Max(maximum.Y, screenY);
        }

        return new Rectangle(
            minimum.X,
            minimum.Y,
            maximum.X - minimum.X + 1,
            maximum.Y - minimum.Y + 1);
    }

    protected override Matrix GetWorldMatrix()
    {
        return Matrix.CreateScale(Width, Height, Length) * Transform;
    }


    public void Select(bool isSelected = true)
    {
        IsSelected = isSelected;
    }    
}

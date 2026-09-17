using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class Soldier : MobileUnit
{
    private readonly AnimationPlayer _animationPlayer;
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Attack, "Attack", 1, 1),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];

    private float _nextIdlePoseTimeer = 0.0f;
    private bool _isMoving = false;

    public Soldier(
        Vector3 position,
        Guid unitId,
        IMovementProfile? movementProfile = null
        ) : base(
            position,
            length: 1,
            width: 1,
            height: 1.8f,
            unitId,
            movementProfile)            
    {
        MoveSpeed = 2.0f;
        RotationSpeed = MathHelper.TwoPi;

        //SetMesh("Soldier-1", deriveDimensions: true);
        SetMesh("Soldier-2", deriveDimensions: true);

        int weapon = Random.Shared.Next(2);
        if (weapon == 0)
            _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["ak47"]);
        else
            _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["breda-m1935pg"]);
        

        _animationPlayer = new AnimationPlayer(_meshSet!.RootMesh.Animations);        
        _animationPlayer.Play("idle");
        _animationPlayer.SetRandomAnimationTime();
        _animationPlayer.Speed = 0.9f + Random.Shared.NextSingle() * 0.2f;
        //_animationPlayer.AddOverlay("pose:idleRifle", weight: 1.0f);        

    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (!_isMoving)
        {
            _nextIdlePoseTimeer -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_nextIdlePoseTimeer <= 0.0f)
            {
                int i = Random.Shared.Next(4);
                if (i == 0)
                    _animationPlayer.AddOverlayTransition("pose:idleRifle", 0.5f);
                if (i == 1)
                    _animationPlayer.AddOverlayTransition("pose:idleRifle2", 0.5f);
                if (i == 2)
                    _animationPlayer.AddOverlayTransition("pose:idleRifle3", 0.5f);
                if (i == 3)
                    _animationPlayer.AddOverlayTransition("pose:idleRifle4", 0.5f);
                    
                _nextIdlePoseTimeer = 3.0f + Random.Shared.NextSingle() * 5.0f;
            }
        }

        bool isMoving = PlannedPath.Count > 0;
        if ((_isMoving != isMoving) && isMoving)
            _animationPlayer.AddOverlayTransition("pose:idleRifle", 0.5f);

        _isMoving = isMoving;
        _animationPlayer.Play(_isMoving ? "run" : "idle");
        //_animationPlayer.AddOverlay("pose:idleRifle", weight: 1.0f);
        _animationPlayer.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    public override void Draw(Effect effect)
    {
        _meshSet?.Draw(effect, GetVisualWorldMatrix(), _animationPlayer.EvaluatePose());
    }
}

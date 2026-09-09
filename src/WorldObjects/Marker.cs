using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public class Marker : WorldObject
{
    private static readonly VertexPositionColorNormalTexture[] MeshVertices =
    [
        new(new Vector3(0.0f, 0.0f, 0.0f), Color.Gold, Vector3.Down),
        new(new Vector3(-0.6f, 1.0f, -0.6f), Color.Gold, Vector3.Up),
        new(new Vector3(0.6f, 1.0f, -0.6f), Color.Gold, Vector3.Up),
        new(new Vector3(0.6f, 1.0f, 0.6f), Color.Gold, Vector3.Up),
        new(new Vector3(-0.6f, 1.0f, 0.6f), Color.Gold, Vector3.Up),
    ];

    private static readonly int[] MeshIndices =
    [
        0, 1, 2,
        0, 2, 3,
        0, 3, 4,
        0, 4, 1,
    ];

    private readonly Vector3 _groundPosition;
    private readonly float _lifetime;
    private float _remainingLifetime;

    public bool IsExpired => _remainingLifetime <= 0.0f;
    public float HoverHeight { get; set; } = 1.0f;

    public Marker(
        Vector3 position,
        float lifetime) : base(position + Vector3.Up * 0.05f)
    {
        _groundPosition = position + Vector3.Up * 0.05f;
        _lifetime = lifetime;
        _remainingLifetime = lifetime;
    }

    public override void Update(GameTime gameTime)
    {
        _remainingLifetime -= (float)gameTime.ElapsedGameTime.TotalSeconds;

        float elapsedLifetime = _lifetime - _remainingLifetime;
        float progress = MathHelper.Clamp(elapsedLifetime / _lifetime, 0.0f, 1.0f);
        float verticalOffset = MathF.Sin(progress * MathHelper.Pi) * HoverHeight;

        SetPosition(_groundPosition + Vector3.Up * verticalOffset);
    }
    
    public override void Draw(Effect effect)
    {        
        RenderHelper.DrawMesh(effect, MeshVertices, MeshIndices);        
    }
}
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public abstract class WorldObject
{
    private readonly GraphicsDevice _graphicsDevice;

    protected abstract VertexPositionColorNormal[] Vertices { get; }
    protected abstract int[] Indices { get; }

    public Matrix Transform { get; protected set; }
    public Vector3 Position => Transform.Translation;

    protected WorldObject(GraphicsDevice graphicsDevice, Vector3 position)
    {
        _graphicsDevice = graphicsDevice;
        Transform = Matrix.CreateTranslation(position);
    }

    public void SetPosition(Vector3 position)
    {
        Matrix transform = Transform;
        transform.Translation = position;
        Transform = transform;
    }

    public void SetTransform(Matrix transform)
    {
        Transform = transform;
    }

    public void Draw(Effect effect)
    {
        effect.Parameters["World"]?.SetValue(GetWorldMatrix());
        DrawMesh(effect);
    }

    public void DrawShadow(Effect effect)
    {
        effect.Parameters["World"]?.SetValue(GetWorldMatrix());
        DrawMesh(effect);
    }

    protected virtual Matrix GetWorldMatrix()
    {
        return Transform;
    }

    private void DrawMesh(Effect effect)
    {
        foreach (EffectPass pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            _graphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                Vertices,
                0,
                Vertices.Length,
                Indices,
                0,
                Indices.Length / 3);
        }
    }
}
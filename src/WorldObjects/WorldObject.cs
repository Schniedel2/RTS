using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public abstract class WorldObject
{

    protected abstract VertexPositionColorNormal[] Vertices { get; }
    protected abstract int[] Indices { get; }

    public Matrix Transform { get; protected set; }
    public Vector3 Position => Transform.Translation;

    protected WorldObject(Vector3 position)
    {
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

    public void Draw(GraphicsDevice graphicsDevice, Effect effect)
    {
        effect.Parameters["World"]?.SetValue(GetWorldMatrix());
        DrawMesh(graphicsDevice, effect);
    }

    public void DrawShadow(GraphicsDevice graphicsDevice,Effect effect)
    {
        effect.Parameters["World"]?.SetValue(GetWorldMatrix());
        DrawMesh(graphicsDevice, effect);
    }

    protected virtual Matrix GetWorldMatrix()
    {
        return Transform;
    }

    private void DrawMesh(GraphicsDevice graphicsDevice, Effect effect)
    {
        foreach (EffectPass pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            graphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                Vertices,
                0,
                Vertices.Length,
                Indices,
                0,
                Indices.Length / 3);
        }
    }

    public abstract void Update(GameTime gameTime);
}
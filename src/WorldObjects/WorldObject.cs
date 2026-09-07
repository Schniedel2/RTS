using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public abstract class WorldObject
{

    protected abstract VertexPositionColorNormal[] Vertices { get; }
    protected abstract int[] Indices { get; }

    public Matrix Transform { get; protected set; }
    public Vector3 Position => Transform.Translation;
    public virtual string StateTypeId => "unit";
    public uint StateRevision { get; set; }
    public bool NetworkStateDirty { get; set; } = true;
    public double NextNetworkUpdateTime { get; set; }
    public virtual int StateVersion => 1;
    public bool IsNetworkObject = false; // disable/enable network synchronization for this object

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

    public virtual void Draw(Effect effect)
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

            Globals.GraphicsDevice.DrawUserIndexedPrimitives(
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
    public virtual void UpdateHost(GameTime gameTime)
    {
        //  is called on the host to update network-related state.
    }
}
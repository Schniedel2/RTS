using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public abstract class WorldObject
{
    protected virtual bool IsTextured => false;

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
        //DrawMesh(effect);
    }

    public virtual void DrawShadow(Effect effect)
    {
        effect.Parameters["World"]?.SetValue(GetWorldMatrix());
        //DrawMesh(effect);
    }

    protected virtual Matrix GetWorldMatrix()
    {
        return Transform;
    }

    /*
    protected void DrawMesh(Effect effect)
    {
        DrawMesh(effect, Vertices, Indices);
    }
    */
    public virtual void Draw2D(SpriteBatch spriteBatch, Camera camera, Viewport viewport)
    {
        
    }

    public abstract void Update(GameTime gameTime);
    public virtual void UpdateHost(GameTime gameTime)
    {
        //  is called on the host to update network-related state.
    }
}

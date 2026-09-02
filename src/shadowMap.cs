using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public class ShadowMap
{
    private readonly GraphicsDevice _graphicsDevice;

    private readonly int _size;

    private RenderTarget2D _renderTarget = null!;

    public Vector3 LightDirection { get; set; }

    public Matrix View { get; private set; }

    public Matrix Projection { get; private set; }

    public RenderTarget2D Texture => _renderTarget;

    public ShadowMap(
        GraphicsDevice graphicsDevice,
        int size = 2048)
    {
        _graphicsDevice = graphicsDevice;
        _size = size;

        _renderTarget =
            new RenderTarget2D(
                _graphicsDevice,
                size,
                size,
                false,
                SurfaceFormat.Single,
                DepthFormat.Depth24);
        
        LightDirection =
            Vector3.Normalize(
                new Vector3(-1.0f, -2.0f, -1.0f));
    }

    public void Update(
        Vector3 terrainCenter,
        float terrainSize,
        float angle)
    {
        // Entfernung der Sonne vom Terrain
        float distance = terrainSize;

        // Höhe der Sonne
        float height = terrainSize * 0.8f;

        // Kreisbahn um das Terrain
        Vector3 lightPosition =
            terrainCenter +
            new Vector3(
                MathF.Cos(angle) * distance,
                height,
                MathF.Sin(angle) * distance);

        // Richtung von der Sonne zum Terrain
        LightDirection =
            Vector3.Normalize(
                terrainCenter - lightPosition);

        // Lichtkamera
        View = Matrix.CreateLookAt(
            lightPosition,
            terrainCenter,
            Vector3.Up);

        // Orthografische Projektion
        Projection =
            Matrix.CreateOrthographic(
                terrainSize,
                terrainSize,
                1.0f,
                terrainSize * 3.0f);
    }
/*
    public void Update(
        Vector3 terrainCenter,
        float terrainSize)
    {
        // Die Sonne steht entgegen der Lichtrichtung.
        Vector3 lightPosition =
            terrainCenter - LightDirection * terrainSize;

        // Die Sonne schaut auf die Mitte der Karte.
        View = Matrix.CreateLookAt(
            lightPosition,
            terrainCenter,
            Vector3.Up);

        // Für die Sonne verwenden wir eine
        // orthografische Projektion.
        float halfSize = terrainSize * 0.5f;

        Projection =
            Matrix.CreateOrthographic(
                terrainSize,
                terrainSize,
                1.0f,
                terrainSize * 3.0f);
    }
*/
    public void Begin()
    {
        _graphicsDevice.SetRenderTarget(_renderTarget);

        _graphicsDevice.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.White,
            1.0f,
            0);
    }

    public void End()
    {
        _graphicsDevice.SetRenderTarget(null);
    }
}

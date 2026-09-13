using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public class ShadowMap
{

    private readonly int _size;

    private RenderTarget2D _renderTarget = null!;

    public Vector3 LightDirection { get; set; }

    public Matrix View { get; private set; }

    public Matrix Projection { get; private set; }

    public RenderTarget2D Texture => _renderTarget;

    public ShadowMap(int size = 2048)
    {
        _size = size;

        _renderTarget =
            new RenderTarget2D(
                Globals.GraphicsDevice,
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

    /// <summary>
    /// Fits the orthographic light camera around the portion of terrain visible
    /// through the player's camera. A conservative border also keeps shadows
    /// from nearby off-screen casters intact.
    /// </summary>
    public bool UpdateForCamera(Camera camera, Viewport viewport, Terrain terrain, float angle)
    {
        Vector3[] visibleGround = new Vector3[4];
        Point[] screenCorners =
        [
            new Point(viewport.X, viewport.Y),
            new Point(viewport.X + viewport.Width, viewport.Y),
            new Point(viewport.X + viewport.Width, viewport.Y + viewport.Height),
            new Point(viewport.X, viewport.Y + viewport.Height)
        ];

        for (int index = 0; index < screenCorners.Length; index++)
        {
            Point corner = screenCorners[index];
            Vector3 nearPoint = viewport.Unproject(
                new Vector3(corner.X, corner.Y, 0.0f), camera.Projection, camera.View, Matrix.Identity);
            Vector3 farPoint = viewport.Unproject(
                new Vector3(corner.X, corner.Y, 1.0f), camera.Projection, camera.View, Matrix.Identity);
            Ray ray = new(nearPoint, Vector3.Normalize(farPoint - nearPoint));
            if (!terrain.TryGetIntersection(ray, out visibleGround[index]))
                return false;
        }

        float minimumX = visibleGround[0].X, maximumX = visibleGround[0].X;
        float minimumZ = visibleGround[0].Z, maximumZ = visibleGround[0].Z;
        Vector3 center = Vector3.Zero;
        foreach (Vector3 point in visibleGround)
        {
            minimumX = MathF.Min(minimumX, point.X);
            maximumX = MathF.Max(maximumX, point.X);
            minimumZ = MathF.Min(minimumZ, point.Z);
            maximumZ = MathF.Max(maximumZ, point.Z);
            center += point;
        }
        center /= visibleGround.Length;

        const float casterBorder = 20.0f;
        float visibleDiagonal = MathF.Sqrt(
            (maximumX - minimumX) * (maximumX - minimumX) +
            (maximumZ - minimumZ) * (maximumZ - minimumZ));
        float projectionSize = MathF.Max(32.0f, visibleDiagonal + casterBorder * 2.0f);
        float lightDistance = projectionSize;
        float lightHeight = projectionSize * 0.8f;
        Vector3 lightOffset = new(
            MathF.Cos(angle) * lightDistance,
            lightHeight,
            MathF.Sin(angle) * lightDistance);
        LightDirection = Vector3.Normalize(-lightOffset);

        // Snap the light-space projection center to a shadow-map texel. This
        // prevents tiny camera movements from making stationary shadows crawl.
        Vector3 lightPosition = center + lightOffset;
        Matrix unsnappedView = Matrix.CreateLookAt(lightPosition, center, Vector3.Up);
        Vector3 centerInLightSpace = Vector3.Transform(center, unsnappedView);
        float worldUnitsPerTexel = projectionSize / _size;
        Vector3 snapInLightSpace = new(
            MathF.Round(centerInLightSpace.X / worldUnitsPerTexel) * worldUnitsPerTexel - centerInLightSpace.X,
            MathF.Round(centerInLightSpace.Y / worldUnitsPerTexel) * worldUnitsPerTexel - centerInLightSpace.Y,
            0.0f);
        Matrix inverseView = Matrix.Invert(unsnappedView);
        Vector3 worldSnap = Vector3.TransformNormal(snapInLightSpace, inverseView);
        center += worldSnap;
        lightPosition += worldSnap;

        View = Matrix.CreateLookAt(lightPosition, center, Vector3.Up);
        Projection = Matrix.CreateOrthographic(
            projectionSize,
            projectionSize,
            1.0f,
            projectionSize * 3.0f);
        return true;
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
        Globals.GraphicsDevice.SetRenderTarget(_renderTarget);

        Globals.GraphicsDevice.Clear(
            ClearOptions.Target | ClearOptions.DepthBuffer,
            Color.White,
            1.0f,
            0);
    }

    public void End()
    {
        Globals.GraphicsDevice.SetRenderTarget(null);
    }
}

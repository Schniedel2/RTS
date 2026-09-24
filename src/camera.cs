using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace RTS;

public class Camera
{
    private MouseState _previousMouseState;
    public float MousePanSpeed { get; set; } = 0.12f;
    public float MouseRotationSpeed { get; set; } = 0.2f;
    public float ScrollZoomSpeed { get; set; } = 0.05f;
    public int EdgeScrollMargin { get; set; } = 20;
    public Vector3 Position { get; private set; }
    public float HeightAboveTerrain { get; private set; } = 22.0f;
    public float MinimumHeightAboveTerrain { get; set; } = 5.0f;
    public float MaximumHeightAboveTerrain { get; set; } = 100.0f;
    public float TerrainFollowSpeed { get; set; } = 8.0f;
    public float YawAngle { get; private set; }
    public float PitchAngle { get; private set; }

    public float MoveSpeed { get; set; } = 30.0f;
    public float RotationSpeed { get; set; } = 11.25f / 4.0f;

    public Matrix View
    {
        get
        {
            Vector3 forward = GetForward();
            return Matrix.CreateLookAt(
                Position,
                Position + forward,
                Vector3.Up);
        }
    }

    public Matrix Projection
    {
        get
        {
            float aspectRatio =
                Globals.GraphicsDevice.Viewport.AspectRatio;

            return Matrix.CreatePerspectiveFieldOfView(
                MathHelper.ToRadians(60),
                aspectRatio,
                0.1f,
                1000.0f);
        }
    }

    public Camera()
    {
        Position = new Vector3(128, HeightAboveTerrain, 128);

        // Blick schräg nach unten
        PitchAngle = -35;
        YawAngle = 0;
    }

    public void CenterOn(Vector2 worldPosition)
    {
        Position = new Vector3(worldPosition.X, Position.Y, worldPosition.Y);
    }

    /// <summary>Centers the camera above a start unit while facing into the map.</summary>
    public void CenterForMatchStart(Vector3 focus, Vector3 mapCenter)
    {
        Position = new Vector3(focus.X, focus.Y + HeightAboveTerrain, focus.Z);
        Vector2 direction = new(mapCenter.X - focus.X, mapCenter.Z - focus.Z);
        if (direction.LengthSquared() > 0.0001f)
        {
            direction.Normalize();
            YawAngle = MathHelper.ToDegrees(MathF.Atan2(-direction.X, -direction.Y));
        }
    }

    public void UpdateMouse(GameTime gameTime)
    {
        Vector3 forward = GetForward();
        forward.Y = 0;
        forward.Normalize();

        Vector3 right = Vector3.Cross(forward, Vector3.Up);
        right.Normalize();

        //  mouse
        MouseState mouse = Mouse.GetState();

        int mouseDeltaX = mouse.X - _previousMouseState.X;
        int mouseDeltaY = mouse.Y - _previousMouseState.Y;

        if (mouse.MiddleButton == ButtonState.Pressed)
        {
            YawAngle += mouseDeltaX * -MouseRotationSpeed;
            PitchAngle -= mouseDeltaY * MouseRotationSpeed;
            PitchAngle = MathHelper.Clamp(PitchAngle, -80.0f, -15.0f);
        }

        int scrollDelta =
            mouse.ScrollWheelValue -
            _previousMouseState.ScrollWheelValue;

        HeightAboveTerrain = MathHelper.Clamp(
            HeightAboveTerrain + scrollDelta * ScrollZoomSpeed,
            MinimumHeightAboveTerrain,
            MaximumHeightAboveTerrain);

        if (mouse.RightButton == ButtonState.Pressed)
        {
            Position -= right * mouseDeltaX * MousePanSpeed;
            Position += forward * mouseDeltaY * MousePanSpeed;
        }

        _previousMouseState = mouse;

    }

    public void UpdateKeyboard(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // -------------------------------------------------
        // Geschwindigkeit
        // -------------------------------------------------

        float speed = MoveSpeed;

        if (keyboard.IsKeyDown(Keys.LeftShift))
            speed *= 3.0f;

        // -------------------------------------------------
        // Rotation
        // -------------------------------------------------

        if (keyboard.IsKeyDown(Keys.Q))
            YawAngle -= speed * RotationSpeed * dt;

        if (keyboard.IsKeyDown(Keys.E))
            YawAngle += speed * RotationSpeed * dt;

        // -------------------------------------------------
        // Bewegung relativ zur Kamera
        // -------------------------------------------------

        Vector3 up = new Vector3(0, 0, 1);

        Vector3 forward = GetForward();
        forward.Y = 0;
        forward.Normalize();

        Vector3 right = Vector3.Cross(forward, Vector3.Up);
        right.Normalize();

        Vector3 movement = Vector3.Zero;

        if (keyboard.IsKeyDown(Keys.W))
            movement += forward;

        if (keyboard.IsKeyDown(Keys.S))
            movement -= forward;

        if (keyboard.IsKeyDown(Keys.D))
            movement += right;

        if (keyboard.IsKeyDown(Keys.A))
            movement -= right;

        if (movement != Vector3.Zero)
        {
            movement.Normalize();
            Position += movement * speed * dt;
        }

        // -------------------------------------------------
        // Höhe / Zoom
        // -------------------------------------------------

        if (keyboard.IsKeyDown(Keys.R))
            HeightAboveTerrain += speed * dt;

        if (keyboard.IsKeyDown(Keys.F))
            HeightAboveTerrain -= speed * dt;

        HeightAboveTerrain = MathHelper.Clamp(
            HeightAboveTerrain,
            MinimumHeightAboveTerrain,
            MaximumHeightAboveTerrain);

        if (keyboard.IsKeyDown(Keys.PageUp))
            PitchAngle += 1;
        if (keyboard.IsKeyDown(Keys.PageDown))
            PitchAngle -= 1;

    }

    /// <summary>Keeps the current zoom distance while smoothly following terrain elevation.</summary>
    public void UpdateTerrainHeight(GameTime gameTime, Terrain terrain)
    {
        int terrainX = Math.Clamp((int)MathF.Floor(Position.X), 0, terrain.Width - 1);
        int terrainZ = Math.Clamp((int)MathF.Floor(Position.Z), 0, terrain.Height - 1);
        float targetY = terrain.GetHeight(terrainX, terrainZ) + HeightAboveTerrain;
        float seconds = Math.Max(0.0f, (float)gameTime.ElapsedGameTime.TotalSeconds);
        float blend = 1.0f - MathF.Exp(-TerrainFollowSpeed * seconds);
        Position = new Vector3(Position.X, MathHelper.Lerp(Position.Y, targetY, blend), Position.Z);
    }

    private Vector3 GetForward()
    {
        Matrix rotation =
            Matrix.CreateRotationX(MathHelper.ToRadians(PitchAngle)) *
            Matrix.CreateRotationY(MathHelper.ToRadians(YawAngle));

        return Vector3.Transform(
            Vector3.Forward,
            rotation);
    }

}

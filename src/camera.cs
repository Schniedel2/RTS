using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace RTS;

public class Camera
{
    private MouseState _previousMouseState;
    public float MousePanSpeed { get; set; } = 0.12f;
    public float MouseRotationSpeed { get; set; } = 0.2f;
    public float ScrollZoomSpeed { get; set; } = 0.05f;
    public int EdgeScrollMargin { get; set; } = 20;
    public Vector3 Position { get; private set; }
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
        Position = new Vector3(128, 60, 128);

        // Blick schräg nach unten
        PitchAngle = -35;
        YawAngle = 0;
    }

    public void CenterOn(Vector2 worldPosition)
    {
        Position = new Vector3(worldPosition.X, Position.Y, worldPosition.Y);
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

        Position += Vector3.Up * scrollDelta * ScrollZoomSpeed;

        if (mouse.RightButton == ButtonState.Pressed)
        {
            Position -= right * mouseDeltaX * MousePanSpeed;
            Position += forward * mouseDeltaY * MousePanSpeed;
        }

        _previousMouseState = mouse;

        // Nicht in den Boden fliegen
        if (Position.Y < 5)
            Position = new Vector3(Position.X, 5, Position.Z);
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
            Position += Vector3.Up * speed * dt;

        if (keyboard.IsKeyDown(Keys.F))
            Position -= Vector3.Up * speed * dt;

        if (keyboard.IsKeyDown(Keys.PageUp))
            PitchAngle += 1;
        if (keyboard.IsKeyDown(Keys.PageDown))
            PitchAngle -= 1;

        // Nicht in den Boden fliegen
        if (Position.Y < 5)
            Position = new Vector3(Position.X, 5, Position.Z);
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

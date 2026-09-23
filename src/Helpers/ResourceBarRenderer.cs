using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public static class ResourceBarRenderer
{
    public static void Draw(SpriteBatch spriteBatch, Camera camera, Viewport viewport,
        Unit unit, float amount, float capacity, Color color)
    {
        if (capacity <= 0.0f || (!unit.IsSelected && amount <= 0.0f)) return;
        Vector3 screen = viewport.Project(unit.Position + Vector3.Up * (unit.Height + 1.2f),
            camera.Projection, camera.View, Matrix.Identity);
        if (screen.Z < 0.0f || screen.Z > 1.0f) return;
        const int width = 76, height = 9;
        Rectangle outer = new((int)screen.X - width / 2, (int)screen.Y, width, height);
        Globals.RenderHelper.DrawRectangle(spriteBatch, outer, Color.Black * 0.7f, Color.White, 1);
        int fillWidth = (int)MathF.Round((width - 2) * MathHelper.Clamp(amount / capacity, 0.0f, 1.0f));
        if (fillWidth > 0)
            Globals.RenderHelper.DrawRectangle(spriteBatch,
                new Rectangle(outer.X + 1, outer.Y + 1, fillWidth, height - 2), color, color, 1);
        RenderHelper.DrawTextCentered(spriteBatch, Globals._debugFont,
            $"{MathF.Floor(amount):0} / {MathF.Floor(capacity):0}",
            new Vector2(screen.X, screen.Y - 10), color);
    }
}

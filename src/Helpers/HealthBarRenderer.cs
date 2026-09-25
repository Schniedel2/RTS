using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public enum HealthBarDisplayMode
{
    Off,
    Always,
    Selected,
    Damaged
}

public enum HealthInformationLevel
{
    Basic,
    Detailed
}

public static class HealthBarRenderer
{
    private const int Height = 7;

    public static bool ShouldDraw(Unit unit, HealthBarDisplayMode mode) =>
        unit.MaxHitPoints > 0.0f && unit.HitPoints > 0.0f && !unit.IsDying && mode switch
        {
            HealthBarDisplayMode.Always => true,
            HealthBarDisplayMode.Selected => unit.IsSelected,
            HealthBarDisplayMode.Damaged => unit.HitPoints < unit.MaxHitPoints,
            _ => false
        };

    public static HealthInformationLevel GetInformationLevel(
        Army? viewerArmy,
        Guid? targetArmyId,
        Vector3 targetPosition)
    {
        if (viewerArmy is null)
            return HealthInformationLevel.Basic;
        // An army always knows the exact condition of its own units. Foreign
        // health requires global or local intelligence coverage.
        return targetArmyId == viewerArmy.Id ||
            viewerArmy.Perks.HasAt(PerkType.DetailedHealth, targetPosition)
                ? HealthInformationLevel.Detailed
                : HealthInformationLevel.Basic;
    }

    public static float GetFillFraction(Unit unit, HealthInformationLevel informationLevel) =>
        informationLevel == HealthInformationLevel.Detailed
            ? MathHelper.Clamp(unit.HitPoints / Math.Max(unit.MaxHitPoints, 1.0f), 0.0f, 1.0f)
            : 1.0f;

    public static void Draw(SpriteBatch spriteBatch, Camera camera, Viewport viewport, Unit unit,
        HealthInformationLevel informationLevel)
    {
        if (!ShouldDraw(unit, Globals.HealthBarDisplayMode))
            return;

        Vector3 depth = viewport.Project(unit.Position, camera.Projection, camera.View, Matrix.Identity);
        if (depth.Z < 0.0f || depth.Z > 1.0f)
            return;

        Rectangle bounds = unit.GetScreenBounds(camera.View, camera.Projection, viewport);
        int width = Math.Clamp((int)MathF.Round(bounds.Width * 0.7f), 30, 80);
        var outer = new Rectangle(bounds.Center.X - width / 2, bounds.Top - Height - 3, width, Height);
        if (outer.Right < 0 || outer.Left >= viewport.Width || outer.Bottom < 0 || outer.Top >= viewport.Height)
            return;

        float health = MathHelper.Clamp(unit.HitPoints / unit.MaxHitPoints, 0.0f, 1.0f);
        Color color = health > 0.6f ? Color.LimeGreen : health > 0.3f ? Color.Orange : Color.Red;
        Globals.RenderHelper.DrawRectangle(spriteBatch, outer, Color.Black * 0.75f, Color.Black, 1);

        int fillWidth = (int)MathF.Round((width - 2) * GetFillFraction(unit, informationLevel));
        if (fillWidth > 0)
            Globals.RenderHelper.DrawRectangle(spriteBatch,
                new Rectangle(outer.X + 1, outer.Y + 1, fillWidth, Height - 2), color, color, 1);
    }
}

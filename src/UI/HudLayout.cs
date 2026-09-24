using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public enum HudLayoutMode
{
    Game,
    Editor,
    Minimal
}

public readonly record struct HudLayout(
    HudLayoutMode Mode,
    Rectangle Minimap,
    Rectangle StatusPanel,
    Rectangle ActionPanel,
    bool ShowMinimap,
    bool ShowStatusPanel,
    bool ShowActionPanel)
{
    private const int Margin = 12;
    private const int MinimapSize = 220;
    private const int StatusHeight = 66;
    private const int ActionWidth = 536;
    private const int ActionHeight = 216;

    public static HudLayout Calculate(Viewport viewport, HudLayoutMode mode)
    {
        Rectangle minimap = new(
            viewport.Width - MinimapSize - Margin,
            viewport.Height - MinimapSize - Margin,
            MinimapSize,
            MinimapSize);
        Rectangle status = new(
            viewport.Width - MinimapSize - Margin,
            Margin,
            MinimapSize,
            StatusHeight);
        Rectangle actions = new(
            0,
            viewport.Height - ActionHeight,
            Math.Min(ActionWidth, viewport.Width),
            ActionHeight);

        return mode switch
        {
            HudLayoutMode.Game => new(mode, minimap, status, actions, true, true, true),
            HudLayoutMode.Editor => new(mode, minimap, Rectangle.Empty, actions, true, false, true),
            _ => new(mode, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, false, false, false)
        };
    }
}

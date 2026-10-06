using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RTS.Network;
using System;

namespace RTS;

public sealed class SatelliteReconPanel
{
    private MouseState _previous;
    private bool _hovered;
    public bool Update(Rectangle bounds, Army? army, GameWorld world, bool inputEnabled)
    {
        MouseState mouse = Mouse.GetState();
        _hovered = inputEnabled && bounds.Contains(mouse.Position);
        if (_hovered && army is not null && !Globals.IsSpectator &&
            mouse.LeftButton == ButtonState.Pressed && _previous.LeftButton != ButtonState.Pressed &&
            Globals.Game.Armies.CanControl(Globals.Game.Network.LocalPeerId, army.Id) &&
            SatelliteRecon.Ready(world, army))
            _ = new PlayerCommandService(Globals.Game.Network, Globals.Game.Network.LocalPeerId)
                .SatelliteReconAsync(army.Id);
        _previous = mouse;
        return _hovered;
    }

    public void Draw(SpriteBatch batch, Rectangle bounds, Army army, GameWorld world)
    {
        string? missing = SatelliteRecon.MissingRequirement(world, army);
        float cooldown = army.SatelliteRecon.Cooldown;
        string status = army.SatelliteRecon.ActiveSeconds > 0
            ? $"Active: {Math.Ceiling(army.SatelliteRecon.ActiveSeconds)}s"
            : cooldown > 0 ? $"{((int)Math.Ceiling(cooldown) / 60):00}:{Math.Ceiling(cooldown) % 60:00}" : "Ready";
        if (missing is not null && army.SatelliteRecon.ActiveSeconds <= 0) status = cooldown > 0 ? $"Paused: {status}" : "Locked";
        Color color = missing is null ? Color.LightGreen : Color.Gray;
        batch.Draw(Globals._whiteTexture, bounds, Color.Black * 0.85f);
        batch.DrawString(Globals._debugFont, "Satellite Recon", new(bounds.X + 10, bounds.Y + 6), color);
        batch.DrawString(Globals._debugFont, status, new(bounds.X + 10, bounds.Y + 33), color);
        if (_hovered)
        {
            string hint = missing ?? "Reveal entire map for 5s; cooldown 180s";
            Vector2 size = Globals._debugFont.MeasureString(hint);
            Rectangle tooltip = new(Math.Max(0, bounds.Right - (int)size.X - 16), bounds.Bottom + 4,
                (int)size.X + 16, (int)size.Y + 12);
            batch.Draw(Globals._whiteTexture, tooltip, Color.Black * 0.95f);
            batch.DrawString(Globals._debugFont, hint, new(tooltip.X + 8, tooltip.Y + 6), Color.White);
        }
    }
}

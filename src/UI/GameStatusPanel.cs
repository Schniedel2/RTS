using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public sealed class GameStatusPanel
{
    private const int Padding = 10;

    public void Draw(SpriteBatch spriteBatch, Rectangle bounds, Army army, GameWorld world)
    {
        spriteBatch.Draw(Globals._whiteTexture, bounds, Color.Black * 0.78f);
        spriteBatch.Draw(Globals._whiteTexture,
            new Rectangle(bounds.X, bounds.Bottom - 2, bounds.Width, 2), new Color(65, 105, 80));

        ArmyPowerStatus power = ArmyPowerStatus.Calculate(world.Units.Units, army.Id);
        spriteBatch.DrawString(Globals._debugFont, $"Resources: {army.Resources:N0}",
            new Vector2(bounds.X + Padding, bounds.Y + 8), new Color(120, 235, 145));

        string sign = power.Balance >= 0 ? "+" : "";
        Color powerColor = power.HasEnoughPower ? new Color(255, 220, 90) : new Color(255, 95, 75);
        spriteBatch.DrawString(Globals._debugFont,
            $"Power: {power.Produced} / {power.Required}  ({sign}{power.Balance})",
            new Vector2(bounds.X + Padding, bounds.Y + 35), powerColor);
    }
}

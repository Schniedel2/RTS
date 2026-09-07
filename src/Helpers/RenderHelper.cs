using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public class RenderHelper
{
    private readonly Texture2D _whiteTexture;

    public RenderHelper(GraphicsDevice graphicsDevice)
    {
        _whiteTexture = new Texture2D(graphicsDevice, 1, 1);
        _whiteTexture.SetData([Color.White]);
    }

    public void DrawRectangle(
        SpriteBatch spriteBatch,
        Rectangle rectangle,
        Color fillColor,
        Color borderColor,
        int borderThickness = 1)
    {
        spriteBatch.Draw(_whiteTexture, rectangle, fillColor);
        
        spriteBatch.Draw(
            _whiteTexture,
            new Rectangle(
                rectangle.Left,
                rectangle.Top,
                rectangle.Width,
                borderThickness),
                borderColor);

        spriteBatch.Draw(
            _whiteTexture,
            new Rectangle(
                rectangle.Left,
                rectangle.Bottom - borderThickness,
                rectangle.Width,
                borderThickness),
                borderColor);

        spriteBatch.Draw(
            _whiteTexture,
            new Rectangle(
                rectangle.Left,
                rectangle.Top,
                borderThickness,
                rectangle.Height),
                borderColor);

        spriteBatch.Draw(
            _whiteTexture,
            new Rectangle(
                rectangle.Right - borderThickness,
                rectangle.Top,
                borderThickness,
                rectangle.Height),
                borderColor);
    }

    public static void DrawTooltip(SpriteBatch spriteBatch, string text, int x, int y)
    {
        for (int offsetX = -1; offsetX <= 1; offsetX++)
        {
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                spriteBatch.DrawString(
                    Globals.TooltipFont,
                    text,
                    new Vector2(x + offsetX, y + offsetY),
                    Color.Black);
            }
        }
        spriteBatch.DrawString(
            Globals.TooltipFont,
            text,
            new Vector2(x, y),
            Color.White);
    }

    public void DrawTextCentered(
        SpriteBatch spriteBatch,
        SpriteFont font,
        string text,
        Vector2 center,
        Color color)
    {
        Vector2 position = center - font.MeasureString(text) * 0.5f;
        spriteBatch.DrawString(font, text, position + Vector2.One, Color.Black);
        spriteBatch.DrawString(font, text, position, color);
    }
}

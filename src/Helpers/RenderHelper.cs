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
}
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace RTS;

public class MarkerHandler
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly List<Marker> _markers = [];

    public MarkerHandler(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }

    public void ShowGotoMarker(Vector3 position)
    {
        _markers.Add(new Marker(_graphicsDevice, position, lifetime: 1.0f));
    }

    public void Update(GameTime gameTime)
    {
        for (int index = _markers.Count - 1; index >= 0; index--)
        {
            Marker marker = _markers[index];
            marker.Update(gameTime);

            if (marker.IsExpired)
                _markers.RemoveAt(index);
        }
    }

    public void Draw(Effect effect)
    {
        foreach (Marker marker in _markers)
            marker.Draw(effect);
    }
}
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace RTS;

public class MarkerHandler
{
    private readonly List<Marker> _markers = [];

    public MarkerHandler()
    {
    }

    public void ShowGotoMarker(Vector3 position)
    {
        _markers.Add(new Marker(position, lifetime: 1.0f));
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

    public void Draw(GraphicsDevice graphicsDevice, Effect effect)
    {
        foreach (Marker marker in _markers)
            marker.Draw(graphicsDevice, effect);
    }
}
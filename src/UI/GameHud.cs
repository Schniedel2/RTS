using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public sealed class GameHud
{
    private readonly Minimap _minimap;
    private readonly ActionPanel _actionPanel;
    private readonly GameStatusPanel _statusPanel = new();
    private float _minimapRefreshElapsed;

    public HudLayout Layout { get; private set; }

    public GameHud(GraphicsDevice graphicsDevice, GameWorld world, Texture2D actionIcons)
    {
        _minimap = new Minimap(graphicsDevice, world, world.Visibility);
        _actionPanel = new ActionPanel(actionIcons);
    }

    public bool Update(GameTime gameTime, Camera camera, Viewport viewport,
        IReadOnlyList<Unit> selectedUnits, Army? localArmy, bool editorMode, bool inputEnabled)
    {
        Layout = HudLayout.Calculate(viewport,
            editorMode ? HudLayoutMode.Editor : HudLayoutMode.Game);

        bool minimapConsumed = Layout.ShowMinimap &&
            _minimap.Update(camera, Layout.Minimap, inputEnabled);
        bool actionPanelConsumed = Layout.ShowActionPanel &&
            _actionPanel.Update(selectedUnits, Layout.ActionPanel, inputEnabled);

        _minimapRefreshElapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (localArmy is not null && _minimapRefreshElapsed >= 0.2f)
        {
            _minimapRefreshElapsed %= 0.2f;
            _minimap.Refresh(localArmy.Id);
        }
        return minimapConsumed || actionPanelConsumed;
    }

    public void Draw(SpriteBatch spriteBatch, Camera camera, Army? localArmy, GameWorld world)
    {
        if (Layout.ShowMinimap)
            _minimap.Draw(spriteBatch, camera, Layout.Minimap);
        if (Layout.ShowStatusPanel && localArmy is not null)
            _statusPanel.Draw(spriteBatch, Layout.StatusPanel, localArmy, world);
        if (Layout.ShowActionPanel)
            _actionPanel.Draw(spriteBatch);
    }

    public void Reset()
    {
        _minimap.Reset();
        _minimapRefreshElapsed = 0.0f;
    }
}

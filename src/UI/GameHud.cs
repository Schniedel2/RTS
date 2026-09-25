using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace RTS;

public sealed class GameHud
{
    private readonly Minimap _minimap;
    private readonly ActionPanel _actionPanel;
    private readonly GameStatusPanel _statusPanel = new();
    private readonly GameWorld _world;
    private float _minimapRefreshElapsed;
    private KeyboardState _previousKeyboardState;

    public HudLayout Layout { get; private set; }

    public GameHud(GraphicsDevice graphicsDevice, GameWorld world, Texture2D actionIcons)
    {
        _world = world;
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
        KeyboardState keyboard = Keyboard.GetState();
        bool homeConsumed = false;
        if (inputEnabled && !editorMode && localArmy is not null &&
            keyboard.IsKeyDown(Keys.H) && !_previousKeyboardState.IsKeyDown(Keys.H) &&
            localArmy.Perks.TryGetNearestSourcePosition(PerkType.Home, camera.Position,
                out Vector3 homePosition))
        {
            camera.CenterOn(homePosition, _world.Terrain);
            homeConsumed = true;
        }
        _previousKeyboardState = keyboard;

        _minimapRefreshElapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (localArmy is not null && _minimapRefreshElapsed >= 0.2f)
        {
            _minimapRefreshElapsed %= 0.2f;
            _minimap.Refresh(localArmy.Id);
        }
        return minimapConsumed || actionPanelConsumed || homeConsumed;
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

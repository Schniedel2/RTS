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
    private readonly ProductionQueuePanel _productionPanel = new();
    private readonly GameWorld _world;
    private float _minimapRefreshElapsed;
    private bool _showMinimap;
    private KeyboardState _previousKeyboardState;

    public HudLayout Layout { get; private set; }
    public Dictionary<int, SelectionGroup> SelectionGroups { get; } = new();

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
        _showMinimap = Layout.ShowMinimap &&
            (editorMode || localArmy?.Perks.Has(PerkType.Minimap) == true);

        bool minimapConsumed = _showMinimap &&
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

        Keys[] groupKeys = {Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9};
        
        foreach (Keys groupKey in groupKeys)
        {            
            if (keyboard.IsKeyDown(groupKey) && !_previousKeyboardState.IsKeyDown(groupKey))
            {
                //  group-key pressed
                int groupNum = groupKey - Keys.D0;
                if (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl))
                {
                    RecreateSelectionGroup(groupNum, Globals.LocalPlayer.SelectedUnits);
                }
                else
                {
                    if (SelectionGroups.TryGetValue(groupNum, out SelectionGroup? group))
                    {
                        Globals.LocalPlayer.SetUnitSelection(group.Units);
                    }
                }
            }
        }
                
        _previousKeyboardState = keyboard;

        _minimapRefreshElapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_showMinimap && localArmy is not null && _minimapRefreshElapsed >= 0.2f)
        {
            _minimapRefreshElapsed %= 0.2f;
            _minimap.Refresh(localArmy.Id);
        }
        return minimapConsumed || actionPanelConsumed || homeConsumed;
    }

    public void Draw(SpriteBatch spriteBatch, Camera camera, Army? localArmy, GameWorld world)
    {
        if (_showMinimap)
            _minimap.Draw(spriteBatch, camera, Layout.Minimap);
        if (Layout.ShowStatusPanel && localArmy is not null)
            _statusPanel.Draw(spriteBatch, Layout.StatusPanel, localArmy, world);
        if (Layout.ShowProductionPanel && localArmy is not null)
            _productionPanel.Draw(spriteBatch, Layout.ProductionPanel, localArmy, world);
        if (Layout.ShowActionPanel)
            _actionPanel.Draw(spriteBatch);
    }

    public void Reset()
    {
        _minimap.Reset();
        _minimapRefreshElapsed = 0.0f;
        _showMinimap = false;
    }

    private void DeleteSelectionGroup(int groupNum)
    {
        if (!SelectionGroups.TryGetValue(groupNum, out SelectionGroup? group))
            return;
        SelectionGroups.Remove(groupNum);
        foreach (var unit in group.Units)
            unit.NotifyRemovedFromSelectionGroup(groupNum);
    }
    public void RecreateSelectionGroup(int groupNum, IReadOnlyList<Unit> selectedUnits)
    {
        DeleteSelectionGroup(groupNum);
        SelectionGroups[groupNum] = new SelectionGroup(selectedUnits);
        foreach (var unit in selectedUnits)
            unit.NotifyAddedToSecetionGroup(groupNum);
    }
}

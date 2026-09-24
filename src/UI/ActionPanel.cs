using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public sealed class ActionPanel
{
    private const int IconSize = 64;
    private const int ButtonSize = 64;
    private const int Padding = 12;
    private const int ButtonsPerRow = 8;

    private readonly Texture2D _iconSheet;
    private readonly Texture2D _pixel;
    private readonly List<(Rectangle Bounds, UnitAction Action)> _buttons = [];
    private readonly HashSet<UnitAction> _disabledActions = [];
    private MouseState _previousMouseState;
    private UnitAction? _activeAction;
    private UnitAction? _hoverAction;
    private bool _isMouseOnPanel = false;
    private string _tooltipText = "";
    private Rectangle _panelBounds;

    public ActionPanel(Texture2D iconSheet)
    {
        _iconSheet = iconSheet;
        _pixel = new Texture2D(Globals.GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
    }

    public bool Update(IReadOnlyList<Unit> selectedUnits, Rectangle availableBounds, bool inputEnabled = true)
    {
        _tooltipText = "";
        _isMouseOnPanel = false;
        _buttons.Clear();
        _disabledActions.Clear();
        if (selectedUnits.Count == 0)
        {
            _activeAction = null;
            _previousMouseState = Mouse.GetState();
            return false;
        }

        IEnumerable<UnitAction> actions = selectedUnits
            .SelectMany(unit => unit.Actions)
            .GroupBy(action => action.Name)
            .Where(group => group.Count() == selectedUnits.Count)
            .Select(group => group.First());

        List<UnitAction> availableActions = actions.ToList();
        if (_activeAction is null)
            if (availableActions.Count > 0)
                _activeAction = availableActions[0];

        int index = 0;
        foreach (UnitAction action in availableActions)
        {
            if (action.ResourceCost > 0 && selectedUnits.FirstOrDefault()?.ArmyId is Guid armyId &&
                (Globals.Game.Armies.Find(armyId)?.Resources ?? 0) < action.ResourceCost)
                _disabledActions.Add(action);
            _buttons.Add((Rectangle.Empty, action));
            index++;
        }

        MouseState mouse = Mouse.GetState();

        int panelHeight = Padding * 2 + ((_buttons.Count + ButtonsPerRow - 1) / ButtonsPerRow) * ButtonSize;
        _panelBounds = new(availableBounds.X, availableBounds.Bottom - panelHeight,
            Math.Min(availableBounds.Width, Padding * 2 + ButtonsPerRow * ButtonSize), panelHeight);
        for (int buttonIndex = 0; buttonIndex < _buttons.Count; buttonIndex++)
        {
            (Rectangle _, UnitAction action) = _buttons[buttonIndex];
            int row = buttonIndex / ButtonsPerRow;
            int column = buttonIndex % ButtonsPerRow;
            _buttons[buttonIndex] = (new Rectangle(
                _panelBounds.X + Padding + column * ButtonSize,
                _panelBounds.Y + Padding + row * ButtonSize,
                ButtonSize, ButtonSize), action);
        }
        if (!inputEnabled || !_panelBounds.Contains(mouse.Position))
        {
            _isMouseOnPanel = false;
            _previousMouseState = mouse;
            return false;
        }

        _isMouseOnPanel = true;
        _hoverAction = null;
        foreach ((Rectangle bounds, UnitAction action) in _buttons)
        {
            if (bounds.Contains(mouse.Position))
            {
                _hoverAction = action;
                _tooltipText = _hoverAction!.Name;
                if (_hoverAction.ResourceCost > 0)
                    _tooltipText += $" ({_hoverAction.ResourceCost} resources)";
                if (_hoverAction.Type == UnitActionType.TilePreview)
                    _tooltipText = $"{_hoverAction!.Name} ({Globals.LocalPlayer._currentTerrainTile})";
            }
        }

        if (_hoverAction is not null)
        {
            if (mouse.LeftButton == ButtonState.Pressed &&
                _previousMouseState.LeftButton == ButtonState.Released)
            {       
                if (!_disabledActions.Contains(_hoverAction) && Globals.LocalPlayer.SelectAction(_hoverAction, false))
                    _activeAction = _hoverAction;
            }
            if (mouse.RightButton == ButtonState.Pressed &&
                _previousMouseState.RightButton == ButtonState.Released)
            {       
                if (!_disabledActions.Contains(_hoverAction) && Globals.LocalPlayer.SelectAction(_hoverAction, true))
                    _activeAction = _hoverAction;
            }
        }

        _previousMouseState = mouse;
        return true;
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (_buttons.Count == 0)
            return;

        spriteBatch.Draw(_pixel, _panelBounds, Color.Black * 0.75f);

        foreach ((Rectangle localBounds, UnitAction action) in _buttons)
        {
            Rectangle bounds = localBounds;
            Rectangle iconBounds = new(
                bounds.X + (ButtonSize - IconSize) / 2,
                bounds.Y + (ButtonSize - IconSize) / 2,
                IconSize,
                IconSize);

            int iconColumn = action.IconColumn;
            int iconRow = action.IconRow;

            if (action.Type == UnitActionType.TilePreview)
            {
                switch (Globals.LocalPlayer._currentTerrainTile)
                {
                    case TerrainTile.Grass:
                        iconColumn = 8;
                        iconRow = 8;
                        break;
                    case TerrainTile.Sand:
                        iconColumn = 5;
                        iconRow = 8;
                        break;
                    case TerrainTile.Dirt:
                        iconColumn = 5;
                        iconRow = 8;
                        break;
                    case TerrainTile.Rock:
                        iconColumn = 9;
                        iconRow = 8;
                        break;
                }
            }
                
            Rectangle source = new(
                iconColumn * IconSize,
                iconRow * IconSize,
                IconSize,
                IconSize);

            // TODO: if action is disabled: spriteBatch.Draw(_pixel, bounds, Color.DimGray);
            spriteBatch.Draw(_iconSheet, iconBounds, source,
                _disabledActions.Contains(action) ? Color.DimGray : Color.White);

            if (_activeAction is not null)
                if (action == _activeAction)
                {
                    //  render "selected"-Border
                    Rectangle source2 = new(
                        IconSize * 1,
                        IconSize * 0,
                        IconSize,
                        IconSize);
                    spriteBatch.Draw(_iconSheet, iconBounds, source2, Color.White);
                }
            
        }

        if (_isMouseOnPanel)
            RenderHelper.DrawTooltip(spriteBatch, _tooltipText, _previousMouseState.Position.X + 24, _previousMouseState.Position.Y + 16);
    }
}

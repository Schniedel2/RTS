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
    private readonly Dictionary<UnitAction, int> _actionCosts = [];
    private readonly Dictionary<UnitAction, IReadOnlyList<PerkType>> _missingPerks = [];
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
        _actionCosts.Clear();
        _missingPerks.Clear();
        selectedUnits = PlayerHandler.ControllableUnits(selectedUnits);
        if (selectedUnits.Count == 0)
        {
            _activeAction = null;
            _previousMouseState = Mouse.GetState();
            return false;
        }

        IEnumerable<UnitAction> actions = selectedUnits
            .SelectMany(unit => unit.Actions)
            .GroupBy(action => (action.Type, action.TargetObjectName, action.MarkerType))
            .Select(group => group.First());

        List<UnitAction> availableActions = actions.ToList();
        _activeAction = Globals.LocalPlayer.ActiveAction;

        int index = 0;
        foreach (UnitAction action in availableActions)
        {
            var recipients = PlayerHandler.Recipients(selectedUnits, action);
            if (PlayerHandler.SingleActor(action) && recipients.Count != 1) _disabledActions.Add(action);
            PurchaseQuote? quote = GetActionQuote(action, recipients);
            int cost = quote?.FinalPrice ?? action.ResourceCost;
            _actionCosts[action] = cost;
            if (quote is not null)
                _missingPerks[action] = quote.MissingPerks;
            if (quote is { IsAvailable: false } ||
                cost > 0 && selectedUnits.FirstOrDefault()?.ArmyId is Guid armyId &&
                (Globals.Game.Armies.Find(armyId)?.Resources ?? 0) < cost)
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
                int count = PlayerHandler.Recipients(selectedUnits, _hoverAction).Count;
                _tooltipText = $"{_hoverAction.Name} ({count}/{selectedUnits.Count})";
                if (PlayerHandler.SingleActor(_hoverAction) && count != 1) _tooltipText += " - Select one eligible unit";
                if (_actionCosts.GetValueOrDefault(_hoverAction) is int cost && cost > 0)
                    _tooltipText += $" ({cost} resources)";
                if (_missingPerks.GetValueOrDefault(_hoverAction) is { Count: > 0 } missing)
                    _tooltipText += $" - Requires: {string.Join(", ", missing.Select(GetPerkDisplayName))}";
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

    private static PurchaseQuote? GetActionQuote(UnitAction action, IReadOnlyList<Unit> selectedUnits)
    {
        Unit? producer = selectedUnits.FirstOrDefault();
        PurchasableType? type = action.Type switch
        {
            UnitActionType.Build => PurchasableType.Building,
            UnitActionType.TrainUnit => PurchasableType.Unit,
            UnitActionType.Research => PurchasableType.Research,
            _ => null
        };
        if (type is null || string.IsNullOrWhiteSpace(action.TargetObjectName))
            return null;

        return Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            type.Value,
            action.TargetObjectName,
            producer?.ArmyId,
            producer?.UnitId));
    }

    private static string GetPerkDisplayName(PerkType perk) => perk switch
    {
        PerkType.BaseEstablished => "Base",
        PerkType.AirTechnology => "Air Technology",
        _ => perk.ToString()
    };

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

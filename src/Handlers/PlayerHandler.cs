using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System;
using System.Linq;

namespace RTS;

public enum ToolShape
{
    Circle,
    Square,
    Dither
}

public class PlayerHandler
{    
    private readonly GameWorld _map;
    private readonly MarkerHandler _markerHandler;
    private readonly List<MobileUnit> _selectedUnits = [];
    private MouseState _previousMouseState;
    private Point _selectionStart;

    public IReadOnlyList<MobileUnit> SelectedUnits => _selectedUnits;
    public UnitAction? ActiveAction { get; private set; }
    private Rectangle _currentSelectionRect;
    private bool _isSelectingUnits;
    public Vector3 MouseWorldPosition { get; private set; }
    private bool IsMouseOnTerrain = false;

    private int _toolSize;
    private ToolShape _toolShape;    
    private double _nextAllowedActionTime;
    public TerrainTile _currentTerrainTile = TerrainTile.Grass;

    public bool SelectAction(UnitAction action, bool alternateAction)
    {
        //  single-use actions are handled immediately / current action-selection remains unchanged
       switch (action.Type)
        {
            case UnitActionType.None:
                return false;

            case UnitActionType.IncToolSize:
                _toolSize++;
                return false;
            case UnitActionType.DecToolSize:
                _toolSize--;
                if (_toolSize < 1)
                    _toolSize = 1;
                return false;
            case UnitActionType.SelectToolCircle:
                _toolShape = ToolShape.Circle;
                return false;
            case UnitActionType.SelectToolRectangle:
                _toolShape = ToolShape.Square;
                return false;
            case UnitActionType.SelectToolDither:
                _toolShape = ToolShape.Dither;
                return false;
            case UnitActionType.TilePreview:
                if (alternateAction)
                    _currentTerrainTile--;
                else
                    _currentTerrainTile++;
                if (_currentTerrainTile > TerrainTile.Max)
                    _currentTerrainTile = TerrainTile.Min;
                if (_currentTerrainTile < TerrainTile.Min)
                    _currentTerrainTile = TerrainTile.Max;
                return false;
            case UnitActionType.AdjustToolSize:
                if (alternateAction)
                    _toolSize--;
                else
                    _toolSize++;
                if (_toolSize < 1)
                    _toolSize = 1;
                return false;
            case UnitActionType.Stop:
                {
                    if (action.Type == UnitActionType.Stop)
                        _ = Globals.Game.NetworkClient.RequestStopAsync(_selectedUnits);
                    return false;
                }
        }

        ActiveAction = action;        
        return true;
    }

    public PlayerHandler(
        GameWorld map,
        MarkerHandler markerHandler)    
    {
        _map = map;
        _markerHandler = markerHandler;

        _toolSize = 8;
        _toolShape = ToolShape.Circle;
        _nextAllowedActionTime = 0;
    }

    public void Update(GameTime gameTime, Camera camera, Viewport viewport)
    {
        MouseState mouse = Mouse.GetState();
        //  update mouse-world position
        Point screenPosition = mouse.Position;
        Ray ray = CreatePickRay(camera, viewport, screenPosition);
        if (_map.Terrain.TryGetIntersection(ray, out Vector3 target))
        {
            MouseWorldPosition = new Vector3(target.X, target.Y, target.Z);
            IsMouseOnTerrain = true;
        }

        if (IsUnitSelectionEnabled())
        {
            if (IsLeftButtonPressed(mouse))
                _selectionStart = mouse.Position;

            if (mouse.LeftButton == ButtonState.Pressed)
            {
                _currentSelectionRect = CreateSelectionRectangle(_selectionStart, mouse.Position);
                if (_currentSelectionRect.Width > 8 || _currentSelectionRect.Height > 8)
                    _isSelectingUnits = true;
            }

            if (IsLeftButtonReleased(mouse))
            {
                if (SelectedUnits.Count == 0)
                    if (!_isSelectingUnits)
                        SelectUnits(camera, viewport, _currentSelectionRect, 1);
                    
                if (_isSelectingUnits)
                    SelectUnits(camera, viewport, _currentSelectionRect, 99);
                else
                    if (IsMouseOnTerrain)
                        PerformClickAction(_selectedUnits, MouseWorldPosition);

                _isSelectingUnits = false;
            }

            if (IsRightButtonPressed(mouse))
                ClearSelection();
        }
        else
        {
            if (IsTerrainEditingEnabled())
            {
                if (gameTime.TotalGameTime.TotalMilliseconds > _nextAllowedActionTime)
                {
                    _nextAllowedActionTime = gameTime.TotalGameTime.TotalMilliseconds + 100; // 100 ms cooldown between actions
                    if (ActiveAction is not null)
                    {
                        if (IsMouseOnTerrain)
                            if (mouse.LeftButton == ButtonState.Pressed)
                                RequestAction(ActiveAction, MouseWorldPosition, null);
                        if (mouse.RightButton == ButtonState.Pressed)
                        {
                            //  alternate actions for terrain editing
                            if (ActiveAction.Type == UnitActionType.RaiseTerrain)
                            {
                                UnitAction altAction = new (UnitActionType.LowerTerrain, ActiveAction.Name, ActiveAction.IconColumn, ActiveAction.IconRow);
                                RequestAction(altAction, MouseWorldPosition, null);
                                
                            }
                            if (ActiveAction.Type == UnitActionType.LowerTerrain)
                            {
                                UnitAction altAction = new (UnitActionType.RaiseTerrain, ActiveAction.Name, ActiveAction.IconColumn, ActiveAction.IconRow);
                                RequestAction(altAction, MouseWorldPosition, null);
                            }
                        }
                    }
                }
            }            
        }

        _previousMouseState = mouse;
    }

    UnitActionType SuggestAction(List<MobileUnit> selectedUnits, Vector3 mouseWorldPosition, out Unit? targetUnit)
    {
        targetUnit = null;
        if (selectedUnits.Count == 0)
            return UnitActionType.None;

        if (ActiveAction?.Type != UnitActionType.Goto)
        {
            if (ActiveAction?.Type != UnitActionType.Follow)
                return UnitActionType.None;
        }

        targetUnit = FindUnitAt(Globals._camera, Globals.GraphicsDevice.Viewport, Mouse.GetState().Position);
        KeyboardState keyboardState = Keyboard.GetState();

        if (targetUnit == null)
        {
            if (ActiveAction?.Type == UnitActionType.Follow)
                return UnitActionType.None;
            if (keyboardState.IsKeyDown(Keys.LeftControl) || keyboardState.IsKeyDown(Keys.RightControl))
                return UnitActionType.Attack;

            return UnitActionType.Goto;
        }

        if (ActiveAction?.Type == UnitActionType.Follow)
            return UnitActionType.Follow;

        if (targetUnit.IsEnemy(selectedUnits.First()))
            return UnitActionType.Attack;

        if (targetUnit.IsSamePlayer(selectedUnits.First()) && (targetUnit is ConstructionSite))
        {
            return UnitActionType.BuildConstruction;
        }

        if (keyboardState.IsKeyDown(Keys.LeftControl) || keyboardState.IsKeyDown(Keys.RightControl))
            return UnitActionType.Attack;

        if (keyboardState.IsKeyDown(Keys.LeftAlt) || keyboardState.IsKeyDown(Keys.RightAlt))
            return UnitActionType.Repair;

        if (targetUnit is Building)
            return UnitActionType.EnterBuilding;

        return UnitActionType.None;
    }

    bool PerformClickAction(List<MobileUnit> selectedUnits, Vector3 mouseWorldPosition)
    {
        if (ActiveAction == null)
            return false;

        Unit? targetUnit;
        UnitActionType suggestedAction = SuggestAction(selectedUnits, mouseWorldPosition, out targetUnit);
        if (suggestedAction != UnitActionType.None)
        {
            SendRequestAction(suggestedAction, mouseWorldPosition, targetUnit);
            return true;
        }
        RequestAction(ActiveAction, mouseWorldPosition, targetUnit);
        return true;
    }

    void ClearSelection()
    {
        foreach (MobileUnit unit in _selectedUnits)
            unit.Select(false);
        _selectedUnits.Clear();
        ActiveAction = null;
    }

    private void SelectUnits(Camera camera, Viewport viewport, Rectangle selection, int maxUnits) 
    {
        ClearSelection();

        foreach (MobileUnit unit in _map.Units.Units)
        {
            Rectangle unitBounds = unit.GetScreenBounds(
                camera.View,
                camera.Projection,
                viewport);

            if (!selection.Intersects(unitBounds))
                continue;

            if (_selectedUnits.Count < maxUnits)
            {
                _selectedUnits.Add(unit);
                unit.Select();
            }
        }

        var commonAction = _selectedUnits
            .SelectMany(unit => unit.Actions)
            .GroupBy(action => action.Type)
            .Where(group => group.Count() == _selectedUnits.Count)
            .Select(group => group.First())
            .FirstOrDefault();
        
        ActiveAction = commonAction;
    }

    private bool IsLeftButtonPressed(MouseState mouse)
    {
        return mouse.LeftButton == ButtonState.Pressed &&
            _previousMouseState.LeftButton == ButtonState.Released;
    }

    private bool IsLeftButtonReleased(MouseState mouse)
    {
        return mouse.LeftButton == ButtonState.Released &&
            _previousMouseState.LeftButton == ButtonState.Pressed;
    }

        private bool IsRightButtonPressed(MouseState mouse)
        {
            return mouse.RightButton == ButtonState.Pressed &&
                _previousMouseState.RightButton == ButtonState.Released;
        }

        private bool IsRightButtonReleased(MouseState mouse)
        {
            return mouse.RightButton == ButtonState.Released &&
                _previousMouseState.RightButton == ButtonState.Pressed;
        }

        private bool SendRequestAction(UnitActionType actionType, Vector3 targetPosition, Unit? targetUnit)
        {            
            if (_selectedUnits.Count == 0)
                return false;

            if (actionType == UnitActionType.Goto)
            {
                Globals.Game.NetworkClient.RequestGotoAsync(_selectedUnits, targetPosition);
                return true;
            }
            if (actionType == UnitActionType.Attack)
            {
                if (targetUnit is not null)
                    _ = Globals.Game.NetworkClient.RequestAttackTargetAsync(_selectedUnits, targetUnit.UnitId);
                else
                    _ = Globals.Game.NetworkClient.RequestAttackTerrainAsync(_selectedUnits, targetPosition);
                return true;
            }
            if (actionType == UnitActionType.Follow && targetUnit is not null)
            {
                _ = Globals.Game.NetworkClient.RequestFollowAsync(_selectedUnits, targetUnit.UnitId);
                return true;
            }
            if (actionType == UnitActionType.BuildConstruction)
            {
                Globals.Game.NetworkClient.RequestBuildConstructionAsync(_selectedUnits, targetUnit?.UnitId ?? Guid.Empty);
                return true;
            }
            return false;
        }

        private void RequestAction(UnitAction action, Vector3 targetPosition, Unit? targetUnit)
        {            
            if (_selectedUnits.Count == 0)
                return;

            if (action.Type == UnitActionType.Goto)
            {
                Globals.Game.NetworkClient.RequestGotoAsync(_selectedUnits, targetPosition);
            }
            if (action.Type == UnitActionType.Attack)
            {
                if (targetUnit is not null)
                    _ = Globals.Game.NetworkClient.RequestAttackTargetAsync(_selectedUnits, targetUnit.UnitId);
                else
                    _ = Globals.Game.NetworkClient.RequestAttackTerrainAsync(_selectedUnits, targetPosition);
            }
            if (action.Type == UnitActionType.Follow && targetUnit is not null)
                _ = Globals.Game.NetworkClient.RequestFollowAsync(_selectedUnits, targetUnit.UnitId);
            if (action.Type == UnitActionType.Build)
            {
                Globals.Game.NetworkClient.RequestBuildAsync(action.TargetObjectName, targetPosition);
            }
            if ((action.Type == UnitActionType.RaiseTerrain) || 
                (action.Type == UnitActionType.FlattenTerrain) || 
                (action.Type == UnitActionType.SmoothTerrain) || 
                (action.Type == UnitActionType.LowerTerrain))
            {
                Globals.Game.NetworkClient.RequestToolActionAsync(action, _toolShape, _toolSize, targetPosition);
            }
            if ((action.Type == UnitActionType.SetTerrainTile) ||
                (action.Type == UnitActionType.FillTile)
                )
            {
                Globals.Game.NetworkClient.RequestToolActionAsync(action, _toolShape, _toolSize, targetPosition, _currentTerrainTile);
            }
        }

        private void RequestBuildConstruction(ConstructionSite constructionSite)
        {
            if (_selectedUnits.Count == 0)
                return;

            _ = Globals.Game.NetworkClient.RequestBuildConstructionAsync(
                _selectedUnits,
                constructionSite.UnitId);
        }

        private ConstructionSite? FindConstructionSiteAt(
            Camera camera,
            Viewport viewport,
            Point screenPosition)
        {
            return _map.Units.Units
                .OfType<ConstructionSite>()
                .FirstOrDefault(site => site.GetScreenBounds(
                    camera.View,
                    camera.Projection,
                    viewport).Contains(screenPosition));
        }

        private MobileUnit? FindMobileUnitAt(Camera camera, Viewport viewport, Point screenPosition)
        {
            return _map.Units.Units.FirstOrDefault(unit => unit.GetScreenBounds(
                camera.View, camera.Projection, viewport).Contains(screenPosition));
        }

        private Unit? FindUnitAt(Camera camera, Viewport viewport, Point screenPosition)
        {
            return _map.Units.Units.FirstOrDefault(unit => unit.GetScreenBounds(
                camera.View, camera.Projection, viewport).Contains(screenPosition));
        }

        private static Ray CreatePickRay(
            Camera camera,
            Viewport viewport,
            Point screenPosition)
        {
            Vector3 nearPoint = viewport.Unproject(
                new Vector3(screenPosition.X, screenPosition.Y, 0.0f),
                camera.Projection,
                camera.View,
                Matrix.Identity);
            Vector3 farPoint = viewport.Unproject(
                new Vector3(screenPosition.X, screenPosition.Y, 1.0f),
                camera.Projection,
                camera.View,
                Matrix.Identity);

            return new Ray(
                nearPoint,
                Vector3.Normalize(farPoint - nearPoint));
        }

        private static bool IsClick(Point start, Point end)
        {
            const int dragThreshold = 8;

            return Math.Abs(end.X - start.X) <= dragThreshold &&
                Math.Abs(end.Y - start.Y) <= dragThreshold;
        }

    private static Rectangle CreateSelectionRectangle(Point start, Point end)
    {
        int left = Math.Min(start.X, end.X);
        int top = Math.Min(start.Y, end.Y);
        int right = Math.Max(start.X, end.X);
        int bottom = Math.Max(start.Y, end.Y);

        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    public bool IsTerrainEditingEnabled()
    {
        if (ActiveAction is null)
            return false;

        return IsMouseOnTerrain && (
            (ActiveAction.Type == UnitActionType.RaiseTerrain) || 
            (ActiveAction.Type == UnitActionType.LowerTerrain) || 
            (ActiveAction.Type == UnitActionType.SmoothTerrain) || 
            (ActiveAction.Type == UnitActionType.FlattenTerrain) ||
            (ActiveAction.Type == UnitActionType.SetTerrainTile) ||
            (ActiveAction.Type == UnitActionType.FillTile) 
            );
    }

    public void Draw3D(Camera camera)
    {
       //  render editor-tool
        if (IsMouseOnTerrain)
        {
            if (IsTerrainEditingEnabled())
            {
                //  render the terrain modification tool at the mouse world position
                //  1. let the current tool determine all affected terrain cells
                MouseWorldPosition = MouseWorldPosition;
                Point[] affectedCells = TerrainHelper.GetCells(Globals.World.Terrain, new Vector2(MouseWorldPosition.X, MouseWorldPosition.Z), _toolShape, _toolSize);
                foreach (Point cell in affectedCells)
                {
                    _map.Terrain.HighlightCell(camera, cell.X, cell.Y);
                }
            }
        }
    }

    public void Draw2D(
        SpriteBatch spriteBatch,
        Camera camera,
        Viewport viewport)
    {                
        foreach (MobileUnit unit in _selectedUnits)
        {
            Rectangle unitBounds = unit.GetScreenBounds(
                camera.View,
                camera.Projection,
                viewport);

            Globals.RenderHelper.DrawRectangle(
                spriteBatch,
                unitBounds,
                Color.Transparent,
                Color.White,
                borderThickness: 2);
        }

        if (_isSelectingUnits)
            Globals.RenderHelper.DrawRectangle(
                spriteBatch,
                _currentSelectionRect,
                Color.White * 0.1f,
                Color.LimeGreen,
                borderThickness: 2);

        if (IsMouseOnTerrain)
        {
            Unit? targetUnit;
            UnitActionType suggestedAction = SuggestAction(_selectedUnits, MouseWorldPosition, out targetUnit);
            if (suggestedAction != UnitActionType.None)
            {
                string suggestion = suggestedAction.ToString();
                RenderHelper.DrawTooltip(spriteBatch, suggestion, _previousMouseState.Position.X + 24, _previousMouseState.Position.Y + 16);
            }
        }
    }

    public bool IsUnitSelectionEnabled()
    {
        return !IsTerrainEditingEnabled();
    }
}

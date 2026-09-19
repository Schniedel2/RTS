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
    enum CurrentMode
    {
        EditTerrain,
        SelectUnits,
        BuildPreview
    }

    private readonly GameWorld _map;
    private readonly MarkerHandler _markerHandler;
    private readonly RenderStateStack _renderStates;
    private readonly List<Unit> _selectedUnits = [];
    private MouseState _previousMouseState;
    public IReadOnlyList<Unit> SelectedUnits => _selectedUnits;
    public UnitAction? ActiveAction { get; private set; }
    private Rectangle _currentSelectionRect;
    private bool _isSelectingUnits;
    public Vector3 MouseWorldPosition { get; private set; }
    public Point MouseScreenPosition { get; private set; }
    public Vector3 PressLeftWorldPosition { get; private set; }
    public Point PressLeftScreenPosition { get; private set; }
    private bool IsMouseOnTerrain = false;
    private bool _isDrag = false;

    private int _toolSize;
    private ToolShape _toolShape;    
    private double _nextAllowedActionTime;
    public TerrainTile _currentTerrainTile = TerrainTile.Grass;
    private float _buildPreviewDegree;

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
            case UnitActionType.TrainUnit:
                {
                    Building? building = _selectedUnits.Count == 1
                        ? _selectedUnits[0] as Building
                        : null;
                    if (building is not null && !string.IsNullOrWhiteSpace(action.TargetObjectName))
                    {
                        _ = Globals.Game.NetworkClient.RequestTrainUnitAsync(
                            building.UnitId,
                            action.TargetObjectName);
                    }
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
        _renderStates = new RenderStateStack(Globals.GraphicsDevice);

        _toolSize = 8;
        _toolShape = ToolShape.Circle;
        _nextAllowedActionTime = 0;
    }

    public void Update(GameTime gameTime, Camera camera, Viewport viewport)
    {
        if (_selectedUnits.RemoveAll(unit => !unit.IsSelectable) > 0)
        {
            ActiveAction = null;
            NotifySelectionChanged();
        }

        _isDrag = false;        

        MouseState mouse = Mouse.GetState();
        //  update mouse-world position
        Point screenPosition = mouse.Position;
        Ray ray = CreatePickRay(camera, viewport, screenPosition);
        if (_map.Terrain.TryGetIntersection(ray, out Vector3 target))
        {
            MouseWorldPosition = new Vector3(target.X, target.Y, target.Z);
            IsMouseOnTerrain = true;
        }

        if (GetMode() == CurrentMode.BuildPreview)
        {            
            if (IsLeftButtonPressed(mouse))
            {
                PressLeftScreenPosition = mouse.Position;
                PressLeftWorldPosition = MouseWorldPosition;                
            }

            if (mouse.LeftButton == ButtonState.Pressed)
            {
                Point drag = mouse.Position - PressLeftScreenPosition;
                if (drag.ToVector2().Length() > 8)
                {
                    _isDrag = true;
                    _buildPreviewDegree = -MathHelper.ToDegrees((float)Math.Atan2(drag.Y, drag.X));
                }
            }
        
            if (IsLeftButtonReleased(mouse))
                if (IsMouseOnTerrain)
                {
                    PerformClickAction(_selectedUnits, PressLeftWorldPosition, _buildPreviewDegree);
                    _buildPreviewDegree = 0;
                }
        }

        if (GetMode() == CurrentMode.SelectUnits)
        {
            if (IsLeftButtonPressed(mouse))
                PressLeftScreenPosition = mouse.Position;

            if (mouse.LeftButton == ButtonState.Pressed)
            {
                _currentSelectionRect = CreateSelectionRectangle(PressLeftScreenPosition, mouse.Position);
                if (_currentSelectionRect.Width > 8 || _currentSelectionRect.Height > 8)
                {
                    _isSelectingUnits = true;
                    _isDrag = true;
                }
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
                        PerformClickAction(_selectedUnits, MouseWorldPosition, 0);

                _isSelectingUnits = false;
            }

            if (IsRightButtonPressed(mouse))
                ClearSelection();
        }
        else
        {
            if (GetMode() == CurrentMode.EditTerrain)
            {
                if (gameTime.TotalGameTime.TotalMilliseconds > _nextAllowedActionTime)
                {
                    _nextAllowedActionTime = gameTime.TotalGameTime.TotalMilliseconds + 100; // 100 ms cooldown between actions
                    if (ActiveAction is not null)
                    {
                        if (IsMouseOnTerrain)
                            if (mouse.LeftButton == ButtonState.Pressed)
                                RequestAction(ActiveAction, MouseWorldPosition, null, 0);
                        if (mouse.RightButton == ButtonState.Pressed)
                        {
                            //  alternate actions for terrain editing
                            if (ActiveAction.Type == UnitActionType.RaiseTerrain)
                            {
                                UnitAction altAction = new (UnitActionType.LowerTerrain, ActiveAction.Name, ActiveAction.IconColumn, ActiveAction.IconRow);
                                RequestAction(altAction, MouseWorldPosition, null, 0);
                                
                            }
                            if (ActiveAction.Type == UnitActionType.LowerTerrain)
                            {
                                UnitAction altAction = new (UnitActionType.RaiseTerrain, ActiveAction.Name, ActiveAction.IconColumn, ActiveAction.IconRow);
                                RequestAction(altAction, MouseWorldPosition, null, 0);
                            }
                        }
                    }
                }
            }            
        }

        _previousMouseState = mouse;
    }

    UnitActionType SuggestAction(List<Unit> selectedUnits, Vector3 mouseWorldPosition, out Unit? targetUnit)
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

        if (targetUnit.IsSamePlayer(selectedUnits.First()) && (targetUnit is Building))
        {
            Building b = (targetUnit as Building)!;
            if (!b.IsCompleted)
                return UnitActionType.BuildConstruction;

            if (!b.IsDamaged)
                return UnitActionType.Repair;
            return UnitActionType.None;
        }

        if (keyboardState.IsKeyDown(Keys.LeftControl) || keyboardState.IsKeyDown(Keys.RightControl))
            return UnitActionType.Attack;

        if (keyboardState.IsKeyDown(Keys.LeftAlt) || keyboardState.IsKeyDown(Keys.RightAlt))
            return UnitActionType.Repair;

        if (targetUnit is Building)
            return UnitActionType.EnterBuilding;

        return UnitActionType.None;
    }

    bool PerformClickAction(List<Unit> selectedUnits, Vector3 mouseWorldPosition, float targetAngleY)
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
        RequestAction(ActiveAction, mouseWorldPosition, targetUnit, targetAngleY);
        return true;
    }

    void ClearSelection(bool notify = true)
    {
        foreach (Unit unit in _selectedUnits)
            unit.Select(false);
        _selectedUnits.Clear();
        ActiveAction = null;
        if (notify)
            NotifySelectionChanged();
    }

    private void SelectUnits(Camera camera, Viewport viewport, Rectangle selection, int maxUnits) 
    {
        ClearSelection(notify: false);

        foreach (Unit unit in _map.Units.Units)
        {
            if (!unit.IsSelectable)
                continue;
            Rectangle unitBounds = unit.GetScreenBounds(
                camera.View,
                camera.Projection,
                viewport);

            if (!selection.Intersects(unitBounds))
                continue;

            if (!Globals.Game.Armies.CanControl(Globals.Game.Network.LocalPeerId, unit.ArmyId))
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
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        _ = Globals.Game.NetworkClient.NotifyUnitsSelectedAsync(
            _selectedUnits.Select(unit => unit.UnitId).ToArray());
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

        private void RequestAction(UnitAction action, Vector3 targetPosition, Unit? targetUnit, float targetAngleY)
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
                Guid buildingId = Guid.NewGuid();
                Globals.Game.NetworkClient.RequestBuildAsync(action.TargetObjectName, targetPosition, targetAngleY, buildingId);
                Globals.Game.NetworkClient.RequestBuildConstructionAsync(_selectedUnits, buildingId);
                ActiveAction = null;
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

        private void RequestBuildConstruction(Building constructionSite)
        {
            if (_selectedUnits.Count == 0)
                return;

            _ = Globals.Game.NetworkClient.RequestBuildConstructionAsync(
                _selectedUnits,
                constructionSite.UnitId);
        }

        private Building? FindBuildingAt(
            Camera camera,
            Viewport viewport,
            Point screenPosition)
        {
            return _map.Units.Units
                .OfType<Building>()
                .FirstOrDefault(site => site.GetScreenBounds(
                    camera.View,
                    camera.Projection,
                    viewport).Contains(screenPosition));
        }

        private Unit? FindUnitAt(Camera camera, Viewport viewport, Point screenPosition)
        {
            return _map.Units.Units.FirstOrDefault(unit => unit.IsSelectable && unit.GetScreenBounds(
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

    private CurrentMode GetMode()
    {    
        if (ActiveAction is null)
            return CurrentMode.SelectUnits;

        if (IsMouseOnTerrain)
        if  (
            (ActiveAction.Type == UnitActionType.RaiseTerrain) || 
            (ActiveAction.Type == UnitActionType.LowerTerrain) || 
            (ActiveAction.Type == UnitActionType.SmoothTerrain) || 
            (ActiveAction.Type == UnitActionType.FlattenTerrain) ||
            (ActiveAction.Type == UnitActionType.SetTerrainTile) ||
            (ActiveAction.Type == UnitActionType.FillTile) 
            )
            return CurrentMode.EditTerrain;
        
        if (ActiveAction.Type == UnitActionType.Build)
            return CurrentMode.BuildPreview;

        return CurrentMode.SelectUnits;
    }

    public void Draw3D(Camera camera)
    {
        //  render editor-tool
        if (IsMouseOnTerrain)
        {
            if (GetMode() == CurrentMode.EditTerrain)
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

            if (ActiveAction is not null)
            {
                //  render the active action's visual representation at the mouse world position
                if (ActiveAction.Type == UnitActionType.Build)
                {
                    Vector3 pos = MouseWorldPosition;
                    if (_isDrag)
                        pos = PressLeftWorldPosition;
                    
                    Building? unit = BuildingFactory.SpawnBuilding(ActiveAction.TargetObjectName, pos, _buildPreviewDegree, Guid.Empty, Guid.Empty);
                    if (unit is not null)
                    {
                        GraphicsDevice graphicsDevice = Globals.GraphicsDevice;
                        _renderStates.PushState();

                        graphicsDevice.BlendState = BlendState.NonPremultiplied;
                        graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                        Globals._unitEffect.Parameters["Opacity"]?.SetValue(0.75f);
                        unit.DrawPreview(Globals._unitEffect);
                        Globals._unitEffect.Parameters["Opacity"]?.SetValue(1.0f);
                        _renderStates.PopState();
                    }
                }
            }
        }
    }

    public void Draw2D(
        SpriteBatch spriteBatch,
        Camera camera,
        Viewport viewport)
    {                
        foreach (Unit unit in _selectedUnits)
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
}

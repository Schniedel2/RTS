using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class TerrainEditorTool : Soldier
{
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.RaiseTerrain, "Raise Terrain", 0, 5),
        new(UnitActionType.LowerTerrain, "Lower Terrain", 1, 5),
        new(UnitActionType.FlattenTerrain, "Flatten Terrain", 2, 5),
        new(UnitActionType.SmoothTerrain, "Smooth Terrain", 2, 5), // icon needed
        new(UnitActionType.SharpenTerrain, "Sharpen Terrain", 2, 5), // icon needed
        new(UnitActionType.SetTerrainTile, "Set Tiles", 3, 5),
        new(UnitActionType.FillTile, "Fill Tiles", 2, 9),
        new(UnitActionType.SelectToolCircle, "Change to circle shaped tool", 8, 6),
        new(UnitActionType.SelectToolRectangle, "Change to square shaped tool", 9, 6),
        new(UnitActionType.SelectToolDither, "Change to dithered shaped tool", 9, 6),
        new(UnitActionType.IncToolSize, "Increase shape size", 8, 5),
        new(UnitActionType.DecToolSize, "Decrease shape size", 9, 5),
        new(UnitActionType.AdjustToolSize, "Adjust shape size", 8, 5),
        new(UnitActionType.PlaceGameplayMarker, "Player Start", 0, 8, MarkerType: GameplayMarkerType.PlayerStart),
        new(UnitActionType.PlaceGameplayMarker, "Initial Camera", 1, 8, MarkerType: GameplayMarkerType.InitialCamera),
        new(UnitActionType.PlaceGameplayMarker, "Expansion Site", 2, 8, MarkerType: GameplayMarkerType.ExpansionSite),
        new(UnitActionType.PlaceGameplayMarker, "Defensive Position", 3, 8, MarkerType: GameplayMarkerType.DefensivePosition),
        new(UnitActionType.PlaceGameplayMarker, "Observation Point", 4, 8, MarkerType: GameplayMarkerType.ObservationPoint),
        new(UnitActionType.PlaceGameplayMarker, "Landing Zone", 5, 8, MarkerType: GameplayMarkerType.LandingZone),
        new(UnitActionType.PlaceGameplayMarker, "Resource Field", 6, 8, MarkerType: GameplayMarkerType.ResourceField),
        new(UnitActionType.DeleteGameplayMarker, "Remove Gameplay Marker", 12, 2),
        new(UnitActionType.PlaceTiberiumSource, "Place Tiberium Source", 0, 9),
        new(UnitActionType.PaintTiberium, "Paint Tiberium", 1, 9),
        new(UnitActionType.RemoveTiberium, "Remove Tiberium + Sources", 2, 9),
        new(UnitActionType.SimulateTiberiumArea, "Grow Tiberium +10s", 3, 9),
        new(UnitActionType.None, "", 15, 0),
        //new(UnitActionType.GetTerrainTile, "Get Terrain Tile", 3, 2), // icon needed
        new(UnitActionType.Save, "Save", 13, 2),
        new(UnitActionType.TilePreview, "Current Tile", 9, 8)
    ];

    public override int GetSightRange()
    {
        if (Globals.LocalPlayer.IsUnitSelected(this))
            return 999999999;
        return base.GetSightRange();
    }

    public TerrainEditorTool(Vector3 position, Guid unitId) : base(position, unitId)
    {
        
    }
}

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
        new(UnitActionType.SetTerrainTile, "Set Tiles", 3, 5),
        new(UnitActionType.FillTile, "Fill Tiles", 2, 9),
        new(UnitActionType.SelectToolCircle, "Change to circle shaped tool", 8, 6),
        new(UnitActionType.SelectToolRectangle, "Change to square shaped tool", 9, 6),
        new(UnitActionType.SelectToolDither, "Change to dithered shaped tool", 9, 6),
        new(UnitActionType.IncToolSize, "Increase shape size", 8, 5),
        new(UnitActionType.DecToolSize, "Decrease shape size", 9, 5),
        new(UnitActionType.AdjustToolSize, "Adjust shape size", 8, 5),
        new(UnitActionType.Filler, "", 15, 0),
        //new(UnitActionType.GetTerrainTile, "Get Terrain Tile", 3, 2), // icon needed
        new(UnitActionType.Save, "Save", 13, 2),
        new(UnitActionType.TilePreview, "Current Tile", 9, 8)
    ];

    public TerrainEditorTool(Vector3 position, Guid unitId) : base(position, unitId)
    {
        
    }
}
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class TerrainEditorTool : Soldier
{
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 0),
        new(UnitActionType.SelectToolCircle, "Select Tool Circle", 0, 2),
        new(UnitActionType.SelectToolRectangle, "Select Tool Rectangle", 2, 2),
        new(UnitActionType.SelectToolFill, "Select Tool Fill", 2, 9),
        new(UnitActionType.IncToolSize, "Increase Tool Size", 0, 2),
        new(UnitActionType.DecToolSize, "Decrease Tool Size", 1, 0),
        new(UnitActionType.RaiseTerrain, "Raise Terrain", 4, 2),
        new(UnitActionType.LowerTerrain, "Lower Terrain", 5, 2),
        new(UnitActionType.FlattenTerrain, "Flatten Terrain", 6, 2),
        new(UnitActionType.SmoothTerrain, "Smooth Terrain", 8, 2),
        new(UnitActionType.SetTerrainTile, "Set Terrain Tile", 0, 0),
        new(UnitActionType.GetTerrainTile, "Get Terrain Tile", 3, 2),
        new(UnitActionType.NextTerrainTile, "Next Terrain Tile", 0, 0),
        new(UnitActionType.PrevTerrainTile, "Previous Terrain Tile", 0, 0)
    ];

    public TerrainEditorTool(Vector3 position, Guid unitId) : base(position, unitId)
    {
        
    }
}
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;

namespace RTS;

public struct Telemetry
{
    public Telemetry() {}

    public int FramesProcessed = 0;
    public int TryFindPath_Calls = 0;
    public double Pathfinding_Total = 0.0;
    public double Pathfinding_Last = 0.0;        
    public double Pathfinding_Avg = 0.0;

    public void Reset()
    {
        FramesProcessed = 0;
        TryFindPath_Calls = 0;
        Pathfinding_Total = 0.0;
        Pathfinding_Last = 0.0;
        Pathfinding_Avg = 0.0;
    }
}

public static class Globals
{
    public static string MapsDirectory = "c:\\temp\\Maps";
    public static string ModelsDirectory = Path.Combine(AppContext.BaseDirectory, "Content", "Models");
    public static GraphicsDeviceManager Graphics = null!;
    public static GraphicsDevice GraphicsDevice = null!;
    public static PlayerHandler LocalPlayer = null!;
    public static MeshHandler MeshHandler = null!;
    public static Texture2D ActionIcons = null!;
    public static Texture2D UnitsTexture = null!;
    public static Texture2D UnitsMaterialMask = null!;
    public static RTSGame Game = null!;
    public static GameWorld World = null!;
    public static RenderHelper RenderHelper = null!;
    public static Effect _shadowEffect = null!;
    public static Effect _terrainEffect = null!;
    public static BasicEffect CellHighlightEffect = null!;
    public static Effect _unitEffect = null!;

    public static Camera _camera = null!;
    public static Texture2D _whiteTexture = null!;
    public static Texture2D _terrainTileSheet = null!;    
    public static DebugRenderer _debugRenderer = null!;
    public static SpriteFont _debugFont = null!;
    public static SpriteFont PanelFont = null!;
    public static SpriteFont TooltipFont = null!;
    public static GameConsole Console = null!;

    public static Telemetry Telemetry;
    public static bool DebugFX_DisableShadowMap = false;
    public static bool DebugFX_DisableLighting = false;
    public static bool Debug_ShadowMap_ShowPreview = false;
    public static bool Debug_ShowGameGrid = false;
    public static bool Debug_ShowUnitBounds = false;
    public static bool Debug_ShowUnitTransforms = false;
    public static bool Debug_ShowMarkers = false;
    public static bool Debug_ShowNetworkMessages = false;
    public static bool Debug_ShowPathfindingMessages = false;
}

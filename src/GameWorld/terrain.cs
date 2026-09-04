using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace RTS;

public class Terrain
{
    public int Width { get; }
    public int Height { get; }
    public float HeightScale { get; }


    private VertexPositionColorNormal[] _vertices = [];
    private int[] _indices = [];
    private int[] _gridIndices = [];
    private float[] HeightMap = [];
    private TerrainTile[,] _tiles = null!;
    
    private Texture2D _tileMapTexture = null!;

    public Terrain(string mapDirectory)
    {
        HeightScale = 12.0f;

        Texture2D heightmapTexture = Texture2D.FromFile(Globals.GraphicsDevice, Path.Combine(mapDirectory, "terrain-heightmap.png"));
        Texture2D tilemapTexture = Texture2D.FromFile(Globals.GraphicsDevice, Path.Combine(mapDirectory, "terrain-tilemap.png"));
        Width = heightmapTexture.Width;
        Height = heightmapTexture.Height;
        BuildHeightMap(heightmapTexture, HeightScale);

        CreateTilemap(tilemapTexture);
        BuildMesh();
    }

    public Terrain(
        int width = 80,
        int height = 80,
        float heightScale = 12.0f,
        Texture2D? heightMapTexture = null)
    {
        Width = width;
        Height = height;

        HeightScale = heightScale;

        if (heightMapTexture is not null &&
            heightMapTexture.Width == Width &&
            heightMapTexture.Height == Height)
        {
            BuildHeightMap(heightMapTexture, HeightScale);
        }
        else
        {
            BuildProceduralHeightMap();
        }
        CreateDummyTileMap();
        UpdateTilemap();

        BuildMesh();
    }

    public void UpdateTilemap()
    {
        CreateTileMapTexture();
    }

    public void CreateTilemap(Texture2D _tilemap)
    {
        _tiles = new TerrainTile[Width, Height];
        Color[] data = new Color[Width * Height];
        _tilemap.GetData(data);

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Color c = data[y * Width + x];
                byte tileId = c.R;
                _tiles[x, y] = (TerrainTile)tileId;
            }
        }
        CreateTileMapTexture();
    }

    private void CreateTileMapTexture()
    {
        _tileMapTexture = new Texture2D(
            Globals.GraphicsDevice,
            Width,
            Height,
            false,
            SurfaceFormat.Color);

        Color[] data = new Color[Width * Height];

        Random r = new Random();

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                byte tileId = (byte)_tiles[x, y];
                data[y * Width + x] = new Color((byte)tileId, (byte)0, (byte)0, (byte)255);
            }
        }
        _tileMapTexture.SetData(data);
    }    

    private Texture2D CreateHeightMapTexture()
    {
        Texture2D heightmapTexture = new Texture2D(
            Globals.GraphicsDevice,
            Width,
            Height,
            false,
            SurfaceFormat.Color);

        Color[] data = new Color[Width * Height];

        Random r = new Random();

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                byte height = (byte)(GetHeight(x, y) * 255.0f);
                data[y * Width + x] = new Color((byte)height, (byte)0, (byte)0, (byte)255);
            }
        }
        heightmapTexture.SetData(data);
        return heightmapTexture;
    }    

    public void DrawShadow(
        Effect effect,
        Matrix world,
        Matrix view,
        Matrix projection)
    {
        effect.Parameters["World"]?.SetValue(world);
        effect.Parameters["View"]?.SetValue(view);
        effect.Parameters["Projection"]?.SetValue(projection);

        foreach (EffectPass pass in
            effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            Globals.GraphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _vertices,
                0,
                _vertices.Length,
                _indices,
                0,
                _indices.Length / 3);
        }
    }

    public void Draw(
        Matrix world,
        Matrix view,
        Matrix projection,
        Matrix lightView,
        Matrix lightProjection,
        Texture2D shadowMap,
        Vector3 lightDirection)
    {
        
        Globals._terrainEffect.Parameters["World"]?.SetValue(world);
        Globals._terrainEffect.Parameters["View"]?.SetValue(view);
        Globals._terrainEffect.Parameters["Projection"]?.SetValue(projection);

        Globals._terrainEffect.Parameters["LightView"]?.SetValue(lightView);
        Globals._terrainEffect.Parameters["LightProjection"]?.SetValue(lightProjection);

        Globals._terrainEffect.Parameters["ShadowTexture"]?.SetValue(shadowMap);
        Globals._terrainEffect.Parameters["LightDirection"]?.SetValue(lightDirection);
        Globals._terrainEffect.Parameters["TileMapTexture"]?.SetValue(_tileMapTexture);
        Globals._terrainEffect.Parameters["MapWidth"]?.SetValue(Width);
        Globals._terrainEffect.Parameters["MapHeight"]?.SetValue(Height);

        foreach (EffectPass pass in
            Globals._terrainEffect.CurrentTechnique.Passes)
        {
            pass.Apply();

            Globals.GraphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                _vertices,
                0,
                _vertices.Length,
                _indices,
                0,
                _indices.Length / 3);
        }
    }

    public void BuildMesh()
    {
        _vertices = new VertexPositionColorNormal[
            Width * Height];

        _gridIndices = new int[(Height) * (Width) * 4];
        _indices = new int[
            (Width - 1) *
            (Height - 1) *
            6];

        // -------------------------------------------------
        // Vertices
        // -------------------------------------------------

        for (int z = 0; z < Height; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                float worldX =
                    x;

                float worldZ =
                    z;

                float y = GetHeight(x, z);

                float normalizedHeight = y;
                Color color = Color.DarkGreen;

                int index =
                    z * Width + x;

                Vector3 upperLeft = new Vector3(worldX - 0.5f, GetHeight(x-1, z-1), worldZ - 0.5f);
                Vector3 upperRight = new Vector3(worldX + 0.5f, GetHeight(x+1, z-1), worldZ - 0.5f);
                Vector3 lowerLeft = new Vector3(worldX - 0.5f, GetHeight(x-1, z+1), worldZ + 0.5f);

                Vector3 normal = -Vector3.Cross(upperRight - upperLeft, lowerLeft - upperLeft);

                _vertices[index] =
                    new VertexPositionColorNormal(
                        new Vector3(worldX, y, worldZ),
                        color,
                        normal);
            }
        }

        // -------------------------------------------------
        // Indices
        // -------------------------------------------------

        int indexPosition = 0;
        int gridIndexPosition = 0;

        for (int z = 0; z < Height - 1; z++)
        {
            for (int x = 0; x < Width - 1; x++)
            {
                int topLeft =
                    z * Width + x;

                int topRight =
                    topLeft + 1;

                int bottomLeft =
                    (z + 1) * Width + x;

                int bottomRight =
                    bottomLeft + 1;

                // Dreieck 1
                _indices[indexPosition++] = bottomLeft;
                _indices[indexPosition++] = topLeft;
                _indices[indexPosition++] = topRight;

                // Dreieck 2
                _indices[indexPosition++] = bottomLeft;
                _indices[indexPosition++] = topRight;
                _indices[indexPosition++] = bottomRight;

                _gridIndices[gridIndexPosition++] = bottomLeft;
                _gridIndices[gridIndexPosition++] = topLeft;
                _gridIndices[gridIndexPosition++] = topLeft;
                _gridIndices[gridIndexPosition++] = topRight;
            }
        }
    }

    private void BuildHeightMap(Texture2D heightMapTexture, float heightScale)
    {
        Color[] pixels = new Color[Width * Height];
        heightMapTexture.GetData(pixels);
        HeightMap = new float[Width * Height];

        for (int index = 0; index < pixels.Length; index++)
            HeightMap[index] = pixels[index].R / 255.0f * heightScale;
    }

    private void BuildProceduralHeightMap()
    {
        HeightMap = new float[Width * Height];
        for (int z = 0; z < Height; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                float h = 0;

                h +=
                    MathF.Sin(x * 0.08f) *
                    MathF.Cos(z * 0.07f) *
                        3.0f;

                h +=
                    MathF.Sin(x * 0.17f + 1.3f) *
                    MathF.Cos(z * 0.13f) *
                    1.5f;

                h +=
                    MathF.Sin(x * 0.035f - 0.7f) *
                    MathF.Cos(z * 0.045f) *
                    5.0f;

                // Großer zentraler Hügel                
                float distance = 0;
                if ((x > 35) && (x <45))
                    if ((z > 35) && (z <45))
                    {
                        distance = -10.0f;
                        h = 12.0f;
                    }
                //float distance =
                //    MathF.Sqrt((40-x) * (40-x) + (40-z) * (40-z));

                h +=
                    MathF.Max(
                        0,
                        10.0f - distance * 0.15f);
                            
                h *= HeightScale;
                int index = z * Width + x;
                HeightMap[index] = h;
            }
        }
    }

    public float GetHeight(int x, int z)
    {
        if (x < 0) x = 0;
        if (x >= Width) x = Width - 1;
        if (z < 0) z = 0;
        if (z >= Height) z = Height - 1;

        int index = z * Width + x;
        return HeightMap[index];
    }

    public void SetHeight(int x, int z, float height)
    {
        if (x < 0) return;
        if (x >= Width) return;
        if (z < 0) return;
        if (z >= Height) return;
        
        if (height < 0) height = 0;
        if (height > HeightScale) height = HeightScale;

        int index = z * Width + x;
         HeightMap[index] = height;
    }

    public bool TryGetIntersection(Ray ray, out Vector3 intersection)
    {
        float stepSize = 0.25f;
        float previousDistance = 0.0f;
        float previousHeightDifference = GetHeightDifference(ray, previousDistance);

        for (float distance = stepSize; distance <= 2000.0f; distance += stepSize)
        {
            float heightDifference = GetHeightDifference(ray, distance);

            if (previousHeightDifference > 0.0f && heightDifference <= 0.0f)
            {
                float fraction = previousHeightDifference /
                    (previousHeightDifference - heightDifference);
                float hitDistance = MathHelper.Lerp(
                    previousDistance,
                    distance,
                    fraction);

                intersection = ray.Position + ray.Direction * hitDistance;
                return true;
            }

            previousDistance = distance;
            previousHeightDifference = heightDifference;
        }

        intersection = Vector3.Zero;
        return false;
    }

    private float GetHeightDifference(Ray ray, float distance)
    {
        Vector3 point = ray.Position + ray.Direction * distance;

        if (point.X < 0.0f || point.Z < 0.0f ||
            point.X > (Width - 1) ||
            point.Z > (Height - 1))
            return float.PositiveInfinity;

        int terrainX = (int)(point.X);
        int terrainZ = (int)(point.Z);

        return point.Y - GetHeight(terrainX, terrainZ);
    }

    public TerrainTile GetTile(int x, int y)
    {
        return _tiles[x, y];
    }

    public void SetTile(int x, int y, TerrainTile tile)
    {
        _tiles[x, y] = tile;
    }

    public void CreateDummyTileMap()
    {        
        _tiles = new TerrainTile[Width, Height];
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                _tiles[x, y] = TerrainTile.Rock;

        for (int x = 12; x < 20; x++)
            for (int y = 12; y < 20; y++)
                _tiles[x, y] = TerrainTile.Grass;
    }

    public void Update(GameTime gameTime)
    {        
    }

    VertexPosition GetVertex(int x, int z)
    {
        float height = GetHeight(x, z);

        Vector3 position = new Vector3(x, height, z);
        return new VertexPosition(position);
    }

    public void HighlightCell(Camera camera, int x, int z)
    {
        const float surfaceOffset = 0.1f;
        Color highlightColor = new Color(255, 255, 255, 96);
        VertexPositionColor[] vertices =
        [
            new(new Vector3(x, GetHeight(x, z) + surfaceOffset, z), highlightColor),
            new(new Vector3(x + 1, GetHeight(x + 1, z) + surfaceOffset, z), highlightColor),
            new(new Vector3(x + 1, GetHeight(x + 1, z + 1) + surfaceOffset, z + 1), highlightColor),
            new(new Vector3(x, GetHeight(x, z) + surfaceOffset, z), highlightColor),
            new(new Vector3(x + 1, GetHeight(x + 1, z + 1) + surfaceOffset, z + 1), highlightColor),
            new(new Vector3(x, GetHeight(x, z + 1) + surfaceOffset, z + 1), highlightColor)
        ];

        Globals.CellHighlightEffect.World = Matrix.Identity;
        Globals.CellHighlightEffect.View = camera.View;
        Globals.CellHighlightEffect.Projection = camera.Projection;

        Globals.GraphicsDevice.BlendState = new BlendState
        {
            ColorSourceBlend = Blend.SourceAlpha,
            ColorDestinationBlend = Blend.InverseSourceAlpha,
            AlphaSourceBlend = Blend.One,
            AlphaDestinationBlend = Blend.InverseSourceAlpha
        };

        foreach (EffectPass pass in Globals.CellHighlightEffect.CurrentTechnique.Passes)
        {
            pass.Apply();

            Globals.GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.TriangleList,
                vertices,
                0,
                vertices.Length / 3);
        }

        Globals.GraphicsDevice.BlendState = BlendState.Opaque;
    }

    public void Save(string mapDirectory)
    {
        string filename = Path.Combine(mapDirectory, "terrain-tilemap.png");
        using (FileStream stream = new FileStream(filename, FileMode.CreateNew))
        {
            _tileMapTexture.SaveAsPng(stream, width: Width, height: Height);
        }

        using(Texture2D heightMapTexture = CreateHeightMapTexture())
        {
            filename = Path.Combine(mapDirectory, "terrain-heightmap.png");
            using (FileStream stream = new FileStream(filename, FileMode.CreateNew))
            {
                heightMapTexture.SaveAsPng(stream, width: Width, height: Height);
            }
        }
   }
}
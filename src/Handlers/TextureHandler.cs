using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace RTS;
public sealed class TextureHandler : IDisposable
{
    public sealed class TextureRegion
    {
        public int AtlasIndex { get; init; }

        // Tatsächlicher Bereich der Originaltextur OHNE Padding
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }

        public int AtlasWidth { get; init; }
        public int AtlasHeight { get; init; }

        public Vector2 UVOffset =>
            new(
                (float)X / AtlasWidth,
                (float)Y / AtlasHeight);

        public Vector2 UVScale =>
            new(
                (float)Width / AtlasWidth,
                (float)Height / AtlasHeight);

        /// <summary>
        /// Wandelt UV 0..1 der Originaltextur in Atlas-UV um.
        /// Kann später auch direkt dein bbmodel-Importer machen.
        /// </summary>
        public Vector2 RemapUV(Vector2 uv)
        {
            return new Vector2(
                (X + uv.X * Width) / AtlasWidth,
                (Y + uv.Y * Height) / AtlasHeight
            );
        }

        public override string ToString()
        {
            return $"Atlas={AtlasIndex}, Pos={X},{Y}, Size={Width}x{Height}";
        }
    }


    private sealed class Atlas
    {
        public Texture2D Texture;

        public int CursorX;
        public int CursorY;
        public int RowHeight;

        // Gleiche Datei innerhalb desselben Atlas nicht zweimal einbauen
        public readonly Dictionary<string, TextureRegion> Regions =
            new(StringComparer.OrdinalIgnoreCase);

        public Atlas(Texture2D texture, int padding)
        {
            Texture = texture;

            CursorX = padding;
            CursorY = padding;
            RowHeight = 0;
        }
    }


    private readonly GraphicsDevice _graphicsDevice;

    private readonly int _atlasWidth;
    private readonly int _atlasHeight;
    private readonly int _padding;

    private readonly List<Atlas> _atlases = new();

    private Atlas? _currentAtlas;


    public int AtlasCount => _atlases.Count;

    public int CurrentAtlasIndex =>
        _currentAtlas == null
            ? -1
            : _atlases.IndexOf(_currentAtlas);


    public TextureHandler(
        GraphicsDevice graphicsDevice,
        int atlasWidth = 2048,
        int atlasHeight = 2048,
        int padding = 2)
    {
        _graphicsDevice = graphicsDevice;

        _atlasWidth = atlasWidth;
        _atlasHeight = atlasHeight;
        _padding = padding;
    }

    public void LoadMeshTextures()
    {
        LoadMeshTextures(Globals.ModelsDirectory);
    }

    public void LoadMeshTextures(string directory)
    {
        AddTexture(Path.Combine(directory, "TankBody-1.png"));
        AddTexture(Path.Combine(directory, "TankTurret-1.png"));
        AddTexture(Path.Combine(directory, "TankBarrel-1.png"));
        NextTexture();
    }

    /// <summary>
    /// Fügt eine PNG/JPG/etc. in den aktuellen Atlas ein.
    /// Wird der Atlas voll, wird automatisch ein neuer begonnen.
    /// </summary>
    public TextureRegion AddTexture(string filename)
    {
        string fullPath = Path.GetFullPath(filename);

        EnsureAtlas();

        //
        // Gleiche Datei im aktuellen Atlas bereits vorhanden?
        //
        if (_currentAtlas!.Regions.TryGetValue(
                fullPath,
                out TextureRegion? existing))
        {
            return existing;
        }


        using Texture2D source = LoadTexture(fullPath);

        int width = source.Width;
        int height = source.Height;


        //
        // Passt die Texture grundsätzlich?
        //
        if (width + _padding * 2 > _atlasWidth ||
            height + _padding * 2 > _atlasHeight)
        {
            throw new InvalidOperationException(
                $"Texture '{filename}' ({width}x{height}) " +
                $"ist zu groß für Atlas {_atlasWidth}x{_atlasHeight}.");
        }


        //
        // Passt sie nicht mehr in die aktuelle Zeile?
        //
        if (_currentAtlas.CursorX +
            width +
            _padding > _atlasWidth)
        {
            NewRow();
        }


        //
        // Passt sie auch vertikal nicht mehr?
        //
        if (_currentAtlas.CursorY +
            height +
            _padding > _atlasHeight)
        {
            CreateAtlas();
        }


        //
        // Sicherheitshalber nach neuem Atlas nochmal prüfen,
        // falls die neue Texture sehr breit ist.
        //
        if (_currentAtlas!.CursorX +
            width +
            _padding > _atlasWidth)
        {
            NewRow();
        }


        int x = _currentAtlas.CursorX;
        int y = _currentAtlas.CursorY;


        //
        // Pixel einlesen
        //
        Color[] pixels = new Color[width * height];
        source.GetData(pixels);


        //
        // Originalbild schreiben
        //
        _currentAtlas.Texture.SetData(
            0,
            new Rectangle(x, y, width, height),
            pixels,
            0,
            pixels.Length);


        //
        // Randpixel nach außen kopieren.
        // Wichtig bei Linear Filtering und MipMaps.
        //
        WritePadding(
            _currentAtlas.Texture,
            pixels,
            x,
            y,
            width,
            height);


        var region = new TextureRegion
        {
            AtlasIndex = CurrentAtlasIndex,

            X = x,
            Y = y,

            Width = width,
            Height = height,

            AtlasWidth = _atlasWidth,
            AtlasHeight = _atlasHeight
        };


        _currentAtlas.Regions.Add(fullPath, region);


        //
        // Cursor weiterschieben
        //
        _currentAtlas.CursorX +=
            width + _padding * 2;

        _currentAtlas.RowHeight =
            Math.Max(
                _currentAtlas.RowHeight,
                height + _padding * 2);


        return region;
    }

    /// <summary>Returns the atlas region previously registered for a texture file.</summary>
    public bool TryGetTextureRegion(string filename, out TextureRegion region)
    {
        string fullPath = Path.GetFullPath(filename);
        foreach (Atlas atlas in _atlases)
        {
            if (atlas.Regions.TryGetValue(fullPath, out TextureRegion? existing))
            {
                region = existing;
                return true;
            }
        }

        region = null!;
        return false;
    }


    /// <summary>
    /// Erzwingt einen neuen Atlas.
    ///
    /// Damit kannst du z.B.
    ///
    /// Vehicles
    ///   ...
    /// NextTexture()
    ///
    /// Buildings
    ///   ...
    /// NextTexture()
    ///
    /// Effects
    ///   ...
    ///
    /// sauber trennen.
    /// </summary>
    public void NextTexture()
    {
        CreateAtlas();
    }


    public Texture2D GetAtlas(int index)
    {
        return _atlases[index].Texture;
    }


    public IReadOnlyList<Texture2D> GetAtlases()
    {
        Texture2D[] result =
            new Texture2D[_atlases.Count];

        for (int i = 0; i < _atlases.Count; i++)
            result[i] = _atlases[i].Texture;

        return result;
    }


    private Texture2D LoadTexture(string filename)
    {
        using FileStream stream =
            File.OpenRead(filename);

        return Texture2D.FromStream(
            _graphicsDevice,
            stream);
    }


    private void EnsureAtlas()
    {
        if (_currentAtlas == null)
            CreateAtlas();
    }


    private void CreateAtlas()
    {
        var texture = new Texture2D(
            _graphicsDevice,
            _atlasWidth,
            _atlasHeight,
            false,
            SurfaceFormat.Color);

        //
        // Transparent initialisieren.
        //
        var clear =
            new Color[_atlasWidth * _atlasHeight];

        texture.SetData(clear);


        var atlas =
            new Atlas(texture, _padding);

        _atlases.Add(atlas);

        _currentAtlas = atlas;
    }


    private void NewRow()
    {
        _currentAtlas!.CursorX = _padding;

        _currentAtlas.CursorY +=
            _currentAtlas.RowHeight;

        _currentAtlas.RowHeight = 0;
    }


    private void WritePadding(
        Texture2D atlas,
        Color[] source,
        int x,
        int y,
        int width,
        int height)
    {
        if (_padding <= 0)
            return;


        //
        // Oberer und unterer Rand
        //
        var topRow =
            new Color[width];

        var bottomRow =
            new Color[width];


        Array.Copy(
            source,
            0,
            topRow,
            0,
            width);

        Array.Copy(
            source,
            (height - 1) * width,
            bottomRow,
            0,
            width);


        for (int p = 1; p <= _padding; p++)
        {
            atlas.SetData(
                0,
                new Rectangle(
                    x,
                    y - p,
                    width,
                    1),
                topRow,
                0,
                width);

            atlas.SetData(
                0,
                new Rectangle(
                    x,
                    y + height - 1 + p,
                    width,
                    1),
                bottomRow,
                0,
                width);
        }


        //
        // Linker und rechter Rand
        //
        var leftColumn =
            new Color[height];

        var rightColumn =
            new Color[height];


        for (int yy = 0; yy < height; yy++)
        {
            leftColumn[yy] =
                source[yy * width];

            rightColumn[yy] =
                source[
                    yy * width +
                    width - 1];
        }


        for (int p = 1; p <= _padding; p++)
        {
            atlas.SetData(
                0,
                new Rectangle(
                    x - p,
                    y,
                    1,
                    height),
                leftColumn,
                0,
                height);

            atlas.SetData(
                0,
                new Rectangle(
                    x + width - 1 + p,
                    y,
                    1,
                    height),
                rightColumn,
                0,
                height);
        }


        //
        // Ecken
        //
        Color topLeft =
            source[0];

        Color topRight =
            source[width - 1];

        Color bottomLeft =
            source[(height - 1) * width];

        Color bottomRight =
            source[height * width - 1];


        for (int py = 1; py <= _padding; py++)
        {
            for (int px = 1; px <= _padding; px++)
            {
                SetPixel(
                    atlas,
                    x - px,
                    y - py,
                    topLeft);

                SetPixel(
                    atlas,
                    x + width - 1 + px,
                    y - py,
                    topRight);

                SetPixel(
                    atlas,
                    x - px,
                    y + height - 1 + py,
                    bottomLeft);

                SetPixel(
                    atlas,
                    x + width - 1 + px,
                    y + height - 1 + py,
                    bottomRight);
            }
        }
    }


    private static void SetPixel(
        Texture2D texture,
        int x,
        int y,
        Color color)
    {
        Color[] pixel = { color };

        texture.SetData(
            0,
            new Rectangle(x, y, 1, 1),
            pixel,
            0,
            1);
    }


    public void Dispose()
    {
        foreach (Atlas atlas in _atlases)
            atlas.Texture.Dispose();

        _atlases.Clear();

        _currentAtlas = null;
    }
}

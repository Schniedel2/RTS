#pragma warning disable CA1416

using System;
using System.IO;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using DrawingBitmap = System.Drawing.Bitmap;
using Microsoft.Xna.Framework.Graphics;
using DrawingColor = System.Drawing.Color;
using DrawingRectangle = System.Drawing.Rectangle;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace RTS;

public static class IOHelper
{
    public static TerrainTile RGBtoTile(XnaColor color)
    {
        if (color == XnaColor.LightGray)
            return TerrainTile.Rock;
        if (color == XnaColor.Green)
            return TerrainTile.Grass;
        if (color == XnaColor.Gray)
            return TerrainTile.Stones;
        if (color == XnaColor.SandyBrown)
            return TerrainTile.Sand;
        if (color == XnaColor.Brown)
            return TerrainTile.Dirt;

        return TerrainTile.Rock; // Default fallback
    }

    public static DrawingColor Tile2Color(TerrainTile tile)
    {
        return tile switch
        {
            TerrainTile.Rock => DrawingColor.LightGray,
            TerrainTile.Grass => DrawingColor.Green,
            TerrainTile.Stones => DrawingColor.Gray,
            TerrainTile.Sand => DrawingColor.SandyBrown,
            TerrainTile.Dirt => DrawingColor.Brown,
            _ => DrawingColor.LightGray, // Default fallback
        };
    }

    public static void SaveHeightMap(
        FileStream stream,
        byte[,] heightMap)
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        using DrawingBitmap bitmap = new(
            width,
            height,
            PixelFormat.Format8bppIndexed);

        // Graustufenpalette erzeugen
        ColorPalette palette = bitmap.Palette;

        for (int i = 0; i < 256; i++)
            palette.Entries[i] = DrawingColor.FromArgb(i, i, i);

        bitmap.Palette = palette;

        DrawingRectangle rect =
            new(0, 0, width, height);

        BitmapData data = bitmap.LockBits(
            rect,
            ImageLockMode.WriteOnly,
            PixelFormat.Format8bppIndexed);

        try
        {
            byte[] pixels =
                new byte[data.Stride * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * data.Stride + x] =
                        heightMap[x, y];
                }
            }

            Marshal.Copy(
                pixels,
                0,
                data.Scan0,
                pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(
            stream,
            ImageFormat.Png);
    }

    public static void SaveTileMapPng(
        FileStream stream,
        byte[,] tileMap)
    {
        int width = tileMap.GetLength(0);
        int height = tileMap.GetLength(1);

        using DrawingBitmap bitmap = new(
            width,
            height,
            PixelFormat.Format8bppIndexed);

        // ------------------------------------------------------------
        // Farbpalette setzen
        // ------------------------------------------------------------

        ColorPalette bitmapPalette = bitmap.Palette;

        for (int i = 0; i < 256; i++)
            bitmapPalette.Entries[i] = DrawingColor.Magenta;

        for (TerrainTile tile = TerrainTile.Min; tile <= TerrainTile.Max; tile++)
            bitmapPalette.Entries[(int)tile] = Tile2Color(tile);

        bitmap.Palette = bitmapPalette;

        // ------------------------------------------------------------
        // Pixel schreiben
        // ------------------------------------------------------------

        DrawingRectangle rectangle =
            new(0, 0, width, height);

        BitmapData data = bitmap.LockBits(
            rectangle,
            ImageLockMode.WriteOnly,
            PixelFormat.Format8bppIndexed);

        try
        {
            int stride = data.Stride;
            byte[] pixels = new byte[stride * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * stride + x] =
                        tileMap[x, y];
                }
            }

            Marshal.Copy(
                pixels,
                0,
                data.Scan0,
                pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(
            stream,
            ImageFormat.Png);
    }

    public static string[] GetFiles(string path, string _filter)
    {
        string[] batchFiles = Directory
            .GetFiles(path, _filter)
            .Select(Path.GetFileName)
            .Where(fileName => fileName is not null)
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
        return batchFiles;
    }

    public static string[] GetDirectories(string path)
    {
        List<string> mapDirectories = new();

         if (!Directory.Exists(path))
            return mapDirectories.ToArray();

        foreach (string dir in Directory.GetDirectories(path))
        {
            string dirName = Path.GetFileName(dir);
            mapDirectories.Add(dirName);
        }

        return mapDirectories.ToArray();
    }
}

#pragma warning restore CA1416
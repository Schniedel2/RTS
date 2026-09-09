using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace RTS;

public class MeshHandler
{
    public Dictionary<string, Mesh> Meshes = new();

    public void LoadMeshes()
    {
        LoadMeshes(Globals.ModelsDirectory);
    }

    public void LoadMeshes(string diretory)
    {
        Meshes["default"] = ObjMeshLoader.Load(Path.Combine(diretory, "default.obj"), Color.White, new CubeTileMapping(32, 0, 0, 2048, 2048));
        Meshes["tank"] = ObjMeshLoader.Load(Path.Combine(diretory, "tank.obj"), Color.White, new CubeTileMapping(32, 0, 0, 2048, 2048));
        Meshes["jeep"] = BBModelLoader.Load(Path.Combine(diretory, "jeep.bbmodel"), Color.White);


        CubeMapping.Apply(Meshes["jeep"], new CubeTileMapping(32, 0, 0, 2048, 2048));
    }

    public void DrawMesh(Effect effect, string meshName, Matrix baseWorld)
    {
        if (Meshes.TryGetValue(meshName, out Mesh? mesh))
            mesh.Draw(effect, baseWorld);
    }

    public void DrawMesh(Effect effect, Mesh mesh, Matrix baseWorld)
    {
        mesh.Draw(effect, baseWorld);
    }
}

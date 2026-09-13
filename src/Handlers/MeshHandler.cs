using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

using RTS.Mapping;

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
        Meshes["default"] = ObjMeshLoader.Load(Path.Combine(diretory, "default.obj"), Color.White);
        Meshes["tank"] = ObjMeshLoader.Load(Path.Combine(diretory, "tank.obj"), Color.White);
        Meshes["jeep"] = BBModelLoader.Load(Path.Combine(diretory, "jeep.bbmodel"), Color.White);
        Meshes["reaktor"] = BBModelLoader.Load(Path.Combine(diretory, "reaktor.bbmodel"), Color.White);
        Meshes["gdi-base"] = BBModelLoader.Load(Path.Combine(diretory, "gdi-base.bbmodel"), Color.White);

        Meshes["TankBody-1"] = BBModelLoader.Load(Path.Combine(diretory, "TankBody-1.bbmodel"), Color.White);
        Meshes["TankTurret-1"] = BBModelLoader.Load(Path.Combine(diretory, "TankTurret-1.bbmodel"), Color.White);
        Meshes["TankBarrel-1"] = BBModelLoader.Load(Path.Combine(diretory, "TankBarrel-1.bbmodel"), Color.White);
        Meshes["TankBarrel-2"] = BBModelLoader.Load(Path.Combine(diretory, "TankBarrel-1.bbmodel"), Color.White);

        Meshes["TankBarrel-1"].LocalTransform = Matrix.CreateScale(0.4f);
        Meshes["TankBarrel-2"].LocalTransform = Matrix.CreateScale(0.4f, 0.4f, 0.6f);

        int tileSize = 64;
        int tx = tileSize*3+8;
        int ty = tileSize*2+8;

        var Camuflage1 = new CubeTileMapping(tileSize, tx*0, ty*0, 2048, 2048);
        var Camuflage2 = new CubeTileMapping(tileSize, tx*1, ty*0, 2048, 2048);
        var Camuflage3 = new CubeProportionalTileMapping(tileSize, tx*2, ty*0, 2048, 2048, TileRepeat: 2);
        var Wheels1 = new CubeTileMapping(tileSize, tx*0, ty*1, 2048, 2048);
        var Barrel1 = new CubeTileMapping(tileSize, tx*1, ty*1, 2048, 2048);

        var Concrete1 = new CubeProportionalTileMapping(tileSize, tx*0, ty*0, 2048, 2048);
        var Bricks1 = new CubeProportionalTileMapping(tileSize, tx*1, ty*0, 2048, 2048);

        //  apply textures to meshes
        CubeMapping.Apply(Meshes["tank"], Camuflage1);
        CubeMapping.Apply(Meshes["default"], Camuflage3);
        CubeMapping.Apply("default/*", Camuflage2);

        CubeMapping.Apply(Meshes["jeep"], Camuflage2);
        CubeMapping.Apply("jeep/t:turret", Camuflage2);
        CubeMapping.Apply(Meshes["reaktor"], Concrete1);
        CubeMapping.Apply(Meshes["gdi-base"], Bricks1);

        //  apply textures to submeshesw
        CubeMapping.Apply("*/t:wheel", Wheels1);
        CubeMapping.Apply("*/t:barrel", Barrel1);

        CubeMapping.Apply("*/t:concrete", Concrete1);
        CubeMapping.Apply("*/t:house", Bricks1);
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

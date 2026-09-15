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

    public void LoadMeshes(string directory)
    {
        Meshes["default"] = ObjMeshLoader.Load(Path.Combine(directory, "default.obj"), Color.White);
        Meshes["reaktor"] = BBModelLoader.Load(Path.Combine(directory, "reaktor.bbmodel"), Color.White);
        Meshes["gdi-base"] = BBModelLoader.Load(Path.Combine(directory, "gdi-base.bbmodel"), Color.White);

        Globals.TextureHandler.AddTexture(Path.Combine(directory, "TankBody-1-MaterialMask.png"));
        Meshes["TankBody-1"] = BBModelLoader.Load(Path.Combine(directory, "TankBody-1.bbmodel"), Color.White);

        Meshes["TankTurret-1"] = BBModelLoader.Load(Path.Combine(directory, "TankTurret-1.bbmodel"), Color.White);
        Meshes["TankBarrel-1"] = BBModelLoader.Load(Path.Combine(directory, "TankBarrel-1.bbmodel"), Color.White);
        Meshes["TankBarrel-2"] = BBModelLoader.Load(Path.Combine(directory, "TankBarrel-1.bbmodel"), Color.White);

        Meshes["bulldozer-1"] = BBModelLoader.Load(Path.Combine(directory, "bulldozer-1.bbmodel"), Color.White);
        //Meshes["bulldozer-1"].LocalTransform = Matrix.CreateScale(0.5f);

        Meshes["blue-pick-up-truck"] = BBModelLoader.Load(Path.Combine(directory, "blue-pick-up-truck.bbmodel"), Color.White);
        Meshes["blue-pick-up-truck"].LocalTransform = Matrix.CreateScale(1.0f);

        Meshes["TankBarrel-1"].LocalTransform = Matrix.CreateScale(0.4f);
        Meshes["TankBarrel-2"].LocalTransform = Matrix.CreateScale(0.4f, 0.4f, 0.6f);

        // TankBody receives its matching mask automatically when imported;
        // the turret is deliberately a complete player-skin surface.
        Meshes["TankTurret-1"].SetFullSkinMaterialMask();

        int tileSize = 64;
        int tx = tileSize*3+8;
        int ty = tileSize*2+8;

        var Concrete1 = new CubeProportionalTileMapping(tileSize, tx*0, ty*0, 2048, 2048);
        var Bricks1 = new CubeProportionalTileMapping(tileSize, tx*1, ty*0, 2048, 2048);

        //  apply textures to meshes

        CubeMapping.Apply(Meshes["reaktor"], Concrete1);
        CubeMapping.Apply(Meshes["gdi-base"], Bricks1);
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

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
        Meshes["animation-template"] = BBModelLoader.Load(Path.Combine(directory, "animation-template.bbmodel"), Color.White);        

        //  guns/weapons for soldiers    
        Meshes["Rifle-1"] = BBModelLoader.Load(Path.Combine(directory, "rifle-1.bbmodel"), Color.White);
        Meshes["ak47"] = BBModelLoader.Load(Path.Combine(directory, "guns/ak47.bbmodel"), Color.White);
        Meshes["m16"] = BBModelLoader.Load(Path.Combine(directory, "guns/m16.bbmodel"), Color.White);
        Meshes["breda-m1935pg"] = BBModelLoader.Load(Path.Combine(directory, "guns/breda-m1935pg.bbmodel"), Color.White);
        Meshes["brok17"] = BBModelLoader.Load(Path.Combine(directory, "guns/brok-17.bbmodel"), Color.White);
        Meshes["kraber-ap-sniper"] = BBModelLoader.Load(Path.Combine(directory, "guns/kraber-ap-sniper.bbmodel"), Color.White);
        Meshes["uzi-mac-10"] = BBModelLoader.Load(Path.Combine(directory, "guns/uzi-mac-10.bbmodel"), Color.White);
        Meshes["minigun"] = BBModelLoader.Load(Path.Combine(directory, "guns/minigun.bbmodel"), Color.White);
        Meshes["hunting"] = BBModelLoader.Load(Path.Combine(directory, "guns/hunting.bbmodel"), Color.White);
        Meshes["ar-15"] = BBModelLoader.Load(Path.Combine(directory, "guns/ar-15.bbmodel"), Color.White);
        Meshes["sten-mk2-apocalypse"] = BBModelLoader.Load(Path.Combine(directory, "guns/sten-mk2-apocalypse.bbmodel"), Color.White);
        
        // Attachments inherit Soldier-1's 0.3 local scale. Rifle-1's authored
        // geometry is correspondingly smaller, so scale it back up locally.
        // Because its root pivot is (0,0,0), this does not move the attachment
        // point itself.
        Meshes["Rifle-1"].LocalTransform = Matrix.CreateScale(3.0f);

        Meshes["default"] = BBModelLoader.Load(Path.Combine(directory, "Soldier-1.bbmodel"), Color.White);
        
        Meshes["Soldier-2"] = BBModelLoader.Load(Path.Combine(directory, "Soldier-2.bbmodel"), Color.White);
        Meshes["Soldier-2"].LocalTransform = Matrix.CreateScale(0.3f);

        Meshes["reaktor"] = BBModelLoader.Load(Path.Combine(directory, "reaktor.bbmodel"), Color.White);
        Meshes["gdi-base"] = BBModelLoader.Load(Path.Combine(directory, "gdi-base.bbmodel"), Color.White);
        Meshes["barracks-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/barracks-1.bbmodel"), Color.White);

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

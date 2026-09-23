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
        Meshes["rpg"] = BBModelLoader.Load(Path.Combine(directory, "guns/rpg.bbmodel"), Color.White);

        Meshes["rpg-projectile"] = BBModelLoader.Load(Path.Combine(directory, "guns/rpg-projectile.bbmodel"), Color.White);

        Meshes["default"] = BBModelLoader.Load(Path.Combine(directory, "Soldier-1.bbmodel"), Color.White);
        
        Meshes["Soldier-2"] = BBModelLoader.Load(Path.Combine(directory, "Soldier-2.bbmodel"), Color.White);
        Meshes["Soldier-2"].LocalTransform = Matrix.CreateScale(0.2f);

        Meshes["reaktor"] = BBModelLoader.Load(Path.Combine(directory, "reaktor.bbmodel"), Color.White);
        Meshes["gdi-base"] = BBModelLoader.Load(Path.Combine(directory, "gdi-base.bbmodel"), Color.White);
        Meshes["barracks-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/barracks-1.bbmodel"), Color.White);
        Meshes["building-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/building-1.bbmodel"), Color.White);
        Meshes["building-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/building-1.bbmodel"), Color.White);
        Meshes["antenna-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/antenna-1.bbmodel"), Color.White);
        Meshes["helipad-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/helipad-1.bbmodel"), Color.White);
        Meshes["silo-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/silo-1.bbmodel"), Color.White);
        
        //  environment
        Meshes["tiberium-1"] = BBModelLoader.Load(Path.Combine(directory, "environment/tiberium-1.bbmodel"), Color.White);
        Meshes["tiberiumSource-1"] = BBModelLoader.Load(Path.Combine(directory, "environment/tiberiumSource-1.bbmodel"), Color.White);

        //  vehicles        
        Meshes["Tank-1"] = BBModelLoader.Load(Path.Combine(directory, "vehicles/tank-1.bbmodel"), Color.White);
        Meshes["heli-1"] = BBModelLoader.Load(Path.Combine(directory, "vehicles/heli-1.bbmodel"), Color.White);
        Meshes["harvester-1"] = BBModelLoader.Load(Path.Combine(directory, "vehicles/harvester-1.bbmodel"), Color.White);


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

        // Apply after all permanent mesh scales have been configured.
        foreach (Mesh mesh in Meshes.Values)
            mesh.ApplySharedTextureMapping();

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

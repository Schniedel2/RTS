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

    public void LoadMeshes(string directory, bool loadTextures = true)
    {
        Meshes["animation-template"] = BBModelLoader.Load(Path.Combine(directory, "animation-template.bbmodel"), Color.White, loadTextures);

        //  guns/weapons for soldiers    
        Meshes["Rifle-1"] = BBModelLoader.Load(Path.Combine(directory, "rifle-1.bbmodel"), Color.White, loadTextures);
        Meshes["ak47"] = BBModelLoader.Load(Path.Combine(directory, "guns/ak47.bbmodel"), Color.White, loadTextures);
        Meshes["m16"] = BBModelLoader.Load(Path.Combine(directory, "guns/m16.bbmodel"), Color.White, loadTextures);
        Meshes["breda-m1935pg"] = BBModelLoader.Load(Path.Combine(directory, "guns/breda-m1935pg.bbmodel"), Color.White, loadTextures);
        Meshes["brok17"] = BBModelLoader.Load(Path.Combine(directory, "guns/brok-17.bbmodel"), Color.White, loadTextures);
        Meshes["kraber-ap-sniper"] = BBModelLoader.Load(Path.Combine(directory, "guns/kraber-ap-sniper.bbmodel"), Color.White, loadTextures);
        Meshes["uzi-mac-10"] = BBModelLoader.Load(Path.Combine(directory, "guns/uzi-mac-10.bbmodel"), Color.White, loadTextures);
        Meshes["minigun"] = BBModelLoader.Load(Path.Combine(directory, "guns/minigun.bbmodel"), Color.White, loadTextures);
        Meshes["hunting"] = BBModelLoader.Load(Path.Combine(directory, "guns/hunting.bbmodel"), Color.White, loadTextures);
        Meshes["ar-15"] = BBModelLoader.Load(Path.Combine(directory, "guns/ar-15.bbmodel"), Color.White, loadTextures);
        Meshes["sten-mk2-apocalypse"] = BBModelLoader.Load(Path.Combine(directory, "guns/sten-mk2-apocalypse.bbmodel"), Color.White, loadTextures);
        Meshes["rpg"] = BBModelLoader.Load(Path.Combine(directory, "guns/rpg.bbmodel"), Color.White, loadTextures);
        Meshes["toolKit"] = BBModelLoader.Load(Path.Combine(directory, "guns/toolKit.bbmodel"), Color.White, loadTextures);
        Meshes["medKit"] = BBModelLoader.Load(Path.Combine(directory, "guns/medKit.bbmodel"), Color.White, loadTextures);

        Meshes["rpg-projectile"] = BBModelLoader.Load(Path.Combine(directory, "guns/rpg-projectile.bbmodel"), Color.White, loadTextures);

        Meshes["default"] = BBModelLoader.Load(Path.Combine(directory, "Soldier-1.bbmodel"), Color.White, loadTextures);
        
        Meshes["Soldier-2"] = BBModelLoader.Load(Path.Combine(directory, "Soldier-2.bbmodel"), Color.White, loadTextures);
        Meshes["Soldier-2"].LocalTransform = Matrix.CreateScale(0.2f);

        Meshes["reaktor-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/reaktor-1.bbmodel"), Color.White, loadTextures);
        Meshes["reaktor-2"] = BBModelLoader.Load(Path.Combine(directory, "buildings/reaktor-2.bbmodel"), Color.White, loadTextures);
        Meshes["gdi-base"] = BBModelLoader.Load(Path.Combine(directory, "buildings/radarbase-1.bbmodel"), Color.White, loadTextures);
        Meshes["barracks-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/barracks-1.bbmodel"), Color.White, loadTextures);
        Meshes["building-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/building-1.bbmodel"), Color.White, loadTextures);
        Meshes["building-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/building-1.bbmodel"), Color.White, loadTextures);
        Meshes["antenna-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/antenna-1.bbmodel"), Color.White, loadTextures);
        Meshes["helipad-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/helipad-1.bbmodel"), Color.White, loadTextures);
        Meshes["silo-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/silo-1.bbmodel"), Color.White, loadTextures);
        Meshes["tiberium-refinery-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/tiberium-refinery-1.bbmodel"), Color.White, loadTextures);
        Meshes["vehicle-factory-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/vehicle-factory-1.bbmodel"), Color.White, loadTextures);
        Meshes["gatling-tower-1"] = BBModelLoader.Load(Path.Combine(directory, "buildings/gatling-tower-1.bbmodel"), Color.White, loadTextures);
        
        //  environment
        Meshes["tiberium-1"] = BBModelLoader.Load(Path.Combine(directory, "environment/tiberium-1.bbmodel"), Color.White, loadTextures);
        Meshes["tiberiumSource-1"] = BBModelLoader.Load(Path.Combine(directory, "environment/tiberiumSource-1.bbmodel"), Color.White, loadTextures);

        //  vehicles        
        Meshes["tank-1"] = BBModelLoader.Load(Path.Combine(directory, "vehicles/tank-1.bbmodel"), Color.White, loadTextures);
        Meshes["gepard-1"] = BBModelLoader.Load(Path.Combine(directory, "vehicles/gepard-1.bbmodel"), Color.White, loadTextures);
        Meshes["heli-1"] = BBModelLoader.Load(Path.Combine(directory, "vehicles/heli-1.bbmodel"), Color.White, loadTextures);
        Meshes["harvester-1"] = BBModelLoader.Load(Path.Combine(directory, "vehicles/harvester-1.bbmodel"), Color.White, loadTextures);
        Meshes["heli-1"] = BBModelLoader.Load(Path.Combine(directory, "vehicles/heli-1.bbmodel"), Color.White, loadTextures);
        Meshes["bulldozer-1"] = BBModelLoader.Load(Path.Combine(directory, "vehicles/bulldozer-1.bbmodel"), Color.White, loadTextures);
        Meshes["motorbike-1"] = BBModelLoader.Load(Path.Combine(directory, "vehicles/motorbike-1.bbmodel"), Color.White, loadTextures);
        Meshes["motorbike-1"].LocalTransform = Matrix.CreateScale(0.2f);

        Meshes["blue-pick-up-truck"] = BBModelLoader.Load(Path.Combine(directory, "blue-pick-up-truck.bbmodel"), Color.White, loadTextures);
        Meshes["blue-pick-up-truck"].LocalTransform = Matrix.CreateScale(1.0f);

        // Apply after all permanent mesh scales have been configured.
        if (loadTextures)
            foreach (Mesh mesh in Meshes.Values) mesh.ApplySharedTextureMapping();

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

using RTS;
using RTS.Network;
using System.Reflection;
using System.Text.Json;
internal static class BotClientChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string text) { if (!condition) throw new Exception("Bot client: " + text); checks++; }
        var valid = new BotClientConfig(1, "127.0.0.1", 27000, "Bot", "balanced-assault");
        valid.Validate("test"); Check(true, "valid config");
        foreach (var invalid in new[] { valid with { SchemaVersion = 2 }, valid with { ServerAddress = "" },
            valid with { Port = 0 }, valid with { Port = 65536 }, valid with { DisplayName = " " },
            valid with { DisplayName = new string('x', 33) }, valid with { MaximumArmies = 0 },
            valid with { MaximumArmies = 33 }, valid with { ProposedProfileId = "unknown" }, valid with { DiagnosticsPath = " " } })
        {
            bool rejected = false;
            try { invalid.Validate("test"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid field rejected");
        }
        string path = Path.GetTempFileName();
        try
        {
            foreach (string json in new[] { "null", "{}", "[]", "{\"schemaVersion\":1,\"schemaVersion\":1}",
                "{\"schemaVersion\":1,\"serverAddress\":\"localhost\",\"port\":27000,\"displayName\":\"Bot\",\"armyId\":\"x\"}" })
            {
                File.WriteAllText(path, json); bool rejected = false;
                try { BotClientConfig.Load(path); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "bad/unknown/missing/duplicate fields rejected");
            }
            File.WriteAllText(path, JsonSerializer.Serialize(valid, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            Check(BotClientConfig.Load(path) == valid, "config round trip");
        }
        finally { File.Delete(path); }
        var profile = AIStrategyProfile.Create(23, Guid.NewGuid());
        var assignment = new AIControllerAssignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, profile);
        var copy = JsonSerializer.Deserialize<AIControllerAssignment>(JsonSerializer.Serialize(assignment))!;
        Check(copy == assignment && copy.ProfileFingerprint.Length == 64, "resolved profile/version/fingerprint round trip");
        Check(AIControllerAssignment.Fingerprint(profile with { RequiredTanks = profile.RequiredTanks + 1 }) != assignment.ProfileFingerprint, "effective value changes fingerprint");
        var registry = new AIControllerAssignments();
        var apply = typeof(AIControllerAssignments).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!;
        apply.Invoke(registry, [copy]);
        foreach (var invalid in new[] { assignment with { ProfileVersion = 2 }, assignment with { ProfileFingerprint = "tampered" }, assignment with { Profile = profile with { Seed = profile.Seed + 1 } } })
        {
            bool rejected = false;
            try { apply.Invoke(registry, [invalid]); } catch (TargetInvocationException e) when (e.InnerException is ArgumentException) { rejected = true; }
            Check(rejected, "mismatched fingerprint/version rejected");
        }
        Check(new BotControllerOffer(1, "anti-armor").IsValid && !new BotControllerOffer(0, null).IsValid && !new BotControllerOffer(1, "unknown").IsValid, "capabilities validated");
        // Load actual assets without any graphics device, console, HUD or texture handler.
        var meshes = new MeshHandler();
        meshes.LoadMeshes(Path.Combine(Environment.CurrentDirectory, "Content", "Models"), loadTextures: false);
        Check(meshes.Meshes.Count > 25, "full real model library imported");
        Check(meshes.Meshes["tiberium-refinery-1"].FootprintBounds.Count > 0, "real authored footprint retained");
        Check(meshes.Meshes["tiberium-refinery-1"].TryGetPivotWorldTransform("pivot:unload", Microsoft.Xna.Framework.Matrix.Identity, new Dictionary<string,float>(), out _), "real unload pivot retained");
        Check(meshes.Meshes["Soldier-2"].Animations.Count > 0 && meshes.Meshes["motorbike-1"].LocalTransform == Microsoft.Xna.Framework.Matrix.CreateScale(0.2f), "animation/scaling metadata retained");
        var particles = new ParticleSystem(graphicsEnabled: false);
        particles.EmitSmoke(Microsoft.Xna.Framework.Vector3.Zero, Microsoft.Xna.Framework.Vector3.Up, SmokeEmissionPresets.VehicleExhaust());
        particles.EmitExplosion(Microsoft.Xna.Framework.Vector3.Zero);
        Check(particles.ActiveParticleCount == 0, "headless effects allocate no particles/atlas");
        return checks;
    }
}


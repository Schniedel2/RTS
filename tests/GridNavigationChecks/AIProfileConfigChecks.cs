using System.Text.Json.Nodes;
using RTS;

internal static class AIProfileConfigChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new Exception("AI profiles: " + message); checks++; }
        string examples = Path.Combine(AppContext.BaseDirectory, "Config", "AI", "Profiles");
        AIProfileCatalog catalog = AIProfileCatalog.LoadDirectory(examples);
        foreach (AIStrategyProfileType type in Enum.GetValues<AIStrategyProfileType>())
        {
            Check(catalog.Profiles[type] == AIProfileCatalog.BuiltIn(type), "shipped defaults: " + type);
            Check(catalog.Resolve(type, 123).Seed == 123, "seed remains runtime state");
        }
        string json = File.ReadAllText(Path.Combine(examples, "balanced-assault.json"));
        void Reject(string input, string field)
        {
            try { AIProfileCatalog.Parse(input, "broken.json"); throw new Exception("Accepted invalid " + field); }
            catch (InvalidDataException error)
            { Check(error.Message.Contains("broken.json") && error.Message.Contains(field), "file/field diagnostic: " + field); }
        }
        void RejectField(string field, JsonNode? value)
        { JsonObject obj = JsonNode.Parse(json)!.AsObject(); obj[field] = value; Reject(obj.ToJsonString(), field); }
        Reject("null", "$"); Reject("[]", "$"); Reject("{", "$");
        Reject(json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"), "schemaVersion");
        foreach (string field in JsonNode.Parse(json)!.AsObject().Select(pair => pair.Key).ToArray())
        {
            JsonObject obj = JsonNode.Parse(json)!.AsObject(); obj.Remove(field);
            if (field is not ("decisionIntervalSeconds" or "resourceReserve" or "scoutReconsiderSeconds")) Reject(obj.ToJsonString(), field);
            RejectField(field, null);
        }
        RejectField("schemaVersion", 2); RejectField("profileId", "other");
        RejectField("type", "Unknown"); RejectField("type", 0);
        RejectField("displayName", " "); RejectField("displayName", new string('x', 81));
        RejectField("unknownField", 1);
        RejectField("requiredGunners", -1); RejectField("requiredGunners", 9);
        RejectField("requiredRakZero", 8); RejectField("requiredTanks", 21);
        RejectField("retreatHealthFraction", -0.1); RejectField("retreatHealthFraction", 1.1);
        RejectField("attackReadinessSeconds", -1); RejectField("attackReadinessSeconds", 601);
        RejectField("defenseRadiusInCells", 0); RejectField("defenseRadiusInCells", 257);
        RejectField("assaultStallTimeoutSeconds", 0); RejectField("assaultStallTimeoutSeconds", 601);
        RejectField("attackReadinessSeconds", "NaN"); RejectField("defenseRadiusInCells", "Infinity");
        Reject(json.Replace("\"attackReadinessSeconds\": 5", "\"attackReadinessSeconds\": 1e1000"), "attackReadinessSeconds");
        string directory = Path.Combine(Path.GetTempPath(), "RTS-AI-Profiles-" + Guid.NewGuid().ToString("N"));
        try
        {
            Check(AIProfileCatalog.LoadDirectory(directory, optional: true).Profiles.SequenceEqual(catalog.Profiles), "missing optional directory defaults");
            try { AIProfileCatalog.LoadDirectory(directory); throw new Exception("Missing explicit directory accepted"); }
            catch (DirectoryNotFoundException) { checks++; }
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "balanced.json");
            JsonObject changed = JsonNode.Parse(json)!.AsObject(); changed["attackReadinessSeconds"] = 17;
            File.WriteAllText(file, changed.ToJsonString());
            AIProfileCatalog custom = AIProfileCatalog.LoadDirectory(directory, optional: true);
            Check(custom.Profiles[AIStrategyProfileType.FastRecon] == AIProfileCatalog.BuiltIn(AIStrategyProfileType.FastRecon), "missing optional file defaults");
            try { AIProfileCatalog.LoadDirectory(directory); throw new Exception("Incomplete explicit directory accepted"); }
            catch (InvalidDataException error) { Check(error.Message.Contains("missing profiles"), "strict directory completeness"); }
            Guid army = Guid.Parse("00000000-0000-0000-0000-000000000001");
            int seed = Enumerable.Range(0, 1000).First(n => AIStrategyProfile.Create(n, army, catalog).Type == AIStrategyProfileType.BalancedAssault);
            AIStrategyProfile resolved = AIStrategyProfile.Create(seed, army, custom);
            Check(resolved.AttackReadinessSeconds == 17, "controller profile factory consumes configuration");
            Check(resolved == AIStrategyProfile.Create(seed, army, custom), "stable match/army selection");
            File.WriteAllText(file, json);
            Check(custom.Resolve(resolved.Type, resolved.Seed) == resolved, "catalog performs no live file reads");
            Check(AIProfileCatalog.LoadDirectory(directory, true).Resolve(resolved.Type, resolved.Seed).AttackReadinessSeconds == 5, "new catalog independently resolves changed file");
            try { ((IDictionary<AIStrategyProfileType, AIProfileConfig>)custom.Profiles).Clear(); throw new Exception("Mutable catalog"); }
            catch (NotSupportedException) { checks++; }
            File.WriteAllText(Path.Combine(directory, "duplicate.json"), json);
            try { AIProfileCatalog.LoadDirectory(directory, true); throw new Exception("Duplicate accepted"); }
            catch (InvalidDataException error) { Check(error.Message.Contains("$.type"), "duplicate strategy rejected"); }
            File.Delete(Path.Combine(directory, "duplicate.json"));
            File.WriteAllText(file, "null");
            try { AIProfileCatalog.LoadDirectory(directory, true); throw new Exception("Invalid optional file silently defaulted"); }
            catch (InvalidDataException error) { Check(error.Message.Contains(file), "present invalid config never defaults"); }
            Check(custom.Resolve(resolved.Type, resolved.Seed) == resolved, "failed reload cannot partially mutate existing catalog");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        return checks;
    }
}

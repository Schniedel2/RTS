using RTS;
using System.Text.Json.Nodes;
internal static class AIRuntimeSettingsChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception("AI settings: " + reason); count++; }
        var profiles = AIProfileCatalog.Default;
        var selection = new AISelectionSettings();
        Guid army = Guid.Parse("a030a67e-e93e-435b-814a-34734830baec");
        for (int match = 0; match < 100; match++)
        {
            uint hash = unchecked((uint)match) ^ 2166136261u;
            foreach (byte value in army.ToByteArray()) hash = unchecked((hash ^ value) * 16777619u);
            int seed = unchecked((int)hash), roll = new Random(seed).Next(100);
            var expected = roll < 40 ? AIStrategyProfileType.BalancedAssault : roll < 65 ? AIStrategyProfileType.InfantryCompany : roll < 90 ? AIStrategyProfileType.AntiArmor : AIStrategyProfileType.FastRecon;
            Check(selection.Select(match, army, profiles) == profiles.Resolve(expected, seed), "legacy selection and seed preserved");
        }
        var fixedProfile = selection with { FixedProfileId = "anti-armor", MatchSeed = 42 };
        Check(fixedProfile.Select(1, army, profiles) == fixedProfile.Select(999, army, profiles), "configured seed overrides generated match seed");
        Check(fixedProfile.Select(1, army, profiles).Type == AIStrategyProfileType.AntiArmor, "fixed profile");
        Check(fixedProfile.Select(1, army, profiles).Seed != fixedProfile.Select(1, Guid.Empty, profiles).Seed, "army still diversifies effective seed");
        Check((selection with { BalancedAssaultWeight = 0, InfantryCompanyWeight = 0, AntiArmorWeight = 0 }).Select(10, army, profiles).Type == AIStrategyProfileType.FastRecon, "weighted selection consumes weights");
        string temp = Path.Combine(Path.GetTempPath(), "RTS-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            foreach (string json in new[] { "null", "[]", "{\"unknown\":1}", "{\"schemaVersion\":2}", "{\"fixedProfileId\":\"missing\"}", "{\"balancedAssaultWeight\":-1}", "{\"matchSeed\":null,\"matchSeed\":1}" })
            {
                File.WriteAllText(temp, json);
                try { AIRuntimeSettings.LoadSelection(temp); throw new Exception("Invalid selection accepted"); }
                catch (InvalidDataException error) { Check(error.Message.Contains(temp), "contextual selection rejection"); }
            }
            foreach (string json in new[] { "null", "{\"schemaVersion\":2}", "{\"planningStepsPerUpdate\":0}", "{\"planningMillisecondsPerUpdate\":1e1000}", "{\"planningMillisecondsPerUpdate\":21}" })
            {
                File.WriteAllText(temp, json);
                try { AIRuntimeSettings.LoadCompute(temp); throw new Exception("Invalid compute accepted"); }
                catch (InvalidDataException error) { Check(error.Message.Contains(temp), "contextual compute rejection"); }
            }
            File.Delete(temp);
            Check(AIRuntimeSettings.LoadSelection(temp, true) == selection, "optional selection defaults");
            Check(AIRuntimeSettings.LoadCompute(temp, true) == new AIComputeSettings(), "optional compute defaults");
            try { AIRuntimeSettings.LoadSelection(temp); throw new Exception("Explicit missing accepted"); }
            catch (FileNotFoundException) { count++; }
            File.WriteAllText(temp, "{\"fixedProfileId\":\"fast-recon\",\"matchSeed\":42}");
            Check(AIRuntimeSettings.LoadSelection(temp).Select(1, army, profiles).Type == AIStrategyProfileType.FastRecon, "file selection applied");
        }
        finally { File.Delete(temp); }
        // Shared round-robin scheduler: both armies progress under a reduced global limit.
        var scheduler = new PlanningScheduler(); int first = 0, second = 0;
        IEnumerable<int> Work(Action tick) { for (int i = 0; i < 100; i++) { tick(); yield return 0; } }
        scheduler.Enqueue(Work(() => first++), () => true, () => { });
        scheduler.Enqueue(Work(() => second++), () => true, () => { });
        var compute = new AIComputeSettings(PlanningStepsPerUpdate: 16, PlanningMillisecondsPerUpdate: 20);
        for (int i = 0; i < 4; i++) { scheduler.Update(compute.PlanningStepsPerUpdate, compute.PlanningMillisecondsPerUpdate); Check(scheduler.LastSteps <= 16, "shared work cap"); }
        Check(first == 32 && second == 32, "fairness across armies at reduced cap");
        Check(fixedProfile.Select(1, army, profiles) == fixedProfile.Select(1, army, profiles), "compute setting independent of behavior");
        // Reserve affects purchases, urgent repair is allowed to spend it.
        var budget = new AIResourcePlanner { ConfiguredReserve = 1000 };
        var begin = typeof(AIResourcePlanner).GetMethod("Begin", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var allocate = typeof(AIResourcePlanner).GetMethod("TryAllocate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        begin.Invoke(budget, new object[] { 1500, 0, 0 });
        Check(!(bool)allocate.Invoke(budget, new object[] { 600, AIOrderPriority.Production })!, "configured reserve protects funds");
        begin.Invoke(budget, new object[] { 1500, 0, 0 });
        Check((bool)allocate.Invoke(budget, new object[] { 600, AIOrderPriority.Survival })!, "survival bypass remains");
        budget.ConfiguredReserve = 800; begin.Invoke(budget, new object[] { 1500, 0, 0 });
        Check((bool)allocate.Invoke(budget, new object[] { 600, AIOrderPriority.Production })!, "default reserve behavior");
        string baseJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "AI", "Profiles", "balanced-assault.json"));
        foreach (var pair in new[] { ("decisionIntervalSeconds", 0), ("resourceReserve", -1), ("scoutReconsiderSeconds", 121) })
        {
            var obj = JsonNode.Parse(baseJson)!.AsObject(); obj[pair.Item1] = pair.Item2;
            try { AIProfileCatalog.Parse(obj.ToJsonString(), "profile.json"); throw new Exception("Bad behavior accepted"); }
            catch (InvalidDataException error) { Check(error.Message.Contains(pair.Item1), "behavior bound"); }
        }
        return count;
    }
}

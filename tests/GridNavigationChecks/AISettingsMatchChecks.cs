using RTS;
using Microsoft.Xna.Framework;
internal static partial class AIReconstructionChecks
{
    public static int RunRepeatableAISettingsMatch()
    {
        // Sequential runs avoid sharing Globals between concurrently simulated worlds.
        string[] Run()
        {
            using Scenario scenario = new(false);
            scenario.AI.BeginMatch(1234, scenario.Army.Id);
            var trace = new List<string>();
            for (int tick = 0; tick < 30; tick++)
            {
                scenario.Tick();
                trace.Add($"{scenario.AI.Controller.StrategyProfile}|{scenario.AI.Controller.Goal}|{scenario.Army.Resources}|" +
                    string.Join(",", scenario.Messages.Select(m => m.Type)));
            }
            return trace.ToArray();
        }
        if (!Run().SequenceEqual(Run())) throw new Exception("Same-seed headless AI comparison diverged.");
        return 30;
    }
}

using System;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    public static int RunAIContextChecks()
    {
        int checks = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new Exception("AI context: " + message); checks++; }
        using Scenario scenario = new(false);
        AIContext context = new(scenario.AI.Player, scenario.World, scenario.Network);
        Check(context.ArmyId == scenario.Army.Id && context.ActorId == scenario.AI.Id, "explicit controller identity");
        Check(ReferenceEquals(context.Army, scenario.Army), "world-owned army lookup");
        Check(ReferenceEquals(context.Pricing, scenario.World.SimulationPricing), "shared world pricing service");
        Check(context.Commands.PlayerId == scenario.AI.Id && context.CreateCommands().PlayerId == scenario.AI.Id, "commands use AI actor");
        context.Advance(new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(0.5)));
        Check(context.SimulationTime == 10 && context.ElapsedSeconds == 0.5f, "simulation clock supplied explicitly");
        Check(context.SessionGeneration == scenario.Network.SessionGeneration, "session generation supplied by own network");
        Check(context.Matches(scenario.AI.Player, scenario.World, scenario.Network), "matching context reusable");
        Check(!context.Matches(new Player(Guid.NewGuid(), "Other"), scenario.World, scenario.Network), "wrong actor rejected");
        bool savedSpectator = Globals.IsSpectator, savedFog = Globals.FogOfWarEnabled;
        RTSGame savedGame = Globals.Game;
        try
        {
            Globals.Game = null!;
            Globals.IsSpectator = true; Globals.FogOfWarEnabled = false;
            Point corner = new(63, 63);
            Check(context.Visibility(corner) == VisibilityState.Unexplored, "spectator and disabled display fog do not reveal AI terrain");
            Check(context.FindProduct(PurchasableType.Unit, "gunner") is not null, "catalog accessible without client facade");
            Check(context.Pricing.GetQuote(new(PurchasableType.Building, "reaktor", context.ArmyId)).IsAvailable, "pricing uses own army perks without client facade");
            Check(AIStrategicCatalog.SelectBuilding(scenario.World, context.ArmyId, AIStrategicBuildingNeed.Power) is not null,
                "strategic catalog works without client facade");
            Guid otherId = Guid.NewGuid();
            Army other = scenario.World.SimulationArmies.EnsureArmy(otherId, Guid.NewGuid(), scenario.Army.TeamId);
            other.Resources = 123;
            scenario.Army.Resources = 456;
            Check(context.Army!.Resources == 456, "other army resources cannot contaminate context");
            scenario.World.Visibility.GetGrid(otherId).Reveal(corner, 0);
            Check(context.Visibility(corner) == VisibilityState.Unexplored, "ally vision not shared implicitly");
            scenario.Army.Intelligence.ShareWorldVision = true;
            Check(context.Visibility(corner) == VisibilityState.Visible, "explicit ally vision honored without presentation cache");
            scenario.Army.Intelligence.ShareWorldVision = false;
            scenario.World.Visibility.GetGrid(context.ArmyId).Reveal(corner, 0);
            Check(context.Visibility(corner) == VisibilityState.Visible, "own vision honored");
            scenario.World.Visibility.GetGrid(context.ArmyId).BeginUpdate();
            Check(context.Visibility(corner) == VisibilityState.Explored, "own explored history honored");
            scenario.Army.Resources = 0;
            scenario.AI.Controller.Update(new GameTime(TimeSpan.FromSeconds(11), TimeSpan.Zero), scenario.AI, context);
            Check(ReferenceEquals(scenario.AI.Controller.Context, context), "host decision update works without client facade");
            scenario.Army.Resources = 456;
            GameWorld independent = new(65, 65, 1, graphicsEnabled: false);
            Army independentArmy = independent.SimulationArmies.EnsureArmy(context.ArmyId, context.ActorId);
            AIContext independentContext = new(scenario.AI.Player, independent, scenario.Network);
            Check(!independentContext.Pricing.GetQuote(new(PurchasableType.Building, "reaktor", context.ArmyId)).IsAvailable,
                "same ArmyId in different world does not borrow perks");
            Check(!context.Matches(scenario.AI.Player, independent, scenario.Network), "different world requires own context");
            independentArmy.Resources = 12;
            Check(independentContext.Army!.Resources == 12 && context.Army.Resources == 456, "world resources remain isolated");
        }
        finally
        { Globals.Game = savedGame; Globals.IsSpectator = savedSpectator; Globals.FogOfWarEnabled = savedFog; }
        Guid original = scenario.AI.Player.ArmyId;
        scenario.AI.Player.SetArmy(Guid.NewGuid());
        Check(!context.Matches(scenario.AI.Player, scenario.World, scenario.Network), "army reassignment invalidates old context");
        try { context.Advance(new()); throw new Exception("Changed army accepted"); }
        catch (InvalidOperationException) { Check(true, "stale army context rejected"); }
        scenario.AI.Player.SetArmy(original);
        scenario.Tick();
        Check(scenario.AI.Controller.Context?.ArmyId == original, "host controller enters explicit context");
        return checks;
    }
}

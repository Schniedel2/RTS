using System;
using Microsoft.Xna.Framework;
using RTS.Network;

namespace RTS;

/// <summary>One controller's explicit simulation dependencies, without client presentation state.</summary>
public sealed class AIContext
{
    public Player Actor { get; }
    public Guid ActorId { get; }
    public Guid ArmyId { get; }
    public GameWorld World { get; }
    public ArmyHandler Armies => World.SimulationArmies;
    public Army? Army => Armies.Find(ArmyId);
    public PricingService Pricing => World.SimulationPricing;
    public NetworkHandler Network { get; }
    public PlayerCommandService Commands { get; }
    private readonly long _session;
    private readonly long? _assignment;
    public double SimulationTime { get; private set; }
    public float ElapsedSeconds { get; private set; }
    public long SessionGeneration => Network.SessionGeneration;

    public AIContext(Player actor, GameWorld world, NetworkHandler network)
    {
        Actor = actor ?? throw new ArgumentNullException(nameof(actor));
        World = world ?? throw new ArgumentNullException(nameof(world));
        Network = network ?? throw new ArgumentNullException(nameof(network));
        ActorId = actor.Id;
        ArmyId = actor.ArmyId;
        Commands = new(network, ActorId);
        _session = network.SessionGeneration; _assignment = network.AIControllers.ForActor(ActorId)?.Generation;
    }

    public bool Matches(Player actor, GameWorld world, NetworkHandler network) =>
        ReferenceEquals(Actor, actor) && actor.ArmyId == ArmyId &&
        ReferenceEquals(World, world) && ReferenceEquals(Network, network) &&
        _session == network.SessionGeneration && _assignment == network.AIControllers.ForActor(ActorId)?.Generation;

    public void Advance(GameTime time)
    {
        if (Actor.ArmyId != ArmyId) throw new InvalidOperationException("Army changed; recreate the AI context.");
        SimulationTime = time.TotalGameTime.TotalSeconds;
        ElapsedSeconds = Math.Max(0, (float)time.ElapsedGameTime.TotalSeconds);
    }

    public VisibilityState Visibility(Point cell) => World.Visibility.GetSimulationVisibility(ArmyId, cell);
    public GameplayDefinition? FindProduct(PurchasableType type, string id) => GameplayCatalog.Find(type, id);
    public PlayerCommandService CreateCommands(AIUnitTaskAgent? task = null) => new(Network, ActorId, task);
}

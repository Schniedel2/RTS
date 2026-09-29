using RTS.Network;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Viewport = Microsoft.Xna.Framework.Graphics.Viewport;
using RTS;
using System.Reflection;
using System.Runtime.CompilerServices;

// Exercise production navigation without creating a graphics device or loading assets.
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
void Field(object target, Type owner, string name, object value) =>
    owner.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
List<Unit> UnitList(UnitHandler handler) =>
    (List<Unit>)typeof(UnitHandler).GetField("_units", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!;
MobileUnit Unit(GroundMovementProfile? profile = null, int width = 1, int length = 1)
{
    MobileUnit unit = Empty<MobileUnit>();
    Field(unit, typeof(Unit), "<Width>k__BackingField", width);
    Field(unit, typeof(Unit), "<Length>k__BackingField", length);
    Field(unit, typeof(MobileUnit), "<MovementProfile>k__BackingField", profile ?? new GroundMovementProfile());
    unit.SetTransform(Matrix.CreateTranslation(1.5f, 0, 1.5f));
    return unit;
}
MobileUnit Mobile(Vector3 position)
{
    MobileUnit unit = new(position, Guid.NewGuid());
    Field(unit, typeof(Unit), "<Length>k__BackingField", 1);
    Field(unit, typeof(Unit), "<Width>k__BackingField", 1);
    Field(unit, typeof(Unit), "<Height>k__BackingField", 1f);
    return unit;
}
Terrain Terrain(int width, int height)
{
    Terrain terrain = Empty<Terrain>();
    Field(terrain, typeof(Terrain), "<Width>k__BackingField", width);
    Field(terrain, typeof(Terrain), "<Height>k__BackingField", height);
    Field(terrain, typeof(Terrain), "HeightMap", new float[width * height]);
    return terrain;
}
GameWorld World(GameGrid grid)
{
    GameWorld world = Empty<GameWorld>();
    Field(world, typeof(GameWorld), "<GameGrid>k__BackingField", grid);
    return world;
}

var visibilityGrid = new VisibilityGrid(9, 9);
visibilityGrid.Reveal(new Point(2, 2), 1);
var exploredSnapshot = (ExploredVisibilitySnapshot)typeof(VisibilityGrid)
    .GetMethod("GetExploredSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(visibilityGrid, [Guid.NewGuid()])!;
var reconciledVisibility = new VisibilityGrid(9, 9);
reconciledVisibility.Reveal(new Point(7, 7), 0);
reconciledVisibility.BeginUpdate();
typeof(VisibilityGrid).GetMethod("ApplyAuthoritativeExplored",
    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reconciledVisibility, [exploredSnapshot]);
Check(reconciledVisibility[new Point(2, 2)] == VisibilityState.Explored &&
      reconciledVisibility[new Point(7, 7)] == VisibilityState.Unexplored &&
      exploredSnapshot.Bits.Length == 11,
    "Host explored-visibility bitsets restore permanent fog history and remove client-only cells");
var snapshotHandler = new UnitHandler();
UnitList(snapshotHandler).Add(Unit());
IReadOnlyList<Unit> stableUnitSnapshot = snapshotHandler.Units;
UnitList(snapshotHandler).Add(Unit());
Check(stableUnitSnapshot.Count == 1 && snapshotHandler.Units.Count == 2,
    "Unit rendering uses a stable snapshot while the live collection changes");
MobileUnit defensiveUnit = Mobile(Vector3.Zero);
Check(defensiveUnit.SetTemporaryTarget(Guid.NewGuid()) &&
      defensiveUnit.HasCombatTarget && !defensiveUnit.HasExplicitTarget,
    "Host-assigned defensive targets drive the same combat state as explicit attacks");
Check(Math.Abs(DamageCalculator.Calculate(25.0f, DamageType.SmallArms, ArmorClass.Infantry) - 25.0f) < 0.001f &&
      Math.Abs(DamageCalculator.Calculate(25.0f, DamageType.SmallArms, ArmorClass.LightVehicle) - 6.25f) < 0.001f &&
      Math.Abs(DamageCalculator.Calculate(25.0f, DamageType.SmallArms, ArmorClass.HeavyVehicle) - 1.25f) < 0.001f,
    "Small arms retain full infantry damage but are reduced by vehicle armor");
Check(Math.Abs(SquadBenefits.OutgoingDamageMultiplier - 1.10f) < 0.001f &&
      Math.Abs(SquadBenefits.IncomingDamageMultiplier - 0.90f) < 0.001f &&
      Math.Abs(SquadBenefits.OutgoingDamageMultiplier * SquadBenefits.IncomingDamageMultiplier - 0.99f) < 0.001f,
    "Squad cohesion grants small offensive and defensive bonuses without amplifying equal squad fights");
Check(AIBaseDefenseController.IsWithinDefenseRadius(Vector3.Zero, new Vector3(36, 0, 0), 1.0f) &&
      !AIBaseDefenseController.IsWithinDefenseRadius(Vector3.Zero, new Vector3(36.1f, 0, 0), 1.0f),
    "AI base defense reacts only to threats inside its configured perimeter");
Check(AISquadPreparationController.RequiredGunners == 3,
    "AI prepares three squad gunners in addition to its reserved scout");
Guid strategyArmyId = Guid.NewGuid();
AIStrategyProfile stableStrategyA = AIStrategyProfile.Create(12345, strategyArmyId);
AIStrategyProfile stableStrategyB = AIStrategyProfile.Create(12345, strategyArmyId);
Check(stableStrategyA == stableStrategyB &&
      Enumerable.Range(1, 100).Select(seed => AIStrategyProfile.Create(seed, strategyArmyId).Type).Distinct().Count() > 1,
    "AI strategy profiles are reproducible per seed and vary across matches");
Check(Enumerable.Range(1, 100)
        .Select(seed => AIStrategyProfile.Create(seed, strategyArmyId).AssaultStallTimeoutSeconds)
        .All(seconds => seconds is >= 45.0f and <= 70.0f),
    "AI strategy profiles use bounded assault progress timeouts");
Check(Enumerable.Range(1, 100)
        .Select(seed => AIStrategyProfile.Create(seed, strategyArmyId).RequiredTanks)
        .All(count => count is 1 or 2) &&
      EconomyCatalog.GetBasePrice(PurchasableType.Unit, "tank") == 1000,
    "AI profiles request a bounded tank complement with centralized pricing");
Check(Pathfinder.MaximumExpandedNodes == 25000,
    "Pathfinding aborts pathological unreachable searches before they can stall the game loop");
Check(Math.Abs(Harvester.HarvestRetrySeconds - 3.0f) < 0.001f,
    "Continuous harvest orders retry temporary resource, storage and path failures");
MobileUnit emptyRouteUnit = Mobile(new Vector3(4.5f, 0, 4.5f));
emptyRouteUnit.ReceiveCommand(new GotoCommand(new Vector2(8.5f, 8.5f)));
Check(emptyRouteUnit.TryReceiveGotoCommand(
          World(new GameGrid(16, 16, 1)),
          new GotoCommand(new Vector2(4.5f, 4.5f)), route: []) &&
      emptyRouteUnit.CurrentCommand is null,
    "An empty authoritative route completes immediately instead of leaving a unit permanently moving");
var reinforcementController = Empty<AISquadPreparationController>();
Field(reinforcementController, typeof(AISquadPreparationController),
    "<State>k__BackingField", AISquadPreparationState.Ready);
reinforcementController.BeginReinforcement();
Check(!reinforcementController.IsReady &&
      reinforcementController.LastDecision.Contains("replacements", StringComparison.OrdinalIgnoreCase),
    "A completed AI squad can re-enter preparation to replace mission losses");
Check(Math.Abs(AISquadRecoveryController.RequiredAverageHealthFraction - 0.80f) < 0.001f,
    "AI squads recover to eighty percent average health before another mission");
Check(AISquadAssaultController.GetTargetPriority(Empty<Turret>()) <
      AISquadAssaultController.GetTargetPriority(Empty<GDIBarracks>()) &&
      AISquadAssaultController.GetTargetPriority(Empty<GDIBarracks>()) <
      AISquadAssaultController.GetTargetPriority(Empty<TiberiumRefinery>()) &&
      AISquadAssaultController.GetTargetPriority(Empty<TiberiumRefinery>()) <
      AISquadAssaultController.GetTargetPriority(Empty<Reaktor>()),
    "AI assault target priority prefers defenses, production, economy and then power");
Unit groundWeaponUnit = Unit();
groundWeaponUnit.AllowedTargetDomains = TargetDomain.Ground;
Check(groundWeaponUnit.CanAttackDomain(TargetDomain.Ground) &&
      !groundWeaponUnit.CanAttackDomain(TargetDomain.Air),
    "Ground-only weapons reject air targets");
groundWeaponUnit.AllowedTargetDomains = TargetDomain.Ground | TargetDomain.Air;
Check(groundWeaponUnit.CanAttackDomain(TargetDomain.Ground) &&
      groundWeaponUnit.CanAttackDomain(TargetDomain.Air),
    "Combined target-domain weapons can engage ground and air targets");
Helicopter landedHelicopter = Empty<Helicopter>();
Check(landedHelicopter.Domain == TargetDomain.Ground &&
      groundWeaponUnit.CanAttackTarget(landedHelicopter),
    "A landed helicopter can be attacked by ordinary ground weapons");
Field(landedHelicopter, typeof(Helicopter), "<FlightState>k__BackingField",
    HelicopterFlightState.Flying);
groundWeaponUnit.AllowedTargetDomains = TargetDomain.Ground;
Check(landedHelicopter.Domain == TargetDomain.Air &&
      !groundWeaponUnit.CanAttackTarget(landedHelicopter),
    "A flying helicopter still requires an air-capable weapon");
var formationLeader = Empty<SquadLeader>();
var formationGunner = Empty<Gunner>();
var formationRocket = Empty<RakZero>();
Field(formationLeader, typeof(Unit), "<UnitId>k__BackingField", Guid.NewGuid());
Field(formationGunner, typeof(Unit), "<UnitId>k__BackingField", Guid.NewGuid());
Field(formationRocket, typeof(Unit), "<UnitId>k__BackingField", Guid.NewGuid());
formationLeader.SetTransform(Matrix.CreateTranslation(5, 0, 15));
IReadOnlyDictionary<Guid, Vector2> formation = SquadFormation.CreateAssignments(
    formationLeader, [formationGunner, formationRocket], new Vector2(10, 10), 0.0f, 1.0f);
Check(formation[formationGunner.UnitId].Y < formation[formationRocket.UnitId].Y &&
      formation[formationRocket.UnitId].Y < formation[formationLeader.UnitId].Y,
    "Squad formation places gunners in front, rocket soldiers in the middle and the leader behind");
Check(Math.Abs(SquadFormation.ResolveFacingDegrees(
          formationLeader, new Vector2(5, 5))) < 0.001f,
    "Formation facing follows the squad leader's travel direction when no drag direction is supplied");
formationGunner.OnHostAction(UnitActionType.AssembleSquad,
    new UnitActionContext(TargetUnitId: formationLeader.UnitId));
Check(formationGunner.SquadLeaderId == formationLeader.UnitId,
    "Host-confirmed squad assembly assigns the leader on every peer");
Guid cohesionArmyId = Guid.NewGuid();
Field(formationLeader, typeof(Unit), "<ArmyId>k__BackingField", (Guid?)cohesionArmyId);
Field(formationGunner, typeof(Unit), "<ArmyId>k__BackingField", (Guid?)cohesionArmyId);
formationGunner.SetTransform(Matrix.CreateTranslation(6, 0, 15));
var cohesionUnits = new UnitHandler();
UnitList(cohesionUnits).Add(formationLeader);
UnitList(cohesionUnits).Add(formationGunner);
var cohesionWorld = Empty<GameWorld>();
Field(cohesionWorld, typeof(GameWorld), "<Units>k__BackingField", cohesionUnits);
Field(cohesionWorld, typeof(GameWorld), "<GameGrid>k__BackingField", new GameGrid(64, 64, 1));
Check(SquadBenefits.HasCohesion(formationLeader, cohesionWorld) &&
      SquadBenefits.HasCohesion(formationGunner, cohesionWorld) &&
      Math.Abs(SquadBenefits.ApplyCombatModifiers(
          cohesionWorld, formationGunner, formationLeader, 100.0f) - 99.0f) < 0.001f,
    "Nearby living squad members and their leader receive balanced cohesion combat bonuses");
formationGunner.SetTransform(Matrix.CreateTranslation(40, 0, 40));
Check(!SquadBenefits.HasCohesion(formationLeader, cohesionWorld) &&
      !SquadBenefits.HasCohesion(formationGunner, cohesionWorld),
    "Squad combat bonuses end when members leave cohesion range");
formationGunner.OnHostAction(UnitActionType.DisbandSquad,
    new UnitActionContext(TargetUnitId: formationLeader.UnitId));
Check(formationGunner.SquadLeaderId is null,
    "Host-confirmed squad disband removes membership on every peer");
NetworkMessage formationGoto = Wire(NetworkCommands.CreateGotoRequest(
    Guid.NewGuid(), [formationLeader.UnitId], 10, 0, 10,
    formationFacingDegrees: 90.0f));
Check(Math.Abs(formationGoto.FormationFacingDegrees!.Value - 90.0f) < 0.001f,
    "Formation facing survives network serialization");
MobileUnit healthBarUnit = Mobile(Vector3.Zero);
healthBarUnit.HitPoints = 40.0f;
Field(healthBarUnit, typeof(Unit), "<MaxHitPoints>k__BackingField", 100.0f);
Check(Math.Abs(healthBarUnit.Heal(15.0f) - 15.0f) < 0.001f &&
      Math.Abs(healthBarUnit.HitPoints - 55.0f) < 0.001f &&
      Math.Abs(healthBarUnit.Heal(100.0f) - 45.0f) < 0.001f &&
      Math.Abs(healthBarUnit.HitPoints - 100.0f) < 0.001f,
    "Host-side healing restores hit points without exceeding maximum health");
Check(Medic.SearchRadiusInCells > Medic.HealingRadiusInCells &&
      Math.Abs(Medic.HealingRadiusInCells - 2.5f) < 0.001f &&
      Math.Abs(Medic.HealPulseSeconds - 1.0f) < 0.001f,
    "Medics search broadly but heal only nearby soldiers at a bounded pulse rate");
Check(HealthBarRenderer.ShouldDraw(healthBarUnit, HealthBarDisplayMode.Always) &&
      !HealthBarRenderer.ShouldDraw(healthBarUnit, HealthBarDisplayMode.Damaged) &&
      !HealthBarRenderer.ShouldDraw(healthBarUnit, HealthBarDisplayMode.Off),
    "Healthbar modes show healthy units only when requested");
healthBarUnit.IsSelected = true;
healthBarUnit.HitPoints = healthBarUnit.MaxHitPoints * 0.5f;
Check(HealthBarRenderer.ShouldDraw(healthBarUnit, HealthBarDisplayMode.Selected) &&
      HealthBarRenderer.ShouldDraw(healthBarUnit, HealthBarDisplayMode.Damaged),
    "Selected and damaged healthbar modes recognize matching units");
var basicHealthArmy = new Army(Guid.NewGuid(), Guid.NewGuid());
Guid foreignHealthArmyId = Guid.NewGuid();
Check(HealthBarRenderer.GetInformationLevel(basicHealthArmy, foreignHealthArmyId, Vector3.Zero) == HealthInformationLevel.Basic &&
      HealthBarRenderer.GetFillFraction(healthBarUnit, HealthInformationLevel.Basic) == 1.0f,
    "Basic health intelligence exposes only a categorical color");
Guid healthTowerSource = Guid.NewGuid();
basicHealthArmy.Perks.SetSource(healthTowerSource,
    [new PerkGrant(PerkType.DetailedHealth, PerkLifetime.WhileProviderOperational,
        PerkScope.Radius, new Vector3(10, 0, 10), 5)]);
Check(HealthBarRenderer.GetInformationLevel(basicHealthArmy, foreignHealthArmyId, new Vector3(12, 0, 10)) == HealthInformationLevel.Detailed &&
      HealthBarRenderer.GetInformationLevel(basicHealthArmy, foreignHealthArmyId, Vector3.Zero) == HealthInformationLevel.Basic &&
      Math.Abs(HealthBarRenderer.GetFillFraction(healthBarUnit, HealthInformationLevel.Detailed) - 0.5f) < 0.001f,
    "Local detailed-health perk exposes exact health only inside its coverage");
Check(HealthBarRenderer.GetInformationLevel(basicHealthArmy, basicHealthArmy.Id, Vector3.Zero) == HealthInformationLevel.Detailed,
    "An army always receives detailed health for its own units");
basicHealthArmy.Perks.RemoveSource(healthTowerSource);
Check(!basicHealthArmy.Perks.Has(PerkType.DetailedHealth),
    "Removing the last temporary perk provider removes its army perk");
var completedTower = Empty<CommunicationsTower>();
completedTower.SetTransform(Matrix.CreateTranslation(20, 0, 20));
completedTower.TotalBuildingPointsNeeded = 100;
Field(completedTower, typeof(Building), "<ConstructionProgress>k__BackingField", 100f);
Check(completedTower.GetProvidedPerks().Single() is
    { Perk: PerkType.DetailedHealth, Scope: PerkScope.Radius, Radius: CommunicationsTower.DetailedHealthRadius },
    "Completed communications tower provides local detailed-health coverage");
var completedBase = Empty<GDIBase>();
completedBase.SetTransform(Matrix.CreateTranslation(30, 0, 40));
completedBase.TotalBuildingPointsNeeded = 100;
Field(completedBase, typeof(Building), "<ConstructionProgress>k__BackingField", 100f);
Field(completedBase, typeof(Building), "<IsEnabled>k__BackingField", true);
IReadOnlyList<PerkGrant> basePerks = completedBase.GetProvidedPerks();
Check(basePerks.Any(grant => grant is { Perk: PerkType.Home, Scope: PerkScope.Global }) &&
      basePerks.Any(grant => grant.Perk == PerkType.BaseEstablished),
    "Completed GDI base provides home and construction-network perks");
Guid homeSource = Guid.NewGuid();
basicHealthArmy.Perks.SetSource(homeSource,
    [new PerkGrant(PerkType.Home, PerkLifetime.WhileProviderOperational,
        PerkScope.Global, new Vector3(30, 0, 40))]);
Check(basicHealthArmy.Perks.TryGetNearestSourcePosition(
        PerkType.Home, Vector3.Zero, out Vector3 homePosition) &&
      homePosition == new Vector3(30, 0, 40),
    "Home perk resolves its provider position for the HUD hotkey");
HudLayout gameHudLayout = HudLayout.Calculate(new Viewport(0, 0, 1280, 720), HudLayoutMode.Game);
Check(gameHudLayout.ShowMinimap && gameHudLayout.ShowStatusPanel &&
      gameHudLayout.ShowProductionPanel && gameHudLayout.ShowActionPanel &&
      gameHudLayout.Minimap.Right == 1268 && gameHudLayout.Minimap.Bottom == 708 &&
      gameHudLayout.StatusPanel.X == gameHudLayout.Minimap.X &&
      gameHudLayout.ProductionPanel.Top > gameHudLayout.StatusPanel.Bottom &&
      gameHudLayout.ProductionPanel.Bottom < gameHudLayout.Minimap.Top,
    "Game HUD centrally lays out minimap, status, production and action areas");
HudLayout editorHudLayout = HudLayout.Calculate(new Viewport(0, 0, 1280, 720), HudLayoutMode.Editor);
Check(editorHudLayout.ShowMinimap && !editorHudLayout.ShowStatusPanel &&
      !editorHudLayout.ShowProductionPanel && editorHudLayout.ShowActionPanel,
    "Editor HUD layout hides game economy and production status");
Guid teamPlayerId = Guid.NewGuid();
Player teamPlayer = new(teamPlayerId, "team-test", teamId: 1, skin: PlayerSkin.Blue);
NetworkMessage teamRequest = NetworkCommands.CreatePlayerTeamUpdateRequest(teamPlayer, 7);
Check(teamRequest is
    {
        Type: NetworkMessageType.RequestPlayerUpdate,
        SenderId: var sender,
        PlayerId: var playerId,
        TeamId: 7,
        PlayerSkin: (int)PlayerSkin.Blue
    } && sender == teamPlayerId && playerId == teamPlayerId,
    "Console team change uses the host-confirmed player update path");
Guid powerArmyId = Guid.NewGuid();
Building completedPowerPlant = Empty<Building>();
Field(completedPowerPlant, typeof(Unit), "<ArmyId>k__BackingField", (Guid?)powerArmyId);
completedPowerPlant.TotalBuildingPointsNeeded = 100;
Field(completedPowerPlant, typeof(Building), "<ConstructionProgress>k__BackingField", 100f);
Field(completedPowerPlant, typeof(Building), "<IsEnabled>k__BackingField", true);
completedPowerPlant.PowerProduction = 100;
Building completedConsumer = Empty<Building>();
Field(completedConsumer, typeof(Unit), "<ArmyId>k__BackingField", (Guid?)powerArmyId);
completedConsumer.TotalBuildingPointsNeeded = 100;
Field(completedConsumer, typeof(Building), "<ConstructionProgress>k__BackingField", 100f);
Field(completedConsumer, typeof(Building), "<IsEnabled>k__BackingField", true);
completedConsumer.PowerConsumption = 40;
Building unfinishedConsumer = Empty<Building>();
Field(unfinishedConsumer, typeof(Unit), "<ArmyId>k__BackingField", (Guid?)powerArmyId);
unfinishedConsumer.TotalBuildingPointsNeeded = 100;
Field(unfinishedConsumer, typeof(Building), "<IsEnabled>k__BackingField", true);
unfinishedConsumer.PowerConsumption = 80;
ArmyPowerStatus powerStatus = ArmyPowerStatus.Calculate(
    [completedPowerPlant, completedConsumer, unfinishedConsumer], powerArmyId);
Check(powerStatus == new ArmyPowerStatus(100, 40) && powerStatus.Balance == 60,
    "Army power HUD totals only completed active buildings");
Field(completedConsumer, typeof(Building), "<IsEnabled>k__BackingField", false);
powerStatus = ArmyPowerStatus.Calculate(
    [completedPowerPlant, completedConsumer, unfinishedConsumer], powerArmyId);
Check(powerStatus == new ArmyPowerStatus(100, 0),
    "Disabled buildings no longer consume army power");

Building switchableBuilding = Empty<Building>();
switchableBuilding.TotalBuildingPointsNeeded = 0;
Field(switchableBuilding, typeof(Building), "<IsEnabled>k__BackingField", true);
switchableBuilding.OnHostAction(UnitActionType.ToggleEnabled);
Check(!switchableBuilding.IsEnabled,
    "Host-confirmed toggle actions disable buildings");
switchableBuilding.OnHostAction(UnitActionType.ToggleEnabled);
Check(switchableBuilding.IsEnabled,
    "Host-confirmed toggle actions re-enable buildings");
var staffedReactor = Empty<Reaktor>();
Check(!staffedReactor.CanFireWeapon &&
      !Empty<TiberiumRefinery>().CanFireWeapon &&
      !Empty<CommunicationsTower>().CanFireWeapon,
    "Ordinary buildings cannot acquire targets or fire weapons");
Field(staffedReactor, typeof(Unit), "<ArmyId>k__BackingField", (Guid?)powerArmyId);
staffedReactor.TotalBuildingPointsNeeded = 0;
staffedReactor.PowerProduction = 100;
var reactorOccupancy = new OccupancyComponent(staffedReactor,
    [new OccupantSlot(OccupantRole.Crew, 2, CanOccupy: unit => unit is Engineer),
     new OccupantSlot(OccupantRole.Garrison, 2)],
    OccupancyOwnershipMode.PreserveOwnership);
reactorOccupancy.EntryEnabled = true;
Field(staffedReactor, typeof(Unit), "<Occupancy>k__BackingField", reactorOccupancy);
var engineerWorker = Empty<Engineer>();
Field(engineerWorker, typeof(Unit), "<UnitId>k__BackingField", Guid.NewGuid());
Field(engineerWorker, typeof(Unit), "<ArmyId>k__BackingField", (Guid?)powerArmyId);
var ordinarySoldier = Empty<Soldier>();
Field(ordinarySoldier, typeof(Unit), "<UnitId>k__BackingField", Guid.NewGuid());
Field(ordinarySoldier, typeof(Unit), "<ArmyId>k__BackingField", (Guid?)powerArmyId);
Check(reactorOccupancy.TryAdd(engineerWorker, null, out OccupantRole engineerRole) &&
      engineerRole == OccupantRole.Crew && staffedReactor.EffectivePowerProduction == 130,
    "Engineer automatically occupies a reactor crew workplace and adds 30 power");
Check(!reactorOccupancy.CanEnter(ordinarySoldier, OccupantRole.Crew) &&
      reactorOccupancy.TryAdd(ordinarySoldier, null, out OccupantRole soldierRole) &&
      soldierRole == OccupantRole.Garrison,
    "Ordinary soldiers may enter a reactor but cannot occupy engineer workplaces");
SmokeEmissionSettings destructionSmoke = SmokeEmissionPresets.DestroyBuilding();
Check(destructionSmoke.StartDelayVariation > 0.0f,
    "Building destruction smoke is emitted once with delayed particle starts");
var transientWeather = new WeatherHandler(8, 8);
transientWeather.AddExplosionWind(new Vector3(2, 0, 2));
transientWeather.ClearTransientEffects();
Check(transientWeather.WindEffects.Count == 0,
    "Match reset clears transient explosion wind effects");
var transientProjectiles = new ProjectileHandler();
transientProjectiles.Fire(Vector3.Zero, Vector3.One);
transientProjectiles.Clear();
Check(transientProjectiles.ActiveProjectileCount == 0,
    "Match reset clears projectiles that could recreate old particle effects");
var transientEmitters = new SmokeEmitterHandler();
transientEmitters.Create(Vector3.Zero, new SmokeEmissionSettings());
transientEmitters.Clear();
Check(transientEmitters.Emitters.Count == 0,
    "Match reset clears persistent smoke emitters");
Check(visibilityGrid[new Point(4, 4)] == VisibilityState.Unexplored, "Visibility starts unexplored");
visibilityGrid.Reveal(new Point(4, 4), 2);
Check(visibilityGrid[new Point(4, 4)] == VisibilityState.Visible && visibilityGrid[new Point(6, 4)] == VisibilityState.Visible, "Sight radius reveals cells");
Check(visibilityGrid[new Point(6, 6)] == VisibilityState.Unexplored, "Sight radius is circular");
visibilityGrid.BeginUpdate();
Check(visibilityGrid[new Point(4, 4)] == VisibilityState.Explored, "Previous sight remains explored");
visibilityGrid.Reveal(new Point(0, 0), 2);
Check(visibilityGrid[new Point(0, 0)] == VisibilityState.Visible && visibilityGrid[new Point(-1, 0)] == VisibilityState.Unexplored, "Sight reveal clips to map bounds");
visibilityGrid.Reset();
Check(visibilityGrid[new Point(0, 0)] == VisibilityState.Unexplored &&
      visibilityGrid[new Point(4, 4)] == VisibilityState.Unexplored,
    "Match start reset clears visible and explored minimap cells");

var startCamera = new Camera();
float initialCameraHeight = startCamera.Position.Y;
startCamera.CenterForMatchStart(new Vector3(10, 2, 20), new Vector3(30, 0, 40));
Check(startCamera.Position == new Vector3(10, 2 + startCamera.HeightAboveTerrain, 20) &&
      startCamera.HeightAboveTerrain == initialCameraHeight,
    "Match start camera centers on the local bulldozer at its terrain-relative height");
Check(Math.Abs(startCamera.YawAngle - (-135.0f)) < 0.001f,
    "Match start camera faces from the bulldozer toward the map center");
Terrain cameraTerrain = Terrain(32, 32);
cameraTerrain.SetHeight(10, 20, 8);
float cameraYBeforeFollow = startCamera.Position.Y;
startCamera.UpdateTerrainHeight(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.1)), cameraTerrain);
Check(startCamera.Position.Y > cameraYBeforeFollow &&
      startCamera.Position.Y < 8 + startCamera.HeightAboveTerrain,
    "Camera follows changing terrain height smoothly while preserving relative zoom");
startCamera.CenterOn(new Vector3(10, 0, 20), cameraTerrain);
Check(startCamera.Position == new Vector3(10, 8 + startCamera.HeightAboveTerrain, 20),
    "Home camera hotkey preserves terrain-relative camera height");

var terrain = Terrain(6, 6);
var grid = new GameGrid(6, 6, 1);
grid.BindTerrain(terrain);
Check(grid.GetCell(1, 1).MaxSlopeDegrees == 0, "Flat slope");
terrain.SetHeight(2, 1, 1);
terrain.SetHeight(2, 2, 1);
Check(Math.Abs(grid.GetCell(1, 1).MaxSlopeDegrees - 45) < 0.001f, "45-degree ramp and revision refresh");
Check(!grid.CanPlace(Unit(), new Point(1, 1)), "Vehicles reject 45-degree ramp");
Check(grid.CanPlace(Unit(new GroundMovementProfile(MovementModes.Walk, 50)), new Point(1, 1)), "Infantry accepts ramp");
terrain.SetHeight(2, 1, 4);
terrain.SetHeight(2, 2, 4);
Check(!grid.CanPlace(Unit(new GroundMovementProfile(MovementModes.Walk, 50)), new Point(1, 1)), "Infantry rejects steep slope");
Check(grid.CanPlace(Unit(new GroundMovementProfile(MovementModes.Walk | MovementModes.Climb, 50)), new Point(1, 1)), "Climber accepts steep slope");
Check(grid.CanPlace(Unit(new GroundMovementProfile(MovementModes.Drive | MovementModes.SteepDrive)), new Point(1, 1)), "Motorcycle capability accepts steep slope");
grid.GetCell(1, 1).AllowedMovement = MovementModes.Climb;
Check(!grid.CanPlace(Unit(new GroundMovementProfile(MovementModes.SteepDrive)), new Point(1, 1)), "Climb-only excludes motorcycle");
grid.GetCell(1, 1).IsBlocked = true;
Check(!grid.CanPlace(Unit(new GroundMovementProfile(MovementModes.Climb)), new Point(1, 1)), "Absolute block excludes climber");
Check(!grid.CanPlace(Unit(), new Point(5, 1)), "No movement outside rendered terrain");
Check(grid.ToCell(new Vector3(-0.1f, 0, 0)).X == -1, "Negative coordinates do not truncate into grid");

var saddle = Terrain(2, 2);
saddle.SetHeight(1, 1, 1);
Check(Math.Abs(saddle.GetMaxSlopeDegrees(0, 0) - 54.73561f) < 0.001f, "Second triangle controls maximum slope");
var coarse = new GameGrid(3, 3, 2);
coarse.BindTerrain(terrain);
Check(coarse.GetCell(0, 0).MaxSlopeDegrees > 70, "Coarse grid includes all covered triangles");
var flat = Terrain(6, 6);
grid.BindTerrain(flat);
Check(grid.GetCell(1, 1).MaxSlopeDegrees == 0, "Terrain replacement invalidates slope cache");
Check(grid.GetCell(1, 1).IsBlocked, "Slope refresh preserves authored rules");

grid = new GameGrid(8, 8, 1);
MobileUnit car = Unit();
var actionUnit = Mobile(new Vector3(1.5f, 0, 1.5f));
actionUnit.ReceiveCommand(new GotoCommand(new Vector2(6.5f, 6.5f)));
actionUnit.OnHostAction(UnitActionType.Stop);
Check(actionUnit.CurrentCommand is null, "Host-confirmed Stop action reaches the unit and clears its command");
Check(grid.TryMove(car, new Point(1, 1)), "Initial registration");
Check(ReferenceEquals(grid.GetOccupant(1, 1), car), "Occupant identity");
grid.GetCell(2, 1).IsBlocked = true;
Check(!grid.TryMove(car, new Point(2, 2)), "Movement cannot cut blocked corner");
Check(!grid.TryMove(car, new Point(4, 1)), "Long movement cannot jump blocked cell");
grid.GetCell(2, 1).IsBlocked = false;
grid.GetCell(2, 1).ExcludeFromPathfinding = true;
Check(grid.TryMove(car, new Point(2, 1)), "Planning exclusion allows explicit movement");
Check(grid.GetOccupant(1, 1) is null && ReferenceEquals(grid.GetOccupant(2, 1), car), "Move updates occupancy");
Matrix authoritativeTransform = Matrix.CreateTranslation(4.5f, 0, 3.5f);
Check(grid.TryApplyAuthoritativeTransform(car, authoritativeTransform), "Authoritative movement correction succeeds");
Check(grid.GetOccupant(2, 1) is null && ReferenceEquals(grid.GetOccupant(4, 3), car) &&
      car.Position == authoritativeTransform.Translation,
    "Authoritative movement correction keeps transform and footprint together");
grid.GetCell(5, 3).IsBlocked = true;
Matrix rejectedTransform = Matrix.CreateTranslation(5.5f, 0, 3.5f);
Check(!grid.TryApplyAuthoritativeTransform(car, rejectedTransform), "Invalid authoritative movement correction is rejected");
Check(ReferenceEquals(grid.GetOccupant(4, 3), car) && grid.GetOccupant(5, 3) is null &&
      car.Position == authoritativeTransform.Translation,
    "Rejected authoritative correction restores transform and footprint together");
grid.Remove(car);
Check(grid.GetOccupant(4, 3) is null, "Remove clears occupancy");

var turningGrid = new GameGrid(12, 12, 1);
turningGrid.BindTerrain(Terrain(12, 12));
MobileUnit longVehicle = Unit(width: 3, length: 4);
longVehicle.SetPosition(new Vector3(5.5f, 0, 5.5f));
Check(turningGrid.TryMove(longVehicle, new Point(5, 5)),
    "Long vehicle registers its initial movement footprint");
Building cornerBuilding = new(new Vector3(7.5f, 0, 5.5f), Guid.NewGuid());
Check(turningGrid.TryPlace(cornerBuilding, cornerBuilding.Position, 0),
    "Corner obstacle occupies a cell outside the validated vehicle footprint");
longVehicle.SetRotationYDegrees(90);
Check(turningGrid.TryUpdateFootprint(longVehicle) &&
      ReferenceEquals(turningGrid.GetOccupant(7, 5), cornerBuilding),
    "Visual vehicle turns preserve the footprint orientation used by pathfinding");
Check(turningGrid.TryMove(longVehicle, new Point(5, 4)),
    "A rotated vehicle continues along its validated corridor past a building corner");

var constructionSite = new Building(new Vector3(5.5f, 0, 5.5f), Guid.NewGuid());
var builder = Mobile(new Vector3(4.5f, 0, 5.5f));
Check(grid.GetFootprintCells(constructionSite, constructionSite.Position, 0).Count == 1,
    "A 1x1 footprint does not reserve edge-touching neighbour cells");
Check(grid.AreFootprintsAdjacent(builder, constructionSite), "Builder footprint may construct from an edge-adjacent cell");
builder.SetPosition(new Vector3(4.5f, 0, 4.5f));
Check(grid.AreFootprintsAdjacent(builder, constructionSite), "Builder footprint may construct from a diagonally adjacent cell");
builder.SetPosition(new Vector3(2.5f, 0, 5.5f));
Check(!grid.AreFootprintsAdjacent(builder, constructionSite), "A one-cell gap is outside construction range");

grid.GetCell(3, 2).AllowedMovement = MovementModes.Walk;
Check(!grid.IsPathfindingAllowed(Unit(width: 2), new Point(2, 2)),
    "Mobile clearance cells enforce terrain access during pathfinding");
grid.GetCell(3, 2).MovementCost = 4;
Check(grid.GetMovementCost(Unit(width: 2), new Point(2, 2)) == 4, "Footprint cost includes expensive edge");
foreach (float invalid in new[] { 0, -1, float.NaN, float.PositiveInfinity, 0.5f })
{
    bool rejected = false;
    try { grid.GetCell(0, 0).MovementCost = invalid; }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "Invalid cost rejected");
}

grid = new GameGrid(7, 5, 1);
var world = World(grid);
var finder = new Pathfinder(world);
car = Unit();
car.SetPosition(new Vector3(0.5f, 0, 2.5f));
for (int x = 1; x <= 5; x++) grid.GetCell(x, 2).MovementCost = 20;
bool aFound = finder.TryFindPath_AStar(car, car.MovementProfile, new Vector2(6.5f, 2.5f), out var aPath);
bool dFound = finder.TryFindPath_Dijkstra(car, car.MovementProfile, new Vector2(6.5f, 2.5f), out var dPath);
float Cost(List<Point> path)
{
    Point from = grid.ToCell(car.Position);
    float cost = 0;
    foreach (Point to in path) { cost += car.MovementProfile.GetMovementCost(world, car, from, to); from = to; }
    return cost;
}
Check(aFound && dFound && Cost(aPath) < 10 && Math.Abs(Cost(aPath) - Cost(dPath)) < 0.001f, "Both searches choose cheaper detour");
grid.GetCell(6, 2).ExcludeFromPathfinding = true;
Check(!finder.TryFindPath_AStar(car, car.MovementProfile, new Vector2(6.5f, 2.5f), out _), "Excluded destination rejected");
grid.GetCell(6, 2).ExcludeFromPathfinding = false;
grid.GetCell(0, 2).ExcludeFromPathfinding = true;
Check(finder.TryFindPath_AStar(car, car.MovementProfile, new Vector2(6.5f, 2.5f), out _), "Can leave excluded starting cell");
for (int y = 0; y < 5; y++) grid.GetCell(3, y).ExcludeFromPathfinding = true;
Check(!finder.TryFindPath_AStar(car, car.MovementProfile, new Vector2(6.5f, 2.5f), out _), "Excluded strip cannot be crossed");
grid = new GameGrid(9, 7, 1);
world = World(grid);
finder = new Pathfinder(world);
car = Unit(width: 3, length: 3);
car.SetPosition(new Vector3(2.5f, 0, 3.5f));
for (int y = 2; y <= 4; y++)
    for (int x = 1; x <= 3; x++) grid.GetCell(x, y).ExcludeFromPathfinding = true;
Check(finder.TryFindPath_AStar(car, car.MovementProfile, new Vector2(6.5f, 3.5f), out _), "Large unit can leave excluded initial footprint");

grid = new GameGrid(8, 8, 1);
var building = Empty<Building>();
Field(building, typeof(Unit), "<Width>k__BackingField", 1);
Field(building, typeof(Unit), "<Length>k__BackingField", 1);
building.SetTransform(Matrix.CreateTranslation(3.5f, 0, 3.5f));
grid.GetCell(3, 3).MovementCost = 3;
Check(grid.TryPlace(building, building.Position, 0), "Building placement");
Check(ReferenceEquals(grid.GetOccupant(3, 3), building), "Building identity available");
Check(!grid.CanPlace(Unit(), new Point(3, 3)), "Building blocks movement");
grid.Remove(building);
Check(grid.CanPlace(Unit(), new Point(3, 3)) && grid.GetCell(3, 3).MovementCost == 3, "Demolition clears occupancy and preserves terrain rules");

// Compare the searches over varying costs and obstacles, including unreachable targets.
Random random = new(42);
for (int sample = 0; sample < 30; sample++)
{
    grid = new GameGrid(9, 9, 1);
    world = World(grid);
    finder = new Pathfinder(world);
    car = Unit();
    car.SetPosition(new Vector3(0.5f, 0, 0.5f));
    for (int y = 0; y < 9; y++)
        for (int x = 0; x < 9; x++)
        {
            grid.GetCell(x, y).MovementCost = random.Next(1, 8);
            grid.GetCell(x, y).IsBlocked = random.NextDouble() < 0.18;
        }
    grid.GetCell(0, 0).IsBlocked = grid.GetCell(8, 8).IsBlocked = false;
    aFound = finder.TryFindPath_AStar(car, car.MovementProfile, new Vector2(8.5f, 8.5f), out aPath);
    dFound = finder.TryFindPath_Dijkstra(car, car.MovementProfile, new Vector2(8.5f, 8.5f), out dPath);
    Check(aFound == dFound && (!aFound || Math.Abs(Cost(aPath) - Cost(dPath)) < 0.001f), "Weighted A*/Dijkstra agreement");
}
// Selection uses precise authored bounds, including offset, mesh scale and building progress.
building = Empty<Building>();
building.TotalBuildingPointsNeeded = 0;
building.SetTransform(Matrix.CreateRotationY(0.6f) * Matrix.CreateTranslation(3, 0, 2));
BoundingBox authored = new(new Vector3(-5.6f, 0, -2.4f), new Vector3(0.8f, 7.2f, 2.4f));
var vertices = authored.GetCorners().Select(p => new VertexPositionColorNormalTexture(p, Color.White, Vector3.Up, Vector2.Zero)).ToArray();
var mesh = new Mesh("offset-building", new[] { new SubMesh("body", vertices, new[] { 0, 1, 2 }, Vector3.Zero) });
mesh.LocalTransform = Matrix.CreateScale(0.75f);
typeof(Unit).GetMethod("SetMeshSet", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(building, new object[] { new MeshSet(mesh), true, 4.0f });
var viewport = new Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 1280, 720);
Matrix view = Matrix.CreateLookAt(new Vector3(15, 14, 20), Vector3.Zero, Vector3.Up);
Matrix projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1280f / 720, 0.1f, 100);
Rectangle ExpectedBounds()
{
    var corners = authored.GetCorners().Select(p => viewport.Project(p, projection, view, mesh.LocalTransform * building.GetWorldMatrix())).ToArray();
    int left = (int)MathF.Floor(corners.Min(p => p.X)), top = (int)MathF.Floor(corners.Min(p => p.Y));
    int right = (int)MathF.Ceiling(corners.Max(p => p.X)), bottom = (int)MathF.Ceiling(corners.Max(p => p.Y));
    return new Rectangle(left, top, right - left, bottom - top);
}
Check(building.GetScreenBounds(view, projection, viewport) == ExpectedBounds(), "Selection preserves mesh origin, scale and rotation without grid padding");
Vector3 pickCenter = Vector3.Transform((authored.Min + authored.Max) * 0.5f,
    mesh.LocalTransform * building.GetWorldMatrix());
Ray towardBuilding = new(pickCenter + Vector3.Up * 20, Vector3.Down);
Check(building.IntersectSelectionRay(towardBuilding) is float hitDistance && hitDistance < 20,
    "Context picking intersects the transformed mesh bounds");
Check(building.IntersectSelectionRay(new Ray(towardBuilding.Position, Vector3.Up)) is null,
    "Objects behind the picking ray cannot become context targets");
Check(building.IntersectSelectionRay(new Ray(pickCenter + new Vector3(100, 20, 0), Vector3.Down)) is null,
    "Free terrain outside the mesh bounds has no unit hit");
Check(Unit().IntersectSelectionRay(towardBuilding) is null,
    "Units without a render mesh cannot become context targets");
building.TotalBuildingPointsNeeded = 100;
Check(building.GetScreenBounds(view, projection, viewport) == ExpectedBounds(), "Selection follows construction scale");
building.SetTransform(Matrix.CreateRotationY(-0.9f) * Matrix.CreateTranslation(-2, 0, 1));
Check(building.GetScreenBounds(view, projection, viewport) == ExpectedBounds(), "Selection follows changed world transform");
// Host validation, network serialization, replay and production rally behavior.
grid = new GameGrid(12, 12, 1);
world = World(grid);
terrain = Terrain(12, 12);
grid.BindTerrain(terrain);
Field(world, typeof(GameWorld), "_terrain", terrain);
var units = new UnitHandler();
Field(world, typeof(GameWorld), "<Units>k__BackingField", units);
Field(world, typeof(GameWorld), "<PathfindingManager>k__BackingField", new PathfindingManager(world));
Globals.World = world;
var game = Empty<RTSGame>();
var armies = new ArmyHandler();
Field(game, typeof(RTSGame), "<Armies>k__BackingField", armies);
Field(game, typeof(RTSGame), "<Pricing>k__BackingField",
    new PricingService(armies, id => units.FindById(id)));
Globals.Game = game;
var transport = Empty<NetworkHandler>();
Guid hostId = Guid.NewGuid(), ownerId = Guid.NewGuid(), armyId = Guid.NewGuid();
Field(transport, typeof(NetworkHandler), "<LocalPeerId>k__BackingField", hostId);
Field(transport, typeof(NetworkHandler), "<IsHost>k__BackingField", true);
Field(game, typeof(RTSGame), "<Network>k__BackingField", transport);
armies.EnsureArmy(armyId, ownerId);
GDIBarracks Barracks(Guid id)
{
    var result = Empty<GDIBarracks>();
    Field(result, typeof(Unit), "<UnitId>k__BackingField", id);
    Field(result, typeof(Unit), "<ArmyId>k__BackingField", armyId);
    Field(result, typeof(Building), "<ProductionQueue>k__BackingField", new ProductionQueue());
    result.SetTransform(Matrix.Identity);
    return result;
}
var barracks = Barracks(Guid.NewGuid());
UnitList(units).Add(barracks);
var input = new NetworkInput(transport);
var host = new NetworkHost(transport, input, world);
NetworkMessage? Request(NetworkMessage request) => (NetworkMessage?)typeof(NetworkHost)
    .GetMethod("TryCreateSetRallyPointCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { request });
NetworkMessage Wire(NetworkMessage message) => JsonSerializer.Deserialize<NetworkMessage>(
    JsonSerializer.Serialize(message, NetworkJson.Options), NetworkJson.Options)!;
Check(float.IsPositiveInfinity(Wire(new NetworkMessage(
        NetworkMessageType.TextMessage, Guid.NewGuid(), X: float.PositiveInfinity)).X),
    "Network serialization cannot crash on a transient non-finite simulation value");

var firstGroupUnit = Mobile(new Vector3(1.5f, 0, 1.5f));
var secondGroupUnit = Mobile(new Vector3(1.5f, 0, 3.5f));
Field(firstGroupUnit, typeof(Unit), "<ArmyId>k__BackingField", armyId);
Field(secondGroupUnit, typeof(Unit), "<ArmyId>k__BackingField", armyId);
UnitList(units).Add(firstGroupUnit);
UnitList(units).Add(secondGroupUnit);
Check(grid.TryMove(firstGroupUnit, grid.ToCell(firstGroupUnit.Position)) &&
      grid.TryMove(secondGroupUnit, grid.ToCell(secondGroupUnit.Position)),
    "Group goto fixtures occupy distinct start cells");
NetworkMessage groupRequest = NetworkCommands.CreateGotoRequest(ownerId,
    [firstGroupUnit.UnitId, secondGroupUnit.UnitId], 8.5f, 0, 8.5f);
var groupCommand = (NetworkMessage?)typeof(NetworkHost)
    .GetMethod("TryCreateGotoCommand", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(host, new object[] { groupRequest });
UnitRoute[] groupRoutes = Wire(groupCommand!).Routes!;
Check(groupRoutes.Length == 2 && groupRoutes.Select(route => new Vector2(route.TargetX!.Value, route.TargetZ!.Value)).Distinct().Count() == 2,
    "Group goto assigns a distinct destination to every unit");
Check(groupRoutes.Any(route => route.TargetX == 8.5f && route.TargetZ == 8.5f) &&
      groupCommand!.Routes!.All(route => route.Cells.Length == 0 || route.Cells[^1] == grid.ToCell(new Vector3(route.TargetX!.Value, 0, route.TargetZ!.Value))),
    "Group goto keeps the clicked point and transmits each route's matching endpoint");
grid.Remove(firstGroupUnit);
grid.Remove(secondGroupUnit);
UnitList(units).Remove(firstGroupUnit);
UnitList(units).Remove(secondGroupUnit);

var blockedMover = Mobile(new Vector3(2.5f, 0, 2.5f));
var movementBlocker = Mobile(new Vector3(3.5f, 0, 2.5f));
Check(grid.TryMove(blockedMover, grid.ToCell(blockedMover.Position)) &&
      grid.TryMove(movementBlocker, grid.ToCell(movementBlocker.Position)),
    "Blocked movement fixtures occupy adjacent cells");
blockedMover.ReceiveCommand(new GotoCommand(new Vector2(3.5f, 2.5f)));
blockedMover.SetPlannedPath([new Point(3, 2)]);
blockedMover.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1.0)));
Check(blockedMover.CurrentCommand is not null, "A temporary blocker is tolerated briefly");
blockedMover.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1.1)));
Check(blockedMover.CurrentCommand is not null && blockedMover.PlannedPath.Count == 0 && blockedMover.MovementStatus == MovementStatus.Planning,
    "A blocked unit requests a replacement route without losing its order");
blockedMover.Stop();
world.PathfindingManager.Update();
grid.Remove(blockedMover);
grid.Remove(movementBlocker);

var gameplayMarkers = new GameplayMarkerHandler();
var firstStart = gameplayMarkers.Add(GameplayMarkerType.PlayerStart, new Vector3(4, 0, 5), 90);
var secondStart = gameplayMarkers.Add(GameplayMarkerType.PlayerStart, new Vector3(8, 0, 9), 180);
var resourceField = gameplayMarkers.Add(GameplayMarkerType.ResourceField, new Vector3(12, 0, 13), 0, 8);
resourceField.Tags.Add("early-game");
resourceField.Properties["richness"] = "high";
Check(firstStart.PlayerSlot == 1 && secondStart.PlayerSlot == 2, "Player start markers receive stable sequential slots");
Check(resourceField.Shape == GameplayMarkerShape.Circle && resourceField.Size == new Vector2(8), "Marker actions retain semantic shape and tool size");
Check(Empty<TerrainEditorTool>().Actions.Any(action =>
        action.Type == UnitActionType.DeleteGameplayMarker && action.Name == "Remove Gameplay Marker"),
    "Terrain editor exposes a clearly named gameplay-marker removal tool");
var removableMarkers = new GameplayMarkerHandler();
GameplayMarker removableStart = removableMarkers.Add(
    GameplayMarkerType.PlayerStart, new Vector3(4, 0, 5), 0);
removableMarkers.Add(GameplayMarkerType.ObservationPoint, new Vector3(12, 0, 12), 0);
Check(removableMarkers.FindNearest(new Vector3(4.4f, 0, 5), 1.0f)?.Id == removableStart.Id &&
      removableMarkers.RemoveNearest(new Vector3(4.4f, 0, 5), 1.0f) &&
      removableMarkers.Markers.Count == 1,
    "Gameplay-marker removal targets only the nearest marker inside the tool radius");
var markerCopy = new GameplayMarkerHandler();
markerCopy.ApplyStates(gameplayMarkers.GetStates());
Check(markerCopy.Markers.Count == 3 && markerCopy.Markers[2].Tags.Contains("early-game") && markerCopy.Markers[2].Properties["richness"] == "high", "Gameplay marker state preserves metadata");
string markerDirectory = Path.Combine(Path.GetTempPath(), $"rts-marker-{Guid.NewGuid():N}");
try
{
    gameplayMarkers.Save(markerDirectory);
    markerCopy.Clear();
    markerCopy.Load(markerDirectory);
    Check(markerCopy.Markers.Count == 3 && markerCopy.Markers[0].RotationDegrees == 90, "Gameplay markers survive map save and load");
}
finally { if (Directory.Exists(markerDirectory)) Directory.Delete(markerDirectory, true); }
var markerWorldData = new WorldData(1, 1, [0], [0.0f], gameplayMarkers.GetStates());
var markerMessage = Wire(NetworkCommands.CreateWorldData(Guid.NewGuid(), markerWorldData));
Check(markerMessage.WorldData?.GameplayMarkers?.Length == 3, "Map publish carries gameplay markers in WorldData");
Field(terrain, typeof(Terrain), "_tiles", new TerrainTile[12, 12]);
var tiberium = new TiberiumHandler();
Field(world, typeof(GameWorld), "<Tiberium>k__BackingField", tiberium);
Point plantedCell = new(3, 3);
tiberium.Paint([plantedCell]);
Check(tiberium.RenderChunkCount == 1, "Painted Tiberium is indexed in a render chunk");
float plantedAmount = tiberium.Cells[plantedCell].Amount;
tiberium.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(30)), allowGrowth: false);
Check(tiberium.Cells[plantedCell].Amount == plantedAmount, "Editor pause freezes Tiberium growth");
tiberium.SimulateArea(new HashSet<Point> { plantedCell }, 10, []);
Check(tiberium.Cells[plantedCell].Amount > plantedAmount, "Area simulation advances selected Tiberium by ten seconds");
var outsideCell = new Point(4, 4);
tiberium.Paint([outsideCell]);
float outsideAmount = tiberium.Cells[outsideCell].Amount;
tiberium.SimulateArea(new HashSet<Point> { plantedCell }, 10, []);
Check(tiberium.Cells[outsideCell].Amount == outsideAmount, "Area simulation leaves Tiberium outside its shape unchanged");
string tiberiumDirectory = Path.Combine(Path.GetTempPath(), $"rts-tiberium-{Guid.NewGuid():N}");
try
{
    tiberium.Save(tiberiumDirectory);
    tiberium.Remove([plantedCell, outsideCell]);
    Check(tiberium.RenderChunkCount == 0, "Empty Tiberium render chunks are discarded");
    tiberium.Load(tiberiumDirectory);
    Check(tiberium.Cells.Count == 2 && tiberium.RenderChunkCount == 1 && tiberium.Cells[plantedCell].Amount > plantedAmount, "Tiberium cells and render chunks survive map save and load");
}
finally { if (Directory.Exists(tiberiumDirectory)) Directory.Delete(tiberiumDirectory, true); }
var editor = Empty<TerrainEditorTool>();
UnitList(units).Add(editor);
Check(world.IsEditorActive, "Terrain editor presence activates editor mode");
UnitList(units).Remove(editor);
Check(!world.IsEditorActive, "Removing the terrain editor leaves editor mode");
var publishedTiberium = new WorldData(1, 1, [0], [0.0f],
    TiberiumCells: tiberium.GetStates(),
    MapObjects: [new MapObjectState(Guid.NewGuid(), "tiberium-source", 2, 0, 2)]);
var publishedTiberiumMessage = Wire(NetworkCommands.CreateWorldData(Guid.NewGuid(), publishedTiberium));
Check(publishedTiberiumMessage.WorldData?.TiberiumCells?.Length == 2 && publishedTiberiumMessage.WorldData.MapObjects?.Length == 1,
    "Map publish carries Tiberium cells and sources");
void Deliver(NetworkMessage command) => typeof(NetworkInput)
    .GetMethod("HandleNetworkMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(input, new object[] { Wire(command) });
Vector3 rallyTarget = new(8.5f, 99, 7.5f);
var request = NetworkCommands.CreateSetRallyPointRequest(ownerId, barracks.UnitId, rallyTarget);
Check(Request(request with { SenderId = Guid.NewGuid() }) is null, "Foreign player cannot set rally point");
Check(Request(request with { UnitId = Guid.NewGuid() }) is null, "Unknown rally owner rejected");
foreach (Vector3 bad in new[] { new Vector3(float.NaN, 0, 2), new Vector3(-1, 0, 2), new Vector3(12, 0, 2) })
    Check(Request(NetworkCommands.CreateSetRallyPointRequest(ownerId, barracks.UnitId, bad)) is null, "Invalid rally coordinates rejected");
grid.GetCell(8, 7).IsBlocked = true;
Check(Request(request) is null, "Blocked rally target rejected");
grid.GetCell(8, 7).IsBlocked = false;
grid.GetCell(8, 7).ExcludeFromPathfinding = true;
Check(Request(request) is null, "Excluded rally target rejected");
grid.GetCell(8, 7).ExcludeFromPathfinding = false;
NetworkMessage confirmed = Request(Wire(request))!;
Check(confirmed.Type == NetworkMessageType.SetRallyPointCommand && confirmed.SenderId == hostId, "Host confirms rally point");
Check(barracks.RallyPoint == new Vector3(8.5f, 0, 7.5f) && barracks.RallyPointRevision == 1, "Host sets terrain height and revision");
Check(barracks.Actions.Any(action => action.Type == UnitActionType.SetRallyPoint), "Barracks exposes rally action");
Check(!Unit().SupportsRallyPoint, "Ordinary units opt out");
var replica = Barracks(barracks.UnitId);
UnitList(units)[0] = replica;
Field(transport, typeof(NetworkHandler), "<IsHost>k__BackingField", false);
Deliver(confirmed);
Check(replica.RallyPoint == barracks.RallyPoint && replica.RallyPointRevision == barracks.RallyPointRevision, "Client applies serialized host confirmation");
replica.ApplyRallyPointState(new RallyPointState(0, false));
Check(replica.RallyPoint is not null, "Old rally update cannot overwrite confirmed point");
var lateReplica = Barracks(barracks.UnitId);
lateReplica.ApplyState(barracks.GetState());
Check(lateReplica.RallyPoint == barracks.RallyPoint, "Building state restores rally point");
UnitList(units)[0] = barracks;
Field(transport, typeof(NetworkHandler), "<IsHost>k__BackingField", true);
Deliver(confirmed with { SenderId = ownerId, RallyPoint = new RallyPointState(100, false) });
Check(barracks.RallyPoint is not null && barracks.RallyPointRevision == 1, "Host rejects forged client confirmation");
barracks.ProductionQueue.Enqueue(Guid.NewGuid(), "grunt", ownerId, 5);
var spawn = Wire(NetworkCommands.CreateProducedUnitCommand(hostId, barracks, barracks.ProductionQueue.ActiveOrder!, Vector3.Zero, new Vector3(2.5f, 0, 2.5f)));
Check(spawn.RallyPoint == barracks.GetRallyPointState(), "Production transmits rally snapshot");
var cleared = Request(NetworkCommands.CreateSetRallyPointRequest(ownerId, barracks.UnitId, null))!;
Check(barracks.RallyPoint is null && cleared.RallyPoint is { HasPosition: false, Revision: 2 }, "Host can clear rally point");
Check(spawn.RallyPoint is { HasPosition: true }, "Existing production snapshot survives later rally changes");
replica.ApplyRallyPointState(cleared.RallyPoint!.Value);
replica.ApplyState(barracks.GetState());
Check(replica.RallyPoint is null, "Clear survives state synchronization");

// Run the actual building-exit transition without graphics or model loading.
var produced = Mobile(new Vector3(2.5f, 0, 2.5f));
void SetSpawnRally(MobileUnit unit) => typeof(MobileUnit)
    .GetMethod("SetProductionRallyPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(unit, new object?[] { spawn.RallyPoint });
void FinishExit(MobileUnit unit) => typeof(MobileUnit)
    .GetMethod("UpdateLeavingBuilding", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(unit, new object[] { new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.1)) });
produced.BeginLeavingBuilding(barracks.UnitId, produced.Position);
SetSpawnRally(produced);
Check(produced.CurrentCommand is null && world.PathfindingManager.PendingRequests == 0, "Rally waits for exit");
FinishExit(produced);
Check(!produced.IsLeavingBuilding && produced.CurrentCommand?.Target == new Vector2(8.5f, 7.5f) && world.PathfindingManager.PendingRequests == 1, "After exit unit requests normal rally path");
grid.Remove(produced);
produced = Mobile(new Vector3(2.5f, 0, 2.5f));
produced.BeginLeavingBuilding(barracks.UnitId, produced.Position);
SetSpawnRally(produced);
produced.Stop();
FinishExit(produced);
Check(produced.CurrentCommand is null && world.PathfindingManager.PendingRequests == 1, "Manual stop cancels pending rally order");
grid.Remove(produced);
var waitingSoldier = Unit();
waitingSoldier.SetPosition(new Vector3(8.5f, 0, 7.5f));
Check(grid.TryMove(waitingSoldier, new Point(8, 7)), "First recruit occupies rally cell");
produced = Mobile(new Vector3(2.5f, 0, 2.5f));
produced.BeginLeavingBuilding(barracks.UnitId, produced.Position);
SetSpawnRally(produced);
FinishExit(produced);
Check(produced.CurrentCommand is GotoCommand nearby && nearby.Target != new Vector2(8.5f, 7.5f) &&
    Vector2.Distance(nearby.Target, new Vector2(8.5f, 7.5f)) < 2, "Later recruit gathers beside occupied rally point");
// Building placement samples every vertex under the rotated footprint.
grid = new GameGrid(12, 12, 1);
world = World(grid);
terrain = Terrain(12, 12);
grid.BindTerrain(terrain);
Field(world, typeof(GameWorld), "_terrain", terrain);
units = new UnitHandler();
Field(world, typeof(GameWorld), "<Units>k__BackingField", units);
Globals.World = world;
building = new Building(new Vector3(3.25f, 0, 3.25f), Guid.NewGuid());
Field(building, typeof(Unit), "<Length>k__BackingField", 1);
Field(building, typeof(Unit), "<Width>k__BackingField", 1);
Field(building, typeof(Unit), "<Height>k__BackingField", 1f);
var placement = building.EvaluatePlacement(world, building.Position, 0);
Check(placement.IsAllowed && placement.HeightDifference == 0, "Flat build site allowed");
Point far = new(placement.Cells.Max(c => c.Cell.X) + 1, placement.Cells.Max(c => c.Cell.Y) + 1);
terrain.SetHeight(far.X, far.Y, 0.5f);
Check(building.EvaluatePlacement(world, building.Position, 0).IsAllowed, "Exact terrain tolerance accepted");
terrain.SetHeight(far.X, far.Y, 0.6f);
placement = building.EvaluatePlacement(world, building.Position, 0);
Check(!placement.IsAllowed && placement.HeightDifference > 0.59f, "Far boundary vertex prevents construction");
Check(placement.Cells.Any(c => c.Issues.HasFlag(PlacementIssue.UnevenTerrain)), "Uneven cells identified for red preview");
Check(!building.CanPlace(building.Position, 0), "Public placement uses same building tolerance");
building.MaximumTerrainHeightDifference = 1;
Check(building.CanPlace(building.Position, 0), "Tolerance configurable per building");
building.MaximumTerrainHeightDifference = 0.5f;
terrain.SetHeight(far.X, far.Y, 0);
Point firstCell = placement.Cells[0].Cell;
grid.GetCell(firstCell).IsBlocked = true;
placement = building.EvaluatePlacement(world, building.Position, 0);
Check(!placement.IsAllowed && placement.Cells.Any(c => c.Issues == PlacementIssue.Blocked), "Blocked cell identified");
grid.GetCell(firstCell).IsBlocked = false;
var blocker = Unit();
blocker.SetPosition(grid.ToWorldPosition(firstCell, 0));
Check(grid.TryMove(blocker, firstCell), "Place blocking mobile unit");
placement = building.EvaluatePlacement(world, building.Position, 0);
Check(!placement.IsAllowed && placement.Cells.Any(c => c.Issues.HasFlag(PlacementIssue.Occupied)), "Occupied cell identified");
grid.Remove(blocker);
Check(!building.EvaluatePlacement(world, new Vector3(0.1f, 0, 0.1f), 0).IsAllowed, "Partial footprint outside map rejected");
Check(!building.EvaluatePlacement(world, new Vector3(float.NaN, 0, 3), 0).IsAllowed, "Nonfinite build position rejected");
Field(building, typeof(Unit), "<Width>k__BackingField", 3);
placement = building.EvaluatePlacement(world, building.Position, 37);
Check(placement.IsAllowed && placement.Cells.Select(c => c.Cell).ToHashSet().SetEquals(
    grid.GetFootprintCells(building, building.Position, 37)), "Preview uses rotated occupancy footprint");
grid = new GameGrid(6, 6, 2);
grid.BindTerrain(terrain);
Field(world, typeof(GameWorld), "<GameGrid>k__BackingField", grid);
Field(building, typeof(Unit), "<Width>k__BackingField", 1);
terrain.SetHeight(3, 3, 1);
Check(!building.EvaluatePlacement(world, building.Position, 0).IsAllowed, "Coarse grid interior vertices tested");
terrain.SetHeight(3, 3, 0);

// Host reserves accepted sites immediately, rejecting competing requests.
grid = new GameGrid(12, 12, 1);
grid.BindTerrain(terrain);
Field(world, typeof(GameWorld), "<GameGrid>k__BackingField", grid);
Field(game, typeof(RTSGame), "_players", new List<Player> { new(ownerId, "builder", armyId: armyId) });
armies.Find(armyId)!.Resources = 10000;
Globals.MeshHandler = new MeshHandler();
var smallBox = new BoundingBox(new Vector3(-0.4f, 0, -0.4f), new Vector3(0.4f, 1, 0.4f));
var smallVertices = smallBox.GetCorners().Select(p => new VertexPositionColorNormalTexture(p, Color.White, Vector3.Up, Vector2.Zero)).ToArray();
Globals.MeshHandler.Meshes["barracks-1"] = new Mesh("test-barracks", new[] { new SubMesh("body", smallVertices, new[] { 0, 1, 2 }, Vector3.Zero) });
Globals.MeshHandler.Meshes["building-1"] = Globals.MeshHandler.Meshes["barracks-1"];
host = new NetworkHost(transport, input, world);
NetworkMessage? BuildRequest(NetworkMessage request) => (NetworkMessage?)typeof(NetworkHost)
    .GetMethod("TryCreateBuildCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { request });
var buildRequest = NetworkCommands.CreateBuildRequest(ownerId, "gdi-barracks", 6.5f, 50, 6.5f, 0, Guid.NewGuid());
terrain.SetHeight(7, 7, 1);
Check(BuildRequest(buildRequest) is null && units.Units.Count == 0, "Host rejects uneven site without registering it");
Check(units.SpawnBuilding("gdi-barracks", new Vector3(6.5f, 0, 6.5f), 0, Guid.NewGuid(), ownerId) is null, "Direct spawn also validates height");
terrain.SetHeight(7, 7, 0);
Check(BuildRequest(buildRequest) is null && units.Units.Count == 0,
    "Host rejects barracks before the army has an operational base");
armies.Find(armyId)!.Perks.GrantPermanent(PerkType.BaseEstablished, Guid.NewGuid());
var buildCommand = BuildRequest(buildRequest);
Check(buildCommand is { Type: NetworkMessageType.BuildCommand, Y: 0 } && units.Units.Count == 1, "Host places valid site at terrain height");
Check(BuildRequest(buildRequest with { UnitId = Guid.NewGuid() }) is null && units.Units.Count == 1, "Host rejects overlapping request in same tick");
Deliver(buildCommand!);
Check(units.Units.Count == 1, "Host confirmation does not duplicate building");
Building builtSite = (Building)units.Units.Single();
int barracksPrice = EconomyCatalog.GetBasePrice(PurchasableType.Building, "gdi-barracks");
Check(builtSite.PurchasePrice == barracksPrice &&
      armies.Find(armyId)!.Resources == 10000 - barracksPrice,
    "Host charges constructor-supplied building price");
Check(builtSite.Actions.Any(action => action.Type == UnitActionType.CancelConstruction) &&
      !builtSite.Actions.Any(action => action.Type == UnitActionType.SellBuilding),
    "Playable building exposes full-refund cancellation only while under construction");
Check(builtSite.Actions.Any(action => action.Type == UnitActionType.Destroy),
    "Playable building exposes destroy action while under construction");
NetworkMessage destroyRequest = Wire(NetworkCommands.CreateDestroyBuildingRequest(ownerId, builtSite.UnitId));
Check(destroyRequest.Type == NetworkMessageType.DestroyBuildingRequest &&
      destroyRequest.UnitId == builtSite.UnitId,
    "Destroy building request survives network serialization");
NetworkMessage? DestroyBuildingRequest(NetworkMessage request) => (NetworkMessage?)typeof(NetworkHost)
    .GetMethod("TryCreateDestroyBuildingCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { request });
Check(DestroyBuildingRequest(destroyRequest) is { Type: NetworkMessageType.DestroyUnitCommand },
    "Host converts an owned building destroy request into an authoritative command without inspecting UI actions");
Check(DestroyBuildingRequest(destroyRequest with { SenderId = Guid.NewGuid() }) is null,
    "Host rejects building destruction from a non-owner");
var actionContext = new UnitActionContext(
    UnitActionPosition.From(new Vector3(4.5f, 1.25f, 7.5f)),
    builtSite.UnitId,
    Value: "test-value",
    IntValue: 12,
    FloatValue: 1.5f,
    Alternate: true);
NetworkMessage unitActionRequest = Wire(NetworkCommands.CreateUnitActionRequest(
    ownerId, [builtSite.UnitId], UnitActionType.Repair, actionContext));
Check(unitActionRequest.UnitActionContext?.TargetPosition?.ToVector3() == new Vector3(4.5f, 1.25f, 7.5f) &&
      unitActionRequest.UnitActionContext.TargetUnitId == builtSite.UnitId &&
      unitActionRequest.UnitActionContext.Value == "test-value" &&
      unitActionRequest.UnitActionContext.IntValue == 12 &&
      unitActionRequest.UnitActionContext.FloatValue == 1.5f &&
      unitActionRequest.UnitActionContext.Alternate,
    "Typed unit action context survives network serialization");
NetworkMessage? UnitActionRequest(NetworkMessage request) => (NetworkMessage?)typeof(NetworkHost)
    .GetMethod("TryCreateUnitActionCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { request });
Check(UnitActionRequest(unitActionRequest) is { Type: NetworkMessageType.UnitActionCommand },
    "Host accepts a valid parameterized action for an owned unit");
Check(UnitActionRequest(unitActionRequest with { SenderId = Guid.NewGuid() }) is null,
    "Host rejects a parameterized action for a unit the sender cannot control");
Check(UnitActionRequest(unitActionRequest with
{
    UnitActionContext = UnitActionContext.At(new Vector3(float.NaN, 0, 0))
}) is null, "Host rejects non-finite action parameters");
NetworkMessage? SellRequest(NetworkMessage request) => (NetworkMessage?)typeof(NetworkHost)
    .GetMethod("TryCreateSellBuildingCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { request });
NetworkMessage? CancelConstructionRequest(NetworkMessage request) => (NetworkMessage?)typeof(NetworkHost)
    .GetMethod("TryCreateCancelConstructionCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { request });
var cancelCommand = CancelConstructionRequest(
    NetworkCommands.CreateCancelConstructionRequest(ownerId, builtSite.UnitId));
Check(cancelCommand is { Type: NetworkMessageType.CancelConstructionCommand, ResourceAmount: 10000 } &&
      builtSite.IsSelling,
    "Host cancels unfinished construction and refunds its complete purchase price");
Check(CancelConstructionRequest(NetworkCommands.CreateCancelConstructionRequest(ownerId, builtSite.UnitId)) is null,
    "Host cannot refund the same construction twice");
Deliver(cancelCommand!);
builtSite.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1.0)));
Check(builtSite.IsReadyForRemoval, "Cancelled construction is removed after shrink animation");

Guid completedSaleId = Guid.NewGuid();
var completedSaleCommand = BuildRequest(buildRequest with { UnitId = completedSaleId });
Check(completedSaleCommand is not null, "A new building may use the released construction footprint");
Building completedSaleSite = (Building)units.FindById(completedSaleId)!;
completedSaleSite.AdvanceConstruction(completedSaleSite.RemainingBuildingPoints);
Check(completedSaleSite.Actions.Any(action => action.Type == UnitActionType.SellBuilding) &&
      !completedSaleSite.Actions.Any(action => action.Type == UnitActionType.CancelConstruction),
    "Completed building replaces cancel with sell action");
Check(CancelConstructionRequest(NetworkCommands.CreateCancelConstructionRequest(ownerId, completedSaleId)) is null,
    "Completed building can no longer be cancelled");
var sellCommand = SellRequest(NetworkCommands.CreateSellBuildingRequest(ownerId, completedSaleId));
Check(sellCommand is { Type: NetworkMessageType.SellBuildingCommand } &&
      sellCommand.ResourceAmount == 10000 - barracksPrice + barracksPrice / 2,
    "Host refunds half the purchase price");
Deliver(sellCommand!);
Check(completedSaleSite.IsSelling && grid.GetOccupant(grid.ToCell(completedSaleSite.Position)) is null,
    "Selling starts shrink animation and releases grid footprint");
completedSaleSite.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1.0)));
Check(completedSaleSite.IsReadyForRemoval, "Sold building is removed after shrink animation");
Check(!Empty<GenericBuilding>().Actions.Any(
    action => action.Type is UnitActionType.SellBuilding or UnitActionType.CancelConstruction),
    "Generic building cannot be sold or construction-cancelled");

var commandCenter = units.SpawnBuilding("command-center", new Vector3(9.5f, 0, 2.5f), 0,
    Guid.NewGuid(), ownerId) as CommandCenter;
Check(commandCenter is not null && commandCenter.IsOperational &&
      commandCenter.Actions.Count(action => action.Type is UnitActionType.AIStartReactor or
          UnitActionType.AIStartRefinery or UnitActionType.AIStartEconomy) == 3 &&
      commandCenter.Actions.Any(action => action.Type == UnitActionType.AIStartScouting) &&
      commandCenter.Actions.Any(action => action.Type == UnitActionType.AIStopGoals),
    "Command Center uses building-1 and exposes economy, scouting and Stop controls");
Field(game, typeof(RTSGame), "_manualArmyGoals",
    new Dictionary<Guid, (Player Actor, ArmyGoalController Controller)>());
Field(game, typeof(RTSGame), "_manualArmyScouts", new Dictionary<Guid, ScoutingController>());
NetworkMessage startEconomyRequest = NetworkCommands.CreateUnitActionRequest(ownerId,
    [commandCenter!.UnitId], UnitActionType.AIStartEconomy, UnitActionContext.Empty);
Check(UnitActionRequest(startEconomyRequest) is { Type: NetworkMessageType.UnitActionCommand },
    "Host accepts an owned Command Center goal action");
var manualGoals = (Dictionary<Guid, (Player Actor, ArmyGoalController Controller)>)typeof(RTSGame)
    .GetField("_manualArmyGoals", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
Check(manualGoals.TryGetValue(armyId, out var manualGoal) &&
      manualGoal.Controller.ActiveGoal == AIArmyGoal.EstablishEconomy,
    "Command Center starts the reusable economy goal for its army");
Check(UnitActionRequest(startEconomyRequest with { SenderId = Guid.NewGuid() }) is null,
    "Foreign players cannot start Command Center goals");
Check(UnitActionRequest(NetworkCommands.CreateUnitActionRequest(ownerId, [commandCenter.UnitId],
          UnitActionType.AIStopGoals, UnitActionContext.Empty)) is { Type: NetworkMessageType.UnitActionCommand } &&
      manualGoals.Count == 0,
    "Command Center stops its army goal through the host");
units.RemoveMapObject(commandCenter);

Guid secondPlayerId = Guid.NewGuid(), secondArmyId = Guid.NewGuid();
Player aiIdentity = new(secondPlayerId, "second", armyId: secondArmyId);
Field(game, typeof(RTSGame), "_players", new List<Player>
{
    new(ownerId, "first", armyId: armyId)
});
Field(game, typeof(RTSGame), "_aiPlayers", new Dictionary<Guid, AIPlayer>
{
    [secondPlayerId] = new AIPlayer(aiIdentity)
});
armies.EnsureArmy(secondArmyId, secondPlayerId);
var startMarkers = new GameplayMarkerHandler();
startMarkers.Add(GameplayMarkerType.PlayerStart, new Vector3(2.5f, 0, 2.5f), 45);
startMarkers.Add(GameplayMarkerType.PlayerStart, new Vector3(9.5f, 0, 9.5f), 225);
Field(world, typeof(GameWorld), "<GameplayMarkers>k__BackingField", startMarkers);
typeof(NetworkHost).GetMethod("RememberStartPositionWish", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(host, new object[] { NetworkCommands.CreateStartPositionWishRequest(ownerId, 2) });
var startCommand = (NetworkMessage?)typeof(NetworkHost)
    .GetMethod("TryCreateStartMultiplayerGameCommand", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(host, new object[] { NetworkCommands.CreateStartMultiplayerGameRequest(hostId) });
Check(startCommand is { Type: NetworkMessageType.StartMultiplayerGameCommand, ResourceAmount: 10000 } &&
    startCommand.MatchStartAssignments?.Length == 2, "Host creates one multiplayer start assignment per player");
MatchStartAssignment[] matchAssignments = startCommand!.MatchStartAssignments!;
Check(matchAssignments.Single(item => item.PlayerId == ownerId).StartPositionSlot == 2 &&
    matchAssignments.Select(item => item.StartPositionSlot).Distinct().Count() == 2,
    "Host honors a free requested start and assigns every start only once");
Check(matchAssignments.Single(item => item.PlayerId == secondPlayerId).IsAI,
    "Host preserves AI armies in multiplayer match assignments");
Check(matchAssignments.Select(item => item.PlayerId).ToHashSet().SetEquals([ownerId, secondPlayerId]) &&
      matchAssignments.Select(item => item.BulldozerId).Distinct().Count() == 2,
    "Every human and AI player receives one distinct new match-start bulldozer");
typeof(RTSGame).GetMethod("PrepareAIPlayersForMatch", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(game, new object[] { matchAssignments });
Check(game.Players.Any(player => player.Id == secondPlayerId) &&
      game.FindAIPlayer(secondPlayerId.ToString()) is AIPlayer startedAI &&
      startedAI.Status == AIPlayerStatus.Active &&
      startedAI.Controller.Goal == AIGoalState.FindingBulldozer,
    "Match start restores and activates the host AI controller");
using (var commandNetwork = new NetworkHandler("CommandServiceTest"))
{
    Field(commandNetwork, typeof(NetworkHandler), "<IsHost>k__BackingField", true);
    Guid actingPlayerId = Guid.NewGuid();
    Guid workerId = Guid.NewGuid();
    Guid requestedBuildingId = Guid.NewGuid();
    var receivedOrders = new List<NetworkMessage>();
    commandNetwork.MessageReceived += receivedOrders.Add;
    var commandService = new PlayerCommandService(commandNetwork, actingPlayerId);
    Guid returnedBuildingId = await commandService.BuildAndConstructAsync(
        "Reaktor", new Vector3(4.5f, 0.0f, 6.5f), 90.0f, [workerId], requestedBuildingId);
    commandNetwork.Update();
    Check(returnedBuildingId == requestedBuildingId &&
          receivedOrders.Select(message => message.Type).SequenceEqual(
              [NetworkMessageType.BuildRequest]),
        "Shared player command service sends placement and workers as one atomic request");
    Check(receivedOrders.All(message => message.SenderId == actingPlayerId) &&
          receivedOrders[0].UnitId == requestedBuildingId &&
          receivedOrders[0].UnitIds!.SequenceEqual([workerId]),
        "Shared player command service preserves the human or AI actor identity");
}
var combinedBuild = NetworkCommands.CreateBuildRequest(Guid.NewGuid(), "Reaktor", 1, 0, 1, 0, Guid.NewGuid())
    with { UnitIds = [Guid.NewGuid()], PurchasePrice = 725 };
var confirmedBuild = NetworkCommands.CreateBuildCommand(Guid.NewGuid(), combinedBuild);
Check(confirmedBuild.UnitIds!.SequenceEqual(combinedBuild.UnitIds) &&
      confirmedBuild.UnitId == combinedBuild.UnitId && confirmedBuild.PurchasePrice == 725,
    "Build confirmation preserves workers and the host-authoritative price");
// Shared texel density must be independent of model bounds and ordinary UVs.
var sharedRegion = new TextureHandler.TextureRegion { AtlasIndex = 0, X = 16, Y = 32, Width = 256, Height = 128, AtlasWidth = 1024, AtlasHeight = 1024 };
SubMesh TexturedPart(string? sharedName, float length)
{
    VertexPositionColorNormalTexture[] v =
    [
        new(new Vector3(0, 0, 0), Color.White, Vector3.Forward, new Vector2(0.7f, 0.8f)),
        new(new Vector3(length, 0, 0), Color.White, Vector3.Forward, new Vector2(0.9f, 0.8f)),
        new(new Vector3(0, 1, 0), Color.White, Vector3.Forward, new Vector2(0.7f, 0.9f))
    ];
    return new SubMesh("face", v, new[] { 0, 1, 2 }, Vector3.Zero, 0, sharedRegion, sharedTextureName: sharedName);
}
var sharedPart = TexturedPart("bricks", 1);
var plainPart = TexturedPart(null, 1);
var uvMesh = new Mesh("mixed", new[] { sharedPart, plainPart });
uvMesh.ApplySharedTextureMapping();
float PixelDistance(SubMesh part) => Math.Abs(part.Vertices[1].TextureCoordinate.X - part.Vertices[0].TextureCoordinate.X) * sharedRegion.AtlasWidth;
Check(Math.Abs(PixelDistance(sharedPart) - 32) < 0.001f, "Default shared density is 32 pixels per unit");
Check(plainPart.Vertices[0].TextureCoordinate == new Vector2(0.7f, 0.8f) && !plainPart.RepeatSharedTexture, "Ordinary UVs remain untouched");
Check(sharedPart.RepeatSharedTexture, "Shared region uses shader-local repeating");
var largePart = TexturedPart("bricks", 20);
new Mesh("large", new[] { largePart }).ApplySharedTextureMapping();
Check(Math.Abs(PixelDistance(largePart) - 640) < 0.001f, "Larger buildings retain pixel density and multiple repeats");
uvMesh.LocalTransform = Matrix.CreateScale(2);
uvMesh.ApplySharedTextureMapping(64);
Check(Math.Abs(PixelDistance(sharedPart) - 128) < 0.001f, "Explicit density includes permanent mesh scale");
var previousUv = sharedPart.Vertices.Select(v => v.TextureCoordinate).ToArray();
uvMesh.ApplySharedTextureMapping(64);
Check(previousUv.SequenceEqual(sharedPart.Vertices.Select(v => v.TextureCoordinate)), "Repeated mapping is idempotent");
Check(Math.Abs((sharedPart.Vertices[2].TextureCoordinate.Y - sharedPart.Vertices[0].TextureCoordinate.Y) * sharedRegion.AtlasHeight + 128) < 0.001f, "Non-square textures preserve vertical density");
var smoothSharedPart = new SubMesh("smooth-shared",
[
    new(new Vector3(0, 0, 0), Color.White, Vector3.Right, Vector2.Zero),
    new(new Vector3(2, 0, 0), Color.White, Vector3.Up, Vector2.Zero),
    new(new Vector3(0, 1, 0), Color.White, Vector3.Backward, Vector2.Zero)
], [0, 1, 2], Vector3.Zero, 0, sharedRegion, sharedTextureName: "bricks");
new Mesh("smooth-shared", new[] { smoothSharedPart }).ApplySharedTextureMapping();
Check(Math.Abs((smoothSharedPart.Vertices[1].TextureCoordinate.X - smoothSharedPart.Vertices[0].TextureCoordinate.X) *
               sharedRegion.AtlasWidth + 64) < 0.001f &&
      Math.Abs((smoothSharedPart.Vertices[2].TextureCoordinate.Y - smoothSharedPart.Vertices[0].TextureCoordinate.Y) *
               sharedRegion.AtlasHeight + 32) < 0.001f,
    "Shared cube mapping uses one geometric projection plane across a smooth-shaded triangle");
foreach (float invalid in new[] { 0, -1, float.NaN, float.PositiveInfinity })
{
    bool rejected = false;
    try { uvMesh.ApplySharedTextureMapping(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "Invalid shared texel density rejected");
}

// Seed atlas metadata only, so the real BBModel importer can be tested without a GPU.
var textureHandler = new TextureHandler(null!);
Type atlasType = typeof(TextureHandler).GetNestedType("Atlas", BindingFlags.NonPublic)!;
object atlas = Activator.CreateInstance(atlasType, new object?[] { null, 2 })!;
var atlasRegions = (Dictionary<string, TextureHandler.TextureRegion>)atlasType.GetField("Regions")!.GetValue(atlas)!;
((System.Collections.IList)typeof(TextureHandler).GetField("_atlases", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(textureHandler)!).Add(atlas);
Globals.TextureHandler = textureHandler;
Globals.MaterialMaskTextureHandler = new TextureHandler(null!);
string fixture = Path.GetTempFileName();
try
{
    atlasRegions["bricks"] = sharedRegion;
    atlasRegions[$"bbmodel:{Path.GetFullPath(fixture)}:texture:0"] = new TextureHandler.TextureRegion
        { AtlasIndex = 1, X = 8, Y = 8, Width = 32, Height = 32, AtlasWidth = 1024, AtlasHeight = 1024 };
    File.WriteAllText(fixture, """
    {"name":"mixed-model","resolution":{"width":32,"height":32},
     "textures":[{"id":"0","name":"ordinary.png"},{"id":"1","name":"shared:bricks"}],
     "elements":[
       {"uuid":"cube","name":"mixed-cube","type":"cube","from":[0,0,0],"to":[10,10,10],"origin":[0,0,0],
        "faces":{"north":{"texture":0,"uv":[0,0,32,32]},"south":{"texture":1,"uv":[0,0,32,32]}}},
       {"uuid":"mesh","name":"mixed-mesh","type":"mesh","origin":[0,0,0],
        "vertices":{"a":[0,0,0],"b":[10,0,0],"c":[0,10,0],"d":[10,10,0]},
        "faces":{"plain":{"texture":0,"vertices":["a","b","c"],"uv":{"a":[0,0],"b":[32,0],"c":[0,32]}},
                 "shared":{"texture":1,"vertices":["b","d","c"],"uv":{"b":[0,0],"d":[32,0],"c":[0,32]}}}}
     ],"outliner":["cube","mesh"]}
    """);
    var imported = BBModelLoader.Load(fixture);
    Check(imported.SubMeshes.Count == 4 && imported.Root.Children.Count == 2, "Mixed materials split into batches without changing hierarchy");
    Check(imported.SubMeshes.Count(p => p.SharedTextureName == "bricks") == 2, "Shared provenance retained for cubes and meshes");
    Check(imported.SubMeshes.Select(p => p.TextureAtlasIndex).Distinct().Count() == 2, "Shared and local textures can use separate atlases");
    var originalUvs = imported.SubMeshes.Where(p => p.SharedTextureName is null).SelectMany(p => p.Vertices.Select(v => v.TextureCoordinate)).ToArray();
    imported.ApplySharedTextureMapping();
    Check(originalUvs.SequenceEqual(imported.SubMeshes.Where(p => p.SharedTextureName is null).SelectMany(p => p.Vertices.Select(v => v.TextureCoordinate))), "Importer preserves authored ordinary face UVs");
    Check(imported.SubMeshes.Where(p => p.SharedTextureName is not null).All(p => p.RepeatSharedTexture), "All shared cube and mesh batches opt into repeating");
}
finally { File.Delete(fixture); }

string shadingFixture = Path.GetTempFileName();
try
{
    atlasRegions[$"bbmodel:{Path.GetFullPath(shadingFixture)}:texture:0"] = new TextureHandler.TextureRegion
        { AtlasIndex = 0, X = 0, Y = 0, Width = 32, Height = 32, AtlasWidth = 1024, AtlasHeight = 1024 };
    atlasRegions[$"bbmodel:{Path.GetFullPath(shadingFixture)}:texture:1"] = new TextureHandler.TextureRegion
        { AtlasIndex = 1, X = 0, Y = 0, Width = 32, Height = 32, AtlasWidth = 1024, AtlasHeight = 1024 };
    File.WriteAllText(shadingFixture, """
    {"name":"shading-model","resolution":{"width":32,"height":32},
     "textures":[{"id":"0","name":"first.png"},{"id":"1","name":"second.png"}],
     "elements":[
       {"uuid":"smooth","name":"smooth-part","type":"mesh","shading":"smooth","origin":[0,0,0],
        "vertices":{"a":[0,0,0],"b":[10,0,0],"c":[0,10,0],"d":[0,0,10]},
        "faces":{"front":{"texture":0,"vertices":["a","b","c"],"uv":{"a":[0,0],"b":[32,0],"c":[0,32]}},
                 "bottom":{"texture":1,"vertices":["a","d","b"],"uv":{"a":[0,0],"d":[0,32],"b":[32,0]}}}},
       {"uuid":"flat","name":"flat-part","type":"mesh","shading":"flat","origin":[20,0,0],
        "vertices":{"a":[0,0,0],"b":[10,0,0],"c":[0,10,0],"d":[0,0,10]},
        "faces":{"front":{"texture":0,"vertices":["a","b","c"],"uv":{"a":[0,0],"b":[32,0],"c":[0,32]}},
                 "bottom":{"texture":1,"vertices":["a","d","b"],"uv":{"a":[0,0],"d":[0,32],"b":[32,0]}}}}
     ],"outliner":["smooth","flat"]}
    """);

    var shaded = BBModelLoader.Load(shadingFixture);
    var smoothVertices = shaded.SubMeshes.Where(part => part.Name == "smooth-part").SelectMany(part => part.Vertices).ToArray();
    var flatVertices = shaded.SubMeshes.Where(part => part.Name == "flat-part").SelectMany(part => part.Vertices).ToArray();
    var smoothSeams = smoothVertices.GroupBy(vertex => vertex.Position).Where(group => group.Count() > 1).ToArray();
    var flatSeams = flatVertices.GroupBy(vertex => vertex.Position).Where(group => group.Count() > 1).ToArray();
    Check(smoothSeams.Length == 2 && smoothSeams.All(group => group.Select(vertex => vertex.Normal).Distinct().Count() == 1), "Smooth bbmodel shading averages normals across texture batches");
    Check(flatSeams.Any(group => group.Select(vertex => vertex.Normal).Distinct().Count() > 1), "Flat bbmodel shading retains face normals");
    Check(smoothVertices.All(vertex => MathF.Abs(vertex.Normal.Length() - 1.0f) < 0.0001f), "Imported smooth vertex normals are normalized");
}
finally { File.Delete(shadingFixture); }
// Continuous bulldozer earthwork: real host controller, headless terrain and wire replay.
Globals.MeshHandler.Meshes["bulldozer-1"] = Globals.MeshHandler.Meshes["barracks-1"];
GDIBulldozer SetupEarthwork()
{
    grid = new GameGrid(40, 40, 1);
    terrain = Terrain(40, 40);
    Field(terrain, typeof(Terrain), "_tiles", new TerrainTile[40, 40]);
    grid.BindTerrain(terrain);
    world = World(grid);
    Field(world, typeof(GameWorld), "_terrain", terrain);
    units = new UnitHandler();
    Field(world, typeof(GameWorld), "<Units>k__BackingField", units);
    Field(world, typeof(GameWorld), "<PathfindingManager>k__BackingField", new PathfindingManager(world));
    Globals.World = world;
    var dozer = new GDIBulldozer(new Vector3(8.5f, 0, 8.5f), Guid.NewGuid());
    Field(dozer, typeof(Unit), "<ArmyId>k__BackingField", armyId);
    var driver = Unit();
    Field(driver, typeof(Unit), "<UnitId>k__BackingField", Guid.NewGuid());
    Field(driver, typeof(Unit), "<ArmyId>k__BackingField", armyId);
    if (!dozer.Occupancy!.TryAdd(driver, OccupantRole.Driver, out _)) throw new Exception("Driver fixture failed");
    UnitList(units).Add(dozer);
    grid.TryMove(dozer, grid.ToCell(dozer.Position));
    return dozer;
}
NetworkMessage DriveRequest(GDIBulldozer dozer, float x = 20.5f, float z = 8.5f) => new(
    NetworkMessageType.EarthworkRequest, ownerId, UnitId: dozer.UnitId, X: x, Z: z, EarthworkKind: EarthworkKind.LevelAndConcrete);
var bulldozer = SetupEarthwork();
var earthMessages = new List<NetworkMessage>();
var earthController = new EarthworkController(world, hostId, earthMessages.Add, false);
Check(earthController.Start(DriveRequest(bulldozer) with { SenderId = Guid.NewGuid() }) is null, "Earthwork rejects foreign control");
Check(earthController.Start(DriveRequest(bulldozer) with { X = float.NaN }) is null, "Earthwork rejects invalid target");
var driveStart = earthController.Start(DriveRequest(bulldozer))!;
Check(driveStart.EarthworkOrder is { IsDrive: true, TargetHeight: 0 }, "Drive order freezes initial terrain height");
Check(Wire(driveStart).EarthworkOrder == driveStart.EarthworkOrder, "Drive order survives network serialization");
earthController.Update(0.1f);
Check(bulldozer.Position.X > 8.5f && terrain.GetTile(8, 8) == TerrainTile.Concrete, "First simulation tick moves and concretes without per-cell delay");
Check(terrain.GetTile(8, 7) == TerrainTile.Concrete && terrain.GetTile(8, 9) == TerrainTile.Concrete && terrain.GetTile(8, 6) != TerrainTile.Concrete, "Cardinal strip matches bulldozer width");
for (int i = 0; i < 70 && earthController.ActiveJobs > 0; i++) earthController.Update(0.1f);
Check(earthController.ActiveJobs == 0 && Math.Abs(bulldozer.Position.X - 20.5f) < 0.001f, "Drive reaches target and terminates");
Check(bulldozer.MoveSpeed == 7 && bulldozer.EarthworkOrder is null, "Completion restores normal movement");
Check(terrain.GetTile(15, 8) == TerrainTile.Concrete && terrain.GetTile(15, 10) != TerrainTile.Concrete, "Drive paints a strip rather than an 8x8 area");
Check(!earthMessages.Any(m => m.Type == NetworkMessageType.GotoCommand), "Drive uses no per-cell pathfinding orders");
var hostHeights = Enumerable.Range(0, 40).SelectMany(z => Enumerable.Range(0, 40).Select(x => terrain.GetHeight(x, z))).ToArray();
var hostTiles = Enumerable.Range(0, 40).SelectMany(z => Enumerable.Range(0, 40).Select(x => terrain.GetTile(x, z))).ToArray();
var replayWorker = SetupEarthwork();
replayWorker.BeginEarthwork(Wire(driveStart).EarthworkOrder!);
foreach (var original in earthMessages.Where(m => m.Type == NetworkMessageType.EarthworkCellCommand))
{
    var m = Wire(original);
    if (!replayWorker.ApplyEarthworkDrive(world, m.EarthworkOrderId!.Value, m.EarthworkSequence, m.EarthworkCells!, new(m.X, m.Z), false))
        throw new Exception("Client rejected authoritative drive patch");
}
Check(hostHeights.SequenceEqual(Enumerable.Range(0, 40).SelectMany(z => Enumerable.Range(0, 40).Select(x => terrain.GetHeight(x, z)))) && hostTiles.SequenceEqual(Enumerable.Range(0, 40).SelectMany(z => Enumerable.Range(0, 40).Select(x => terrain.GetTile(x, z)))), "Client replay exactly matches host terrain");
var lastPatch = earthMessages.Last(m => m.Type == NetworkMessageType.EarthworkCellCommand);
Check(!replayWorker.ApplyEarthworkDrive(world, lastPatch.EarthworkOrderId!.Value, lastPatch.EarthworkSequence, lastPatch.EarthworkCells!, new(lastPatch.X, lastPatch.Z), false), "Duplicate patches are ignored");

bulldozer = SetupEarthwork();
terrain.SetHeight(15, 8, Earthwork.MaximumHeightChange + 1);
var limitedPreview = Earthwork.Preview(world, bulldozer, new(20, 8), EarthworkKind.LevelAndConcrete);
Check(limitedPreview.IsAllowed && limitedPreview.Cells.Any(c => !c.Allowed), "Preview allows a safe prefix and marks the blocked continuation");
earthMessages.Clear();
earthController = new(world, hostId, earthMessages.Add, false);
Check(earthController.Start(DriveRequest(bulldozer)) is not null, "Distant obstacle does not reject usable prefix");
earthController.Update(20);
Check(earthController.ActiveJobs == 0 && bulldozer.Position.X < 15 && terrain.GetHeight(15, 8) == Earthwork.MaximumHeightChange + 1, "Large tick stops before excessive height without modifying it");
Check(terrain.GetTile(10, 8) == TerrainTile.Concrete && terrain.GetTile(16, 8) != TerrainTile.Concrete, "Completed strip remains after obstacle stop");

bulldozer = SetupEarthwork();
earthController = new(world, hostId, earthMessages.Add, false);
earthController.Start(DriveRequest(bulldozer));
earthController.Update(0.1f);
grid.GetCell(12, 8).IsBlocked = true;
earthController.Update(10);
Check(earthController.ActiveJobs == 0 && bulldozer.Position.X < 12 && terrain.GetTile(12, 8) != TerrainTile.Concrete, "New obstacle immediately stops drive");

bulldozer = SetupEarthwork();
var neighbor = Unit();
neighbor.SetPosition(new Vector3(11.5f, 0, 10.5f));
grid.TryMove(neighbor, new Point(11, 10));
Check(!Earthwork.Preview(world, bulldozer, new(20, 8), EarthworkKind.LevelAndConcrete).IsAllowed, "Shared-vertex neighbor occupancy protects adjacent structures");

bulldozer = SetupEarthwork();
earthController = new(world, hostId, earthMessages.Add, false);
earthController.Start(DriveRequest(bulldozer));
earthController.Update(0.1f);
var stoppedPosition = bulldozer.Position;
earthController.CancelForRequest(new(NetworkMessageType.StopRequest, ownerId, UnitIds: new[] { bulldozer.UnitId }));
earthController.Update(1);
Check(earthController.ActiveJobs == 0 && bulldozer.Position == stoppedPosition && bulldozer.MoveSpeed == 7, "Stop cancels drive and restores speed");

bulldozer = SetupEarthwork();
for (int z = 0; z < 40; z++) for (int x = 0; x < 40; x++) terrain.SetHeight(x, z, 1);
terrain.SetTile(8, 8, TerrainTile.Concrete);
earthController = new(world, hostId, earthMessages.Add, false);
var raisedStart = earthController.Start(DriveRequest(bulldozer, 16.5f, 16.5f))!;
Check(raisedStart.EarthworkOrder!.TargetHeight == 1, "Starting concrete determines target height");
terrain.SetHeight(15, 15, 1.5f);
earthController.Update(10);
Check(earthController.ActiveJobs == 0 && terrain.GetHeight(15, 15) == 1, "Diagonal drive regrades terrain to frozen height");
Check(terrain.GetTile(12, 12) == TerrainTile.Concrete && terrain.GetTile(12, 7) != TerrainTile.Concrete, "Diagonal strip has no gaps or distant side effects");
var removePreview = Earthwork.Preview(world, bulldozer, new(13, 13), EarthworkKind.RemoveConcrete);
Check(!removePreview.Order.IsDrive && removePreview.Cells.Count == 64, "Concrete removal retains area workflow");
Earthwork.ApplyCell(world, removePreview.Order, new(12, 12), false);
Check(terrain.GetTile(12, 12) == TerrainTile.Dirt && terrain.GetHeight(12, 12) == 1, "Concrete removal preserves height");

bulldozer = SetupEarthwork();
grid.GetCell(9, 8).AllowedMovement = MovementModes.Walk;
Check(!Earthwork.Preview(world, bulldozer, new(20, 8), EarthworkKind.LevelAndConcrete).IsAllowed, "Drive respects no-vehicle cells");
Check(!Earthwork.Preview(world, bulldozer, new(0, 0), EarthworkKind.RemoveConcrete).IsAllowed, "Removal rejects map-edge area");
bulldozer = SetupEarthwork();
earthController = new(world, hostId, earthMessages.Add, false);
earthController.Start(DriveRequest(bulldozer));
earthController.Update(0.1f);
var beforeBlockedSection = Enumerable.Range(0, 40).SelectMany(z => Enumerable.Range(0, 40).Select(x => terrain.GetTile(x, z))).ToArray();
grid.GetCell(11, 9).IsBlocked = true;
earthController.Update(0.1f);
Check(earthController.ActiveJobs == 0 && beforeBlockedSection.SequenceEqual(Enumerable.Range(0, 40).SelectMany(z => Enumerable.Range(0, 40).Select(x => terrain.GetTile(x, z)))), "One blocked blade edge rejects entire section atomically");

bulldozer = SetupEarthwork();
earthController = new(world, hostId, earthMessages.Add, false);
earthController.Start(DriveRequest(bulldozer, 0.5f, 8.5f));
earthController.Update(10);
Check(earthController.ActiveJobs == 0 && bulldozer.Position.X > 0.5f, "Vehicle width stops drive before map edge");
Check(grid.GetOccupant(grid.ToCell(bulldozer.Position)) == bulldozer, "Stopped worker retains grid occupancy");

bulldozer = SetupEarthwork();
earthController = new(world, hostId, earthMessages.Add, false);
earthController.Start(DriveRequest(bulldozer));
bulldozer.Occupancy!.TryRemove(bulldozer.Occupancy.Occupants[0].UnitId, out _);
earthController.Update(1);
Check(earthController.ActiveJobs == 0 && bulldozer.EarthworkOrder is null, "Driver loss stops earthwork");
Check(earthController.Start(DriveRequest(bulldozer)) is null, "Empty bulldozer cannot start earthwork");
// Helicopter host simulation, supplies, landing reservations and snapshot replay.
Globals.MeshHandler.Meshes["heli-1"] = Globals.MeshHandler.Meshes["barracks-1"];
Globals.MeshHandler.Meshes["helipad-1"] = Globals.MeshHandler.Meshes["barracks-1"];
var includedHelipad = new Helipad(Vector3.Zero, Guid.NewGuid());
includedHelipad.AdvanceConstruction(includedHelipad.TotalBuildingPointsNeeded);
Check(includedHelipad.TryQueueIncludedHelicopter(ownerId) &&
      includedHelipad.ProductionQueue.ActiveOrder is
          { UnitTypeId: "helicopter", DurationSeconds: 0.1f } &&
      includedHelipad.IncludedUnitGranted &&
      !includedHelipad.TryQueueIncludedHelicopter(ownerId) &&
      includedHelipad.Actions.Any(action => action.Type == UnitActionType.TrainUnit &&
          action.TargetObjectName == "helicopter"),
    "Completed helipad grants one included helicopter and offers paid replacements");
Helicopter SetupHelicopter(int passengers = 0)
{
    var old = SetupEarthwork();
    grid.Remove(old);
    UnitList(units).Clear();
    var helicopter = new Helicopter(new(10.5f, 0, 10.5f), Guid.NewGuid(), passengers);
    Field(helicopter, typeof(Unit), "<ArmyId>k__BackingField", armyId);
    UnitList(units).Add(helicopter);
    if (!helicopter.InitializeOnGround(world)) throw new Exception("Helicopter fixture could not land");
    return helicopter;
}
void FlyTicks(Helicopter helicopter, int count)
{
    for (int i = 0; i < count; i++) helicopter.SimulateFlight(world, 0.1f);
}
Helipad AddPad(Vector3 position)
{
    var pad = new Helipad(position, Guid.NewGuid()) { LandingLocalPosition = new(0, 0.1f, 0) };
    Field(pad, typeof(Unit), "<ArmyId>k__BackingField", armyId);
    pad.AdvanceConstruction(pad.TotalBuildingPointsNeeded);
    UnitList(units).Add(pad);
    if (!grid.TryPlace(pad, position, 0)) throw new Exception("Helipad fixture placement failed");
    return pad;
}
var heli = SetupHelicopter();
Check(heli.IsLanded && grid.GetOccupant(10, 10) == heli, "Helicopter starts landed with occupied ground footprint");
Check(UnitFactory.SpawnUnit("heli", new(20, 0, 20), 0, Guid.NewGuid(), ownerId) is Helicopter, "Helicopter console/factory alias");
Check(!heli.CanFireWeapon && !heli.TryConsumeAmmunition(), "Landed helicopter cannot shoot");
float fullFuel = heli.Fuel;
Check(heli.TryReceiveGotoCommand(world, new(new(24.5f, 10.5f))), "Fly command takes off");
Check(grid.GetOccupant(10, 10) is null && world.PathfindingManager.PendingRequests == 0, "Flying aircraft releases ground grid and bypasses pathfinding");
grid.GetCell(18, 10).IsBlocked = true;
terrain.SetHeight(18, 10, 4);
FlyTicks(heli, 150);
Check(Math.Abs(heli.Position.X - 24.5f) < 0.01f && heli.Position.Y >= 8, "Flight crosses blocked ground and raised terrain");
Check(heli.Fuel < fullFuel && heli.Fuel > 0, "Flight and hover consume fuel");
var beforeTurn = heli.Position;
heli.TryReceiveGotoCommand(world, new(new(10.5f, 10.5f)));
FlyTicks(heli, 10);
Check(heli.Position.X < beforeTurn.X && Math.Abs(heli.Position.Z - beforeTurn.Z) < 0.01f, "Helicopter reverses direction without a ground turning radius");
heli.Stop();
var hoveringAt = new Vector2(heli.Position.X, heli.Position.Z);
FlyTicks(heli, 5);
Check(new Vector2(heli.Position.X, heli.Position.Z) == hoveringAt && heli.FlightState == HelicopterFlightState.Hovering, "Stop hovers in place");
Check(heli.TryConsumeAmmunition() && heli.Ammunition == 39, "Successful helicopter shot consumes one round");
heli.PlayShotEffects();
Check(heli.VisualRecoilOffset == Vector3.Zero && heli.VisualRecoilPitchDegrees == 0, "Helicopter has no recoil");
float fuelBeforeGround = heli.Fuel;
Check(heli.RequestLanding(world, new(24.5f, 24.5f)), "Safe ground landing accepted");
FlyTicks(heli, 200);
Check(heli.IsLanded && heli.AssignedHelipadId is null && grid.GetOccupant(24, 24) == heli, "Ground landing reserves and occupies destination");
float landedFuel = heli.Fuel;
FlyTicks(heli, 30);
Check(heli.Fuel == landedFuel && heli.Fuel < fuelBeforeGround && heli.Ammunition == 39, "Ground landing stops consumption but provides no supplies");

var pad = AddPad(new(30.5f, 0, 30.5f));
Check(pad.CanAccept(world, heli) && heli.ReturnToHelipad(world), "Completed allied helipad accepts return");
var rivalHeli = new Helicopter(new(6.5f, 0, 30.5f), Guid.NewGuid());
Field(rivalHeli, typeof(Unit), "<ArmyId>k__BackingField", armyId);
UnitList(units).Add(rivalHeli);
Check(!pad.CanAccept(world, rivalHeli), "Approaching helicopter reserves helipad against a second aircraft");
Check(rivalHeli.ReturnToHelipad(world) && rivalHeli.AssignedHelipadId is null, "Occupied pad falls back to a ground landing");
FlyTicks(heli, 200);
Check(heli.IsLanded && heli.AssignedHelipadId == pad.UnitId && Math.Abs(heli.Position.Y - pad.GetLandingPosition(heli).Y) < 0.001f, "Helicopter lands on pad surface instead of terrain or flag height");
Check(heli.Fuel == heli.MaximumFuel && heli.Ammunition == heli.MaximumAmmunition, "Landed pad refills fuel and ammunition");
Check(grid.GetOccupant(30, 30) == pad, "Landing on pad never replaces building occupancy");
heli.TakeOff(world);
Check(pad.CanAccept(world, rivalHeli), "Takeoff releases helipad reservation");

heli = SetupHelicopter(4);
var passenger = Unit();
Field(passenger, typeof(Unit), "<UnitId>k__BackingField", Guid.NewGuid());
Field(passenger, typeof(Unit), "<ArmyId>k__BackingField", armyId);
Check(heli.Occupancy!.CanEnter(passenger, OccupantRole.Passenger), "Transport extension allows passengers while landed");
heli.TakeOff(world);
Check(!heli.Occupancy.CanEnter(passenger, OccupantRole.Passenger), "Passengers cannot board during flight");
heli.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.1)));
Check(heli.MainRotorDegree > 0 && heli.RearRotorDegree > 0, "Rotor animation runs even when optional model pivots are absent");
FlyTicks(heli, 40);
while (heli.TryConsumeAmmunition()) { }
Check(heli.Ammunition == 0 && !heli.CanFireWeapon, "Ammunition cannot become negative and empty magazine blocks fire");
FlyTicks(heli, 100);
Check(heli.IsLanded && heli.Ammunition == 0, "Empty magazine triggers landing fallback when no pad is available");

heli = SetupHelicopter();
heli.TakeOff(world);
FlyTicks(heli, 40);
Field(heli, typeof(Helicopter), "<Fuel>k__BackingField", 0.01f);
FlyTicks(heli, 50);
Check(heli.IsLanded && heli.Fuel == 0 && heli.HitPoints > 0, "Empty fuel tank performs safe emergency landing");
Check(!heli.TakeOff(world), "Empty tank prevents another takeoff");

heli = SetupHelicopter();
pad = AddPad(new(28.5f, 0, 28.5f));
heli.TakeOff(world);
FlyTicks(heli, 40);
Field(heli, typeof(Helicopter), "<Fuel>k__BackingField", 23f);
heli.SimulateFlight(world, 0.1f);
Check(heli.AssignedHelipadId == pad.UnitId, "Low fuel automatically selects a free allied pad");
UnitList(units).Remove(pad);
grid.Remove(pad);
heli.SimulateFlight(world, 0.1f);
Check(heli.AssignedHelipadId is null, "Destroyed helipad invalidates reservation");

heli = SetupHelicopter();
host = new NetworkHost(transport, input, world);
NetworkMessage? HeliOrder(NetworkMessage request) => (NetworkMessage?)typeof(NetworkHost).GetMethod("TryCreateHelicopterOrder", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { request });
var takeoffRequest = new NetworkMessage(NetworkMessageType.HelicopterOrderRequest, ownerId, UnitId: heli.UnitId, HelicopterOrder: HelicopterOrder.TakeOff);
Check(HeliOrder(takeoffRequest with { SenderId = Guid.NewGuid() }) is null, "Host rejects unauthorized flight orders");
Check(HeliOrder(takeoffRequest with { HelicopterOrder = (HelicopterOrder)999 }) is null, "Host rejects unknown flight orders");
Check(HeliOrder(takeoffRequest) is { Type: NetworkMessageType.UnitStateCommand }, "Host confirms takeoff through authoritative state");
FlyTicks(heli, 40);
heli.TryConsumeAmmunition();
var flightState = Wire(NetworkCommands.CreateUnitStateCommand(hostId, heli.GetState())).UnitState!;
var clientHeli = new Helicopter(Vector3.Zero, heli.UnitId);
clientHeli.ApplyState(flightState);
Check(clientHeli.Position == heli.Position && clientHeli.Fuel == heli.Fuel && clientHeli.Ammunition == heli.Ammunition && clientHeli.FlightState == heli.FlightState, "Flight snapshot wire replay matches position, supplies and phase");
clientHeli.ApplyState(flightState with { Revision = flightState.Revision - 1, Payload = JsonSerializer.SerializeToUtf8Bytes(new HelicopterState(0, 0, 0, 0, HelicopterFlightState.Landed, 0, 0, null, null, null, null)) });
Check(clientHeli.Position == heli.Position, "Stale flight state cannot rewind client");
var fireRequest = new NetworkMessage(NetworkMessageType.AttackRequest, hostId, UnitIds: new[] { heli.UnitId }, X: heli.Position.X + 1, Y: 0, Z: heli.Position.Z);
NetworkMessage? HeliFire(NetworkMessage request) => (NetworkMessage?)typeof(NetworkHost).GetMethod("TryCreateAttackCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { request });
int ammunitionBefore = heli.Ammunition;
Check(HeliFire(fireRequest with { SenderId = Guid.NewGuid() }) is null && heli.Ammunition == ammunitionBefore, "Unauthorized attack cannot consume ammunition");
Check(HeliFire(fireRequest with { X = 1000 }) is null && heli.Ammunition == ammunitionBefore, "Out-of-range shot rejected before ammunition consumption");
Check(HeliFire(fireRequest) is not null && heli.Ammunition == ammunitionBefore - 1, "Authoritative attack consumes ammunition exactly once");
Check(HeliFire(fireRequest) is null && heli.Ammunition == ammunitionBefore - 1, "Duplicate rapid attack request cannot bypass weapon cooldown");
Check(!heli.Actions.Any(a => a.Type == UnitActionType.LeaveContainer), "Combat helicopter exposes no empty transport action");
heli = SetupHelicopter();
pad = AddPad(new(25.5f, 0, 25.5f));
Check(heli.ReturnToHelipad(world), "Pad reservation for diversion test");
FlyTicks(heli, 5);
heli.SetAttackGroundTarget(new(15.5f, 0, 15.5f));
Check(heli.AssignedHelipadId is null, "Explicit attack releases previous landing reservation");
FlyTicks(heli, 50);
Check(heli.CanFireWeapon && !heli.IsLanded, "Attack can replace a return-to-pad order");
heli.RequestLanding(world, new(22.5f, 22.5f));
FlyTicks(heli, 20);
Check(heli.RequestLanding(world, new(8.5f, 28.5f)), "Landing destination can be replaced during approach");
FlyTicks(heli, 200);
Check(heli.IsLanded && Math.Abs(heli.Position.Z - 28.5f) < 0.01f, "Diverted landing reaches the new position before descending");

// Import the actual user-authored models with atlas metadata, without a graphics device.
Globals.MaterialMaskTextureHandler = textureHandler;
foreach (string relative in new[] { "vehicles/heli-1.bbmodel", "buildings/helipad-1.bbmodel", "blue-pick-up-truck.bbmodel" })
{
    string modelPath = Path.GetFullPath(Path.Combine("Content/models", relative));
    using var modelJson = JsonDocument.Parse(File.ReadAllText(modelPath));
    int textureIndex = 0;
    foreach (var texture in modelJson.RootElement.GetProperty("textures").EnumerateArray())
    {
        string name = texture.GetProperty("name").GetString()!;
        atlasRegions[name.StartsWith("shared:") ? name[7..] : $"bbmodel:{modelPath}:texture:{textureIndex}"] = sharedRegion;
        atlasRegions[$"bbmodel:{modelPath}:material-mask:{textureIndex}"] = sharedRegion;
        string sourceName = texture.TryGetProperty("relative_path", out var rel) ? rel.GetString()! : name;
        string sourcePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(modelPath)!, sourceName));
        atlasRegions[Path.Combine(Path.GetDirectoryName(sourcePath)!, Path.GetFileNameWithoutExtension(sourcePath) + "-MaterialMask.png")] = sharedRegion;
        textureIndex++;
    }
    var actualMesh = BBModelLoader.Load(modelPath);
    Check(actualMesh.SubMeshes.Count > 0, "Authored helicopter/helipad geometry imports");
    Globals.MeshHandler.Meshes[Path.GetFileNameWithoutExtension(relative)] = actualMesh;
}
var actualHeli = new Helicopter(new(10, 0, 10), Guid.NewGuid());
var actualMeshSet = new MeshSet(Globals.MeshHandler.Meshes["heli-1"]);
Check(actualMeshSet.SetPivotRotation("pivot:rotor_main", Quaternion.CreateFromAxisAngle(Vector3.Up, 1)), "Actual main rotor pivot is animated");
Check(actualMeshSet.Pivots.Any(p => p.Name == "pivot:turret"), "Actual helicopter turret pivot imports");
Check(actualMeshSet.SetPivotRotation("pivot:rotor_rear", Quaternion.Identity), "Actual rear rotor pivot is animated");
Check(!new MeshSet(Globals.MeshHandler.Meshes["helipad-1"]).SetPivotRotation("pivot:rotor_rear", Quaternion.Identity), "Absent rear rotor pivot is optional");
var unanimatedMeshSet = new MeshSet(Globals.MeshHandler.Meshes["heli-1"]);
actualMeshSet.TryGetPivotWorldTransform("pivot:rotor_main", Matrix.Identity, out var animatedRotor);
unanimatedMeshSet.TryGetPivotWorldTransform("pivot:rotor_main", Matrix.Identity, out var restingRotor);
Check(animatedRotor != restingRotor, "Rotor animation is isolated per helicopter instance");
Check(float.IsFinite(actualHeli.GroundOffset) && actualHeli.Height > 0, "Actual helicopter bounds provide valid landing offset");
// Height-aware damage: an aircraft and ground unit can share X/Z without sharing hits.
heli = SetupHelicopter();
heli.TakeOff(world);
FlyTicks(heli, 40);
var belowAircraft = Unit();
Field(belowAircraft, typeof(Unit), "<UnitId>k__BackingField", Guid.NewGuid());
Field(belowAircraft, typeof(Unit), "<Height>k__BackingField", 1f);
belowAircraft.SetPosition(new(heli.Position.X, 0, heli.Position.Z));
UnitList(units).Insert(0, belowAircraft);
host = new NetworkHost(transport, input, world);
Unit? ImpactTarget(Guid attacker, Vector3 point) => (Unit?)typeof(NetworkHost).GetMethod("FindImpactTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, new object[] { attacker, point });
Check(ImpactTarget(Guid.NewGuid(), heli.Position + Vector3.Up * (heli.Height * 0.5f)) == heli, "Aerial hit selects helicopter rather than unit below");
Check(ImpactTarget(heli.UnitId, belowAircraft.Position) == belowAircraft, "Helicopter ground attack cannot hit itself");
Check(ImpactTarget(belowAircraft.UnitId, belowAircraft.Position) is null, "Ground impact cannot hit an aircraft above it");

heli = SetupHelicopter();
pad = AddPad(new(25.5f, 0, 25.5f));
heli.ReturnToHelipad(world);
for (int i = 0; i < 200 && heli.FlightState != HelicopterFlightState.Landing; i++) heli.SimulateFlight(world, 0.1f);
Check(heli.FlightState == HelicopterFlightState.Landing, "Pad approach enters descent phase");
Field(heli, typeof(Helicopter), "<Fuel>k__BackingField", 0f);
FlyTicks(heli, 100);
Check(heli.IsLanded && heli.HitPoints > 0 && heli.AssignedHelipadId == pad.UnitId && heli.Fuel > 0, "Fuel exhaustion above reserved pad completes landing and refuels");

heli = SetupHelicopter();
heli.TakeOff(world);
FlyTicks(heli, 40);
for (int z = 0; z < 40; z++) for (int x = 0; x < 40; x++) grid.GetCell(x, z).IsBlocked = true;
Field(heli, typeof(Helicopter), "<Fuel>k__BackingField", 0f);
FlyTicks(heli, 100);
Check(heli.HitPoints == 0 && heli.IsLanded, "No safe ground and no fuel results in emergency touchdown failure rather than unlimited hovering");
var actualBounds = new MeshSet(Globals.MeshHandler.Meshes["heli-1"]).GetBounds();
Check(actualHeli.Width >= actualBounds.Max.X - actualBounds.Min.X && actualHeli.Length >= actualBounds.Max.Z - actualBounds.Min.Z, "Ground landing footprint includes the authored helicopter geometry");
var contactNames = new[] { "pivot:landing_contact_1", "pivot:landing_contact_2", "pivot:landing_contact_3" };
var contactPoints = contactNames.Select(name =>
{
    Check(actualMeshSet.TryGetPivotWorldPosition(name, Matrix.Identity, out Vector3 point), $"Actual {name} imports");
    return point;
}).ToArray();
Check(actualMeshSet.TryGetPivotWorldPosition("pivot:flight_center", Matrix.Identity, out Vector3 flightCenter), "Actual flight center pivot imports");
Quaternion landingAttitude = (Quaternion)typeof(Helicopter).GetField("_landingAttitude", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actualHeli)!;
var contactHeights = contactPoints.Select(point => Vector3.Transform(point, landingAttitude).Y).ToArray();
Check(contactHeights.Max() - contactHeights.Min() < 0.001f, "Three landing contacts define a common flat-ground attitude");
Check(Math.Abs(actualHeli.GroundOffset + contactHeights.Average()) < 0.001f, "Landing height places all three contacts on ground");
actualHeli.TakeOff(world);
actualHeli.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.1)));
Field(actualHeli, typeof(Helicopter), "_visualPitch", 0.2f);
Matrix tiltedWorld = actualHeli.GetWorldMatrix();
Vector3 transformedFlightCenter = Vector3.Transform(flightCenter, tiltedWorld);
Vector3 untiltedFlightCenter = Vector3.Transform(flightCenter,
    Matrix.CreateRotationY((float)typeof(Helicopter).GetField("_renderYaw", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actualHeli)!) *
    Matrix.CreateTranslation((Vector3)typeof(Helicopter).GetField("_renderPosition", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actualHeli)!));
Check(Vector3.Distance(transformedFlightCenter, untiltedFlightCenter) < 0.001f, "Flight tilt rotates the full model around flight center");

// Fixed host simulation poses are interpolated visually, while flight tilt reacts with spring damping.
heli = SetupHelicopter();
heli.TryReceiveGotoCommand(world, new(new(25.5f, 20.5f)));
var visualBeforeTick = heli.GetWorldMatrix().Translation;
heli.SimulateFlight(world, 0.1f);
Vector3 authoritativeAfterTick = heli.Position;
Check(heli.GetWorldMatrix().Translation == visualBeforeTick && authoritativeAfterTick != visualBeforeTick, "Host simulation queues a visual pose instead of jumping immediately");
heli.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.05)));
Vector3 visualHalfTick = heli.GetWorldMatrix().Translation;
Check(Vector3.Distance(visualHalfTick, visualBeforeTick) > 0 && Vector3.Distance(visualHalfTick, authoritativeAfterTick) > 0, "Render pose interpolates between fixed simulation ticks");
for (int i = 0; i < 20; i++)
{
    heli.SimulateFlight(world, 0.1f);
    for (int frame = 0; frame < 6; frame++) heli.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1.0 / 60)));
}
Check(Math.Abs(heli.VisualPitchDegrees) > 0.5f || Math.Abs(heli.VisualRollDegrees) > 0.5f, "Flight direction produces visible pitch or bank");
float tiltBeforeStop = Math.Abs(heli.VisualPitchDegrees) + Math.Abs(heli.VisualRollDegrees);
heli.Stop();
for (int frame = 0; frame < 120; frame++) heli.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1.0 / 60)));
Check(Math.Abs(heli.VisualPitchDegrees) + Math.Abs(heli.VisualRollDegrees) < tiltBeforeStop, "Hover damping settles flight tilt");
// Exercise the actual tank steering loop at different frame rates, including
// offset rear targets which straight reversing could never reach.
foreach (float step in new[] { 1f / 60, 0.1f, 0.25f })
foreach (Point destination in new[] { new Point(22, 18), new Point(22, 23), new Point(20, 19) })
{
    var steeringGrid = new GameGrid(48, 48, 1);
    steeringGrid.BindTerrain(Terrain(48, 48));
    Globals.World = World(steeringGrid);
    Tank tank = Empty<Tank>();
    Field(tank, typeof(Unit), "<Width>k__BackingField", 2);
    Field(tank, typeof(Unit), "<Length>k__BackingField", 4);
    Field(tank, typeof(MobileUnit), "<MovementProfile>k__BackingField", new GroundMovementProfile());
    Field(tank, typeof(MobileUnit), "_plannedPath", new List<Point> { destination });
    tank.SetTransform(Matrix.CreateTranslation(20.5f, 0, 20.5f));
    tank.MoveSpeed = 3;
    tank.RotationSpeed = 1;
    tank.WaypointArrivalRadius = 0.2f;
    tank.ReverseSpeed = 1.4f;
    tank.ReverseWithoutTurningDistance = 6;
    tank.TurnInPlaceDotThreshold = 0.995f;
    var move = typeof(Tank).GetMethod("MoveAlongPath", BindingFlags.Instance | BindingFlags.NonPublic)!;
    for (int frame = 0; frame < 20 / step && tank.PlannedPath.Count > 0; frame++)
        move.Invoke(tank, new object[] { new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(step)) });
    Check(tank.PlannedPath.Count == 0, $"Tank reaches offset waypoint {destination} at timestep {step}");
    Check(Vector3.Distance(tank.Position, new Vector3(destination.X + 0.5f, 0, destination.Y + 0.5f)) <= 0.2f,
        "Tank arrives without overshooting or oscillating");
}
var maskGrid = new GameGrid(40, 40, 1);
maskGrid.BindTerrain(Terrain(40, 40));
MobileUnit lShaped = Unit();
Field(lShaped, typeof(Unit), "<FootprintRegions>k__BackingField", new BoundingBox[]
{
    new(new Vector3(-1.4f, 0, -1.4f), new Vector3(-0.6f, 1, 1.4f)),
    new(new Vector3(-0.4f, 0, 0.6f), new Vector3(1.4f, 1, 1.4f))
});
IReadOnlyList<Point> lCells = maskGrid.GetFootprintCells(lShaped, new Vector3(20.5f, 0, 20.5f), 0);
Check(lCells.Contains(new Point(19, 20)) && lCells.Contains(new Point(20, 21)), "Authored footprint combines multiple regions");
Check(!lCells.Contains(new Point(20, 20)), "L-shaped footprint keeps its inner corner free");

// Authored building clearance remains traversable while preventing later building footprints.
var clearanceGrid = new GameGrid(30, 30, 1);
clearanceGrid.BindTerrain(Terrain(30, 30));
Globals.World = World(clearanceGrid);
Building clearanceA = Empty<Building>();
Field(clearanceA, typeof(Unit), "<Width>k__BackingField", 1);
Field(clearanceA, typeof(Unit), "<Length>k__BackingField", 1);
Field(clearanceA, typeof(Unit), "<FootprintRegions>k__BackingField", new BoundingBox[]
{
    new(new Vector3(-0.4f, 0, -0.4f), new Vector3(0.4f, 1, 0.4f))
});
Field(clearanceA, typeof(Unit), "<ClearanceRegions>k__BackingField", new BoundingBox[]
{
    new(new Vector3(1.6f, 0, -0.4f), new Vector3(2.4f, 1, 0.4f))
});
clearanceA.SetPosition(new Vector3(10.5f, 0, 10.5f));
Check(clearanceGrid.TryPlace(clearanceA, clearanceA.Position, 0), "Building registers authored clearance");
MobileUnit clearanceWalker = Unit();
Field(clearanceWalker, typeof(Unit), "<Width>k__BackingField", 1);
Field(clearanceWalker, typeof(Unit), "<Length>k__BackingField", 1);
Check(clearanceGrid.TryMove(clearanceWalker, new Point(12, 10)), "Units can traverse building clearance");
clearanceGrid.Remove(clearanceWalker);
Building clearanceCandidate = Empty<Building>();
Field(clearanceCandidate, typeof(Unit), "<Width>k__BackingField", 1);
Field(clearanceCandidate, typeof(Unit), "<Length>k__BackingField", 1);
Check(!clearanceGrid.CanPlace(clearanceCandidate, new Vector3(12.5f, 0, 10.5f), 0), "Clearance blocks a new building footprint");

// Mobile BBModel footprint is hard occupancy; mobile clearance is a soft avoidance reservation.
MobileUnit authoredVehicle = Unit(width: 3, length: 5);
Field(authoredVehicle, typeof(Unit), "<FootprintRegions>k__BackingField", new BoundingBox[]
{
    new(new Vector3(-0.4f, 0, -0.4f), new Vector3(0.4f, 1, 0.4f))
});
Field(authoredVehicle, typeof(Unit), "<ClearanceRegions>k__BackingField", new BoundingBox[]
{
    new(new Vector3(1.6f, 0, -0.4f), new Vector3(2.4f, 1, 0.4f))
});
authoredVehicle.SetPosition(new Vector3(15.5f, 0, 15.5f));
Check(clearanceGrid.TryMove(authoredVehicle, new Point(15, 15)) &&
      ReferenceEquals(clearanceGrid.GetOccupant(15, 15), authoredVehicle) &&
      clearanceGrid.GetOccupant(17, 15) is null,
    "Authored mobile footprint occupies only its hard BBModel core");
MobileUnit avoidanceProbe = Unit();
Check(clearanceGrid.CanPlace(avoidanceProbe, new Point(17, 15)) &&
      clearanceGrid.GetMovementCost(avoidanceProbe, new Point(17, 15)) >=
        GameGrid.MobileClearanceMovementCost,
    "Other units may cross mobile clearance but pathfinding strongly avoids it");
Building vehicleClearanceCandidate = Empty<Building>();
Field(vehicleClearanceCandidate, typeof(Unit), "<Width>k__BackingField", 1);
Field(vehicleClearanceCandidate, typeof(Unit), "<Length>k__BackingField", 1);
Check(!clearanceGrid.CanPlace(vehicleClearanceCandidate,
        new Vector3(17.5f, 0, 15.5f), 0),
    "Mobile clearance prevents a building from sealing a vehicle corridor");
MobileUnit fractionalClearanceVehicle = Unit(width: 3, length: 5);
Field(fractionalClearanceVehicle, typeof(Unit), "<ClearanceRegions>k__BackingField", new BoundingBox[]
{
    // Bulldozer-1.bbmodel: 3.2 x 4.8 world units, shifted 0.3 units backwards.
    new(new Vector3(-1.6f, 0, -2.7f), new Vector3(1.6f, 1, 2.1f))
});
fractionalClearanceVehicle.SetPosition(new Vector3(15.5f, 0, 15.5f));
IReadOnlyList<Point> fractionalClearance = clearanceGrid.GetMovementClearanceCells(
    fractionalClearanceVehicle, new Point(15, 15));
Check(fractionalClearance.Count == 15,
    "Fractional mobile clearance samples cell centers instead of inflating to every touched cell");
authoredVehicle.SetRotationYDegrees(90);
Check(clearanceGrid.TryUpdateFootprint(authoredVehicle) &&
      ReferenceEquals(clearanceGrid.GetOccupant(15, 15), authoredVehicle),
    "Rotating authored mobile clearance leaves the hard core footprint stable");
Point rotatedClearanceCell = clearanceGrid
    .GetMovementClearanceCells(authoredVehicle, new Point(15, 15))
    .First(cell => cell != new Point(15, 15));
Vector3 rotatedClearancePosition = clearanceGrid.ToWorldPosition(rotatedClearanceCell, 0);
Check(!clearanceGrid.CanPlace(vehicleClearanceCandidate,
        rotatedClearancePosition, 0),
    "Building placement follows a vehicle's rotated authored clearance");
Building clearanceB = Empty<Building>();
Field(clearanceB, typeof(Unit), "<Width>k__BackingField", 1);
Field(clearanceB, typeof(Unit), "<Length>k__BackingField", 1);
Field(clearanceB, typeof(Unit), "<FootprintRegions>k__BackingField", new BoundingBox[]
{
    new(new Vector3(-0.4f, 0, -0.4f), new Vector3(0.4f, 1, 0.4f))
});
Field(clearanceB, typeof(Unit), "<ClearanceRegions>k__BackingField", new BoundingBox[]
{
    new(new Vector3(-2.4f, 0, -0.4f), new Vector3(-1.6f, 1, 0.4f))
});
clearanceB.SetPosition(new Vector3(14.5f, 0, 10.5f));
Check(clearanceGrid.TryPlace(clearanceB, clearanceB.Position, 0), "Clearance areas may overlap");
clearanceGrid.Remove(clearanceA);
Check(!clearanceGrid.CanPlace(clearanceCandidate, new Vector3(12.5f, 0, 10.5f), 0), "Overlapping clearance keeps remaining owner");
clearanceGrid.Remove(clearanceB);
Check(clearanceGrid.CanPlace(clearanceCandidate, new Vector3(12.5f, 0, 10.5f), 0), "Removing buildings releases their clearance");

var hierarchyRoot = new MeshNode("root", Vector3.Zero);
var visibleNode = new MeshNode("body", Vector3.Zero);
visibleNode.SubMeshes.Add(new SubMesh("body",
    new[] { new VertexPositionColorNormalTexture(Vector3.Zero, Color.White, Vector3.Up, Vector2.Zero) },
    new[] { 0 }, Vector3.Zero));
hierarchyRoot.Children.Add(visibleNode);
var clearancePivot = new MeshNode("pivot:clearance", Vector3.Zero);
var clearanceRegion = new MeshNode("region", Vector3.Zero);
clearanceRegion.SubMeshes.Add(new SubMesh("clearance-region", new[]
{
    new VertexPositionColorNormalTexture(new Vector3(-1, 0, -1), Color.White, Vector3.Up, Vector2.Zero),
    new VertexPositionColorNormalTexture(new Vector3(1, 0, 1), Color.White, Vector3.Up, Vector2.Zero)
}, new[] { 0, 1 }, Vector3.Zero));
clearancePivot.Children.Add(clearanceRegion);
hierarchyRoot.Children.Add(clearancePivot);
var clearanceMesh = new Mesh("clearance-test", hierarchyRoot);
Check(clearanceMesh.ClearanceBounds.Count == 1, "Mesh extracts pivot:clearance geometry");
Check(clearanceMesh.SubMeshes.All(part => part.Name != "clearance-region"), "Clearance helper geometry is not rendered");

Globals.MeshHandler.Meshes["silo-1"] = Globals.MeshHandler.Meshes["barracks-1"];
Globals.MeshHandler.Meshes["tiberium-refinery-1"] = Globals.MeshHandler.Meshes["barracks-1"];
var storageSilo = new Silo(Vector3.Zero, Guid.NewGuid());
Check(Math.Abs(storageSilo.StoreResources(storageSilo.ResourceCapacity + 200) - storageSilo.ResourceCapacity) < 0.001f &&
    Math.Abs(storageSilo.StoredResources - storageSilo.ResourceCapacity) < 0.001f,
    "Silo accepts resources only up to its capacity");
var storageReplay = new Silo(Vector3.Zero, storageSilo.UnitId);
storageReplay.ApplyState(storageSilo.GetState());
Check(Math.Abs(storageReplay.StoredResources - storageReplay.ResourceCapacity) < 0.001f,
    "Silo fill level is included in building network state");
var resourceGrid = new GameGrid(4, 4, 1);
var resourceWorld = World(resourceGrid);
var resourceUnits = new UnitHandler();
Field(resourceWorld, typeof(GameWorld), "<Units>k__BackingField", resourceUnits);
Guid resourceArmyId = Guid.NewGuid();
var resourceArmy = new Army(resourceArmyId, Guid.NewGuid()) { Resources = 1000 };
var firstStorage = new Silo(Vector3.Zero, Guid.NewGuid());
firstStorage.AdvanceConstruction(firstStorage.TotalBuildingPointsNeeded);
Field(firstStorage, typeof(Unit), "<ArmyId>k__BackingField", resourceArmyId);
firstStorage.StoreResources(400);
var secondStorage = new TiberiumRefinery(Vector3.Zero, Guid.NewGuid());
secondStorage.AdvanceConstruction(secondStorage.TotalBuildingPointsNeeded);
Field(secondStorage, typeof(Unit), "<ArmyId>k__BackingField", resourceArmyId);
secondStorage.StoreResources(300);
UnitList(resourceUnits).Add(firstStorage);
UnitList(resourceUnits).Add(secondStorage);
Check(ArmyResourceService.TrySpend(resourceArmy, resourceWorld, 600) &&
      resourceArmy.Resources == 400 &&
      Math.Abs(firstStorage.StoredResources + secondStorage.StoredResources - 100) < 0.001f &&
      firstStorage.NetworkStateDirty && secondStorage.NetworkStateDirty,
    "Spending army resources drains owned refinery and silo storage");
var refinery = new TiberiumRefinery(Vector3.Zero, Guid.NewGuid());
refinery.AdvanceConstruction(refinery.TotalBuildingPointsNeeded);
var pricing = new PricingService(new ArmyHandler(), _ => null);
Check(EconomyCatalog.GetBasePrice(PurchasableType.Unit, "harvester") == 500 &&
      EconomyCatalog.GetBasePrice(PurchasableType.Unit, "helicopter") == 1200 &&
      pricing.GetQuote(new PurchaseRequest(PurchasableType.Unit, "harvester")).FinalPrice == 500 &&
      refinery.TryGetProductionDuration("harvester", out float harvesterProductionSeconds) &&
      MathF.Abs(harvesterProductionSeconds - 10.0f) < 0.001f &&
      refinery.Actions.Any(action => action.Type == UnitActionType.TrainUnit &&
          action.TargetObjectName == "harvester"),
    "Central pricing quotes harvester and helicopter prices");
var prerequisiteArmies = new ArmyHandler();
Guid prerequisiteArmyId = Guid.NewGuid();
Army prerequisiteArmy = prerequisiteArmies.EnsureArmy(prerequisiteArmyId, Guid.NewGuid());
var prerequisitePricing = new PricingService(prerequisiteArmies, _ => null);
PurchaseQuote lockedReactor = prerequisitePricing.GetQuote(new PurchaseRequest(
    PurchasableType.Building, "Reaktor", prerequisiteArmyId));
prerequisiteArmy.Perks.GrantPermanent(PerkType.BaseEstablished, Guid.NewGuid());
PurchaseQuote unlockedReactor = prerequisitePricing.GetQuote(new PurchaseRequest(
    PurchasableType.Building, "Reaktor", prerequisiteArmyId));
PurchaseQuote lockedHelipad = prerequisitePricing.GetQuote(new PurchaseRequest(
    PurchasableType.Building, "Helipad", prerequisiteArmyId));
PurchaseQuote airResearch = prerequisitePricing.GetQuote(new PurchaseRequest(
    PurchasableType.Research, ResearchProjects.AirTechnologyId, prerequisiteArmyId));
prerequisiteArmy.Perks.GrantPermanent(PerkType.AirTechnology, Guid.NewGuid());
PurchaseQuote unlockedHelipad = prerequisitePricing.GetQuote(new PurchaseRequest(
    PurchasableType.Building, "Helipad", prerequisiteArmyId));
Check(!lockedReactor.IsAvailable && lockedReactor.MissingPerks.Contains(PerkType.BaseEstablished) &&
      unlockedReactor.IsAvailable &&
      !lockedHelipad.IsAvailable && lockedHelipad.MissingPerks.Contains(PerkType.AirTechnology) &&
      airResearch is { IsAvailable: true, FinalPrice: 1000 } && unlockedHelipad.IsAvailable &&
      prerequisitePricing.GetQuote(new PurchaseRequest(
          PurchasableType.Building, "GDI-Base", prerequisiteArmyId)).IsAvailable,
    "Base and Air Technology perks unlock their central building requirements");
var researchBase = Empty<GDIBase>();
Check(researchBase.TryGetProductionDuration(ResearchProjects.AirTechnologyId,
          out float airResearchSeconds) && MathF.Abs(airResearchSeconds - 15.0f) < 0.001f,
    "Air Technology research takes 15 seconds in the GDI base");
NetworkMessage researchRequest = Wire(NetworkCommands.CreateResearchRequest(
    Guid.NewGuid(), Guid.NewGuid(), ResearchProjects.AirTechnologyId));
NetworkMessage researchCompleted = Wire(NetworkCommands.CreateResearchCompletedCommand(
    Guid.NewGuid(), prerequisiteArmyId, Guid.NewGuid(), ResearchProjects.AirTechnologyId));
Check(researchRequest.Type == NetworkMessageType.ResearchRequest &&
      researchCompleted.Type == NetworkMessageType.ResearchCompletedCommand &&
      researchCompleted.UnitTypeId == ResearchProjects.AirTechnologyId,
    "Research request and completion survive network serialization");
var completedBarracks = new GDIBarracks(Vector3.Zero, Guid.NewGuid());
completedBarracks.AdvanceConstruction(completedBarracks.TotalBuildingPointsNeeded);
Check(EconomyCatalog.GetBasePrice(PurchasableType.Unit, "gunner") == 100 &&
      EconomyCatalog.GetBasePrice(PurchasableType.Unit, "squad-leader") == 400 &&
      EconomyCatalog.GetBasePrice(PurchasableType.Unit, "medic") == 300 &&
      completedBarracks.Actions.Any(action => action.Type == UnitActionType.TrainUnit &&
          action.TargetObjectName == "gunner") &&
      completedBarracks.Actions.Any(action => action.Type == UnitActionType.TrainUnit &&
          action.TargetObjectName == "squad-leader") &&
      completedBarracks.TryGetProductionDuration("medic", out float medicProductionSeconds) &&
      Math.Abs(medicProductionSeconds - 5.0f) < 0.001f &&
      completedBarracks.Actions.Any(action => action.Type == UnitActionType.TrainUnit &&
          action.TargetObjectName == "medic"),
    "Barracks offers centrally priced basic soldiers, medics and squad leaders");
Globals.MeshHandler.Meshes["vehicle-factory-1"] = Globals.MeshHandler.Meshes["barracks-1"];
var completedVehicleFactory = new VehicleFactory(
    Vector3.Zero, Guid.NewGuid(), "vehicle-factory-1");
completedVehicleFactory.AdvanceConstruction(completedVehicleFactory.TotalBuildingPointsNeeded);
Check(completedVehicleFactory.TryGetProductionDuration("jeep", out float jeepProductionSeconds) &&
      Math.Abs(jeepProductionSeconds - 7.0f) < 0.001f &&
      completedVehicleFactory.TryQueueProduction(
          Guid.NewGuid(), "jeep", Guid.NewGuid(), jeepProductionSeconds) &&
      completedVehicleFactory.ProductionQueue.ActiveOrder?.UnitTypeId == "jeep",
    "Vehicle factory accepts Jeep production and starts its queue progress");
Guid refineryOwner = Guid.NewGuid();
Check(refinery.TryQueueIncludedHarvester(refineryOwner) &&
    refinery.ProductionQueue.ActiveOrder?.UnitTypeId == "harvester",
    "Completed refinery queues its included harvester");
Check(!refinery.TryQueueIncludedHarvester(refineryOwner),
    "Refinery grants its included harvester only once");
Globals.MeshHandler.Meshes["harvester-1"] = Globals.MeshHandler.Meshes["barracks-1"];
var returnHarvester = new Harvester(Vector3.Zero, Guid.NewGuid());
Check(returnHarvester.Actions.Any(action => action.Type == UnitActionType.ReturnToStorage),
    "Harvester exposes return and unload action");
var selectionLeader = Empty<SquadLeader>();
var assembleAction = selectionLeader.Actions.First(action => action.Type == UnitActionType.AssembleSquad);
Check(PlayerHandler.Recipients(new Unit[] { returnHarvester, selectionLeader, completedBarracks }, assembleAction)
        .SequenceEqual(new Unit[] { selectionLeader }),
    "Mixed selection sends squad assembly only to the eligible leader");
Check(PlayerHandler.SingleActor(assembleAction) &&
      !PlayerHandler.SingleActor(new UnitAction(UnitActionType.Goto, "Goto", 0, 0)),
    "Squad assembly requires one actor while movement supports multiple recipients");
Check(PlayerHandler.Recipients(new Unit[] { returnHarvester, completedBarracks },
        new UnitAction(UnitActionType.Harvest, "Harvest", 0, 0)).SequenceEqual(new Unit[] { returnHarvester }),
    "Mixed selection sends harvest only to harvesters");
var returnRequest = Wire(new NetworkMessage(NetworkMessageType.HarvesterReturnRequest,
    Guid.NewGuid(), UnitId: returnHarvester.UnitId));
Check(returnRequest.Type == NetworkMessageType.HarvesterReturnRequest &&
    returnRequest.UnitId == returnHarvester.UnitId,
    "Harvester return request survives network serialization");
returnHarvester.ReceiveCommand(new GotoCommand(new Vector2(12.5f, 14.5f)));
string harvesterDebug = returnHarvester.GetDebugCommandText();
Check(harvesterDebug.Contains("Harvest=Idle") && harvesterDebug.Contains("cargo=") &&
    harvesterDebug.Contains("Goto (") && harvesterDebug.Contains("path="),
    "Harvester debug text combines AI phase, cargo and movement command");

var exitTerrain = Terrain(50, 50);
var exitGrid = new GameGrid(50, 50, 1);
exitGrid.BindTerrain(exitTerrain);
var exitWorld = World(exitGrid);
Field(exitWorld, typeof(GameWorld), "_terrain", exitTerrain);
Globals.World = exitWorld;
Building rotatedFactory = Empty<Building>();
Field(rotatedFactory, typeof(Unit), "<Width>k__BackingField", 8);
Field(rotatedFactory, typeof(Unit), "<Length>k__BackingField", 6);
rotatedFactory.SetPosition(new Vector3(25.5f, 0, 25.5f));
rotatedFactory.SetRotationYDegrees(45);
Check(exitGrid.TryPlace(rotatedFactory, rotatedFactory.Position, 45), "Rotated production building placed for exit test");
MobileUnit producedVehicle = Unit(width: 3, length: 4);
Vector3 blockedExit = rotatedFactory.Position + Vector3.TransformNormal(Vector3.Forward,
    Matrix.CreateRotationY(MathHelper.ToRadians(45))) * 3.0f;
Check(!exitGrid.CanPlace(producedVehicle, blockedExit, 45), "Authored exit may overlap rotated building footprint");
Check(ProductionExitResolver.TryResolve(exitWorld, rotatedFactory, producedVehicle,
    rotatedFactory.Position, blockedExit, out Vector3 safeExit), "Production exit resolver finds exterior vehicle placement");
Check(exitGrid.TryMove(producedVehicle, exitGrid.ToCell(safeExit)),
    "Resolved production exit accepts complete rotated vehicle footprint");
exitGrid.Remove(producedVehicle);
producedVehicle.SetPosition(new Vector3(15.5f, 0, 25.5f));
Check(exitGrid.TryMove(producedVehicle, exitGrid.ToCell(producedVehicle.Position)),
    "Returning vehicle registered away from storage");
Check(ProductionExitResolver.TryResolve(exitWorld, rotatedFactory, producedVehicle,
    producedVehicle.Position, blockedExit, out Vector3 unloadApproach) &&
    new Pathfinder(exitWorld).TryFindPath(producedVehicle, producedVehicle.MovementProfile,
        new Vector2(unloadApproach.X, unloadApproach.Z), out _),
    "Returning vehicle receives reachable unload approach outside rotated storage footprint");

var clearTerrain = Terrain(20, 20);
var clearGrid = new GameGrid(20, 20, 1);
clearGrid.BindTerrain(clearTerrain);
var clearWorld = World(clearGrid);
Field(clearWorld, typeof(GameWorld), "_terrain", clearTerrain);
MobileUnit friendlyBlocker = Unit();
Check(clearGrid.TryMove(friendlyBlocker, new Point(8, 8)), "Friendly placement blocker registered");
Building clearPreview = Empty<Building>();
Field(clearPreview, typeof(Unit), "<Width>k__BackingField", 2);
Field(clearPreview, typeof(Unit), "<Length>k__BackingField", 2);
BuildingPlacement clearablePlacement = BuildingPlacement.Evaluate(clearWorld, clearPreview,
    new Vector3(8.5f, 0, 8.5f), 0, 1);
Check(clearablePlacement.TryGetMovableBlockers(clearGrid, _ => true, out MobileUnit[] clearBlockers) &&
    clearBlockers.SequenceEqual(new[] { friendlyBlocker }),
    "Placement occupied only by controllable units is clearable");
Check(!clearablePlacement.TryGetMovableBlockers(clearGrid, _ => false, out _),
    "Uncontrolled unit keeps building placement invalid");
var mixedPlacement = new BuildingPlacement(
    [new PlacementCell(new Point(8, 8), PlacementIssue.Occupied | PlacementIssue.Blocked, 0, 0)], 0);
Check(!mixedPlacement.TryGetMovableBlockers(clearGrid, _ => true, out _),
    "Move away is not offered when placement has additional terrain problems");
MobileUnit clearanceBlocker = Unit();
Field(clearanceBlocker, typeof(Unit), "<ClearanceRegions>k__BackingField", new BoundingBox[]
{
    new(new Vector3(0.6f, 0, -0.4f), new Vector3(1.4f, 1, 0.4f))
});
Check(clearGrid.TryMove(clearanceBlocker, new Point(7, 8)),
    "Mobile clearance blocker registered beside occupied footprint cell");
var combinedMobilePlacement = new BuildingPlacement(
    [new PlacementCell(new Point(8, 8), PlacementIssue.Occupied | PlacementIssue.Reserved, 0, 0)], 0);
Check(combinedMobilePlacement.TryGetMovableBlockers(clearGrid, _ => true,
        out MobileUnit[] combinedBlockers) &&
      combinedBlockers.ToHashSet().SetEquals(new[] { friendlyBlocker, clearanceBlocker }) &&
      BuildingPlacement.IsMovableBlocker(combinedMobilePlacement.Cells[0], clearGrid, _ => true),
    "Footprint cells occupied and reserved by controllable mobile units are shown as clearable");
clearGrid.Remove(clearanceBlocker);
var spacingUnits = new UnitHandler();
Field(clearWorld, typeof(GameWorld), "<Units>k__BackingField", spacingUnits);
Building existingDepot = new(new Vector3(5.5f, 0, 5.5f), Guid.NewGuid());
Building depotPreview = new(Vector3.Zero, Guid.NewGuid());
foreach (Building depot in new[] { existingDepot, depotPreview })
{
    Field(depot, typeof(Unit), "<Width>k__BackingField", 1);
    Field(depot, typeof(Unit), "<Length>k__BackingField", 1);
}
UnitList(spacingUnits).Add(existingDepot);
MethodInfo spacingCheck = typeof(ArmyGoalController).GetMethod(
    "HasBuildingSpacing", BindingFlags.Static | BindingFlags.NonPublic)!;
bool crowdedDepot = (bool)spacingCheck.Invoke(null,
    [clearWorld, depotPreview, new Vector3(8.5f, 0, 5.5f), 0.0f,
        ArmyGoalController.MinimumBuildingSpacingCells])!;
bool accessibleDepot = (bool)spacingCheck.Invoke(null,
    [clearWorld, depotPreview, new Vector3(9.5f, 0, 5.5f), 0.0f,
        ArmyGoalController.MinimumBuildingSpacingCells])!;
Check(!crowdedDepot && accessibleDepot,
    "AI building sites retain a Harvester-wide vehicle access corridor");

GameplayDefinition? catalogTank = GameplayCatalog.Find(PurchasableType.Unit, "tank");
Check(catalogTank is { BasePrice: 1000 } &&
    catalogTank.Producers.Any(producer => producer.TypeId == "vehicle-factory" &&
        Math.Abs(producer.ProductionSeconds - 10.0f) < 0.001f) &&
    catalogTank.AI?.Roles.HasFlag(AIUnitRole.AntiVehicle) == true,
    "Gameplay catalog shares tank economy, production and AI metadata");
Check(GameplayCatalog.TryGetProductionDuration("vehicle-factory", PurchasableType.Unit,
        "tank", out float catalogTankSeconds) && Math.Abs(catalogTankSeconds - 10.0f) < 0.001f &&
    !GameplayCatalog.TryGetProductionDuration("gdi-barracks", PurchasableType.Unit,
        "tank", out _),
    "Gameplay catalog validates products against their producer list");
Check(GameplayCatalog.CreateProductionActions("gdi-barracks")
        .Any(action => action.Type == UnitActionType.TrainUnit && action.TargetObjectName == "gunner") &&
    GameplayCatalog.CreateResearchActions("gdi-base")
        .Any(action => action.Type == UnitActionType.Research &&
            action.TargetObjectName == ResearchProjects.AirTechnologyId) &&
    GameplayCatalog.Find(PurchasableType.Unit, "heli")?.TypeId == "helicopter",
    "Production UI actions and aliases come from the gameplay catalog");
BuildingMetadata? refineryMetadata = GameplayCatalog.Find(
    PurchasableType.Building, "tiberium-refinery")?.Building;
BuildingMetadata? reactorMetadata = GameplayCatalog.Find(
    PurchasableType.Building, "reaktor")?.Building;
BuildingMetadata? communicationsMetadata = GameplayCatalog.Find(
    PurchasableType.Building, "communicationstower")?.Building;
Check(communicationsMetadata?.VisionRange == 50,
    "Communications tower exposes its strategic vision range through the gameplay catalog");
Check(refineryMetadata is
        { MaxHitPoints: 2500, ConstructionPoints: 4500, PowerConsumption: 30,
          ResourceCapacity: 5000 } &&
    reactorMetadata is
        { MaxHitPoints: 500, ConstructionPoints: 500, PowerProduction: 100 },
    "Gameplay catalog owns building health, construction, power and storage values");
GameplayDefinition? antiVehicleChoice = AIUnitSelector.SelectBest("vehicle-factory",
    new AIProductionNeed(AIUnitRole.Attacker, AIMovementDomain.GroundVehicle,
        AntiVehicle: 1.0f, Defense: 0.5f));
GameplayDefinition? scoutVehicleChoice = AIUnitSelector.SelectBest("vehicle-factory",
    new AIProductionNeed(AIUnitRole.Attacker, AIMovementDomain.GroundVehicle,
        Mobility: 0.5f, Scouting: 1.0f));
Check(antiVehicleChoice?.TypeId == "tank" && scoutVehicleChoice?.TypeId == "jeep",
    "AI unit selector chooses different factory products for combat needs");
Check(AIUnitSelector.SelectBest("gdi-barracks",
        new AIProductionNeed(AIUnitRole.Attacker, AIMovementDomain.GroundVehicle)) is null,
    "AI unit selector respects producer and movement requirements");
Check(AIUnitSelector.SelectBest("gdi-barracks",
        new AIProductionNeed(AIUnitRole.Leader, AIMovementDomain.Infantry))?.TypeId == "squad-leader" &&
    AIUnitSelector.SelectBest("gdi-barracks",
        new AIProductionNeed(AIUnitRole.Healer, AIMovementDomain.Infantry))?.TypeId == "medic" &&
    AIUnitSelector.SelectBest("gdi-barracks",
        new AIProductionNeed(AIUnitRole.Attacker | AIUnitRole.AntiInfantry,
            AIMovementDomain.Infantry, AntiInfantry: 1.0f))?.TypeId == "gunner" &&
    AIUnitSelector.SelectBest("gdi-barracks",
        new AIProductionNeed(AIUnitRole.Attacker | AIUnitRole.AntiVehicle,
            AIMovementDomain.Infantry, AntiVehicle: 1.0f))?.TypeId == "rak-zero",
    "AI squad roles select leader, healer and matching infantry from barracks metadata");
Check(AIUnitSelector.SelectBest("gdi-barracks",
        new AIProductionNeed(AIUnitRole.Defender | AIUnitRole.AntiInfantry,
            AIMovementDomain.Infantry, AntiInfantry: 1.0f))?.TypeId == "gunner" &&
    GameplayCatalog.HasAIRoles("gunner", AIUnitRole.Scout | AIUnitRole.Defender),
    "AI base defense and scouting use catalog roles instead of the Gunner class");
Helicopter airborneThreat = Empty<Helicopter>();
Field(airborneThreat, typeof(Helicopter), "<FlightState>k__BackingField",
    HelicopterFlightState.Flying);
AIThreatSnapshot mixedThreat = AIThreatAssessment.FromEnemies(
    [Empty<Gunner>(), Empty<Tank>(), airborneThreat]);
Check(mixedThreat.AntiInfantryNeed > AIThreatSnapshot.Baseline.AntiInfantryNeed &&
      mixedThreat.AntiVehicleNeed > AIThreatSnapshot.Baseline.AntiVehicleNeed &&
      mixedThreat.AntiAirNeed > AIThreatSnapshot.Baseline.AntiAirNeed,
    "AI threat assessment distinguishes infantry, vehicle and air observations");
Guid lossArmy = Guid.NewGuid();
MobileUnit lostUnit = Unit();
Field(lostUnit, typeof(Unit), "<ArmyId>k__BackingField", (Guid?)lossArmy);
var lossAssessment = new AIThreatAssessment(lossArmy);
lossAssessment.RecordLoss(lostUnit, Empty<Tank>(), AICombatContext.BaseDefense);
Check(lossAssessment.RecordedLosses == 1 &&
      lossAssessment.LastLossContext == AICombatContext.BaseDefense,
    "AI loss memory retains the combat context of an own casualty");
GameplayDefinition? catalogAirDefense = AIUnitSelector.SelectBest(
    "gdi-bulldozer",
    new AIProductionNeed(AIUnitRole.Defender | AIUnitRole.AntiAir,
        AIMovementDomain.Static, AntiAir: 1.0f),
    productType: PurchasableType.Building);
Check(catalogAirDefense is { TypeId: "turret-minigun" } &&
      catalogAirDefense.Building?.PowerConsumption == 20 &&
      catalogAirDefense.AI?.AntiAir > 0.8f,
    "AI defense planner discovers powered Gatling defense through catalog metadata");
Check(AIDefensePlanner.DesiredDefenseCount(0.5f) == 0 &&
      AIDefensePlanner.DesiredDefenseCount(0.55f) == 1 &&
      AIDefensePlanner.DesiredDefenseCount(1.30f) == 2 &&
      AIDefensePlanner.DesiredDefenseCount(5.0f) == AIDefensePlanner.MaximumAirDefenses,
    "Adaptive air-defense count scales with threat and remains bounded");
GameplayDefinition plannedTurret = GameplayCatalog.Find(
    PurchasableType.Building, "turret-minigun")!;
AIProductionPlan turretPlan = AIProductionPlanner.CreatePlan(
    plannedTurret, ["gdi-bulldozer", "gdi-base"], [PerkType.BaseEstablished],
    currentPowerBalance: 0);
Check(turretPlan.IsValid &&
      turretPlan.Steps.Select(step => step.TypeId).SequenceEqual(["reaktor", "turret-minigun"]),
    "Production planner inserts power before an underpowered defense building");
GameplayDefinition plannedHelipad = GameplayCatalog.Find(PurchasableType.Building, "helipad")!;
AIProductionPlan helipadPlan = AIProductionPlanner.CreatePlan(
    plannedHelipad, ["gdi-bulldozer", "gdi-base"], [PerkType.BaseEstablished],
    currentPowerBalance: 100);
Check(helipadPlan.IsValid && helipadPlan.Steps.Count == 2 &&
      helipadPlan.Steps[0] is
        { Kind: AIProductionPlanStepKind.Research, TypeId: ResearchProjects.AirTechnologyId } &&
      helipadPlan.Steps[1] is
        { Kind: AIProductionPlanStepKind.BuildBuilding, TypeId: "helipad" },
    "Production planner resolves research before a perk-gated building");
GameplayDefinition plannedTank = GameplayCatalog.Find(PurchasableType.Unit, "tank")!;
AIProductionPlan tankPlan = AIProductionPlanner.CreatePlan(
    plannedTank, ["gdi-bulldozer"], [PerkType.BaseEstablished], currentPowerBalance: 100);
Check(tankPlan.IsValid &&
      tankPlan.Steps.Select(step => step.TypeId).SequenceEqual(["vehicle-factory", "tank"]),
    "Production planner resolves a missing producer before training a unit");
BuildingMetadata reactorCrewMetadata = GameplayCatalog.Find(
    PurchasableType.Building, "reaktor")!.Building!;
Check(reactorCrewMetadata.CrewCapacity == 4 &&
      reactorCrewMetadata.PowerProductionPerCrew == 30 &&
      GameplayCatalog.HasAIRoles("engineer", AIUnitRole.Crew),
    "Reactor crew capacity, power bonus and Engineer capability share catalog metadata");
Guid crewArmy = Guid.NewGuid();
var crewWorld = Empty<GameWorld>();
var crewUnits = new UnitHandler();
Field(crewWorld, typeof(GameWorld), "<Units>k__BackingField", crewUnits);
var crewReactor = Empty<Reaktor>();
Field(crewReactor, typeof(Unit), "<ArmyId>k__BackingField", (Guid?)crewArmy);
crewReactor.TotalBuildingPointsNeeded = 1;
Field(crewReactor, typeof(Building), "<ConstructionProgress>k__BackingField", 1.0f);
var crewReactorOccupancy = new OccupancyComponent(crewReactor,
    [new OccupantSlot(OccupantRole.Crew, 4, CanOccupy: unit => unit.IsCrewMember)],
    OccupancyOwnershipMode.CaptureOnEntry);
crewReactorOccupancy.EntryEnabled = true;
Field(crewReactor, typeof(Unit), "<Occupancy>k__BackingField", crewReactorOccupancy);
var availableEngineer = Empty<Engineer>();
Field(availableEngineer, typeof(Unit), "<ArmyId>k__BackingField", (Guid?)crewArmy);
Field(availableEngineer, typeof(Unit), "<IsCrewMember>k__BackingField", true);
UnitList(crewUnits).AddRange([crewReactor, availableEngineer]);
AIPowerSolution assignCrew = AICrewPowerPlanner.Evaluate(crewWorld, crewArmy, 25);
Check(assignCrew.Kind == AIPowerSolutionKind.AssignCrew && assignCrew.PowerGain == 30,
    "Power planner assigns an available Engineer before constructing another reactor");
UnitList(crewUnits).Remove(availableEngineer);
var crewBarracks = Empty<GDIBarracks>();
Field(crewBarracks, typeof(Unit), "<ArmyId>k__BackingField", (Guid?)crewArmy);
crewBarracks.TotalBuildingPointsNeeded = 1;
Field(crewBarracks, typeof(Building), "<ConstructionProgress>k__BackingField", 1.0f);
Field(crewBarracks, typeof(Building), "<ProductionQueue>k__BackingField", new ProductionQueue());
UnitList(crewUnits).Add(crewBarracks);
AIPowerSolution trainCrew = AICrewPowerPlanner.Evaluate(crewWorld, crewArmy, 30);
Check(trainCrew is { Kind: AIPowerSolutionKind.TrainCrew, CrewTypeId: "engineer", PowerGain: 30 },
    "Power planner trains catalog-selected crew for a small remaining deficit");
Check(AICrewPowerPlanner.Evaluate(crewWorld, crewArmy, 31).Kind == AIPowerSolutionKind.BuildPower,
    "Power planner prefers a reactor when one new Engineer cannot cover the deficit");
Guid plannedCrewId = Guid.NewGuid();
Guid plannedReactorId = Guid.NewGuid();
AIProductionPlanStep crewPlanStep = AIProductionPlanStep.AssignCrew(
    plannedCrewId, plannedReactorId);
Check(crewPlanStep is
    {
        Kind: AIProductionPlanStepKind.AssignCrew,
        UnitId: not null,
        TargetUnitId: not null,
        OccupantRole: RTS.OccupantRole.Crew
    } && crewPlanStep.UnitId == plannedCrewId && crewPlanStep.TargetUnitId == plannedReactorId,
    "Production plans can express a concrete crew assignment");
var planExecutor = Empty<AIProductionPlanExecutor>();
var executorPlan = new AIProductionPlan([new AIProductionPlanStep(
    AIProductionPlanStepKind.TrainUnit, "tank", "vehicle-factory")]);
Check(planExecutor.Start(executorPlan) && planExecutor.IsBusy &&
      planExecutor.State == AIPlanExecutionState.InProgress &&
      planExecutor.CurrentStep?.TypeId == "tank" &&
      !planExecutor.Start(executorPlan),
    "Production plan executor owns one active plan and exposes its current step");
planExecutor.Reset();
Check(!planExecutor.IsBusy && planExecutor.State == AIPlanExecutionState.Idle &&
      planExecutor.CurrentStep is null,
    "Production plan executor resets its lifecycle state");
// Search-local caching must preserve custom profiles and see world changes on the next request.
var cacheGrid = new GameGrid(20, 20, 1);
var cacheWorld = World(cacheGrid);
var cacheFinder = new Pathfinder(cacheWorld);
var cacheUnit = Unit();
var countedProfile = new CountingMovementProfile();
long callsBefore = Globals.Telemetry.TryFindPath_Calls;
Check(cacheFinder.TryFindPathFrom(cacheUnit, countedProfile, new Point(1, 1),
    new Vector2(18.5f, 18.5f), out var cachedRoute), "Direct host search finds a route");
Check(Globals.Telemetry.TryFindPath_Calls == callsBefore + 1,
    "Direct host searches contribute exactly once to telemetry");
Check(countedProfile.Visits.Where(pair => pair.Key != new Point(1, 1) && pair.Key != new Point(18, 18))
    .All(pair => pair.Value == 1), "A search checks each intermediate footprint at most once");
cacheGrid.GetCell(18, 18).IsBlocked = true;
Check(!cacheFinder.TryFindPathFrom(cacheUnit, countedProfile, new Point(1, 1),
    new Vector2(18.5f, 18.5f), out _), "Search caches never hide new world obstacles");
var advancingUnit = Mobile(Vector3.Zero);
var advanceMethod = typeof(AISquadAssaultController).GetMethod("NeedsAdvanceOrder",
    BindingFlags.Static | BindingFlags.NonPublic)!;
bool NeedsAdvance(Vector3 target) => (bool)advanceMethod.Invoke(null, [advancingUnit, target])!;
Check(NeedsAdvance(new Vector3(1000, 0, 1000)), "Idle attackers outside range request movement");
Check(!NeedsAdvance(Vector3.Zero), "Attackers within range avoid redundant pathfinding");
advancingUnit.ReceiveCommand(new GotoCommand(new Vector2(1000, 1000)));
Check(!NeedsAdvance(new Vector3(1000, 0, 1000)), "Combat refresh preserves a pending movement order");
var zeroSizeUnit = Mobile(new Vector3(3.5f, 0, 3.5f));
Field(zeroSizeUnit, typeof(Unit), "<Width>k__BackingField", 0);
Field(zeroSizeUnit, typeof(Unit), "<Length>k__BackingField", 0);
var terrainTransformMethod = typeof(MobileUnit).GetMethod("CreateTerrainTransform", BindingFlags.Instance | BindingFlags.NonPublic)!;
Matrix safeTerrainTransform = (Matrix)terrainTransformMethod.Invoke(zeroSizeUnit, [Terrain(10, 10)])!;
Check(float.IsFinite(safeTerrainTransform.Determinant()) && Math.Abs(safeTerrainTransform.Determinant() - 1) < 0.001f,
    "Missing vehicle dimensions do not produce a NaN transform after factory exit");
var jeep = new Jeep(Vector3.Zero, Guid.NewGuid());
Check(jeep.Width > 0 && jeep.Length > 0 && jeep.Height > 0,
    "Jeep derives valid dimensions from its mesh for grid occupancy and terrain alignment");
// Navigation integration: stalled routes retain their task and Shift queue, then recover.
GameWorld MovementWorld()
{
    var navigationGrid = new GameGrid(24, 24, 1);
    var navigationTerrain = Terrain(24, 24);
    navigationGrid.BindTerrain(navigationTerrain);
    var navigationWorld = World(navigationGrid);
    Field(navigationWorld, typeof(GameWorld), "_terrain", navigationTerrain);
    Field(navigationWorld, typeof(GameWorld), "<Units>k__BackingField", new UnitHandler());
    Field(navigationWorld, typeof(GameWorld), "<PathfindingManager>k__BackingField", new PathfindingManager(navigationWorld));
    return navigationWorld;
}
var previousMovementWorld = Globals.World;
var navigationWorld = MovementWorld();
Globals.World = navigationWorld;
var waitingVehicle = Mobile(new Vector3(2.5f, 0, 2.5f));
var temporaryBlocker = Mobile(new Vector3(3.5f, 0, 2.5f));
waitingVehicle.MoveSpeed = 2;
navigationWorld.GameGrid.TryMove(waitingVehicle, new Point(2, 2));
navigationWorld.GameGrid.TryMove(temporaryBlocker, new Point(3, 2));
waitingVehicle.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(3.5f, 2.5f)), route: [new Point(3, 2)]);
waitingVehicle.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(6.5f, 2.5f)),
    appendToQueue: true, route: [new Point(4, 2), new Point(5, 2), new Point(6, 2)]);
for (int frame = 0; frame < 80; frame++)
{
    waitingVehicle.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.05)));
    navigationWorld.PathfindingManager.Update();
}
Check(waitingVehicle.CurrentCommand?.Target == new Vector2(3.5f, 2.5f) &&
    waitingVehicle.LastQueuedTarget == new Vector2(6.5f, 2.5f) &&
    waitingVehicle.MovementStatus == MovementStatus.Blocked,
    "Failed replanning preserves both the current task and Shift queue");
long blockedSearchesBefore = Globals.Telemetry.TryFindPath_Calls;
for (int frame = 0; frame < 400; frame++)
{
    waitingVehicle.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.05)));
    navigationWorld.PathfindingManager.Update();
}
Check(Globals.Telemetry.TryFindPath_Calls - blockedSearchesBefore <= 7,
    "Permanent obstacles use bounded backoff instead of per-frame searches");
navigationWorld.GameGrid.Remove(temporaryBlocker);
for (int frame = 0; frame < 400 && waitingVehicle.CurrentCommand is not null; frame++)
{
    waitingVehicle.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.05)));
    navigationWorld.PathfindingManager.Update();
}
Check(waitingVehicle.CurrentCommand is null && navigationWorld.GameGrid.ToCell(waitingVehicle.Position) == new Point(6, 2),
    "Removing a blocker resumes travel and executes the preserved next order");

navigationWorld = MovementWorld();
Globals.World = navigationWorld;
var slowTurningVehicle = Mobile(new Vector3(5.5f, 0, 5.5f));
slowTurningVehicle.CanOnlyMoveForward = true;
slowTurningVehicle.CanTurnInPlace = true;
slowTurningVehicle.RotationSpeed = 0.25f;
slowTurningVehicle.MoveSpeed = 1;
navigationWorld.GameGrid.TryMove(slowTurningVehicle, new Point(5, 5));
slowTurningVehicle.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(5.5f, 7.5f)),
    route: [new Point(5, 6), new Point(5, 7)]);
for (int frame = 0; frame < 60; frame++)
    slowTurningVehicle.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.05)));
Check(slowTurningVehicle.CurrentCommand is not null && navigationWorld.PathfindingManager.PendingRequests == 0 &&
    slowTurningVehicle.MovementStatus == MovementStatus.FollowingRoute,
    "Turning deliberately for more than two seconds is not mistaken for a stall");
for (int frame = 0; frame < 500 && slowTurningVehicle.CurrentCommand is not null; frame++)
    slowTurningVehicle.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.05)));
Check(slowTurningVehicle.CurrentCommand is null && navigationWorld.GameGrid.ToCell(slowTurningVehicle.Position) == new Point(5, 7),
    "A slowly turning vehicle completes its route after aligning");

foreach (float step in new[] { 1f / 60, 0.1f, 0.25f })
{
    navigationWorld = MovementWorld();
    Globals.World = navigationWorld;
    var cornerVehicle = Mobile(new Vector3(2.2f, 0, 2.1f));
    cornerVehicle.SetRotationYDegrees(-90);
    cornerVehicle.CanOnlyMoveForward = true;
    cornerVehicle.CanTurnInPlace = true;
    cornerVehicle.MoveSpeed = 3;
    cornerVehicle.RotationSpeed = 2;
    navigationWorld.GameGrid.GetCell(6, 3).IsBlocked = true;
    navigationWorld.GameGrid.GetCell(6, 4).IsBlocked = true;
    navigationWorld.GameGrid.TryMove(cornerVehicle, new Point(2, 2));
    Point[] cornerRoute = [new(3, 2), new(4, 2), new(5, 2), new(5, 3), new(5, 4), new(5, 5), new(6, 5), new(7, 5)];
    cornerVehicle.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(7.5f, 5.5f)), route: cornerRoute);
    bool stayedClear = true;
    for (int frame = 0; frame < 30 / step && cornerVehicle.CurrentCommand is not null; frame++)
    {
        cornerVehicle.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(step)));
        navigationWorld.PathfindingManager.Update();
        stayedClear &= !navigationWorld.GameGrid.GetCell(navigationWorld.GameGrid.ToCell(cornerVehicle.Position)).IsBlocked;
    }
    Check(stayedClear && cornerVehicle.CurrentCommand is null &&
        navigationWorld.GameGrid.ToCell(cornerVehicle.Position) == new Point(7, 5),
        $"Route lookahead negotiates building corners at timestep {step} without cutting obstacles");
}

// Host navigation snapshots restore an authoritative route, its progress and the queue.
navigationWorld = MovementWorld();
Globals.World = navigationWorld;
var routeOwner = Mobile(new Vector3(2.5f, 0, 2.5f));
routeOwner.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(5.5f, 2.5f)),
    route: [new(3, 2), new(4, 2), new(5, 2)]);
routeOwner.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(8.5f, 2.5f)), true,
    [new(6, 2), new(7, 2), new(8, 2)]);
UnitState routeSnapshot = routeOwner.GetState();
var routeReplica = new MobileUnit(new Vector3(2.25f, 0, 2.5f), routeOwner.UnitId);
Field(routeReplica, typeof(Unit), "<Width>k__BackingField", 1);
Field(routeReplica, typeof(Unit), "<Length>k__BackingField", 1);
navigationWorld.GameGrid.TryMove(routeReplica, new Point(2, 2));
routeReplica.ApplyState(routeSnapshot);
Check(routeReplica.Position == routeOwner.Position && routeReplica.PlannedPath.SequenceEqual(routeOwner.PlannedPath) &&
    routeReplica.LastQueuedTarget == new Vector2(8.5f, 2.5f),
    "A serialized navigation snapshot carries the shared route and queued destinations");
var visualMatrixMethod = typeof(MobileUnit).GetMethod("GetVisualWorldMatrix", BindingFlags.Instance | BindingFlags.NonPublic)!;
Check(Math.Abs(((Matrix)visualMatrixMethod.Invoke(routeReplica, null)!).Translation.X - 2.25f) < 0.001f &&
    navigationWorld.GameGrid.GetOccupant(new Point(2, 2)) == routeReplica,
    "Small network corrections update the logical grid immediately and preserve the current render position");
routeOwner.SetPosition(new Vector3(3.5f, 0, 2.5f));
typeof(MobileUnit).GetMethod("CompleteWaypoint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(routeOwner, null);
UnitState progressSnapshot = (UnitState)typeof(MobileUnit).GetMethod("GetMovementState", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(routeOwner, [false])!;
Check(JsonSerializer.Deserialize<MobileUnitState>(progressSnapshot.Payload, NetworkJson.Options)!.Navigation is null,
    "Routine position updates omit unchanged route and queue arrays");
routeReplica.ApplyState(progressSnapshot);
Check(routeReplica.PlannedPath.SequenceEqual([new Point(4, 2), new Point(5, 2)]),
    "Position-only progress snapshots prevent clients from chasing old waypoints after correction");

// A replacement for the same destination invalidates an older pending search.
routeReplica.Stop();
routeReplica.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(10.5f, 2.5f)));
int oldRequestId = routeReplica._pathRequestId;
routeReplica.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(10.5f, 2.5f)), route: [new(4, 2)]);
navigationWorld.PathfindingManager.Update();
Check(routeReplica._pathRequestId != oldRequestId && routeReplica.PlannedPath.SequenceEqual([new Point(4, 2)]),
    "Older searches cannot overwrite a newer route to the same destination");
routeReplica.Stop();
Check(routeReplica.CurrentCommand is null && routeReplica.MovementStatus == MovementStatus.Idle && routeReplica.PlannedPath.Count == 0,
    "Explicit Stop still cancels movement and recovery");
NetworkMessage coordinateMessage = new(NetworkMessageType.GotoCommand, Guid.NewGuid(),
    Routes: [new UnitRoute(routeOwner.UnitId, [new Point(7, 11), new Point(8, 12)], 8.5f, 12.5f)]);
NetworkMessage coordinateReplay = JsonSerializer.Deserialize<NetworkMessage>(
    JsonSerializer.Serialize(coordinateMessage, NetworkJson.Options), NetworkJson.Options)!;
Check(coordinateReplay.Routes![0].Cells.SequenceEqual(coordinateMessage.Routes![0].Cells),
    "Network Goto JSON preserves every nonzero cell coordinate");
bool rejectedEmptyCell = false;
try { JsonSerializer.Deserialize<Point>("{}", NetworkJson.Options); }
catch (JsonException) { rejectedEmptyCell = true; }
Check(rejectedEmptyCell, "Missing route coordinates are rejected instead of silently becoming origin cells");

// Exercise the actual connected-client branch without a graphics device or game server.
var authorityNetwork = Globals.Game.Network;
var clientSocketListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
clientSocketListener.Start();
using (var clientTransport = new System.Net.Sockets.TcpClient())
using (var clientNetwork = new NetworkHandler("MovementReplicaTest"))
{
    var acceptedSocket = clientSocketListener.AcceptTcpClientAsync();
    await clientTransport.ConnectAsync(System.Net.IPAddress.Loopback,
        ((System.Net.IPEndPoint)clientSocketListener.LocalEndpoint).Port);
    using var serverTransport = await acceptedSocket;
    Field(clientNetwork, typeof(NetworkHandler), "_serverConnection", clientTransport);
    Field(clientNetwork, typeof(NetworkHandler), "_sessionAccepted", true);
    Field(Globals.Game, typeof(RTSGame), "<Network>k__BackingField", clientNetwork);
    try
    {
        navigationWorld = MovementWorld();
        Globals.World = navigationWorld;
        var connectedReplica = new MobileUnit(new Vector3(2.25f, 0, 2.5f), routeOwner.UnitId);
        Field(connectedReplica, typeof(Unit), "<Width>k__BackingField", 1);
        Field(connectedReplica, typeof(Unit), "<Length>k__BackingField", 1);
        connectedReplica.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(5.5f, 2.5f)));
        Check(navigationWorld.PathfindingManager.PendingRequests == 0,
            "A connected client waits for a host route instead of starting a local search");
        long clientSearchesBefore = Globals.Telemetry.TryFindPath_Calls;
        connectedReplica.ApplyState(routeSnapshot);
        Check(connectedReplica.PlannedPath.SequenceEqual([new Point(3, 2), new Point(4, 2), new Point(5, 2)]),
            "Connected clients receive the exact authoritative route");
        connectedReplica.ApplyState(progressSnapshot);
        Check(connectedReplica.PlannedPath.SequenceEqual([new Point(4, 2), new Point(5, 2)]),
            "Client route cursor follows authoritative progress");
        for (int frame = 0; frame < 100; frame++)
        {
            connectedReplica.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.05)));
            navigationWorld.PathfindingManager.Update();
        }
        Check(Globals.Telemetry.TryFindPath_Calls == clientSearchesBefore &&
            connectedReplica.CurrentCommand?.Target == new Vector2(5.5f, 2.5f) &&
            connectedReplica.LastQueuedTarget == new Vector2(8.5f, 2.5f),
            "Prediction cannot independently replan or advance the queued order before the host");
        Check(Vector3.Distance(((Matrix)visualMatrixMethod.Invoke(connectedReplica, null)!).Translation,
            connectedReplica.Position) < 0.001f,
            "Visual correction converges without changing authoritative navigation");
        var clientSite = Empty<Building>();
        connectedReplica.TryReceiveBuildConstructionCommand(navigationWorld, clientSite);
        Check(navigationWorld.PathfindingManager.PendingRequests == 0 &&
            Globals.Telemetry.TryFindPath_Calls == clientSearchesBefore,
            "Construction approach searches are host-only too");
    }
    finally
    {
        Field(Globals.Game, typeof(RTSGame), "<Network>k__BackingField", authorityNetwork);
        clientSocketListener.Stop();
    }
}

navigationWorld = MovementWorld();
Globals.World = navigationWorld;
var emptyQueueVehicle = Mobile(new Vector3(2.5f, 0, 2.5f));
emptyQueueVehicle.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(3.5f, 2.5f)), route: [new Point(3, 2)]);
emptyQueueVehicle.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(3.5f, 2.5f)), true, []);
emptyQueueVehicle.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(4.5f, 2.5f)), true, [new Point(4, 2)]);
for (int frame = 0; frame < 100 && emptyQueueVehicle.CurrentCommand is not null; frame++)
    emptyQueueVehicle.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.05)));
Check(emptyQueueVehicle.CurrentCommand is null && navigationWorld.GameGrid.ToCell(emptyQueueVehicle.Position) == new Point(4, 2),
    "A zero-length queued route does not discard the following destination");

navigationWorld = MovementWorld();
Globals.World = navigationWorld;
var retryBuilder = Mobile(new Vector3(2.5f, 0, 2.5f));
retryBuilder.MoveSpeed = 3;
var retrySite = new Building(new Vector3(8.5f, 0, 8.5f), Guid.NewGuid());
retrySite.TotalBuildingPointsNeeded = 100;
UnitList(navigationWorld.Units).Add(retrySite);
navigationWorld.GameGrid.TryPlace(retrySite, retrySite.Position, 0);
navigationWorld.GameGrid.TryMove(retryBuilder, new Point(2, 2));
for (int y = 6; y <= 10; y++)
    for (int x = 6; x <= 10; x++)
        if (x != 8 || y != 8) navigationWorld.GameGrid.GetCell(x, y).IsBlocked = true;
retryBuilder.TryReceiveBuildConstructionCommand(navigationWorld, retrySite);
retryBuilder.TryReceiveGotoCommand(navigationWorld, new GotoCommand(new Vector2(3.5f, 12.5f)), appendToQueue: true);
Check(retryBuilder.TargetBuildingId == retrySite.UnitId && retryBuilder.MovementStatus == MovementStatus.Blocked &&
    retryBuilder.LastQueuedTarget == new Vector2(3.5f, 12.5f),
    "An inaccessible construction site retains the construction assignment and subsequent travel order");
for (int y = 6; y <= 10; y++)
    for (int x = 6; x <= 10; x++)
        navigationWorld.GameGrid.GetCell(x, y).IsBlocked = false;
for (int frame = 0; frame < 600 && !retryBuilder.IsBuilding; frame++)
{
    retryBuilder.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.05)));
    navigationWorld.PathfindingManager.Update();
}
Check(retryBuilder.IsBuilding && retryBuilder.TargetBuildingId == retrySite.UnitId &&
    retryBuilder.LastQueuedTarget == new Vector2(3.5f, 12.5f),
    "A freed construction approach resumes building without discarding the queued destination");
Field(retrySite, typeof(Building), "<ConstructionProgress>k__BackingField", 100.0f);
for (int frame = 0; frame < 600 && (retryBuilder.IsBuilding || retryBuilder.CurrentCommand is not null); frame++)
{
    retryBuilder.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.05)));
    navigationWorld.PathfindingManager.Update();
}
Check(!retryBuilder.IsBuilding && retryBuilder.CurrentCommand is null &&
    navigationWorld.GameGrid.ToCell(retryBuilder.Position) == new Point(3, 12),
    "Completing construction executes the preserved queued movement order");

Globals.World = previousMovementWorld;

// The network inbox performs bounded game-thread work and only coalesces
// replaceable position samples inside an uninterrupted command interval.
using (var boundedNetwork = new NetworkHandler("BoundedInboxTest"))
{
    var received = new List<NetworkMessage>();
    boundedNetwork.MessageReceived += received.Add;
    for (int index = 0; index < NetworkHandler.MaximumMessagesPerUpdate + 17; index++)
        boundedNetwork.EnqueueLocalMessage(new NetworkMessage(NetworkMessageType.TextMessage,
            boundedNetwork.LocalPeerId, Text: index.ToString()));
    boundedNetwork.Update();
    Check(received.Count == NetworkHandler.MaximumMessagesPerUpdate && boundedNetwork.PendingMessages == 17,
        "Network update bounds command processing per game frame");
    boundedNetwork.Update();
    Check(received.Count == NetworkHandler.MaximumMessagesPerUpdate + 17 &&
          received.Select(message => int.Parse(message.Text!)).SequenceEqual(
              Enumerable.Range(0, NetworkHandler.MaximumMessagesPerUpdate + 17)),
        "Bounded network processing preserves command order across frames");
}

using (var coalescingNetwork = new NetworkHandler("PositionCoalescingTest"))
{
    var received = new List<NetworkMessage>();
    coalescingNetwork.MessageReceived += received.Add;
    Guid movingId = Guid.NewGuid();
    NetworkMessage Position(long revision, uint sample) => NetworkCommands.CreateUnitStateCommand(
        coalescingNetwork.LocalPeerId,
        new UnitState(movingId, sample, "mobile-unit-state", 1,
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                navigation = (object?)null,
                navigationRevision = revision,
                sample
            }, NetworkJson.Options)));
    coalescingNetwork.EnqueueLocalMessage(Position(4, 1));
    coalescingNetwork.EnqueueLocalMessage(Position(4, 2));
    coalescingNetwork.EnqueueLocalMessage(new NetworkMessage(NetworkMessageType.StopCommand,
        coalescingNetwork.LocalPeerId, UnitId: movingId));
    coalescingNetwork.EnqueueLocalMessage(Position(4, 3));
    coalescingNetwork.Update();
    Check(received.Count == 3 && received[0].UnitState?.Revision == 2 &&
          received[1].Type == NetworkMessageType.StopCommand && received[2].UnitState?.Revision == 3,
        "Position snapshots coalesce without crossing command barriers");
}

using (var hostNetwork = new NetworkHandler("LoopbackHost"))
using (var clientNetwork = new NetworkHandler("LoopbackClient"))
{
    Guid snapshotUnitId = Guid.NewGuid();
    var snapshot = new SessionSnapshot(
        new WorldData(1, 1, [0], [0.0f]), [],
        [new RuntimeUnitSnapshot("soldier", snapshotUnitId, Guid.Empty, null,
            0.5f, 0, 0.5f, 0, 100, UnitBehavior.Passive, 0,
            new UnitState(snapshotUnitId, 1, "unit-state", 1, []), [])],
        [], 12.5);
    hostNetwork.SetSessionSnapshotProvider(() => new NetworkMessage(
        NetworkMessageType.SessionSnapshot, hostNetwork.LocalPeerId, SessionSnapshot: snapshot));
    int port = hostNetwork.CreateSessionAsync("LoopbackSession").GetAwaiter().GetResult();
    var initialTypes = new List<NetworkMessageType>();
    clientNetwork.MessageReceived += message => initialTypes.Add(message.Type);
    clientNetwork.JoinSessionAsync("127.0.0.1", port).GetAwaiter().GetResult();
    DateTime deadline = DateTime.UtcNow.AddSeconds(3);
    while (clientNetwork.Status != NetworkConnectionStatus.Connected && DateTime.UtcNow < deadline)
    {
        hostNetwork.Update();
        clientNetwork.Update();
        Thread.Sleep(5);
    }
    Check(clientNetwork.Status == NetworkConnectionStatus.Connected &&
          initialTypes.IndexOf(NetworkMessageType.JoinAccepted) < initialTypes.IndexOf(NetworkMessageType.SessionSnapshot) &&
          initialTypes.IndexOf(NetworkMessageType.SessionSnapshot) < initialTypes.IndexOf(NetworkMessageType.SessionReady),
        "Late join receives acceptance, complete snapshot, and readiness in order");
    Check(clientNetwork.SessionId == hostNetwork.SessionId && clientNetwork.Members.Count == 0,
        "Loopback join establishes one session without publishing a partial peer");
}

var diagnosticUnitId = Guid.NewGuid();
SessionSnapshot DiagnosticSnapshot(float hitPoints, float x = 0.5f,
    double tiberiumCreatedAt = 0, float tiberiumAmount = 0) => new(
    new WorldData(1, 1, [0], [0.0f], TiberiumCells:
        [new TiberiumSeedState(0, 0, tiberiumCreatedAt, tiberiumAmount, 0, 1, 1, 1, 1)]), [],
    [new RuntimeUnitSnapshot("soldier", diagnosticUnitId, Guid.Empty, null,
        x, 0, 0.5f, 0, hitPoints, UnitBehavior.Passive, 0,
        new UnitState(diagnosticUnitId, 1, "unit-state", 1, []), [])], [], 0);
MethodInfo createDigest = typeof(NetworkSyncDiagnostics).GetMethod("CreateDigest",
    BindingFlags.Static | BindingFlags.NonPublic)!;
MethodInfo findDifferences = typeof(NetworkSyncDiagnostics).GetMethod("FindDifferences",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var expectedDigest = (SyncDiagnosticDigest)createDigest.Invoke(null, [DiagnosticSnapshot(100), 1L])!;
var changedDigest = (SyncDiagnosticDigest)createDigest.Invoke(null, [DiagnosticSnapshot(75), 1L])!;
var diagnosticDifferences = ((IEnumerable<string>)findDifferences.Invoke(null,
    [expectedDigest, changedDigest])!).ToArray();
Check(diagnosticDifferences.Contains("units") &&
      diagnosticDifferences.Contains($"unit:{diagnosticUnitId:N}:health"),
    "Sync diagnostics identify the category and exact unit for a state divergence");
var toleratedDigest = (SyncDiagnosticDigest)createDigest.Invoke(null,
    [DiagnosticSnapshot(100, 3.4f, 50, 150), 1L])!;
var divergentDigest = (SyncDiagnosticDigest)createDigest.Invoke(null,
    [DiagnosticSnapshot(100, 3.6f, 50, 150), 1L])!;
Check(!((IEnumerable<string>)findDifferences.Invoke(null,
          [expectedDigest, toleratedDigest])!).Any() &&
      ((IEnumerable<string>)findDifferences.Invoke(null,
          [expectedDigest, divergentDigest])!).Contains($"movement:{diagnosticUnitId:N}"),
    "Sync diagnostics tolerate sampling time and ordinary replication lag but report large movement drift");

Console.WriteLine($"Passed {checks} gameplay, UV, earthwork and helicopter checks.");

sealed class CountingMovementProfile : IMovementProfile
{
    public Dictionary<Point, int> Visits { get; } = [];
    public bool CanEnter(GameWorld map, MobileUnit unit, Point cell)
    {
        Visits[cell] = Visits.GetValueOrDefault(cell) + 1;
        return map.GameGrid.Contains(cell) && !map.GameGrid.GetCell(cell).IsBlocked;
    }
    public float GetMovementCost(GameWorld map, MobileUnit unit, Point from, Point to) =>
        from.X != to.X && from.Y != to.Y ? 1.4142135f : 1f;
}

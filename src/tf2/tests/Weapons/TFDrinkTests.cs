using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Shared.Rules;
using TF2.Tests.Player;
using TF2.Tests.Projectiles;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFLunchboxWeapon"/>'s drink arms: the scout's raise
/// (<c>DoTauntAttack</c>'s scout arm, tf_player.cpp:18636-18675) holds the
/// Bonk! phase and the Crit-a-Cola minicrits for the meter's dead time, the
/// phase's end pays the stock slow (<c>OnRemovePhase</c>, :3551-3577), and the
/// hand goes back the way the last item came. The lunchbox behaviour's press
/// gates with the base's <c>CanAttack</c>, phase included
/// (tf_player_shared.cpp:12246-12250).
/// </summary>
public sealed class TFDrinkTests
{
    private const string DrinkName = TFWeaponTypes.Drink;

    private static readonly EntityHandle Player = new(0x0001_0000u | PlayerRig.Index);

    /// <summary>The server's drink side over one joined scout: the condition
    /// writer with the effect arms, the stun stack as the player's part, and
    /// an empty hand unless a fact sets an item.</summary>
    private static (PlayerRig Rig, TFLunchboxWeapon Lunchbox, TFPlayerConditions Conditions, TFStunStack Stack)
        DrinkRig(AttributeContainer? item = null, Arsenal? arsenal = null,
                 TF2.Server.Entities.Ctf.ICtfRules? rules = null, TF2.Server.Rules.ITeamRoundState? rounds = null)
    {
        var rig = new PlayerRig();
        rig.Engine.Returns("Entities.GetClassName", "player"); // the hurt checks name the plain player
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine,
                                                new ListHooks<ITFConditionHook>(new TFPlayerConditionEffectArms(rig.Engine, rig.Carriers)), rig.Engine);
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine,
                                            rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, TF2.Tests.Weapons.WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog,
                                            arsenal: new Arsenal(rig.Log, rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                 rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                 new ScriptedEntityRegistry(TestEngines.Server()), rig.Engine,
                                                                 new DamageHitGroups()),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine, equipHooks: new TF2.Tests.Rules.ListHooks<ITFWeaponEquipHook>());
        var players = new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers, rig.Items, new AttributeHooks());
        var stack = new TFStunStack(rig.Carriers, players, rig.Engine);
        rig.Extra.Add(stack);
        rig.As(TFClass.Scout);

        var owners = new FakeOwners();
        if (item is not null)
            owners.Items[PlayerRig.Index] = item;

        var lunchbox = new TFLunchboxWeapon(players, resupply, rig.Carriers, owners, new AttributeHooks(),
                                            new TFTauntCommand(players, rig.Engine, rig.Engine, rig.Engine, rig.Carriers,
                                                new TF2.Server.Voice.TFConceptSpeech(new TF2.Tests.Entities.FakeConceptRules(), rig.Carriers,
                                                    new TF2.Tests.Entities.FakeConceptTeams(), rig.Engine,
                                                    new TF2.Server.Voice.TFSceneImage(TF2.Tests.Voice.TFSceneImageTests.Files()),
                                                    rig.Engine, new TF2.Tests.Entities.FakeRandom(),
                                                    new LoadoutDeps.IdleRound(), new FakeOwners(), rig.Log),
                                                owners, new AttributeHooks(),
                                                new TF2.Server.Voice.TFSceneImage(TF2.Tests.Voice.TFSceneImageTests.Files())),
                                            rig.Engine, rig.Engine, rig.Engine, new NoEntities(),
                                            DeclaredConVars.Fresh<TFScoutEnergydrinkConsumeRateConVar>(),
                                            arsenal ?? CarriedDrink(),
                                            rules ?? new TF2.Tests.Medigun.TFHealingCtf(),
                                            rounds ?? new TF2.Tests.Entities.FakeRoundState());
        return (rig, lunchbox, conditions, stack);
    }

    private static AttributeContainer Item(string attribute, float value, int defindex)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(
            new AttributeDefinition(defindex, attribute, attribute, AttributeDescriptionFormat.Additive,
                AttributeEffectType.Neutral, false, false, AttributeValueType.Default, MergedKeys.Block("x")),
            value));
        return item;
    }

    /// <summary>The held classname of the arsenal's hands.</summary>
    private static string Held(Arsenal arsenal)
    {
        foreach (var (className, _, active) in arsenal.Holdings(Player))
            if (active)
                return className;

        return "";
    }

    [Fact]
    public void ABonkDrinkMakesTheScoutImmune()
    {
        var (rig, lunchbox, conditions, _) = DrinkRig();

        // Plain asks land before the raise (no condition on the scout).
        var rules = new TFDamageRules(new FakePredictedData { Carrier = rig.State }, rig.Engine,
                                      new TFDamageScopes(), rig.Carriers);
        var hit = new DamageContext(Player, Player, 40f, 0);
        Assert.Equal(Decision.UseStockRule, rules.Decide(in hit));

        // The Bonk raise: the phase for the meter's dead time (:18651-18653,
        // (100 / rate) + 1 on the convar's default 12.5).
        lunchbox.Drink(PlayerRig.Index);
        Assert.True(rig.State.InCond(TFCond.Phase));
        Assert.Equal(TFLunchboxWeapon.DrinkSeconds(12.5f), conditions.Of(PlayerRig.Index)!.Duration(TFCond.Phase), 3);

        // While the phase holds, plain damage is refused (:9329-9361) and the
        // dodged amount stands tallied for the slow the phase's end pays.
        Assert.Equal(Decision.No, rules.Decide(in hit));
        Assert.Equal(40, rig.Carriers.ConditionsOf(PlayerRig.Index)!.PreventedDamage(TFCond.Phase));
    }

    /// <summary>
    /// The flag arm of <c>CanStartPhase</c>
    /// (shared/tf/tf_player_shared.cpp:12705-12709): a carrier's Bonk never
    /// opens the phase; the hint (<c>HINT_CANNOT_PHASE_WITH_FLAG</c>) has no
    /// route here, so the refusal lands silent — and the hand still goes back
    /// (:18672), the drink's tail outside the gate.
    /// </summary>
    [Fact]
    public void AFlagCarrierDrinksWithoutPhasing()
    {
        // The carrier's own bit stands on the rules seam: CarriedBy answers the
        // index, the way the healing's CTF fake holds it.
        var ctf = new TF2.Tests.Medigun.TFHealingCtf();
        ctf.Carriers.Add(PlayerRig.Index);
        var arsenal = CarriedDrink();
        var (rig, lunchbox, _, _) = DrinkRig(arsenal: arsenal, rules: ctf);

        lunchbox.Drink(PlayerRig.Index);

        Assert.False(rig.State.InCond(TFCond.Phase));
        Assert.Equal("tf_weapon_scattergun", Held(arsenal));
    }

    /// <summary>
    /// The round arm of <c>CanStartPhase</c> (:12715-12718): through the
    /// winners' round the losing team drinks without phasing, the winning one
    /// phases as ever — the rig's scout is Red (<c>Players.GetTeam</c> of
    /// PlayerRig), so the same round states split by the winner's team alone.
    /// </summary>
    [Fact]
    public void TheWinnersRoundPhasesOnlyItsWinners()
    {
        var rounds = new TF2.Tests.Entities.FakeRoundState { State = RoundState.TeamWin, WinningTeam = TFTeam.Blue };
        var (rig, lunchbox, _, _) = DrinkRig(rounds: rounds);

        // Blue won; the Red scout is the loser: the phase is refused.
        lunchbox.Drink(PlayerRig.Index);
        Assert.False(rig.State.InCond(TFCond.Phase));

        // The same round under its winners: the phase lands as under no win.
        rounds.WinningTeam = TFTeam.Red;
        lunchbox.Drink(PlayerRig.Index);
        Assert.True(rig.State.InCond(TFCond.Phase));
    }

    /// <summary>
    /// The gate's otherwise-arm beside the two refusals: outside the winners'
    /// round the plain drink phases — the standing
    /// <see cref="ABonkDrinkMakesTheScoutImmune"/> walks it with the default
    /// running-round fake, so here only the win-state half is held to it: an
    /// <see cref="RoundState.Unassigned"/>-style idle round is no refusal.
    /// </summary>
    [Fact]
    public void AnIdleRoundStillPhasesTheDrink()
    {
        var rounds = new TF2.Tests.Entities.FakeRoundState { State = RoundState.RoundRunning, WinningTeam = TFTeam.Blue };
        var (rig, lunchbox, _, _) = DrinkRig(rounds: rounds);

        lunchbox.Drink(PlayerRig.Index);

        Assert.True(rig.State.InCond(TFCond.Phase));
    }

    [Fact]
    public void ABonkedScoutCannotAttack()
    {
        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.Shipped()));
        var (w, vm) = world.Armed(DrinkName, clip: 0, reserve: 1);

        world.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.Phase, 9f);
        world.Run(w, vm, (int)InputButtons.Attack, now: 10f);

        // The phase arm of CanAttack (:12246-12250): a bonked scout swings
        // nothing — the eat and its cost both wait.
        Assert.Equal(0, world.Effects.LunchboxEats);
        Assert.Equal(1, w.Reserve);

        // Past the phase the same press eats (:182).
        world.Owners.Conditions.Remove(TFWeaponWorld.Owner);
        world.Run(w, vm, (int)InputButtons.Attack, now: 20f);
        Assert.Equal(1, world.Effects.LunchboxEats);
        Assert.Equal(0, w.Reserve);
    }

    /// <summary>
    /// The real press, all the way: <c>world.Run</c> with the attack button
    /// eats, the eat is the taunt, the taunt's speak answers through the
    /// rules, the raise's tick drinks, and the drink's tail hands the hand
    /// back — the same chain a joined scout walks (tf_player.cpp:17922-17932,
    /// :18636-18675, :18672).
    /// </summary>
    [Fact]
    public void TheRealPressRunsTheDrinkThroughTheTauntToThePhaseAndBack()
    {
        var rig = new PlayerRig().As(TFClass.Scout);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        rig.Engine.Returns("Players.GetActiveWeapon", DrinkName);

        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine,
                                                new ListHooks<ITFConditionHook>(new TFPlayerConditionEffectArms(rig.Engine, rig.Carriers)), rig.Engine);
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine,
                                            rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, TF2.Tests.Weapons.WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog,
                                            arsenal: new Arsenal(rig.Log, rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                 rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                 new ScriptedEntityRegistry(TestEngines.Server()), rig.Engine,
                                                                 new DamageHitGroups()),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine, equipHooks: new TF2.Tests.Rules.ListHooks<ITFWeaponEquipHook>());
        var players = new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers, rig.Items, new AttributeHooks());

        // The voice of the press: the rules answer the taunt concept, as the
        // shipped talker file does for the scout's held drink.
        var rules = new TF2.Tests.Entities.FakeConceptRules();
        var voice = new TF2.Server.Voice.TFConceptSpeech(rules, rig.Carriers,
                                                         new TF2.Tests.Entities.FakeConceptTeams(), rig.Engine,
                                                         TF2.Tests.Entities.ObjectiveRig.ConceptSceneImage(), rig.Engine,
                                                         new TF2.Tests.Entities.FakeRandom(),
                                                         new LoadoutDeps.IdleRound(), new TF2.Tests.Weapons.FakeOwners(),
                                                         rig.Log);

        var arsenal = CarriedDrink();
        var taunts = new TFTauntCommand(players, rig.Engine, rig.Engine, rig.Engine, rig.Carriers, voice,
                                        new NoTFWeaponOwners(), new AttributeHooks(),
                                        TF2.Tests.Entities.ObjectiveRig.ConceptSceneImage());
        var lunchbox = new TFLunchboxWeapon(players, resupply, rig.Carriers, new FakeOwners(), new AttributeHooks(),
                                            taunts, rig.Engine, rig.Engine, rig.Engine, new NoEntities(),
                                            DeclaredConVars.Fresh<TFScoutEnergydrinkConsumeRateConVar>(), arsenal,
                                            new TF2.Tests.Medigun.TFHealingCtf(), new TF2.Tests.Entities.FakeRoundState());

        var scripts = new TFWeaponScripts(new RecordingLog(), WeaponFixtures.WithExtras(WeaponFixtures.Shipped()));
        var attack = new TFTauntAttack(rig.Carriers, players, rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                       rig.Engine, scripts, new TFDamageScopes(), new NoEntities(), lunchbox,
                                       new NoTFWeaponOwners(), new AttributeHooks(), new TFTauntAttackTests.FixedRandom(), rig.Engine, rig.Engine, rig.Engine,
                                       new TFBlastApplier(rig.Engine, rig.Engine, rig.Engine, new TFDamageScopes(), new DamageHitGroups(),
                                                          new DamageInflictors(), rig.Carriers, new RecordingLog(), rig.Engine,
                                                          new NoTFWeaponOwners(), new AttributeHooks(),
                                                          new ProjectileRig.Resupply(), rig.Engine, rig.Engine,
                                                          new ProjectileRig.DirectoryView(), rig.Carriers,
                                                          DeclaredConVars.Fresh<TF2.Server.TFPreroundPushFromDamageEnableConVar>()), new RecordingEffectDispatch(), new TFTauntAttackTests.ChargeMeter(), new FakeStats(), new TFTauntAttackTests.RecordingProjectile());
        attack.Attach(Player);

        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.Shipped()));
        world.Effects.LunchboxEffects = lunchbox;
        var (w, vm) = world.Armed(DrinkName, clip: 0, reserve: 1);

        // The press (:175-190): the eat asked, the drink spent, the taunt's
        // window opened on the matched response (:17862-17744).
        world.Run(w, vm, (int)InputButtons.Attack, now: 10f);
        Assert.Equal(1, world.Effects.LunchboxEats);
        Assert.Equal(0, w.Reserve);
        Assert.True(rig.State.InCond(TFCond.Taunting));
        Assert.Equal(TF2.Server.Voice.TFConceptSpeech.PlayerTaunt, rules.Asked.Single().Concept);

        // The raise's tick (ScoutDrinkSeconds past the arm): the drink lands
        // — Bonk's phase for the meter's dead time (:18651-18653) — and the
        // hand goes back to what it held before the drink came up (:18672).
        attack.Tick(2f);
        Assert.True(rig.State.InCond(TFCond.Phase));
        Assert.Equal(TFLunchboxWeapon.DrinkSeconds(12.5f), conditions.Of(PlayerRig.Index)!.Duration(TFCond.Phase), 3);
        Assert.Equal("tf_weapon_scattergun", Held(arsenal));
    }

    /// <summary>
    /// The drink's taunt scene is one of the gestureless ones — the shipped
    /// <c>scenes/player/scout/low/taunt04_v1.vcd</c> names no speak event —
    /// and the press still taunts: the matched response is the success, the
    /// condition stands and the raise lands (stock's scene plays its
    /// choreography whatever it sounds, tf_player.cpp:17862).
    /// </summary>
    [Fact]
    public void TheDrinkTauntSpeaksThroughAGesturelessScene()
    {
        var rig = new PlayerRig().As(TFClass.Scout);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        rig.Engine.Returns("Players.GetActiveWeapon", DrinkName);

        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine,
                                                new ListHooks<ITFConditionHook>(new TFPlayerConditionEffectArms(rig.Engine, rig.Carriers)), rig.Engine);
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine,
                                            rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, TF2.Tests.Weapons.WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog,
                                            arsenal: new Arsenal(rig.Log, rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                 rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                 new ScriptedEntityRegistry(TestEngines.Server()), rig.Engine,
                                                                 new DamageHitGroups()),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine, equipHooks: new TF2.Tests.Rules.ListHooks<ITFWeaponEquipHook>());
        var players = new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers, rig.Items, new AttributeHooks());

        var rules = new TF2.Tests.Entities.FakeConceptRules
        {
            Kind = ResponseKind.Scene,
            Name = "scenes/concept.vcd", // the image's sole entry, no sounds
        };
        var gestureless = new TF2.Server.Voice.TFSceneImage(TF2.Tests.Voice.TFSceneImageTests.Files(("scenes/scenes.image",
            TF2.Tests.Voice.TFSceneImageTests.Image(("scenes/concept.vcd", 968, Array.Empty<string>())))));
        var voice = new TF2.Server.Voice.TFConceptSpeech(rules, rig.Carriers,
                                                         new TF2.Tests.Entities.FakeConceptTeams(), rig.Engine,
                                                         gestureless, rig.Engine,
                                                         new TF2.Tests.Entities.FakeRandom(),
                                                         new LoadoutDeps.IdleRound(), new TF2.Tests.Weapons.FakeOwners(),
                                                         rig.Log);

        var arsenal = CarriedDrink();
        var taunts = new TFTauntCommand(players, rig.Engine, rig.Engine, rig.Engine, rig.Carriers, voice,
                                        new NoTFWeaponOwners(), new AttributeHooks(), gestureless);
        var lunchbox = new TFLunchboxWeapon(players, resupply, rig.Carriers, new FakeOwners(), new AttributeHooks(),
                                            taunts, rig.Engine, rig.Engine, rig.Engine, new NoEntities(),
                                            DeclaredConVars.Fresh<TFScoutEnergydrinkConsumeRateConVar>(), arsenal,
                                            new TF2.Tests.Medigun.TFHealingCtf(), new TF2.Tests.Entities.FakeRoundState());

        var scripts = new TFWeaponScripts(new RecordingLog(), WeaponFixtures.WithExtras(WeaponFixtures.Shipped()));
        var attack = new TFTauntAttack(rig.Carriers, players, rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                       rig.Engine, scripts, new TFDamageScopes(), new NoEntities(), lunchbox,
                                       new NoTFWeaponOwners(), new AttributeHooks(), new TFTauntAttackTests.FixedRandom(), rig.Engine, rig.Engine, rig.Engine,
                                       new TFBlastApplier(rig.Engine, rig.Engine, rig.Engine, new TFDamageScopes(), new DamageHitGroups(),
                                                          new DamageInflictors(), rig.Carriers, new RecordingLog(), rig.Engine,
                                                          new NoTFWeaponOwners(), new AttributeHooks(),
                                                          new ProjectileRig.Resupply(), rig.Engine, rig.Engine,
                                                          new ProjectileRig.DirectoryView(), rig.Carriers,
                                                          DeclaredConVars.Fresh<TF2.Server.TFPreroundPushFromDamageEnableConVar>()), new RecordingEffectDispatch(), new TFTauntAttackTests.ChargeMeter(), new FakeStats(), new TFTauntAttackTests.RecordingProjectile());
        attack.Attach(Player);

        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.Shipped()));
        world.Effects.LunchboxEffects = lunchbox;
        var (w, vm) = world.Armed(DrinkName, clip: 0, reserve: 1);

        world.Run(w, vm, (int)InputButtons.Attack, now: 10f);
        Assert.True(rig.State.InCond(TFCond.Taunting), "the gestureless scene still taunts");

        attack.Tick(2f);
        Assert.True(rig.State.InCond(TFCond.Phase));
        Assert.Equal("tf_weapon_scattergun", Held(arsenal));
    }

    [Fact]
    public void TheDrinkEndsWithStocksSlowdown()
    {
        var (rig, _, conditions, stack) = DrinkRig();

        // A plain hit dodged behind the phase is tallied (:9355-9359).
        conditions.Add(PlayerRig.Player, TFCond.Phase, 9f);
        var rules = new TFDamageRules(new FakePredictedData { Carrier = rig.State }, rig.Engine,
                                      new TFDamageScopes(), rig.Carriers);
        var hit = new DamageContext(Player, Player, 40f, 0);
        Assert.Equal(Decision.No, rules.Decide(in hit));

        // The phase's end (:3551-3577): the five-second movement slow, scaled
        // by the dodged damage — the remap the effect arm keeps.
        Assert.True(conditions.Remove(PlayerRig.Player, TFCond.Phase));

        Assert.NotNull(stack.ActiveStun);
        var stun = stack.ActiveStun!;
        Assert.Equal(255f * TFPlayerConditionEffectArms.SlowAmount(40), stun.Amount, 3);
        Assert.Equal(TFPlayerConditionEffectArms.SlowDuration, stun.ExpireTime, 3); // the rig's clock is 0
        Assert.Equal(TFStunFlags.Movement, stun.Flags);
        Assert.True(rig.State.InCond(TFCond.Stunned));
    }

    [Fact]
    public void ACritAColaMiniCrits()
    {
        // The held drink's mode names the buff (GetLunchboxType,
        // tf_weapon_lunchbox.h:63): LUNCHBOX_ADDS_MINICRITS is Crit-a-Cola
        // (:18647-18649), its own seconds of it, no phase.
        var (rig, lunchbox, conditions, _) = DrinkRig(Item(TFLunchboxBehaviour.ModeAttribute,
                                                           TFLunchboxBehaviour.ModeAddsMinicrits, 914));

        lunchbox.Drink(PlayerRig.Index);

        Assert.True(rig.State.InCond(TFCond.EnergyBuff));
        Assert.False(rig.State.InCond(TFCond.Phase));
        Assert.Equal(TFLunchboxWeapon.DrinkSeconds(12.5f), conditions.Of(PlayerRig.Index)!.Duration(TFCond.EnergyBuff), 3);
    }

    /// <summary>The arsenal's hands, the PDA switch's shape: the scout's two
    /// carried weapons with the drink up and the scattergun behind it.</summary>
    private static Arsenal CarriedDrink()
    {
        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses()));

        var engine = TestServices.Mock()
            .Returns("Entities.IsValid", true).Returns("Entities.GetIndex", PlayerRig.Index)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 64))
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(true, 0.5f, new Vector(0f, 0f, 8f), new Vector(0f, 0f, 1f), EntityHandle.None))
            .Returns("Clock.Now", 10f)
            .Ignores("EntityRender.LookupSequenceByActivity");

        engine.ReturnsEachCall("Players.IsAlive", () => engine.Calls[^1].ArgAs<EntityHandle>(0) == Player);
        engine.ReturnsEachCall("Players.GetHandle",
                               () => engine.Calls[^1].ArgAs<int>(0) == PlayerRig.Index ? Player : new EntityHandle(0x0001_0000u | (PlayerRig.Index + 1)));

        var weapon = new EntityHandle(0x0500u);
        var viewModel = new EntityHandle(0x0501u);
        var created = new Queue<EntityHandle>([weapon, viewModel]);
        engine.ReturnsEachCall("Entities.Create", () => created.Dequeue());

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = TestServices.Services(out _);
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        registry.Spawn(weapon, EntityNames.SsWeapon);
        registry.Spawn(viewModel, EntityNames.PredictedViewModel);

        var arsenal = new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine, engine, engine,
                                  registry, engine, new DamageHitGroups(), weapons: world.Catalog, ammo: world.Ammo);

        Assert.True(arsenal.GiveWeapon(Player, "tf_weapon_scattergun"), "the arsenal gives the scout's scattergun");
        Assert.True(arsenal.GiveWeapon(Player, DrinkName), "and the drink");
        Assert.True(arsenal.SwitchSlot(Player, 1) == SwitchOutcome.Switched, "the drink comes up");
        Assert.Equal(DrinkName, Held(arsenal));

        return arsenal;
    }

    [Fact]
    public void TheDrinkIsFullAtSpawnAndStaysInHand()
    {
        // The drink arrives held and full beside the scout's gun, and the
        // raise's tail (SelectLastItem, :18672) walks the hands back along the
        // last item — the held pair above is the spawn.
        var arsenal = CarriedDrink();
        Assert.Equal(DrinkName, Held(arsenal));

        var (_, lunchbox, _, _) = DrinkRig(arsenal: arsenal);
        lunchbox.Drink(PlayerRig.Index);

        // The remembered hand: the drink goes back down as stock's raise ends.
        Assert.Equal("tf_weapon_scattergun", Held(arsenal));
    }

    [Fact]
    public void TheMeterRechargesAfterADrink()
    {
        // The charge-rate item's meter fills on the frame tail and the spent
        // drink comes back (OnResourceMeterFilled, :437-444) — the same meter
        // the family's scout item runs its recharge on.
        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.Shipped()));
        world.Owners.Items[TFWeaponWorld.Owner] = Item(TFItemMeter.ChargeRateAttribute, 1f, 915);
        var (w, vm) = world.Armed(DrinkName, clip: 0, reserve: 0);
        w.ItemMeter = 0.5f;
        w.ItemMeterTime = 9f;

        world.Run(w, vm, 0, now: 10f);

        Assert.Equal(1f, w.ItemMeter, 4);
        Assert.Equal(1, w.Reserve);
    }

    /// <summary>No scripted entity of ours anywhere: the owners' directory asks.</summary>
    private sealed class NoEntities : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }
}

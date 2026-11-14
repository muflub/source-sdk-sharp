using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Shared.Anim;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The gesture seam (<see cref="IWeaponGestureRule"/>): a mode's rule names
/// the event its weapon's actions show, over the core's HL2MP tables, and
/// the arsenal asks it where it used to ask the tables directly.
/// </summary>
public sealed class WeaponGestureSeamTests
{
    private static EntityHandle Player => new(0x0001_0001);

    private sealed class ListHooks<T>(params T[] all) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => all;

        public void Each(string what, Action<T> call)
        {
            foreach (T h in all)
                call(h);
        }
    }

    /// <summary>A mode's rule: every claim is the event the table holds, keyed by trigger.</summary>
    private sealed class ModeRule(IReadOnlyDictionary<(WeaponGestureTrigger, string), PlayerAnimEvent> claims) : IWeaponGestureRule
    {
        public PlayerAnimEvent? Gesture(WeaponGestureTrigger trigger, string weaponClass)
            => claims.TryGetValue((trigger, weaponClass), out var e) ? e : null;
    }

    private sealed class AnimTemps : TempEntsDouble
    {
        public readonly List<(TeFilter Filter, int Player, PlayerAnimEvent Event, int Data)> Sent = new();

        public override void PlayerAnimEvent(TeFilter filter, float delay, int packedPlayer, int animEvent, int data)
            => Sent.Add((filter, packedPlayer, (PlayerAnimEvent)animEvent, data));
    }

    private static (Arsenal, RecordingEngine, AnimTemps, ScriptedEntityRegistry) Armed(AnimTemps temps, IWeaponGestureRule? rule, string weapon = EntityNames.WeaponSmg1)
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", Vector.Zero)
            .Returns("Entities.GetEyeAngles", Vector.Zero)
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("Clock.Now", 10f);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out var entityEngine);
        entityEngine.Strict = false;
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        var gestures = new WeaponGestures(rule is null ? null : new ListHooks<IWeaponGestureRule>(rule));
        var arsenal = new Arsenal(new RecordingLog(), new WeaponSwitchTests.SpawningEntities(engine, registry), engine, engine, engine,
                                  engine, engine, engine, engine, registry, engine, new DamageHitGroups(),
                                  gestures: gestures) { Temps = temps };
        Assert.True(arsenal.GiveWeapon(Player, weapon));
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;
        Assert.Contains(arsenal.Switch(Player, weapon), (SwitchOutcome[])[SwitchOutcome.Switched, SwitchOutcome.AlreadyHeld]);
        return (arsenal, engine, temps, registry);
    }

    private static void Command(Arsenal a, RecordingEngine e, float now, InputButtons buttons, int number)
    {
        e.Returns("Clock.Now", now);
        var cmd = new UserCmd { CommandNumber = number, Buttons = (int)buttons };
        a.OnPlayerCommand(Player, in cmd, firstTime: true);
    }

    /// <summary>A mode's claim is the event the player is shown, instead of the core's default.</summary>
    [Fact]
    public void TheModesClaimIsTheEventShown()
    {
        var temps = new AnimTemps();
        var rule = new ModeRule(new Dictionary<(WeaponGestureTrigger, string), PlayerAnimEvent>
        {
            [(WeaponGestureTrigger.Shot, "weapon_smg1")] = PlayerAnimEvent.AttackSecondary,
        });
        var (arsenal, engine, _, _) = Armed(temps, rule);
        Command(arsenal, engine, 30f, InputButtons.Attack, 1);

        var sent = Assert.Single(temps.Sent);
        Assert.Equal(PlayerAnimEvent.AttackSecondary, sent.Event);
    }

    /// <summary>A swing asks the rule's swing row: a mode's melee has a gesture of its own to show.</summary>
    [Fact]
    public void ASwingShowsTheEventTheRuleNames()
    {
        var temps = new AnimTemps();
        var rule = new ModeRule(new Dictionary<(WeaponGestureTrigger, string), PlayerAnimEvent>
        {
            [(WeaponGestureTrigger.Swing, EntityNames.WeaponCrowbar)] = PlayerAnimEvent.AttackSecondary,
        });
        var (arsenal, engine, _, _) = Armed(temps, rule, EntityNames.WeaponCrowbar);
        Command(arsenal, engine, 30f, InputButtons.Attack, 1);

        Assert.Equal(PlayerAnimEvent.AttackSecondary, Assert.Single(temps.Sent).Event);
    }

    /// <summary>With a chain that holds no claim the core's answer stands: the same ask, the HL2MP event.</summary>
    [Fact]
    public void TheCoreDefaultStandsUnderAnEmptyChain()
    {
        var temps = new AnimTemps();
        var (arsenal, engine, _, _) = Armed(temps, new ModeRule(new Dictionary<(WeaponGestureTrigger, string), PlayerAnimEvent>()));
        Command(arsenal, engine, 30f, InputButtons.Attack, 1);

        var sent = Assert.Single(temps.Sent);
        Assert.Equal((PlayerAnimEvent.AttackPrimary, WeaponCatalog.Stock().IndexOf(EntityNames.WeaponSmg1)), (sent.Event, sent.Data));
    }

    /// <summary>
    /// The event's filter keeps the firing player: this client plays no
    /// predicted body gesture, so the owner would otherwise never see their
    /// own gesture (stock's TE_PlayerAnimEvent drops them only because their
    /// client replays it, tf_player.cpp:349).
    /// </summary>
    [Fact]
    public void TheOwnerSurvivesThePredictionCull()
    {
        var temps = new AnimTemps();
        var (arsenal, engine, _, _) = Armed(temps, null);
        Command(arsenal, engine, 30f, InputButtons.Attack, 1);

        var sent = Assert.Single(temps.Sent);
        Assert.True(sent.Filter.IgnorePredictionCull, "the owner is culled with the rest");
    }

    /// <summary>A rule that throws is not the end of the ask: the core's answer follows.</summary>
    [Fact]
    public void AThrowingRuleFallsToTheCore()
    {
        var temps = new AnimTemps();
        var log = new RecordingLog();
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", Vector.Zero)
            .Returns("Entities.GetEyeAngles", Vector.Zero)
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("Clock.Now", 10f);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out var entityEngine);
        entityEngine.Strict = false;
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        var broken = new BrokenRule();
        var gestures = new WeaponGestures(new ListHooks<IWeaponGestureRule>(broken), log);
        var arsenal = new Arsenal(new RecordingLog(), new WeaponSwitchTests.SpawningEntities(engine, registry), engine, engine, engine,
                                  engine, engine, engine, engine, registry, engine, new DamageHitGroups(),
                                  gestures: gestures) { Temps = temps };
        Assert.True(arsenal.GiveWeapon(Player, EntityNames.WeaponSmg1));
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;
        Assert.Contains(arsenal.Switch(Player, EntityNames.WeaponSmg1), (SwitchOutcome[])[SwitchOutcome.Switched, SwitchOutcome.AlreadyHeld]);
        Command(arsenal, engine, 30f, InputButtons.Attack, 1);

        var sent = Assert.Single(temps.Sent);
        Assert.Equal(PlayerAnimEvent.AttackPrimary, sent.Event);
        Assert.NotEmpty(log.Lines);
    }

    /// <summary>
    /// A shell-by-shell reload's rounds and finish show what the mode claims,
    /// per shell and at the end: the shotgun loads three, and the events are
    /// the loops and then the end, nothing else (HL2MP's shotgun shows no
    /// reload gesture, so the core default stays silent here).
    /// </summary>
    [Fact]
    public void AShellReloadLoopsAndEndsWhereTheRuleClaims()
    {
        var temps = new AnimTemps();
        var rule = new ModeRule(new Dictionary<(WeaponGestureTrigger, string), PlayerAnimEvent>
        {
            [(WeaponGestureTrigger.ReloadShell, "weapon_shotgun")] = PlayerAnimEvent.ReloadLoop,
            [(WeaponGestureTrigger.ReloadEnd, "weapon_shotgun")] = PlayerAnimEvent.ReloadEnd,
        });
        var (arsenal, engine, _, registry) = Armed(temps, rule, EntityNames.WeaponShotgun);
        WeaponNetState w = ((SsWeapon)registry.Get(arsenal.CarriedPair(Player).Weapon)!).State;
        w.Clip = 3;
        w.Reserve = 10;
        Command(arsenal, engine, 10f, InputButtons.Reload, 1);

        int number = 2;

        for (float t = 10.015f; t <= 12f; t += 0.015f)
            Command(arsenal, engine, t, (InputButtons)0, number++);

        Assert.Equal(6, w.Clip);
        Assert.Equal(Enumerable.Range(0, 3).Select(_ => PlayerAnimEvent.ReloadLoop)
                              .Append(PlayerAnimEvent.ReloadEnd), temps.Sent.Select(s => s.Event));
    }

    private sealed class BrokenRule : IWeaponGestureRule
    {
        public PlayerAnimEvent? Gesture(WeaponGestureTrigger trigger, string weaponClass) => throw new InvalidOperationException("broken");
    }
}

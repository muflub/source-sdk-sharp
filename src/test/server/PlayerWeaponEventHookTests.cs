using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Shared.Anim;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="IPlayerWeaponEventHook"/> (plan_hl2mp_ai §4.5): the arsenal
/// tells the hooks what a client watching a player would be shown of its
/// weapon, once per new command: a shot, a reload's start and end.
/// </summary>
public sealed class PlayerWeaponEventHookTests
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

    private sealed class Seen : IPlayerWeaponEventHook
    {
        public readonly List<(PlayerWeaponEvent Event, int Count)> All = new();
        public readonly List<int> Types = new();

        public void OnWeaponEvent(EntityHandle player, int weaponType, PlayerWeaponEvent e, int count)
        {
            All.Add((e, count));
            Types.Add(weaponType);
        }
    }

    private sealed class AnimTemps : TempEntsDouble
    {
        public readonly List<(TeFilter Filter, int Player, PlayerAnimEvent Event, int Data)> Sent = new();

        public override void PlayerAnimEvent(TeFilter filter, float delay, int packedPlayer, int animEvent, int data)
            => Sent.Add((filter, packedPlayer, (PlayerAnimEvent)animEvent, data));
    }

    private static (Arsenal Arsenal, RecordingEngine Engine, ScriptedEntityRegistry Registry, Seen Seen) Armed(AnimTemps? temps = null)
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
        var seen = new Seen();
        var arsenal = new Arsenal(new RecordingLog(), new WeaponSwitchTests.SpawningEntities(engine, registry), engine, engine, engine,
                                  engine, engine, engine, engine, registry, engine, new DamageHitGroups(),
                                  weaponEvents: new ListHooks<IPlayerWeaponEventHook>(seen)) { Temps = temps };
        Assert.True(arsenal.GiveWeapon(Player, EntityNames.WeaponSmg1));
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;
        Assert.Contains(arsenal.Switch(Player, EntityNames.WeaponSmg1), (SwitchOutcome[])[SwitchOutcome.Switched, SwitchOutcome.AlreadyHeld]);
        return (arsenal, engine, registry, seen);
    }

    private static void Command(Arsenal a, RecordingEngine e, float now, InputButtons buttons, int number, bool firstTime = true)
    {
        e.Returns("Clock.Now", now);
        var cmd = new UserCmd { CommandNumber = number, Buttons = (int)buttons };
        a.OnPlayerCommand(Player, in cmd, firstTime);
    }

    [Fact]
    public void AShotIsToldOnceAsTheRoundItSpent()
    {
        var (arsenal, engine, _, seen) = Armed();
        Command(arsenal, engine, 30f, InputButtons.Attack, 1);
        Command(arsenal, engine, 30f, InputButtons.Attack, 1, firstTime: false);
        Assert.Equal([(PlayerWeaponEvent.Fired, 1)], seen.All);
    }

    [Fact]
    public void AReloadIsToldWhenItStartsAndWhenItEnds()
    {
        var (arsenal, engine, registry, seen) = Armed();
        WeaponNetState w = ((SsWeapon)registry.Get(arsenal.CarriedPair(Player).Weapon)!).State;
        w.Clip = 10;
        w.Reserve = 100;
        arsenal.CarryFor(Player).Ammo["SMG1"] = 100;
        Command(arsenal, engine, 30f, InputButtons.Reload, 1);
        Command(arsenal, engine, 40f, (InputButtons)0, 2);
        Assert.Equal([(PlayerWeaponEvent.ReloadStarted, 0), (PlayerWeaponEvent.ReloadFinished, 45)], seen.All);
    }

    /// <summary>
    /// A reload the player switches away from ends there, as the reload a
    /// watching client sees is over when the other weapon comes up: told as
    /// the reloading weapon's end, its clip unfilled. Before, nothing told it
    /// (the new weapon's command never saw the old one's reload), so a bot
    /// believed it, and a watcher believed of it, a reload for the rest of a life.
    /// </summary>
    [Fact]
    public void AReloadSwitchedAwayFromIsToldAsEnded()
    {
        var (arsenal, engine, registry, seen) = Armed();
        WeaponNetState w = ((SsWeapon)registry.Get(arsenal.CarriedPair(Player).Weapon)!).State;
        w.Clip = 10;
        w.Reserve = 100;
        arsenal.CarryFor(Player).Ammo["SMG1"] = 100;
        Command(arsenal, engine, 30f, InputButtons.Reload, 1);
        Assert.True(arsenal.GiveWeapon(Player, EntityNames.WeaponPistol));
        arsenal.Switch(Player, EntityNames.WeaponPistol);
        Command(arsenal, engine, 30.1f, (InputButtons)0, 2);
        Command(arsenal, engine, 40f, (InputButtons)0, 3);

        int smg1 = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponSmg1);
        Assert.Equal([(PlayerWeaponEvent.ReloadStarted, 0), (PlayerWeaponEvent.ReloadFinished, 10)], seen.All);
        Assert.Equal(smg1, seen.Types[1]);
    }

    /// <summary>
    /// The shot every client that sees the player is shown
    /// (CSDKPlayer::DoAnimationEvent's TE_PlayerAnimEvent, HL2MP's
    /// SetAnimation( PLAYER_ATTACK1 )): the primary attack event with the
    /// weapon's type, to the PVS of the player's eyes, once per new command.
    /// </summary>
    [Fact]
    public void AShotSendsThePrimaryAttackEventToThePvsOfTheEyes()
    {
        var temps = new AnimTemps();
        var (arsenal, engine, _, _) = Armed(temps);
        Command(arsenal, engine, 30f, InputButtons.Attack, 1);
        Command(arsenal, engine, 30f, InputButtons.Attack, 1, firstTime: false);

        var sent = Assert.Single(temps.Sent);
        Assert.Equal((TempEntWriter.PackEHandle(Player), PlayerAnimEvent.AttackPrimary, WeaponCatalog.Stock().IndexOf(EntityNames.WeaponSmg1)), (sent.Player, sent.Event, sent.Data));
        Assert.Equal(TeFilters.Pvs(new Vector(0f, 0f, 64f)), sent.Filter);
    }

    [Fact]
    public void AReloadSendsTheReloadEvent()
    {
        var temps = new AnimTemps();
        var (arsenal, engine, registry, _) = Armed(temps);
        WeaponNetState w = ((SsWeapon)registry.Get(arsenal.CarriedPair(Player).Weapon)!).State;
        w.Clip = 10;
        w.Reserve = 100;
        arsenal.CarryFor(Player).Ammo["SMG1"] = 100;
        Command(arsenal, engine, 30f, InputButtons.Reload, 1);
        Assert.Equal([PlayerAnimEvent.Reload], temps.Sent.Select(t => t.Event));
    }

    /// <summary>
    /// A melee swing shows the attack gesture, hit or miss
    /// (CBaseHL2MPBludgeonWeapon::Swing's SetAnimation( PLAYER_ATTACK1 )),
    /// though it spends nothing the shot test reads.
    /// </summary>
    [Fact]
    public void ACrowbarSwingSendsTheAttackEvent()
    {
        var temps = new AnimTemps();
        var (arsenal, engine, _, _) = Armed(temps);
        Assert.True(arsenal.GiveWeapon(Player, EntityNames.WeaponCrowbar));
        arsenal.Switch(Player, EntityNames.WeaponCrowbar);
        Command(arsenal, engine, 30f, InputButtons.Attack, 1);

        var sent = Assert.Single(temps.Sent);
        Assert.Equal((PlayerAnimEvent.AttackPrimary, WeaponCatalog.Stock().IndexOf(EntityNames.WeaponCrowbar)), (sent.Event, sent.Data));
    }

    /// <summary>The SMG1's grenade shows it (CWeaponSMG1::SecondaryAttack's "player shoot animation").</summary>
    [Fact]
    public void TheSmg1sGrenadeSendsTheAttackEvent()
    {
        var temps = new AnimTemps();
        var (arsenal, engine, registry, _) = Armed(temps);
        WeaponNetState w = ((SsWeapon)registry.Get(arsenal.CarriedPair(Player).Weapon)!).State;
        w.SecondaryReserve = 2;
        arsenal.CarryFor(Player).Ammo["SMG1_Grenade"] = 2;
        Command(arsenal, engine, 30f, InputButtons.Attack2, 1);

        Assert.True(w.SecondaryReserve < 2, "the grenade was paid for");
        var sent = Assert.Single(temps.Sent);
        Assert.Equal((PlayerAnimEvent.AttackPrimary, WeaponCatalog.Stock().IndexOf(EntityNames.WeaponSmg1)), (sent.Event, sent.Data));
    }

    /// <summary>HL2MP's physcannon calls no SetAnimation (weapon_physcannon.cpp): a punt shows no gesture.</summary>
    [Fact]
    public void ThePhyscannonShowsNoGesture()
    {
        Assert.False(PlayerAnimGestures.ShotShowsGesture(EntityNames.WeaponPhyscannon));
        Assert.False(PlayerAnimGestures.SwingShowsGesture(EntityNames.WeaponPhyscannon));
        Assert.False(PlayerAnimGestures.SecondaryShowsGesture(EntityNames.WeaponPhyscannon));
        Assert.False(PlayerAnimGestures.SecondaryShowsGesture("weapon_ar2"));
    }

    /// <summary>Each weapon's acttable gesture, and the two weapons whose code shows none (the crossbow's shot, the shotgun's reload).</summary>
    [Fact]
    public void TheGestureIsTheWeaponsActtableRow()
    {
        Assert.Equal("ACT_HL2MP_GESTURE_RANGE_ATTACK_SMG1", PlayerAnimGestures.Activity(PlayerAnimEvent.AttackPrimary, "weapon_smg1"));
        Assert.Equal("ACT_HL2MP_GESTURE_RELOAD_PISTOL", PlayerAnimGestures.Activity(PlayerAnimEvent.Reload, "weapon_357"));
        Assert.Equal("ACT_HL2MP_GESTURE_RANGE_ATTACK_MELEE", PlayerAnimGestures.Activity(PlayerAnimEvent.AttackPrimary, "weapon_stunstick"));
        Assert.False(PlayerAnimGestures.ShotShowsGesture("weapon_crossbow"));
        Assert.False(PlayerAnimGestures.ReloadShowsGesture("weapon_shotgun"));
        Assert.True(PlayerAnimGestures.ShotShowsGesture("weapon_shotgun"));
    }

    /// <summary>Q_log2( PLAYERANIMEVENT_COUNT ) + 1: the event's six bits hold every event.</summary>
    [Fact]
    public void TheEventsBitsHoldEveryEvent()
        => Assert.Equal(6, (int)Math.Floor(Math.Log2((int)PlayerAnimEvent.Count)) + 1);
}

/*
 * PHASE 0.4: THE MACHINE ASKS A ROW'S BEHAVIOUR AT ITS DECISION POINTS.
 *
 * WeaponLogic stays sealed and runs stock; a row whose Behaviour key resolves
 * through an IWeaponBehaviourSource is asked at the named points (the veto
 * before a shot, the fire interval, the clip size, the tails after each
 * attack and the per-command post-frame). The facts drive RunCommand, the
 * entry both sides use, over a mod row that is the pistol under another name,
 * so the stock numbers (0.2 s interval, 18-round clip) are the control.
 */

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

public class WeaponBehaviourTests
{
    private const string Gun = "wep_behaviour_gun";
    private const string Key = "wep.behaviour";
    private const int Attack = (int)InputButtons.Attack;
    private const int ReloadKey = (int)InputButtons.Reload;

    // The pistol, renamed, pointed at the key and given its own interval of
    // 0.2 (the pistol's is 0.5 and its rules are a behaviour of their own):
    // clip 18.
    private static readonly WeaponType GunRow =
        WeaponCatalog.Stock().For(EntityNames.WeaponPistol)!.Value with { Name = Gun, Behaviour = Key, Interval = 0.2f };

    private sealed class GunSource : IWeaponTypeSource
    {
        public IEnumerable<WeaponType> Rows() => [GunRow];
    }

    private sealed class BehaviourSource(params (string Key, IWeaponBehaviour Behaviour)[] behaviours) : IWeaponBehaviourSource
    {
        public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours()
            => behaviours.Select(b => KeyValuePair.Create(b.Key, b.Behaviour));
    }

    private sealed class Arms : IWeaponArms
    {
        public int Bullets;
        public int Sounds;

        public void Sound(string soundScript) => Sounds++;
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) => Bullets++;
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }
        public int LookupSequence(WeaponAnim anim) => (int)anim + 1;
        public float AnimDuration(WeaponAnim anim) => 1.5f;
    }

    private sealed class DoublesTheInterval : IWeaponBehaviour
    {
        public float ModifyFireInterval(in WeaponType type, float interval, int ownerIndex) => interval * 2f;
    }

    private sealed class Vetoes : IWeaponBehaviour
    {
        public int Asked;
        public bool BeforePrimaryAttack(in WeaponAttackContext attack) { Asked++; return false; }
    }

    private sealed class ThirtyRoundClip : IWeaponBehaviour
    {
        public int ModifyClipSize(in WeaponType type, int clipSize, int ownerIndex) => 30;
    }

    private sealed class Records : IWeaponBehaviour
    {
        public int PostFrames;
        public readonly List<int> ClipSeenAfterShot = new();

        public void PrimaryAttack(in WeaponAttackContext attack) => ClipSeenAfterShot.Add(attack.Weapon.Clip);
        public void ItemPostFrame(in WeaponAttackContext attack) => PostFrames++;
    }

    private static WeaponCatalog Catalog() => new(() => [new GunSource()], new RecordingLog());

    private static WeaponLogic Machine(IWeaponArms arms, WeaponBehaviours behaviours)
        => new(arms, Catalog(), behaviours);

    private static WeaponBehaviours With(IWeaponBehaviour behaviour, ILog? log = null)
        => new(() => [new BehaviourSource((Key, behaviour))], log ?? new RecordingLog());

    private static (WeaponNetState w, ViewModelNetState vm) Armed(int clip, int reserve)
    {
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();

        w.WeaponTypeId = Catalog().IndexOf(Gun);
        w.Clip = clip;
        w.Reserve = reserve;
        return (w, vm);
    }

    [Fact]
    public void ABehaviourDoublingTheFireIntervalMovesNextPrimaryAttackByExactlyThat()
    {
        var (stockW, stockVm) = Armed(clip: 18, reserve: 30);
        Machine(new Arms(), WeaponBehaviours.Stock()).RunCommand(stockW, stockVm, Attack, now: 10f, effectsLive: true);

        var (w, vm) = Armed(clip: 18, reserve: 30);
        Machine(new Arms(), With(new DoublesTheInterval())).RunCommand(w, vm, Attack, now: 10f, effectsLive: true);

        // Stock: one 0.2 s interval. Doubled: two. The shot itself is stock's.
        Assert.Equal(10.2f, stockW.NextPrimaryAttack, precision: 4);
        Assert.Equal(10.4f, w.NextPrimaryAttack, precision: 4);
        Assert.Equal(GunRow.Interval, w.NextPrimaryAttack - stockW.NextPrimaryAttack, precision: 4);
        Assert.Equal(stockW.Clip, w.Clip);
    }

    [Fact]
    public void ARowNamingAnUnregisteredBehaviourRunsStockAndSaysSoOnce()
    {
        var log = new RecordingLog();
        var nobody = new WeaponBehaviours(() => [], log);
        var machine = Machine(new Arms(), nobody);
        var (w, vm) = Armed(clip: 18, reserve: 30);

        machine.RunCommand(w, vm, Attack, now: 10f, effectsLive: true);
        machine.RunCommand(w, vm, Attack, now: 10.25f, effectsLive: true);

        Assert.Equal(16, w.Clip);
        Assert.Equal(10.4f, w.NextPrimaryAttack, precision: 4);
        Assert.Single(log.Lines, l => l.Warning && l.Text.Contains($"'{Key}'"));
    }

    [Fact]
    public void AVetoBeforeThePrimaryAttackSpendsNothingAndFiresNothing()
    {
        var arms = new Arms();
        var veto = new Vetoes();
        var (w, vm) = Armed(clip: 18, reserve: 30);

        Machine(arms, With(veto)).RunCommand(w, vm, Attack, now: 10f, effectsLive: true);

        Assert.Equal(1, veto.Asked);
        Assert.Equal(18, w.Clip);
        Assert.Equal(0, arms.Bullets);
        Assert.Equal(0, arms.Sounds);
        Assert.Equal(0, vm.Sequence);
    }

    [Fact]
    public void ABehavioursClipSizeDecidesWhenAReloadIsFull()
    {
        var machine = Machine(new Arms(), With(new ThirtyRoundClip()));
        var (w, vm) = Armed(clip: 18, reserve: 50);

        // 18 is the row's full clip, so stock would refuse this reload.
        machine.RunCommand(w, vm, ReloadKey, now: 10f, effectsLive: true);
        Assert.True(w.InReload, "a clip of 18 is not full when the behaviour says 30");

        machine.RunCommand(w, vm, 0, now: 11.6f, effectsLive: true);
        Assert.False(w.InReload);
        Assert.Equal(30, w.Clip);
        Assert.Equal(38, w.Reserve);
    }

    [Fact]
    public void ThePrimaryAttackTailRunsOnceAfterTheRoundIsSpent()
    {
        var records = new Records();
        var (w, vm) = Armed(clip: 18, reserve: 30);

        Machine(new Arms(), With(records)).RunCommand(w, vm, Attack, now: 10f, effectsLive: true);

        Assert.Equal([17], records.ClipSeenAfterShot);
    }

    [Fact]
    public void ItemPostFrameRunsForEveryCommandWhateverTheWeaponDid()
    {
        var records = new Records();
        var machine = Machine(new Arms(), With(records));
        var (w, vm) = Armed(clip: 1, reserve: 30);

        machine.RunCommand(w, vm, Attack, now: 10f, effectsLive: true);   // fires the last round
        machine.RunCommand(w, vm, 0, now: 10.5f, effectsLive: true);      // empty and idle: reload starts
        machine.RunCommand(w, vm, 0, now: 10.6f, effectsLive: true);      // mid-reload, returns early
        machine.RunCommand(w, vm, 0, now: 20f, effectsLive: true);        // the reload completes

        Assert.True(w.Clip == 18, $"the commands did what they say (clip {w.Clip})");
        Assert.Equal(4, records.PostFrames);
    }

    [Fact]
    public void ASecondSourceClaimingATakenKeyIsRefusedAndTheFirstOwnerAnswers()
    {
        var log = new RecordingLog();
        var first = new DoublesTheInterval();
        var behaviours = new WeaponBehaviours(
            () => [new BehaviourSource((Key, first)), new BehaviourSource((Key.ToUpperInvariant(), new Vetoes()))],
            log);

        Assert.Same(first, behaviours.For(Key));
        Assert.Single(log.Lines, l => l.Warning && l.Text.Contains("already owns"));
    }

    //-------------------------------------------------------------------------
    // The server's arsenal asks the same behaviour the machine does.
    //-------------------------------------------------------------------------

    private static readonly EntityHandle Player = new(0x0001_0001u);

    /// <summary>
    /// A fixture player armed with the mod gun by an arsenal over
    /// <paramref name="behaviours"/>, on a clock the caller can move.
    /// </summary>
    private static Arsenal ArmedArsenal(WeaponBehaviours behaviours, Func<float> clock)
        => ArmedArsenal(behaviours, clock, out _);

    private static Arsenal ArmedArsenal(WeaponBehaviours behaviours, Func<float> clock,
                                        out WeaponSwitchTests.SpawningEntities spawned)
    {
        var engine = TestServices.Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", 1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 64))
            .Returns("Entities.GetEyeAngles", new Vector(0, 0, 0))
            .Returns("Players.GetShootOrigin", new Vector(0, 0, 64))
            .Returns("Players.IsAlive", true)
            .Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
            .ReturnsEachCall("Clock.Now", () => clock())
            .Ignores("EntityRender.LookupSequenceByActivity");

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = TestServices.Services(out _);
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");

        spawned = new WeaponSwitchTests.SpawningEntities(engine, registry);
        var arsenal = new Arsenal(new RecordingLog(), spawned,
                                  engine, engine, engine, engine, engine, engine, engine, registry, engine, new DamageHitGroups(),
                                  weapons: Catalog(), behaviours: behaviours);

        Assert.True(arsenal.GiveWeapon(Player, Gun), "the mod row can be given");
        return arsenal;
    }

    [Fact]
    public void AFreshGiveFillsTheClipTheBehaviourDeclares()
    {
        Assert.Equal([(Gun, 18, true)], ArmedArsenal(WeaponBehaviours.Stock(), () => 10f).Holdings(Player));
        Assert.Equal([(Gun, 30, true)], ArmedArsenal(With(new ThirtyRoundClip()), () => 10f).Holdings(Player));
    }

    private sealed class LaunchesItsOwn : IWeaponBehaviour
    {
        public static readonly Vector Velocity = new(100f, 200f, 300f);

        public bool BeforePrimaryAttack(in WeaponAttackContext attack)
        {
            if (attack.EffectsLive)
                attack.Arms.LaunchWith(attack.Type, "wep_test_rocket", Velocity, fuse: 2.5f);

            return false;
        }
    }

    [Fact]
    public void ABehavioursLaunchReportsItsOwnVelocityAndFuse()
    {
        float now = 10f;
        var arsenal = ArmedArsenal(With(new LaunchesItsOwn()), () => now);
        var launches = new List<(EntityHandle Flying, EntityHandle Launcher, Vector Velocity, float Fuse)>();
        arsenal.Launched = (flying, launcher, velocity, fuse) => launches.Add((flying, launcher, velocity, fuse));

        now = 11f;   // past the deploy
        var cmd = new UserCmd { CommandNumber = 1, Buttons = Attack };
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);

        var launch = Assert.Single(launches);
        Assert.True(launch.Flying != EntityHandle.None, "the projectile was created");
        Assert.Equal((Player, LaunchesItsOwn.Velocity, 2.5f), (launch.Launcher, launch.Velocity, launch.Fuse));
    }

    /// <summary>A behaviour that runs the whole command, recording what it was handed.</summary>
    private sealed class RunsItsOwn : IWeaponBehaviour
    {
        public readonly List<(int Buttons, int Pressed)> Commands = [];
        public int PostFrames;

        public bool RunCommand(in WeaponAttackContext attack, int buttons, int pressed)
        {
            Commands.Add((buttons, pressed));
            return true;
        }

        public void ItemPostFrame(in WeaponAttackContext attack) => PostFrames++;
    }

    [Fact]
    public void ABehaviourThatRunsTheCommandReplacesStocksFireAndIdle()
    {
        var arms = new Arms();
        var own = new RunsItsOwn();
        var (w, vm) = Armed(clip: 18, reserve: 30);

        // Held through an attack and released past the idle time: stock would
        // fire a round and then send the idle.
        Machine(arms, With(own)).RunCommand(w, vm, Attack, now: 10f, effectsLive: true);
        Machine(arms, With(own)).RunCommand(w, vm, 0, now: 20f, effectsLive: true);

        Assert.Equal((18, 0, 0, 0), (w.Clip, arms.Bullets, arms.Sounds, vm.Sequence));
        // The command it ran was its whole frame: no stock tail after it.
        Assert.Equal(0, own.PostFrames);
    }

    [Fact]
    public void ABehaviourThatRunsTheCommandIsHandedTheButtonsAndTheirLeadingEdge()
    {
        var own = new RunsItsOwn();
        var machine = Machine(new Arms(), With(own));
        var (w, vm) = Armed(clip: 18, reserve: 30);

        machine.RunCommand(w, vm, Attack, now: 10f, effectsLive: true);
        machine.RunCommand(w, vm, Attack | ReloadKey, now: 10.1f, effectsLive: true);

        Assert.Equal([(Attack, Attack), (Attack | ReloadKey, ReloadKey)], own.Commands);
        Assert.Equal(Attack | ReloadKey, w.LastButtons);
    }

    //-------------------------------------------------------------------------
    // The arsenal asks a behaviour about putting its weapon away.
    //-------------------------------------------------------------------------

    /// <summary>Records the deploy and holster hooks, and answers the holster and drop questions as told.</summary>
    private sealed class Hooks(bool canHolster = true, bool leavesPickup = true) : IWeaponBehaviour
    {
        public int Deploys;
        public int Holsters;

        public void Deploy(in WeaponAttackContext attack) => Deploys++;
        public bool CanHolster(in WeaponAttackContext attack) => canHolster;
        public void Holster(in WeaponAttackContext attack) => Holsters++;
        public bool LeavesPickup => leavesPickup;
    }

    [Fact]
    public void AWeaponThatWillNotBePutAwayRefusesTheSwitch()
    {
        var hooks = new Hooks(canHolster: false);
        var arsenal = ArmedArsenal(With(hooks), () => 10f);
        Assert.True(arsenal.GiveWeapon(Player, EntityNames.WeaponPistol), "a second weapon to switch to");

        Assert.Equal(SwitchOutcome.CannotHolster, arsenal.Switch(Player, EntityNames.WeaponPistol));
        Assert.Equal((Gun, true), (arsenal.Holdings(Player)[0].ClassName, arsenal.Holdings(Player)[0].Active));
        Assert.Equal(0, hooks.Holsters);
    }

    [Fact]
    public void ASwitchHolstersTheOutgoingWeaponAndDeploysTheIncoming()
    {
        var hooks = new Hooks();
        var arsenal = ArmedArsenal(With(hooks), () => 10f);
        Assert.True(arsenal.GiveWeapon(Player, EntityNames.WeaponPistol), "a second weapon to switch to");

        Assert.Equal(1, hooks.Deploys);
        Assert.Equal(SwitchOutcome.Switched, arsenal.Switch(Player, EntityNames.WeaponPistol));
        Assert.Equal((1, 1), (hooks.Deploys, hooks.Holsters));

        Assert.Equal(SwitchOutcome.Switched, arsenal.Switch(Player, Gun));
        Assert.Equal((2, 1), (hooks.Deploys, hooks.Holsters));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADeathDropLeavesAPickupOnlyForAWeaponThatLeavesOne(bool leavesPickup)
    {
        var hooks = new Hooks(leavesPickup: leavesPickup);
        var arsenal = ArmedArsenal(With(hooks), () => 10f, out var spawned);

        arsenal.DropActive(Player, Vector.Zero);

        Assert.Equal(leavesPickup, spawned.CreatedClasses.Contains(Gun));
        Assert.Equal((1, 0), (hooks.Holsters, arsenal.Holdings(Player).Count));
    }

    private sealed class SeesTheCommand : IWeaponBehaviour
    {
        public readonly List<(string Point, WeaponCommand Command)> Seen = new();

        public bool BeforePrimaryAttack(in WeaponAttackContext attack) { Seen.Add(("before", attack.Command)); return true; }
        public void PrimaryAttack(in WeaponAttackContext attack) => Seen.Add(("primary", attack.Command));
        public void Reload(in WeaponAttackContext attack) => Seen.Add(("reload", attack.Command));
        public void ItemPostFrame(in WeaponAttackContext attack) => Seen.Add(("post", attack.Command));
    }

    [Fact]
    public void EveryBehaviourPointIsHandedTheCommandTheMachineRuns()
    {
        var command = new WeaponCommand(Seed: 0x1234567, WeaponIndex: 0x500, OwnerIndex: 3);
        var seen = new SeesTheCommand();
        var machine = Machine(new Arms(), With(seen));

        var (w, vm) = Armed(clip: 1, reserve: 30);
        machine.RunCommand(w, vm, Attack, now: 10f, effectsLive: true, command);      // the last round
        machine.RunCommand(w, vm, ReloadKey, now: 11f, effectsLive: false, command);  // a replay's reload

        Assert.Equal(["before", "primary", "post", "reload", "post"], seen.Seen.Select(s => s.Point));
        Assert.All(seen.Seen, s => Assert.Equal(command, s.Command));
    }

    /// <summary>Records the owner each clip and interval point was asked about.</summary>
    private sealed class SeesTheOwner : IWeaponBehaviour
    {
        public readonly List<(string Point, int Owner)> Seen = new();

        public float ModifyFireInterval(in WeaponType type, float interval, int ownerIndex)
        { Seen.Add(("interval", ownerIndex)); return interval; }

        public int ModifyClipSize(in WeaponType type, int clipSize, int ownerIndex)
        { Seen.Add(("clip", ownerIndex)); return clipSize; }
    }

    [Fact]
    public void TheClipAndIntervalPointsAreAskedAboutTheCommandsOwner()
    {
        var command = new WeaponCommand(Seed: 0x1234567, WeaponIndex: 0x500, OwnerIndex: 3);
        var seen = new SeesTheOwner();
        var (w, vm) = Armed(clip: 18, reserve: 30);

        Machine(new Arms(), With(seen)).RunCommand(w, vm, Attack, now: 10f, effectsLive: true, command);

        // The step's clip, the attack's clip, and the interval the clocks are
        // counted in — every one about the player holding the weapon.
        Assert.Equal(["clip", "clip", "interval"], seen.Seen.Select(s => s.Point));
        Assert.All(seen.Seen, s => Assert.Equal(3, s.Owner));
    }

    [Fact]
    public void WithoutACommandTheClipAndIntervalPointsSeeNoOwner()
    {
        var seen = new SeesTheOwner();
        var (w, vm) = Armed(clip: 18, reserve: 30);

        Machine(new Arms(), With(seen)).RunCommand(w, vm, Attack, now: 10f, effectsLive: true);

        Assert.NotEmpty(seen.Seen);
        Assert.All(seen.Seen, s => Assert.Equal(0, s.Owner));
    }

    [Fact]
    public void TheArsenalAsksTheClipSizeAboutTheOwningPlayer()
    {
        var seen = new SeesTheOwner();

        ArmedArsenal(With(seen), () => 10f);   // the construction gives the gun

        // The equip fills the clip and the deploy loads it — both about the
        // player whose hands they fill (the fixture player's entity index).
        Assert.NotEmpty(seen.Seen);
        Assert.All(seen.Seen, s => Assert.Equal(1, s.Owner));
    }

    [Fact]
    public void TheCommandIsTheStockSeedOfItsNumberAndThePairsEntityIndices()
    {
        var cmd = new UserCmd { CommandNumber = 42 };

        var command = WeaponCommand.For(in cmd, weapon: new EntityHandle(0x0003_0500u), owner: new EntityHandle(0x0002_0004u));

        // ReadUsercmd's MD5_PseudoRandom(command_number) & 0x7fffffff, and the
        // entries of the two handles (the serial bits are not the index).
        Assert.Equal(new WeaponCommand((int)(UserCmdCodec.Md5PseudoRandom(42) & 0x7fffffffu), 0x500, 4), command);
        Assert.NotEqual(WeaponCommand.For(new UserCmd { CommandNumber = 43 }, default, default).Seed, command.Seed);
    }

    /// <summary>Scales the reload's duration and records the owner it was asked about.</summary>
    private sealed class ScalesTheReloadTime(float scale) : IWeaponBehaviour
    {
        public readonly List<(string Point, int Owner)> Seen = new();

        public float ModifyReloadTime(in WeaponType type, float duration, int ownerIndex)
        { Seen.Add(("reloadtime", ownerIndex)); return duration * scale; }
    }

    /// <summary>
    /// The reload's duration, as a point of the behaviour — stock's
    /// <c>CTFWeaponBase::SetReloadTimer</c> arms (<c>tf_weaponbase.cpp:2266-2331</c>):
    /// the machine times the reload by the viewmodel's animation, the behaviour
    /// scales that time, and the viewmodel plays its animation at the base over
    /// the scaled so the animation ends with the clock (stock's
    /// <c>SetPlaybackRate( flBaseReloadTime / flReloadTime )</c>, :2322).
    /// </summary>
    [Fact]
    public void ABehaviourScalingTheReloadTimeMovesTheClockAndTheViewmodelRate()
    {
        var (stockW, stockVm) = Armed(clip: 9, reserve: 30);
        Machine(new Arms(), WeaponBehaviours.Stock()).RunCommand(stockW, stockVm, ReloadKey, now: 10f, effectsLive: true);

        var (w, vm) = Armed(clip: 9, reserve: 30);
        Machine(new Arms(), With(new ScalesTheReloadTime(0.5f))).RunCommand(w, vm, ReloadKey, now: 10f, effectsLive: true);

        // The arms' animation is 1.5 s. Stock: the clock is the animation and
        // the rate is 1. Halved: the clock is 0.75 and the rate 2.
        Assert.Equal(11.5f, stockW.NextPrimaryAttack, precision: 4);
        Assert.Equal(1f, stockVm.PlaybackRate, precision: 4);
        Assert.True(w.InReload);
        Assert.Equal(10.75f, w.NextPrimaryAttack, precision: 4);
        Assert.Equal(10.75f, w.TimeWeaponIdle, precision: 4);
        Assert.Equal(2f, vm.PlaybackRate, precision: 4);
    }

    /// <summary>
    /// The reload's clock is predicted state, so the point that scales it is
    /// asked about the command's owner alone — the answer the client's replay
    /// can derive from the same inputs the server read.
    /// </summary>
    [Fact]
    public void TheReloadTimePointIsAskedAboutTheCommandsOwner()
    {
        var command = new WeaponCommand(Seed: 0x1234567, WeaponIndex: 0x500, OwnerIndex: 3);
        var scales = new ScalesTheReloadTime(1f);
        var (w, vm) = Armed(clip: 9, reserve: 30);

        Machine(new Arms(), With(scales)).RunCommand(w, vm, ReloadKey, now: 10f, effectsLive: true, command);

        Assert.Equal([("reloadtime", 3)], scales.Seen);
    }

    /// <summary>
    /// The empty gun that reloads itself (<c>ReloadOrSwitchWeapons</c>'s reload,
    /// which stock runs through the weapon's <c>Reload</c> and so through
    /// <c>SetReloadTimer</c>) waits the scaled duration too — and the click's
    /// own floor still floors what a heavy scale takes below it.
    /// </summary>
    [Fact]
    public void TheEmptyGunThatReloadsItselfWaitsTheScaledDuration()
    {
        var (stockW, stockVm) = Armed(clip: 0, reserve: 30);
        Machine(new Arms(), WeaponBehaviours.Stock()).RunCommand(stockW, stockVm, Attack, now: 10f, effectsLive: true);

        var (w, vm) = Armed(clip: 0, reserve: 30);
        Machine(new Arms(), With(new ScalesTheReloadTime(0.5f))).RunCommand(w, vm, Attack, now: 10f, effectsLive: true);

        var (flooredW, flooredVm) = Armed(clip: 0, reserve: 30);
        Machine(new Arms(), With(new ScalesTheReloadTime(0.25f))).RunCommand(flooredW, flooredVm, Attack, now: 10f, effectsLive: true);

        // The animation is 1.5 s; stock waits it whole, the behaviour's half
        // waits 0.75, and the quarter waits the click's 0.5 s floor.
        Assert.Equal(11.5f, stockW.NextPrimaryAttack, precision: 4);
        Assert.True(w.InReload);
        Assert.Equal(10.75f, w.NextPrimaryAttack, precision: 4);
        Assert.Equal(stockW.Clip, w.Clip);
        Assert.True(flooredW.InReload);
        Assert.Equal(10.5f, flooredW.NextPrimaryAttack, precision: 4);
    }
}

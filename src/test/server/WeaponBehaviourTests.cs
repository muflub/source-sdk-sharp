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
        public float ModifyFireInterval(in WeaponType type, float interval) => interval * 2f;
    }

    private sealed class Vetoes : IWeaponBehaviour
    {
        public int Asked;
        public bool BeforePrimaryAttack(in WeaponAttackContext attack) { Asked++; return false; }
    }

    private sealed class ThirtyRoundClip : IWeaponBehaviour
    {
        public int ModifyClipSize(in WeaponType type, int clipSize) => 30;
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
    {
        var engine = TestServices.Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", 1)
            .Returns("Entities.GetClassName", EntityNames.SsPlayer)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 64))
            .Returns("Entities.GetEyeAngles", new Vector(0, 0, 0))
            .Returns("Players.GetShootOrigin", new Vector(0, 0, 64))
            .Returns("Players.IsAlive", true)
            .ReturnsEachCall("Clock.Now", () => clock())
            .Ignores("EntityRender.LookupSequenceByActivity");

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = TestServices.Services(out _);
        registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");

        var arsenal = new Arsenal(new RecordingLog(), new WeaponSwitchTests.SpawningEntities(engine, registry),
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
}

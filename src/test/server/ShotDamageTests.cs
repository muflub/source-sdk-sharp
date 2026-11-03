/*
 * TF2 STAGE 1 CORE: A SHOT'S DAMAGE AND ITS CRIT FLAG ARE THE WEAPON'S.
 *
 * The machine asks a behaviour for a primary attack's ShotDamage (damage per
 * bullet or swing, and whether it is critical) before the arms fire, with the
 * row's own numbers as the stock answer. The server's arms deal that damage and
 * open the crit flag beside the damage call in the DamageHitGroups scope; the
 * rules read it into DamageContext. The core multiplies nothing: what a crit
 * is worth is the mod's answer (stock TF: CTFWeaponBase, DMG_CRITICAL and x3).
 *
 * The rows are the SMG and the crowbar renamed, so the stock answers (the SMG1
 * ammo row's 4 per bullet, the crowbar's 10 per swing) are the control.
 */

using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class ShotDamageTests
{
    private const string Gun = "wep_shot_gun";
    private const string Bat = "wep_shot_bat";
    private const string Key = "wep.shot";
    private const int Attack = (int)InputButtons.Attack;

    private static readonly WeaponType GunRow =
        WeaponCatalog.Stock().For("weapon_smg1")!.Value with { Name = Gun, Behaviour = Key };
    private static readonly WeaponType BatRow =
        WeaponCatalog.Stock().For(EntityNames.WeaponCrowbar)!.Value with { Name = Bat, Behaviour = Key };

    private static readonly EntityHandle Player = new(0x0001_0001u);
    private static readonly EntityHandle Crate = new(0x0002_0007u);

    private sealed class ShotRows : IWeaponTypeSource
    {
        public IEnumerable<WeaponType> Rows() => [GunRow, BatRow];
    }

    private sealed class Source(IWeaponBehaviour behaviour) : IWeaponBehaviourSource
    {
        public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours()
            => [KeyValuePair.Create(Key, behaviour)];
    }

    /// <summary>Answers a fixed shot and remembers what it was offered, and on which run.</summary>
    private sealed class Decides(ShotDamage answer) : IWeaponBehaviour
    {
        public readonly List<(ShotDamage Offered, bool Live)> Asked = new();

        public ShotDamage ModifyShot(in WeaponAttackContext attack, ShotDamage shot)
        {
            Asked.Add((shot, attack.EffectsLive));
            return answer;
        }
    }

    /// <summary>Arms that record the shot each arm was handed.</summary>
    private sealed class Arms : IWeaponArms
    {
        public readonly List<ShotDamage> Bullets = new();
        public readonly List<ShotDamage> Swings = new();

        public void Sound(string soundScript) { }
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees)
            => throw new InvalidOperationException("the machine fires through the shot's arm");
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees, ShotDamage shot)
            => Bullets.Add(shot);
        public void MeleeSwing(in WeaponType type)
            => throw new InvalidOperationException("the machine swings through the shot's arm");
        public void MeleeSwing(in WeaponType type, ShotDamage shot) => Swings.Add(shot);
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }
        public int LookupSequence(WeaponAnim anim) => (int)anim + 1;
        public float AnimDuration(WeaponAnim anim) => 1.5f;
    }

    private static WeaponCatalog Catalog() => new(() => [new ShotRows()], new RecordingLog());

    private static WeaponBehaviours With(IWeaponBehaviour? behaviour)
        => behaviour is null ? WeaponBehaviours.Stock() : new(() => [new Source(behaviour)], new RecordingLog());

    private static (WeaponNetState w, ViewModelNetState vm) Armed(string row)
    {
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = Catalog().IndexOf(row);
        w.Clip = 10;
        w.Reserve = 30;
        return (w, vm);
    }

    private static Arms Fire(string row, IWeaponBehaviour? behaviour, bool live = true)
    {
        var arms = new Arms();
        var (w, vm) = Armed(row);
        new WeaponLogic(arms, Catalog(), With(behaviour)).RunCommand(w, vm, Attack, now: 10f, effectsLive: live);
        return arms;
    }

    //-------------------------------------------------------------------------
    // The machine.
    //-------------------------------------------------------------------------

    [Fact]
    public void WithNoBehaviourABulletCarriesItsAmmoRowsDamageAndNoCrit()
    {
        float smg1 = AmmoCatalog.Stock().For(GunRow.AmmoName)!.Value.Damage;

        var arms = Fire(Gun, behaviour: null);

        Assert.True(smg1 > 0f, "the control: the SMG1 ammo row has a damage");
        Assert.Equal([new ShotDamage(smg1, Critical: false, WeaponScriptDamage: true)], arms.Bullets);
    }

    [Fact]
    public void WithNoBehaviourASwingCarriesTheRowsMeleeDamageAndNoCrit()
    {
        var arms = Fire(Bat, behaviour: null);

        Assert.True(BatRow.MeleeDamage > 0f, "the control: the crowbar row has a damage");
        Assert.Equal([new ShotDamage(BatRow.MeleeDamage, Critical: false)], arms.Swings);
    }

    [Fact]
    public void ABehavioursShotIsTheDamageAndTheCritTheBulletArmIsHanded()
    {
        var decides = new Decides(new ShotDamage(37f, Critical: true));

        var arms = Fire(Gun, decides);

        Assert.Equal([new ShotDamage(37f, Critical: true)], arms.Bullets);
        Assert.Equal([(new ShotDamage(AmmoCatalog.Stock().For(GunRow.AmmoName)!.Value.Damage, WeaponScriptDamage: true), true)],
                     decides.Asked);
    }

    [Fact]
    public void ABehavioursShotIsTheDamageAndTheCritTheSwingIsHanded()
    {
        var decides = new Decides(new ShotDamage(65f, Critical: true));

        var arms = Fire(Bat, decides);

        Assert.Equal([new ShotDamage(65f, Critical: true)], arms.Swings);
        Assert.Equal([(new ShotDamage(BatRow.MeleeDamage), true)], decides.Asked);
    }

    [Fact]
    public void TheReplayAsksForTheShotAndFiresNothing()
    {
        var decides = new Decides(new ShotDamage(37f, Critical: true));

        var arms = Fire(Gun, decides, live: false);

        Assert.Equal([false], decides.Asked.Select(a => a.Live));
        Assert.Empty(arms.Bullets);
    }

    //-------------------------------------------------------------------------
    // The server's arms: the damage dealt, and the flag beside it.
    //-------------------------------------------------------------------------

    private static (Arsenal Arsenal, RecordingEngine Engine) ArmedArsenal(string row, IWeaponBehaviour? behaviour,
                                                                          DamageHitGroups hits,
                                                                          WeaponScriptDatabase? scripts = null)
    {
        var engine = Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
            .Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f))
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(true, 0.5f, new Vector(40f, 0f, 64f), new Vector(-1f, 0f, 0f), Crate))
            .Returns("WorldQuery.TraceLineSurface",
                     new SurfaceTraceResult(true, 0.001f, new Vector(64f, 0f, 64f), new Vector(-1f, 0f, 0f),
                                            Crate, 13, 0, HitGroup.Chest))
            .Returns("Clock.Now", 10f);
        engine.Strict = false;   // a valid player also animates; that is not these facts

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");

        var arsenal = new Arsenal(new RecordingLog(), new WeaponSwitchTests.SpawningEntities(engine, registry),
                                  engine, engine, engine, engine, engine, engine, engine, registry, engine, hits,
                                  weapons: Catalog(), behaviours: With(behaviour), scripts: scripts);

        Assert.True(arsenal.GiveWeapon(Player, row), "the mod row can be given");
        return (arsenal, engine);
    }

    /// <summary>
    /// Pulls the trigger once, past the deploy, and answers each damage call's
    /// damage and the crit flag open WHILE it ran, and the flag after.
    /// </summary>
    private static (List<(float Damage, bool Critical)> Hits, bool After) Shoot(string row, IWeaponBehaviour? behaviour,
                                                                                WeaponScriptDatabase? scripts = null)
    {
        var hits = new DamageHitGroups();
        var (arsenal, engine) = ArmedArsenal(row, behaviour, hits, scripts);

        var seen = new List<(float, bool)>();
        engine.WhenCalled("Damage.ApplyAt", call => seen.Add((call.ArgAs<float>(2), hits.Critical)));

        engine.Returns("Clock.Now", 30f);
        var cmd = new UserCmd { CommandNumber = 1, Buttons = Attack };
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);

        return (seen, hits.Critical);
    }

    [Fact]
    public void ACriticalBulletDealsTheBehavioursDamageWithTheFlagOpen()
    {
        var crit = Shoot(Gun, new Decides(new ShotDamage(37f, Critical: true)));
        var stock = Shoot(Gun, behaviour: null);

        Assert.Equal([(37f, true)], crit.Hits);
        Assert.False(crit.After, "the flag does not outlive the damage call");
        Assert.Equal([(AmmoCatalog.Stock().For(GunRow.AmmoName)!.Value.Damage, false)], stock.Hits);
    }

    /// <summary>
    /// The weapon script's damage is the shot's choice, not a guess from its
    /// number: a behaviour's shot (a TF weapon's) whose damage happens to equal
    /// the ammo row's is still dealt as it says, while the machine's stock shot
    /// takes the script's (HL2MP's FireBullets).
    /// </summary>
    [Fact]
    public void ABehavioursShotAtTheAmmoRowsDamageIsDealtAsGivenNotAsTheScripts()
    {
        float smg1 = AmmoCatalog.Stock().For(GunRow.AmmoName)!.Value.Damage;
        var scripts = new WeaponScriptDatabase(new RecordingLog());
        scripts.AddFromText(Gun, "WeaponData { \"damage\" \"12\" }");

        var decided = Shoot(Gun, new Decides(new ShotDamage(smg1)), scripts);
        var stock = Shoot(Gun, behaviour: null, scripts);

        Assert.True(smg1 != 12f, "the control: the script's damage is not the ammo row's");
        Assert.Equal([(smg1, false)], decided.Hits);
        Assert.Equal([(12f, false)], stock.Hits);
    }

    [Fact]
    public void ACriticalSwingDealsTheBehavioursDamageWithTheFlagOpen()
    {
        var crit = Shoot(Bat, new Decides(new ShotDamage(65f, Critical: true)));
        var stock = Shoot(Bat, behaviour: null);

        Assert.Equal([(65f, true)], crit.Hits);
        Assert.False(crit.After, "the flag does not outlive the damage call");
        Assert.Equal([(BatRow.MeleeDamage, false)], stock.Hits);
    }

    //-------------------------------------------------------------------------
    // The rules: the open flag is the DamageContext's.
    //-------------------------------------------------------------------------

    private sealed class Spy : IDamageRule
    {
        public readonly List<DamageContext> Seen = new();
        public Decision Decide(in DamageContext hit) { Seen.Add(hit); return Decision.UseStockRule; }
    }

    [Fact]
    public void TheRulesContextIsCriticalInsideACriticalShotsScopeOnly()
    {
        var hits = new DamageHitGroups();
        var spy = new Spy();
        var player = PlayerDamageRulesTests.Player(PlayerDamageRulesTests.Rules(team: 0, rule: spy, hits: hits));
        var attacker = new EntityHandle(0x0001_0002u);

        using (hits.Push(HitGroup.Head, critical: true))
            player.OnDamagedAt(attacker, 90f, DamageType.Bullet, default, default, HitGroup.Head);
        player.OnDamagedAt(attacker, 30f, DamageType.Bullet, default, default, HitGroup.Head);

        Assert.Equal([(90f, true), (30f, false)], spy.Seen.Select(c => (c.Damage, c.Critical)));
    }
}

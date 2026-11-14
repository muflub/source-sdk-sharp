using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Tests.Entities.Items;
using TF2.Tests.Projectiles;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <see cref="TFTauntAttack"/>: the three kill taunts' timed arms — the
/// keying on the scene the response rules answered
/// (tf_player.cpp:17979-18068), the PostThink window (:2691-2695) and
/// <c>DoTauntAttack</c>'s box and high noon (:18387-18451, :18613-18635),
/// the fencing chain re-arming itself through an empty room (:18436-18445).
/// </summary>
public sealed class TFTauntAttackTests
{
    /// <summary>One hit the component dealt, the scope's custom and weapon read at the call.</summary>
    private sealed record TauntHit(EntityHandle Victim, float Damage, DamageType Type, TFDamageCustom Custom,
                                   TFWeaponId Weapon, Vector Force, Vector Position);

    /// <summary>The damage op, recording each hit with the TF scope open at it.</summary>
    private sealed class TauntDamage(TFDamageScopes scopes) : IEngineDamage
    {
        public List<TauntHit> Hits { get; } = [];

        public void ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType, Vector force, Vector position)
        {
            scopes.TryCurrent(out var custom, out var weapon, out _);
            Hits.Add(new TauntHit(victim, damage, damageType, custom, weapon, force, position));
        }

        public void Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType)
            => ApplyAt(victim, attacker, damage, damageType, default, default);

        public void SetForceServerRagdoll(EntityHandle entity, bool force) { }

        public TakeDamageMode SetTakeDamageMode(EntityHandle entity, TakeDamageMode mode) => mode;

        public void RegisterRules(DamageRuleMasks masks) { }

        public void TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType,
                               Vector? direction = null, Vector forceOrigin = default, bool physicsOnly = false) { }

        public int TakeDamageAnswered(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType,
                                      Vector? direction = null, Vector forceOrigin = default, bool physicsOnly = false)
        {
            // The blast's hits arrive answered (TFBlastApplier.Deliver), the same scope read.
            scopes.TryCurrent(out var custom, out var weapon, out _);
            Hits.Add(new TauntHit(entity, damage, damageType, custom, weapon, default, forceOrigin));
            return 1;
        }
    }

    /// <summary>No scripted entity of ours stands in the way: the box's
    /// candidates are the players the engine names.</summary>
    private sealed class NoEntities : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>One fighter of <paramref name="playerClass"/> at the origin
    /// facing +X, and one enemy 64 units ahead of him: the geometry the box
    /// and the high noon read, every answer a fact can turn.</summary>
    private sealed class TauntRig
    {
        public readonly EntityHandle Fighter;
        public readonly EntityHandle Victim;

        public RecordingEngine Engine { get; } = RecordingEngine.Default();
        public FakeMapPlayers Players { get; } = new();
        public TFPlayerCarriers Carriers { get; } = new();
        public TFDamageScopes Scopes { get; } = new();
        public TauntDamage Damage { get; }
        public TFWeaponScripts Scripts { get; } = new(new RecordingLog(), WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses()));

        /// <summary>The arm's draw: the least of the range, the way every
        /// other test random answers (see <see cref="FixedRandom"/>).</summary>
        public FixedRandom Random { get; } = new();

        /// <summary>The effect sink behind the breath particle's name lookup.</summary>
        public RecordingEffectDispatch Temps { get; } = new();

        /// <summary>The medic's meter, read by the Uberslice's charge facts.</summary>
        public ChargeMeter Mediguns { get; } = new();

        /// <summary>The stats sink the inhale books its heals on.</summary>
        public FakeStats Stats { get; } = new();

        /// <summary>The launch seam behind the scorch shot's taunt shot.</summary>
        public RecordingProjectile Projectile { get; } = new();

        /// <summary>The tick's fighter, alive: the fired gate's leg a fact can drop.</summary>
        public bool Alive { set => Players.All[Fighter].Alive = value; }

        /// <summary>What the traces answer: the hit, its fraction and the entity named.</summary>
        public TraceResult Trace;

        /// <summary>Where the victim's collision box sits (its extents are a point).</summary>
        public Vector VictimOrigin { get; set; } = new(64f, 0f, 64f);

        /// <summary>The victim's team, as the engine answers it.</summary>
        public int VictimTeam { get; set; } = (int)TFTeam.Blue;

        public TFTauntAttack Attack { get; }

        public TauntRig(TFClass playerClass, TF2.Server.Weapons.ITFLunchboxEffects? lunchbox = null,
                        ITFWeaponOwners? owners = null, AttributeHooks? hooks = null)
        {
            (Fighter, _) = Players.Add(TFTeam.Red, playerClass);
            (Victim, _) = Players.Add(TFTeam.Blue, TFClass.HeavyWeapons);
            Players.All[Fighter].Conditions.Add(TFCond.Taunting);

            Damage = new TauntDamage(Scopes);
            Attack = new TFTauntAttack(Carriers, Players, Engine, Engine, Damage, Engine, Engine, Scripts, Scopes, new NoEntities(),
                                       lunchbox ?? new LunchboxStub(), owners ?? new NoTFWeaponOwners(), hooks ?? new AttributeHooks(),
                                       Random, Engine, Engine, Engine,
                                       new TFBlastApplier(Engine, Damage, Engine, Scopes, new DamageHitGroups(),
                                                          new DamageInflictors(), Carriers, new RecordingLog(), Engine,
                                                          owners ?? new NoTFWeaponOwners(), hooks ?? new AttributeHooks(),
                                                          new ProjectileRig.Resupply(), Engine, Engine,
                                                          new ProjectileRig.DirectoryView(), Carriers,
                                                          DeclaredConVars.Fresh<TF2.Server.TFPreroundPushFromDamageEnableConVar>()), Temps, Mediguns, Stats, Projectile);
            Attack.Attach(Fighter);

            Engine.Returns("Networking.EntityGetState", EntityState.Solid);
            Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
            // The held weapon, whose script id rides the scope.
            Engine.Returns("Players.GetActiveWeapon", "tf_weapon_scattergun");
            Engine.Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f));
            Engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
            Engine.Returns("Entities.GetBounds", (default(Vector), default(Vector)));
            Engine.ReturnsEachCall("Entities.GetWorldSpaceCenter", () => Arg() is EntityHandle h && h == Victim ? new Vector(64f, 0f, 64f) : new Vector(0f, 0f, 64f));
            Engine.ReturnsEachCall("Entities.GetOrigin", () => Arg() is EntityHandle h && h == Victim ? VictimOrigin : default(Vector));
            Engine.ReturnsEachCall("Entities.IsPlayer", () => Arg() is EntityHandle h && h == Victim);
            Engine.ReturnsEachCall("Players.GetTeam", () => Arg() is EntityHandle h && h == Victim ? VictimTeam : (int)TFTeam.Red);
            // One line for every ask: the victim stops it, until a fact says
            // a wall does or the shot flies free.
            Trace = new TraceResult(true, 0.5f, default, default, Victim);
            Engine.ReturnsEachCall("WorldQuery.TraceLine", () => Trace);
            // The box's candidates: the victim, until a fact empties the room.
            Engine.Returns("WorldQuery.EntitiesInSphere", 1);
            Engine.FillsSphere(Victim);
        }

        private object? Arg() => Engine.Calls.Count > 0 && Engine.Calls[^1].Args.Length > 0 ? Engine.Calls[^1].Args[0] : null;
    }

    private static TauntRig Armed(TFClass playerClass, string scene, float now = 100f)
    {
        var rig = new TauntRig(playerClass);
        rig.Attack.Arm(scene, now);
        return rig;
    }

    [Fact]
    public void TheAttackWaitsForItsTimeAndFiresOnce()
    {
        // PostThink's window (tf_player.cpp:2691-2695): the set time fires
        // once, past and not at, and the cleared time does not refire.
        var rig = Armed(TFClass.Pyro, TFTauntAttack.PyroHadoukenScene);

        rig.Attack.Tick(102.05f);
        Assert.Empty(rig.Damage.Hits);

        rig.Attack.Tick(102.15f);
        Assert.Single(rig.Damage.Hits);

        rig.Attack.Tick(103f);
        Assert.Single(rig.Damage.Hits);
    }

    [Fact]
    public void ADeadTaunterFiresNothingAndTheChainWithIt()
    {
        // DoTauntAttack's gate (:18379): no life, nothing — the whole arm,
        // the fencing chain's next swing included.
        var rig = Armed(TFClass.Spy, "scenes/player/spy/low/taunt03_v1.vcd");
        rig.Alive = false;

        rig.Attack.Tick(101.9f);
        rig.Attack.Tick(102.4f);
        rig.Attack.Tick(104.2f);

        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void AnUntauntedPlayerFiresNothing()
    {
        var rig = new TauntRig(TFClass.Pyro);
        rig.Players.All[rig.Fighter].Conditions.Remove(TFCond.Taunting);
        rig.Attack.Arm(TFTauntAttack.PyroHadoukenScene, 100f);

        rig.Attack.Tick(102.2f);

        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void TheHadoukenBurnsTheRoomForFiveHundredThrownUpAtFortyFiveDegrees()
    {
        // :18425-18427 — 500 DMG_BURN|DMG_IGNITE, the custom, the -45° launch
        // of 25000 (:18401) at the point 0.75 of the way over (:18412-18413).
        var rig = Armed(TFClass.Pyro, TFTauntAttack.PyroHadoukenScene);

        rig.Attack.Tick(102.2f);

        TauntHit hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(rig.Victim, hit.Victim);
        Assert.Equal(500f, hit.Damage);
        Assert.Equal(DamageType.Burn | DamageType.Plasma, hit.Type);
        Assert.Equal(TFDamageCustom.TauntatkHadouken, hit.Custom);
        Assert.Equal(17677.7f, hit.Force.X, 1);
        Assert.Equal(0f, hit.Force.Y, 1);
        Assert.Equal(17677.7f, hit.Force.Z, 1);
        Assert.Equal(new Vector(48f, 0f, 64f), hit.Position);
    }

    [Fact]
    public void TheHighNoonShootsWhatTheAimRestsOn()
    {
        // :18613-18632 — the eye's full pitch to 500 units, hitboxes in the
        // mask; whoever it stops on, a player of another real team, takes 500
        // DMG_BULLET from the WorldSpaceCenter, thrown up as the box's are.
        var rig = Armed(TFClass.HeavyWeapons, TFTauntAttack.HeavyHighNoonScene);

        rig.Attack.Tick(101.9f);

        TauntHit hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(rig.Victim, hit.Victim);
        Assert.Equal(500f, hit.Damage);
        Assert.Equal(DamageType.Bullet, hit.Type);
        Assert.Equal(TFDamageCustom.TauntatkHighNoon, hit.Custom);
        Assert.Equal(17677.7f, hit.Force.X, 1);
        Assert.Equal(17677.7f, hit.Force.Z, 1);
        Assert.Equal(new Vector(0f, 0f, 64f), hit.Position);
    }

    [Fact]
    public void AHighNoonThatHitsNothingFiresNothing()
    {
        // :18624 — fraction 1 is the whole flight, nobody in the way.
        var rig = Armed(TFClass.HeavyWeapons, TFTauntAttack.HeavyHighNoonScene);
        rig.Trace = new TraceResult(false, 1f, default, default, EntityHandle.None);

        rig.Attack.Tick(101.9f);

        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void TheHighNoonSparesTheShooterOwnTeam()
    {
        // :18628 — the same team's number is refused.
        var rig = Armed(TFClass.HeavyWeapons, TFTauntAttack.HeavyHighNoonScene);
        rig.VictimTeam = (int)TFTeam.Red;

        rig.Attack.Tick(101.9f);

        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void TheHighNoonSparesTheSharedTeams()
    {
        // :18628 — past LAST_SHARED_TEAM: unassigned and spectator are not
        // combatants to shoot.
        var rig = Armed(TFClass.HeavyWeapons, TFTauntAttack.HeavyHighNoonScene);
        rig.VictimTeam = (int)TFTeam.Spectator;

        rig.Attack.Tick(101.9f);

        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void TheFencingOpensWithTheWeakSlashHeldInRange()
    {
        // :18416-18419 — either slash: 25 DMG_SLASH|DMG_PREVENT_PHYSICS_FORCE,
        // the 100 push that cannot shove the victim out of the stab's box.
        var rig = Armed(TFClass.Spy, "scenes/player/spy/low/taunt03_v1.vcd");

        rig.Attack.Tick(101.9f);

        TauntHit hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(25f, hit.Damage);
        Assert.Equal(DamageType.Slash | DamageType.PreventPhysicsForce, hit.Type);
        Assert.Equal(TFDamageCustom.TauntatkFencing, hit.Custom);
        Assert.Equal(70.7f, hit.Force.X, 1);
    }

    [Fact]
    public void TheFencingChainReachesTheStabThroughItsTimes()
    {
        // :18436-18445 — slash B 0.47 after the slash A, the stab 1.73 after
        // that: 500 DMG_SLASH on the 20000 launch.
        var rig = Armed(TFClass.Spy, "scenes/player/spy/low/taunt03_v2.vcd");

        rig.Attack.Tick(101.85f);
        Assert.Single(rig.Damage.Hits);

        rig.Attack.Tick(102.4f);
        Assert.Equal(2, rig.Damage.Hits.Count);
        Assert.Equal(25f, rig.Damage.Hits[1].Damage);

        rig.Attack.Tick(104.2f);
        Assert.Equal(3, rig.Damage.Hits.Count);

        TauntHit stab = rig.Damage.Hits[2];
        Assert.Equal(500f, stab.Damage);
        Assert.Equal(DamageType.Slash, stab.Type);
        Assert.Equal(14142.1f, stab.Force.X, 1);
    }

    [Fact]
    public void TheFencingChainSwingsOnAnEmptyRoom()
    {
        // :18436's re-arm sits outside the box's if(count) (:18398-18434):
        // a slash into nobody still leads to the next.
        var rig = Armed(TFClass.Spy, "scenes/player/spy/low/taunt03_v1.vcd");

        rig.Engine.FillsSphere();
        rig.Attack.Tick(101.9f);
        Assert.Empty(rig.Damage.Hits);

        rig.Engine.FillsSphere(rig.Victim);
        rig.Attack.Tick(102.4f);
        Assert.Single(rig.Damage.Hits);
        Assert.Equal(25f, rig.Damage.Hits[0].Damage);
    }

    [Fact]
    public void TheTaunterIsNotHisOwnTarget()
    {
        // :18406 — the list carries the taunter; the swing skips him.
        var rig = Armed(TFClass.Pyro, TFTauntAttack.PyroHadoukenScene);

        rig.Engine.FillsSphere(rig.Fighter, rig.Victim);
        rig.Attack.Tick(102.2f);

        TauntHit hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(rig.Victim, hit.Victim);
    }

    [Fact]
    public void AWallBetweenEndsTheSwing()
    {
        // :18409's FVisible: something else stopping the eye's line, no hit.
        var rig = Armed(TFClass.Pyro, TFTauntAttack.PyroHadoukenScene);
        rig.Trace = new TraceResult(true, 0.5f, default, default, EntityHandle.None);

        rig.Attack.Tick(102.2f);

        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void AVictimClearOfTheBoxIsNotHit()
    {
        // The sphere is the candidate cut (:18397's partition); the box
        // (:18394-18395) is the test that stands.
        var rig = Armed(TFClass.Pyro, TFTauntAttack.PyroHadoukenScene);
        rig.VictimOrigin = new Vector(500f, 0f, 0f);

        rig.Attack.Tick(102.2f);

        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void AnIntangibleEffigyIsNotHit()
    {
        // FL_CLIENT|FL_OBJECT (:18397): a non-solid carrier effect is no
        // candidate, player or not.
        var rig = Armed(TFClass.Pyro, TFTauntAttack.PyroHadoukenScene);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.None);

        rig.Attack.Tick(102.2f);

        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void TheHeldWeaponRidesTheHit()
    {
        // CTakeDamageInfo's weapon (:18419 et al): the scopes carry the
        // active weapon's script id beside the custom.
        var rig = Armed(TFClass.Pyro, TFTauntAttack.PyroHadoukenScene);

        rig.Attack.Tick(102.2f);

        TFWeaponId expected = rig.Scripts.Get("tf_weapon_scattergun")!.Id;
        Assert.Equal(expected, Assert.Single(rig.Damage.Hits).Weapon);
    }

    [Fact]
    public void ANewTauntReplacesThePendingOne()
    {
        // :17874-17875 zeroes before the keying (:17979): a second taunt
        // before the first fires takes the window's place — the first arm's
        // moment passes silent, the second's fires.
        var rig = new TauntRig(TFClass.Pyro);
        rig.Attack.Arm(TFTauntAttack.PyroHadoukenScene, 100f);
        rig.Attack.Tick(100.5f);

        rig.Attack.Arm(TFTauntAttack.PyroHadoukenScene, 101f);

        rig.Attack.Tick(102.2f);
        Assert.Empty(rig.Damage.Hits);

        rig.Attack.Tick(103.2f);
        Assert.Single(rig.Damage.Hits);
    }

    [Fact]
    public void AClasslessSceneArmsNothing()
    {
        // The keying is per class and per scene (:17980-18068): a soldier's
        // taunt — or a pyro's wrong scene — leaves no arm behind, and the
        // window never opens.
        var rig = new TauntRig(TFClass.Soldier);
        rig.Attack.Arm(TFTauntAttack.PyroHadoukenScene, 100f);
        rig.Attack.Tick(103f);

        Assert.Empty(rig.Damage.Hits);

        var pyro = new TauntRig(TFClass.Pyro);
        pyro.Attack.Arm(TFTauntAttack.HeavyHighNoonScene, 100f);
        pyro.Attack.Tick(101.9f);

        Assert.Empty(pyro.Damage.Hits);
    }

    [Fact]
    public void TheSandvichArmBitesTheHeldLunchboxAndKeepsEating()
    {
        // The weapon-keyed arm (:17933-17947): any scene while the lunchbox is
        // the hand, the heavy's eat — then a bite a second on (:18564-18566),
        // till the taunt ends and the gate at the head drops the chain.
        var lunch = new LunchboxStub();
        var rig = new TauntRig(TFClass.HeavyWeapons, lunch);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_lunchbox");
        rig.Attack.Arm("scenes/player/heavy/low/taunt01.vcd", 100f);

        rig.Attack.Tick(101.1f);
        Assert.Equal(new[] { rig.Fighter.EntryIndex }, lunch.Bites);

        rig.Attack.Tick(102.2f);
        Assert.Equal(2, lunch.Bites.Count);

        rig.Players.All[rig.Fighter].Conditions.Remove(TFCond.Taunting);
        rig.Attack.Tick(103.3f);
        Assert.Equal(2, lunch.Bites.Count);
    }

    [Fact]
    public void TheEatKeepsItsClockThroughALunchboxlessBite()
    {
        // :18564's re-arm sits outside the cast's if (:18557-18562): the bite
        // needs the lunchbox, the next one is armed regardless — the hand put
        // away mid-taunt is caught again on the following bite.
        var lunch = new LunchboxStub();
        var rig = new TauntRig(TFClass.HeavyWeapons, lunch);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_lunchbox");
        rig.Attack.Arm("scenes/player/heavy/low/taunt01.vcd", 100f);

        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_scattergun");
        rig.Attack.Tick(101.1f);
        Assert.Empty(lunch.Bites);

        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_lunchbox");
        rig.Attack.Tick(102.2f);
        Assert.Equal(new[] { rig.Fighter.EntryIndex }, lunch.Bites);
    }

    /// <summary>The held item's own word on the attack: stock's
    /// <c>GetTauntAttackByName</c> keys the arm on the held item's
    /// <c>taunt attack name</c> before the scene's word
    /// (tf_player.cpp:17483-17495, matched case-insensitively at
    /// tf_shareddefs.cpp:1066-1075), so the gasblast's box — its own custom
    /// behind the hadouken's (:18429-18431) — rides a pyro's hadouken taunt
    /// when the held item says so, on the scene arm's own delay.</summary>
    [Fact]
    public void TheGasBlastWordInTheHeldItemNamesTheAttack()
    {
        var owners = new FakeOwners();
        var hooks = new AttributeHooks();
        var rig = new TauntRig(TFClass.Pyro, null, owners, hooks);
        owners.Held[rig.Fighter.EntryIndex] = "tf_weapon_scattergun";
        owners.Items[rig.Fighter.EntryIndex] = NamedItem("TAUNTATK_PYRO_GASBLAST", timeSeconds: null);

        rig.Attack.Arm(TFTauntAttack.PyroHadoukenScene, 100f);

        rig.Attack.Tick(102.05f);
        Assert.Empty(rig.Damage.Hits);

        rig.Attack.Tick(102.2f);            // the pyro arms' own delay still stands
        TauntHit hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(TFDamageCustom.TauntatkGasblast, hit.Custom);
    }

    /// <summary>The item's second word, <c>taunt attack time</c>
    /// (:17490-17494): it overrides the arm's own delay — the shipped
    /// thermal thruster's 4.7 s of wind-up keeps the hit past the scene's
    /// 2.1 s and lands it on the item's count.</summary>
    [Fact]
    public void TheTimeWordOfTheHeldItemSetsWhenTheAttackLands()
    {
        var owners = new FakeOwners();
        var hooks = new AttributeHooks();
        var rig = new TauntRig(TFClass.Pyro, null, owners, hooks);
        owners.Held[rig.Fighter.EntryIndex] = "tf_weapon_scattergun";
        owners.Items[rig.Fighter.EntryIndex] = NamedItem("TAUNTATK_PYRO_GASBLAST", timeSeconds: 4.7f);

        rig.Attack.Arm(TFTauntAttack.PyroHadoukenScene, 100f);

        rig.Attack.Tick(102.2f);            // the scene's own delay would have fired here
        Assert.Empty(rig.Damage.Hits);

        rig.Attack.Tick(104.8f);            // past the item's own 4.7 s
        TauntHit hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(TFDamageCustom.TauntatkGasblast, hit.Custom);
    }

    /// <summary>The held item's two words, as the schema's types carry them:
    /// the name a string attribute, the time a plain float one.</summary>
    private static AttributeContainer NamedItem(string attackName, float? timeSeconds)
    {
        var item = new AttributeContainer();
        item.Set(new AttributeValue(
            new AttributeDefinition(9001, TFTauntAttack.TauntAttackNameAttribute, TFTauntAttack.TauntAttackNameAttribute,
                                    AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                    false, false, AttributeValueType.String, MergedKeys.Block("tauntname")),
            0, attackName));
        if (timeSeconds is { } seconds)
            item.Set(AttributeValue.OfFloat(
                new AttributeDefinition(9002, TFTauntAttack.TauntAttackTimeAttribute, TFTauntAttack.TauntAttackTimeAttribute,
                                        AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                        false, false, AttributeValueType.Default, MergedKeys.Block("taunttime")),
                seconds));
        return item;
    }

    /// <summary>The held fists' fist type, as the hook walk carries it (<see cref="AttributeHooks.Int"/>
    /// reads the <c>Default</c>-typed float of stock's <c>GetFistType</c>, tf_weapon_fists.h:60): the
    /// radial buff's own mode is the 1 of FISTTYPE_RADIAL_BUFF (:21-22).</summary>
    private static AttributeContainer FistsMode(int mode)
    {
        var container = new AttributeContainer();
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(9003, "attr 9003", "set_weapon_mode", AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false, AttributeValueType.Default,
                                    MergedKeys.Block("fistsmode")), mode));
        return container;
    }

    /// <summary>The Eyelander's swing (:18941-18961): the eye's 128-unit line
    /// without hitboxes — MASK_SOLID never carries them (:18948) — and whoever
    /// it stops on, another team's player, takes 500 DMG_CLUB at the taunter's
    /// centre, thrown towards the taunter on stock's own sign (:18956-18958).</summary>
    [Fact]
    public void TheBarbarianSwingThrowsTheVictimTowardsTheTaunter()
    {
        var rig = Armed(TFClass.Demoman, TFTauntAttack.DemomanBarbarianScene);

        rig.Attack.Tick(102.6f);

        TauntHit hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(500f, hit.Damage);
        Assert.Equal(DamageType.Club, hit.Type);
        Assert.Equal(TFDamageCustom.TauntatkBarbarianSwing, hit.Custom);
        Assert.Equal(-12000f, hit.Force.X, 1);
        Assert.Equal(0f, hit.Force.Y, 1);
        Assert.Equal(0f, hit.Force.Z, 1);
        Assert.Equal(new Vector(0f, 0f, 64f), hit.Position);

        EngineCall trace = Assert.Single(rig.Engine.Calls, c => c.Member == "WorldQuery.TraceLine");
        Assert.Equal(new Vector(128f, 0f, 64f), trace.ArgAs<Vector>(1));   // eye + the 128 of :18945
        Assert.Equal(TraceMask.Solid, trace.ArgAs<TraceMask>(2));
    }

    /// <summary>:18950 — the line flies its whole 128 past a further victim;
    /// nobody is stopped, nobody is hit.</summary>
    [Fact]
    public void AVictimPastTheSwingsHundredTwentyEightIsNotHit()
    {
        var rig = Armed(TFClass.Demoman, TFTauntAttack.DemomanBarbarianScene);
        rig.Trace = new TraceResult(false, 1f, default, default, EntityHandle.None);

        rig.Attack.Tick(102.6f);

        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>:18954 — the same team's number is refused, as the high noon's
    /// is: a swing spares its own side.</summary>
    [Fact]
    public void TheBarbarianSwingSparesTheSameTeam()
    {
        var rig = Armed(TFClass.Demoman, TFTauntAttack.DemomanBarbarianScene);
        rig.VictimTeam = (int)TFTeam.Red;

        rig.Attack.Tick(102.6f);

        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>The scene's two and a half seconds (:18093-18100): the swing
    /// waits the whole delay and fires only past it.</summary>
    [Fact]
    public void TheBarbarianSwingWaitsTwoAndAHalfSeconds()
    {
        var rig = Armed(TFClass.Demoman, TFTauntAttack.DemomanBarbarianScene);

        rig.Attack.Tick(102.5f);
        Assert.Empty(rig.Damage.Hits);

        rig.Attack.Tick(102.6f);
        Assert.Single(rig.Damage.Hits);
    }

    /// <summary>The Sandman's grand slam (:18676-18730): with the sight line
    /// reaching whole (:18709), the knock stands the victim up (:18712-18715),
    /// takes 500 DMG_BULLET on the -45° launch of 130000 (:18717-18718), and
    /// fires the achievement event with both user ids (:18720-18727).</summary>
    [Fact]
    public void TheGrandSlamKnocksTheFacedVictimUpAndFilesItsEvent()
    {
        var rig = Armed(TFClass.Scout, TFTauntAttack.ScoutGrandSlamScene);
        rig.Trace = new TraceResult(true, 1f, default, default, rig.Victim);   // clear sight (:18709)
        rig.Engine.Returns("Players.GetUserId", 7);                             // both ids answer it (:18724-18725)
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(42));           // :18721
        rig.Engine.Returns("GameEvents.EventFire", true);                       // :18726

        rig.Attack.Tick(104.1f);

        TauntHit hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(500f, hit.Damage);
        Assert.Equal(DamageType.Bullet, hit.Type);
        Assert.Equal(TFDamageCustom.TauntatkGrandSlam, hit.Custom);
        Assert.Equal(91923.9f, hit.Force.X, 1);      // Forward(-45 pitch, yaw, 0) * 130000 (:18717)
        Assert.Equal(91923.9f, hit.Force.Z, 1);      // the upward half of the launch
        Assert.Equal(new Vector(0f, 0f, 64f), hit.Position);

        Assert.Equal(1, rig.Engine.CountOf("Entities.SetVelocity"));             // stand still (:18712)

        EngineCall punch = Assert.Single(rig.Engine.Calls, c => c.Member == "Players.SetPunch");
        Assert.Equal(10f, punch.ArgAs<Vector>(1).X, 1);                          // the drawn least of 10 to 15 (:18715)
        Assert.Equal(0f, punch.ArgAs<Vector>(1).Y, 1);

        Assert.Equal((10, 15), Assert.Single(rig.Random.DrawnRanges));           // :18715's own range
        EngineCall los = Assert.Single(rig.Engine.Calls, c => c.Member == "WorldQuery.TraceLine");
        Assert.Equal(TraceMask.Brushes, los.ArgAs<TraceMask>(2));                // MASK_SOLID_BRUSHONLY (:18707)

        EngineCall create = Assert.Single(rig.Engine.Calls, c => c.Member == "GameEvents.EventCreate");
        Assert.Equal("scout_grand_slam", create.ArgAs<string>(0));
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));
        EngineCall scoutId = Assert.Single(rig.Engine.Calls, c => c.Member == "GameEvents.EventSetInt" && c.ArgAs<string>(1) == "scout_id");
        Assert.Equal(7, scoutId.ArgAs<int>(2));
        EngineCall targetId = Assert.Single(rig.Engine.Calls, c => c.Member == "GameEvents.EventSetInt" && c.ArgAs<string>(1) == "target_id");
        Assert.Equal(7, targetId.ArgAs<int>(2));
    }

    /// <summary>:18690-18696 — the facing dot below 0.80 refuses: the victim's
    /// bounds touch the box, but the line from the centre runs too far off
    /// the yaw, so the knock never looks at him.</summary>
    [Fact]
    public void TheGrandSlamRefusesAVictimOffTheFacing()
    {
        var rig = Armed(TFClass.Scout, TFTauntAttack.ScoutGrandSlamScene);
        rig.Trace = new TraceResult(true, 1f, default, default, rig.Victim);       // sight is clear, so only the facing decides
        rig.VictimOrigin = new Vector(64f, 90f, 64f);                              // inside the box on broad bounds
        rig.Engine.Returns("Entities.GetBounds", (new Vector(-10f, -70f, -10f), new Vector(10f, 10f, 10f)));
        rig.Engine.ReturnsEachCall("Entities.GetWorldSpaceCenter", () => ArgIsVictim(rig) ? new Vector(64f, 90f, 64f) : new Vector(0f, 0f, 64f));

        rig.Attack.Tick(104.1f);

        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>:18709 — fraction below 1 means something stood between the
    /// centres: a wall ends the knock.</summary>
    [Fact]
    public void AWallBetweenEndsTheGrandSlam()
    {
        var rig = Armed(TFClass.Scout, TFTauntAttack.ScoutGrandSlamScene);

        rig.Attack.Tick(104.1f);   // the rig's line stops short, as behind a wall

        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>The scene's four seconds and three hundredths (:18036-18042):
    /// the slam waits the whole delay and fires only past it.</summary>
    [Fact]
    public void TheGrandSlamWaitsFourSecondsAndAHundredThreeTenths()
    {
        var rig = Armed(TFClass.Scout, TFTauntAttack.ScoutGrandSlamScene);
        rig.Trace = new TraceResult(true, 1f, default, default, rig.Victim);
        rig.Engine.Returns("GameEvents.EventCreate", IntPtr.Zero);   // the hit's tail: no event to build here

        rig.Attack.Tick(104.0f);
        Assert.Empty(rig.Damage.Hits);

        rig.Attack.Tick(104.1f);
        Assert.Single(rig.Damage.Hits);
    }

    /// <summary>The Huntsman's arrow stab (:18481-18524, keyed :18070-18076):
    /// the impale 0.85 s in holds the traced victim 3 s at full strength, no
    /// damage (:18498-18506); the kill it re-arms 1.30 s on (:18533-18536)
    /// skewers him for 500 with the arrow custom, thrown towards the taunter
    /// at the victim's centre (:18519-18523).</summary>
    [Fact]
    public void TheArrowStabStunsOnTheImpaleAndSkewersOnTheKill()
    {
        var rig = Armed(TFClass.Sniper, TFTauntAttack.SniperArrowScene);
        var stuns = new TFStunStack(rig.Carriers, rig.Players, rig.Engine);
        stuns.Attach(rig.Victim);

        rig.Attack.Tick(100.9f);   // the impale 0.85 s in (:18074)

        Assert.NotNull(stuns.ActiveStun);                                                                        // held whole (:18498)
        Assert.Equal(3f, stuns.ActiveStun!.Duration, 1);                                                         // :18499
        Assert.Equal(TFStunFlags.Movement | TFStunFlags.Controls | TFStunFlags.NoEffects, stuns.ActiveStun.Flags);   // :18501
        Assert.Equal(rig.Fighter, stuns.ActiveStun.Attacker);
        Assert.Empty(rig.Damage.Hits);                                                                           // the impale hurts nobody (:18498)

        rig.Attack.Tick(102.4f);   // the kill, re-armed 1.30 s after the fire (:18536)

        TauntHit hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(rig.Victim, hit.Victim);
        Assert.Equal(500f, hit.Damage);                                                          // :18519
        Assert.Equal(DamageType.Bullet | DamageType.PreventPhysicsForce, hit.Type);             // :18519
        Assert.Equal(TFDamageCustom.TauntatkArrowStab, hit.Custom);                              // the hit's own scope (:18520)
        Assert.Equal(-12000f, hit.Force.X, 1);                                                   // thrown towards the taunter (:18521-18523)
        Assert.Equal(0f, hit.Force.Y, 1);
        Assert.Equal(0f, hit.Force.Z, 1);
        Assert.Equal(new Vector(64f, 0f, 64f), hit.Position);                                    // the victim's centre (:18523)
    }

    /// <summary>:18519 — the kill re-traces: a victim who stepped out of the
    /// eye line before the kill takes nothing, though the impale's hold stands.</summary>
    [Fact]
    public void TheArrowKillRefusesAVictimWhoSteppedOutOfTheLine()
    {
        var rig = Armed(TFClass.Sniper, TFTauntAttack.SniperArrowScene);
        var stuns = new TFStunStack(rig.Carriers, rig.Players, rig.Engine);
        stuns.Attach(rig.Victim);

        rig.Attack.Tick(100.9f);
        Assert.NotNull(stuns.ActiveStun);

        rig.Trace = new TraceResult(false, 1f, default, default, EntityHandle.None);   // stepped clear of the line
        rig.Attack.Tick(102.4f);

        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>:18492 — a player on the taunter's own side is no victim:
    /// neither the hold nor the skewer lands on him.</summary>
    [Fact]
    public void TheArrowStabPassesOverATeammate()
    {
        var rig = Armed(TFClass.Sniper, TFTauntAttack.SniperArrowScene);
        rig.VictimTeam = (int)TFTeam.Red;   // the taunter's own side
        var stuns = new TFStunStack(rig.Carriers, rig.Players, rig.Engine);
        stuns.Attach(rig.Victim);

        rig.Attack.Tick(100.9f);
        rig.Attack.Tick(102.4f);

        Assert.Null(stuns.ActiveStun);
        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>The Equalizer's grenade kill (:18458-18476, keyed :18086): at
    /// the named hand's bone a TF explosion lands, and the blast of 200 over a
    /// radius of 100 takes the enemy beside it whole, the grenade custom
    /// riding the hit.</summary>
    [Fact]
    public void TheGrenadeBlastsWhoeverIsBesideTheHand()
    {
        var rig = Armed(TFClass.Soldier, TFTauntAttack.SoldierGrenadeScene);
        rig.Engine.Returns("Entities.LookupBone", 2);
        rig.Engine.Returns("Entities.GetIndex", 1);                                // the named hand resolves (:18459)
        rig.Engine.Returns("Entities.GetBonePosition", true);
        rig.Engine.Returns("Entities.GetBonePosition.origin", new Vector(64f, 0f, 63f));   // the bone's frame, beside the victim
        rig.Engine.Returns("Precache.PrecacheModel", 7);                                       // the default fireball sprite
        rig.Trace = new TraceResult(false, 1f, default, default, EntityHandle.None);          // a clear path to the victim

        rig.Attack.Tick(103.6f);   // the blast 3.5 s in (:18086)

        EngineCall effect = Assert.Single(rig.Engine.Calls, c => c.Member == "Effects.ExplosionEffect");   // :18470-18471
        Assert.Equal(new Vector(64f, 0f, 63f), effect.ArgAs<Vector>(0));
        Assert.Equal(7, effect.ArgAs<int>(1));                                   // the precached sprite rides along
        Assert.Equal(3f, effect.ArgAs<float>(2), 1);                             // the radius's scale 0.03 (:18471)
        Assert.Equal(100, effect.ArgAs<int>(5));
        Assert.Equal(200, effect.ArgAs<int>(6));

        TauntHit hit = Assert.Single(rig.Damage.Hits);   // the enemy at the hand takes the whole of it
        Assert.Equal(rig.Victim, hit.Victim);
        Assert.Equal(200f, hit.Damage);
        Assert.True(hit.Type.HasFlag(DamageType.Blast));                          // DMG_BLAST (:18473)
        Assert.Equal(TFDamageCustom.TauntatkGrenade, hit.Custom);                 // (:18474)
    }

    /// <summary>:5642-5646 — past the radius the box test skips the victim:
    /// one 150 units off the hand is outside the 100 and takes nothing.</summary>
    [Fact]
    public void TheGrenadeMissesWhoeverIsBeyondTheRadius()
    {
        var rig = Armed(TFClass.Soldier, TFTauntAttack.SoldierGrenadeScene);
        rig.Engine.Returns("Entities.LookupBone", 2);
        rig.Engine.Returns("Entities.GetIndex", 1);
        rig.Engine.Returns("Entities.GetBonePosition", true);
        rig.Engine.Returns("Entities.GetBonePosition.origin", new Vector(64f, 0f, 63f));
        rig.Engine.Returns("Precache.PrecacheModel", 7);
        rig.Trace = new TraceResult(false, 1f, default, default, EntityHandle.None);
        rig.VictimOrigin = new Vector(150f, 0f, 0f);   // outside the 100-unit radius

        rig.Attack.Tick(103.6f);

        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>:5636-5640 — no own pass is armed, so the thrower inside his
    /// own grenade takes it too: both players are hit, nobody is ignored.</summary>
    [Fact]
    public void TheGrenadeBlastsItsOwnThrowerToo()
    {
        var rig = Armed(TFClass.Soldier, TFTauntAttack.SoldierGrenadeScene);
        rig.Engine.Returns("Entities.LookupBone", 2);
        rig.Engine.Returns("Entities.GetIndex", 1);
        rig.Engine.Returns("Entities.GetBonePosition", true);
        rig.Engine.Returns("Entities.GetBonePosition.origin", new Vector(64f, 0f, 63f));
        rig.Engine.Returns("Precache.PrecacheModel", 7);
        rig.Engine.Returns("WorldQuery.EntitiesInSphere", 2);                 // both of them stand in the way
        rig.Engine.FillsSphere(rig.Fighter, rig.Victim);                      // the thrower first
        rig.Trace = new TraceResult(false, 1f, default, default, EntityHandle.None);

        rig.Attack.Tick(103.6f);

        Assert.Equal(2, rig.Damage.Hits.Count);                                                                       // the sweep skips nobody (:5638)
        Assert.Contains(rig.Damage.Hits, h => h.Victim == rig.Fighter && h.Damage > 0f);                              // the thrower, inside his own blast
        Assert.Contains(rig.Damage.Hits, h => h.Victim == rig.Victim && h.Damage >= 199.99f && h.Damage <= 200.01f);   // the enemy whole beside it
    }

    /// <summary>The Frontier Justice's guitar smash (:18103-18107 keyed, arm
    /// :18962-18980): the same 128-unit eye line as the barbarian swing, and the
    /// enemy it stops on takes 500 DMG_CLUB with the guitar custom, thrown
    /// towards the taunter at a gentle twelve (:18977-18979) at the taunter's
    /// centre.</summary>
    [Fact]
    public void TheGuitarSmashThrowsTheVictimTowardsTheTaunterAtTwelve()
    {
        var rig = Armed(TFClass.Engineer, TFTauntAttack.EngineerGuitarSmashScene);

        rig.Attack.Tick(103.7f);   // the smash 3.695 s in (:18105)

        TauntHit hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(rig.Victim, hit.Victim);
        Assert.Equal(500f, hit.Damage);
        Assert.Equal(DamageType.Club, hit.Type);
        Assert.Equal(TFDamageCustom.TauntatkEngineerGuitarSmash, hit.Custom);
        Assert.Equal(-12f, hit.Force.X, 1);                                     // stock's own twelve (:18979)
        Assert.Equal(0f, hit.Force.Y, 1);
        Assert.Equal(0f, hit.Force.Z, 1);
        Assert.Equal(new Vector(0f, 0f, 64f), hit.Position);                    // the taunter's centre (:18979)

        EngineCall trace = Assert.Single(rig.Engine.Calls, c => c.Member == "WorldQuery.TraceLine");
        Assert.Equal(new Vector(128f, 0f, 64f), trace.ArgAs<Vector>(1));        // eye + the 128 of :18966
    }

    /// <summary>:18971 — the line flies its whole 128 past a victim 150 units
    /// off: nobody is stopped, nobody is smashed.</summary>
    [Fact]
    public void TheGuitarSmashMissesAVictimBeyondTheTrace()
    {
        var rig = Armed(TFClass.Engineer, TFTauntAttack.EngineerGuitarSmashScene);
        rig.Trace = new TraceResult(false, 1f, default, default, EntityHandle.None);   // a whole 128 past the eye, the victim 150 off

        rig.Attack.Tick(103.7f);

        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>:18975 — the same team's number is refused, as the swing's and
    /// the stab's are.</summary>
    [Fact]
    public void TheGuitarSmashSparesTheSameTeam()
    {
        var rig = Armed(TFClass.Engineer, TFTauntAttack.EngineerGuitarSmashScene);
        rig.VictimTeam = (int)TFTeam.Red;   // the taunter's own side

        rig.Attack.Tick(103.7f);

        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>The scene's three seconds and six hundred ninety-five
    /// hundredths (:18105): the smash waits the whole delay.</summary>
    [Fact]
    public void TheGuitarSmashWaitsThreeSecondsAndSixHundredNinetyFiveHundredths()
    {
        var rig = Armed(TFClass.Engineer, TFTauntAttack.EngineerGuitarSmashScene);

        rig.Attack.Tick(103.6f);
        Assert.Empty(rig.Damage.Hits);

        rig.Attack.Tick(103.7f);
        Assert.Single(rig.Damage.Hits);
    }

    /// <summary>The Gunslinger's impale (:18108-18112 keyed, :18498-18511): the
    /// traced enemy is held 3 s at full strength (:18505) and punched for 1
    /// DMG_BULLET|DMG_PREVENT_PHYSICS_FORCE along the eye forward (:18510), the
    /// arm custom riding the hit.</summary>
    [Fact]
    public void TheArmImpaleStunsForThreeSecondsAndPunchesForOne()
    {
        var rig = Armed(TFClass.Engineer, TFTauntAttack.EngineerArmScene);
        var stuns = new TFStunStack(rig.Carriers, rig.Players, rig.Engine);
        stuns.Attach(rig.Victim);

        rig.Attack.Tick(103.25f);   // the impale 3.2 s in (:18110)

        Assert.NotNull(stuns.ActiveStun);                                                                            // held (:18505)
        Assert.Equal(3f, stuns.ActiveStun!.Duration, 1);
        Assert.Equal(TFStunFlags.Movement | TFStunFlags.Controls | TFStunFlags.NoEffects, stuns.ActiveStun.Flags);   // TF_STUN_BOTH | TF_STUN_NO_EFFECTS (:18505)
        Assert.Equal(rig.Fighter, stuns.ActiveStun.Attacker);

        TauntHit hit = Assert.Single(rig.Damage.Hits);                                                               // the punch is the impale's own (:18508-18511)
        Assert.Equal(rig.Victim, hit.Victim);
        Assert.Equal(1f, hit.Damage);
        Assert.Equal(DamageType.Bullet | DamageType.PreventPhysicsForce, hit.Type);
        Assert.Equal(TFDamageCustom.TauntatkEngineerArmKill, hit.Custom);
        Assert.Equal(1f, hit.Force.X, 1);                                     // the eye forward itself (:18510)
        Assert.Equal(new Vector(64f, 0f, 64f), hit.Position);                 // the victim's centre (:18510)
    }

    /// <summary>The blend chain (:18544-18553): after the impale, a blend every
    /// hundredth of a second each punching for 1, until the thirteenth hands
    /// over to the kill — 500 DMG_BLAST on the eye forward of 12000, at the
    /// victim's centre (:18526-18528).</summary>
    [Fact]
    public void TheArmBlendsEveryHundredthSecondUntilTheKillLands()
    {
        var rig = Armed(TFClass.Engineer, TFTauntAttack.EngineerArmScene);

        rig.Attack.Tick(103.25f);                       // the impale
        Assert.Single(rig.Damage.Hits);

        for (int blend = 0; blend < 13; blend++)        // :18549 — thirteen blends, then the kill
        {
            rig.Attack.Tick(103.4f + blend * 0.1f);     // each blend's 0.05 window passes whole under the tick
            Assert.Equal(blend + 2, rig.Damage.Hits.Count);
            Assert.Equal(1f, rig.Damage.Hits[blend + 1].Damage);
        }

        rig.Attack.Tick(104.7f);                        // the kill, 0.05 past the thirteenth blend
        TauntHit kill = Assert.Single(rig.Damage.Hits, h => h.Damage > 1f);   // :18526-18528
        Assert.Equal(500f, kill.Damage);
        Assert.Equal(DamageType.Blast, kill.Type);
        Assert.Equal(TFDamageCustom.TauntatkEngineerArmKill, kill.Custom);
        Assert.Equal(12000f, kill.Force.X, 1);          // eye forward × 12000 (:18527)
        Assert.Equal(new Vector(64f, 0f, 64f), kill.Position);               // the victim's centre (:18527)
    }

    /// <summary>:18544's re-arm sits outside the trace's if (:18488): a victim who
    /// stepped out of the line takes nothing more, though the chain keeps its
    /// hundredths coming.</summary>
    [Fact]
    public void TheArmTakesNothingMoreFromAVictimWhoSteppedAway()
    {
        var rig = Armed(TFClass.Engineer, TFTauntAttack.EngineerArmScene);

        rig.Attack.Tick(103.25f);                       // the impale's punch lands
        Assert.Single(rig.Damage.Hits);

        rig.Trace = new TraceResult(false, 1f, default, default, EntityHandle.None);   // stepped clear of the eye line
        rig.Attack.Tick(103.4f);
        rig.Attack.Tick(103.55f);                       // the blends keep coming past the empty line

        Assert.Single(rig.Damage.Hits);
    }

    /// <summary>:18492 — a player on the taunter's own side is no victim: neither
    /// the hold nor the punches land on him.</summary>
    [Fact]
    public void TheArmPassesOverATeammate()
    {
        var rig = Armed(TFClass.Engineer, TFTauntAttack.EngineerArmScene);
        rig.VictimTeam = (int)TFTeam.Red;   // the taunter's own side

        rig.Attack.Tick(103.25f);
        rig.Attack.Tick(103.4f);
        rig.Attack.Tick(104.7f);

        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>The Uberslice (:18056-18060 keyed, arms :18906-18931): the traced enemy is held
    /// 1.5 s at full strength and punched for 1 DMG_BULLET|PREVENT_PHYSICS_FORCE along the eye forward
    /// at the taunter's centre (:18913-18915); the kill it re-arms 0.75 s on (:18938) skewers him for
    /// 500 on the normalized throw towards the medic (:18921-18924) and puts half a charge on the
    /// medic's own meter (:18926-18930).</summary>
    [Fact]
    public void TheUbersliceImpalesThenKillsAndHalfChargesTheMedigun()
    {
        var rig = Armed(TFClass.Medic, TFTauntAttack.MedicUbersliceScene);
        var stuns = new TFStunStack(rig.Carriers, rig.Players, rig.Engine);
        stuns.Attach(rig.Victim);

        rig.Attack.Tick(102.3f);   // the impale 2.2 s in (:18058)

        Assert.NotNull(stuns.ActiveStun);                                                                            // held (:18913)
        Assert.Equal(1.5f, stuns.ActiveStun!.Duration, 1);                                                           // :18913
        Assert.Equal(TFStunFlags.Movement | TFStunFlags.Controls | TFStunFlags.NoEffects, stuns.ActiveStun.Flags);   // TF_STUN_BOTH | TF_STUN_NO_EFFECTS (:18913)
        Assert.Equal(rig.Fighter, stuns.ActiveStun.Attacker);

        TauntHit impale = Assert.Single(rig.Damage.Hits);                                                            // the punch is the impale's own (:18915)
        Assert.Equal(rig.Victim, impale.Victim);
        Assert.Equal(1f, impale.Damage);
        Assert.Equal(DamageType.Bullet | DamageType.PreventPhysicsForce, impale.Type);
        Assert.Equal(TFDamageCustom.TauntatkUberslice, impale.Custom);
        Assert.Equal(1f, impale.Force.X, 1);                                     // the eye forward itself (:18915)
        Assert.Equal(new Vector(0f, 0f, 64f), impale.Position);                  // the taunter's centre (:18915)
        Assert.Equal(0f, rig.Mediguns.Charge, 1);                                // charge rides only the kill (:18926)

        rig.Attack.Tick(103.1f);   // the kill, re-armed 0.75 s after the fire (:18938)

        TauntHit kill = Assert.Single(rig.Damage.Hits, h => h.Damage > 1f);                                          // :18924
        Assert.Equal(500f, kill.Damage);
        Assert.Equal(DamageType.Bullet | DamageType.PreventPhysicsForce, kill.Type);
        Assert.Equal(TFDamageCustom.TauntatkUberslice, kill.Custom);
        Assert.Equal(-12000f, kill.Force.X, 1);        // thrown towards the taunter (:18921-18922)
        Assert.Equal(0f, kill.Force.Y, 1);
        Assert.Equal(0f, kill.Force.Z, 1);
        Assert.Equal(new Vector(0f, 0f, 64f), kill.Position);                      // the taunter's centre (:18924)
        Assert.Equal(0.5f, rig.Mediguns.Charge, 1);                                // half a charge on the kill (:18929)
    }

    /// <summary>:18902 — a player on the taunter's own side is no victim: neither the hold nor either
    /// punch lands on him, and the medic's meter stays untouched with him.</summary>
    [Fact]
    public void TheUbersliceSparesATeammateAndHisMeterWithIt()
    {
        var rig = Armed(TFClass.Medic, TFTauntAttack.MedicUbersliceScene);
        rig.VictimTeam = (int)TFTeam.Red;   // the taunter's own side

        rig.Attack.Tick(102.3f);
        rig.Attack.Tick(103.1f);

        Assert.Empty(rig.Damage.Hits);
        Assert.Equal(0f, rig.Mediguns.Charge);                                     // the kill spares the meter too (:18927)
    }

    /// <summary>The scene's two seconds and two tenths (:18058): the hold waits the whole delay —
    /// nothing before it, the punch and the stun on the tick past it.</summary>
    [Fact]
    public void TheUbersliceWaitsTwoSecondsAndTwoTenths()
    {
        var rig = Armed(TFClass.Medic, TFTauntAttack.MedicUbersliceScene);
        var stuns = new TFStunStack(rig.Carriers, rig.Players, rig.Engine);
        stuns.Attach(rig.Victim);

        rig.Attack.Tick(102.1f);
        Assert.Empty(rig.Damage.Hits);
        Assert.Null(stuns.ActiveStun);             // nothing before the delay (:18058)

        rig.Attack.Tick(102.3f);
        Assert.Single(rig.Damage.Hits);
        Assert.NotNull(stuns.ActiveStun);
    }

    /// <summary>The Kritzkrieg's inhale (:18045-18055 keyed, arm :18873-18887): the hurt medic gains one health
    /// per tenth of a second from 0.8 s until the inhale's time at 1.8 s — about eleven heals, each booked to
    /// their healing stat (:18877-18880) — and none after the window closes (:18883-18887). The press plays the
    /// breath particle on the eyes attachment with the arm (:18050-18052).</summary>
    [Fact]
    public void TheInhaleHealsAHurtMedicUntilItsWindowCloses()
    {
        var rig = new TauntRig(TFClass.Medic);
        rig.Engine.Returns("Precache.ParticleSystemIndex", 9);
        rig.Engine.Returns("Entities.LookupAttachmentAny", 7);
        rig.Attack.Arm(TFTauntAttack.MedicInhaleScene, 100f);

        // The breath rides the press (:18050-18052): the record names the medic, the point-follow attach
        // type and the looked-up eyes attachment, the particle the team's own (:18051).
        DispatchedEffect breath = Assert.Single(rig.Temps.Effects);
        Assert.Equal("ParticleEffect", breath.Name);
        Assert.Equal(9, breath.Data.HitBox);                              // the looked-up system index
        Assert.Equal(4, breath.Data.DamageType);                          // PATTACH_POINT_FOLLOW (:18052)
        Assert.Equal(7, breath.Data.AttachmentIndex);                     // the eyes attachment (:18052)
        Assert.Equal(rig.Fighter.EntryIndex, breath.Data.EntIndex);
        EngineCall precache = Assert.Single(rig.Engine.Calls, c => c.Member == "Precache.ParticleSystemIndex");
        Assert.Equal(TFTauntAttack.InhaleParticleRed, precache.ArgAs<string>(0));   // healhuff_red for the red medic (:18051)

        FakeMapPlayer medic = rig.Players.All[rig.Fighter];
        medic.Health = 100;
        medic.MaxHealth = 150;

        int heals = 0;
        for (int frame = 0; frame <= 48; frame++)          // every tick up to the whole 0.8 s
        {
            int before = medic.Health;
            rig.Attack.Tick((float)(100.0 + frame / 60.0));
            if (medic.Health != before)
                heals++;
        }

        Assert.Equal(0, heals);                                  // nothing before the first heal's time (:18047)

        int healsByWindowEnd = heals;
        int healsAtTailStart = heals;
        for (int frame = 49; frame <= 180; frame++)          // every tick through the window and its tail
        {
            int before = medic.Health;
            rig.Attack.Tick((float)(100.0 + frame / 60.0));
            if (medic.Health != before)
                healsByWindowEnd++;
            if (frame == 150)
                healsAtTailStart = healsByWindowEnd;         // snapshot well past 1.8 s, once the chain has had its time...
        }

        Assert.InRange(healsByWindowEnd, 9, 12);                   // about a tenth-second heal each between 0.8 and 1.8 (:18047-18048)
        Assert.Equal(healsAtTailStart, healsByWindowEnd);          // ...which stops the chain past itself (:18883)
        Assert.Equal(100 + healsByWindowEnd, medic.Health);        // every heal one whole point, capped at the max (:18875)
        Assert.Equal(healsByWindowEnd, rig.Stats.Paid.Count);      // every heal booked whole (:18879)
        Assert.All(rig.Stats.Paid, paid => Assert.Equal(1, paid.Amount));     // one health each (:18875)
    }

    /// <summary>:18875-18880 — a medic at full health takes nothing: the capped heal answers zero and the healing stat stays unbooked.</summary>
    [Fact]
    public void AFullMedicGainsNothingFromTheInhale()
    {
        var rig = new TauntRig(TFClass.Medic);
        rig.Engine.Returns("Precache.ParticleSystemIndex", 9);
        rig.Engine.Returns("Entities.LookupAttachmentAny", 7);
        rig.Attack.Arm(TFTauntAttack.MedicInhaleScene, 100f);
        FakeMapPlayer medic = rig.Players.All[rig.Fighter];
        medic.Health = 150;
        medic.MaxHealth = 150;

        for (int frame = 0; frame <= 120; frame++)
            rig.Attack.Tick((float)(100.0 + frame / 60.0));

        Assert.Equal(150, medic.Health);                                   // the heal holds at the class max (:18875)
        Assert.Empty(rig.Stats.Paid);                                      // nothing taken, nothing booked (:18877)
    }

    /// <summary>The Scorch Shot (:18012-18015 keyed at 1.9 s, arm :18837-18848): past the delay the held
    /// flare gun launches its own projectile once — the script's damage, no charge, the owner riding the
    /// command — and the shot does not refire (:18845).</summary>
    [Fact]
    public void TheScorchShotLaunchesTheFlareGunOncePastTheDelay()
    {
        var rig = Armed(TFClass.Pyro, TFTauntAttack.PyroScorchShotScene);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_flaregun");

        rig.Attack.Tick(101.9f);                                           // the delay itself has not passed (:18014)
        Assert.Empty(rig.Projectile.Fired);

        rig.Attack.Tick(102f);
        (int owner, float damage, float charge) = Assert.Single(rig.Projectile.Fired);
        Assert.Equal(rig.Fighter.EntryIndex, owner);
        Assert.Equal(rig.Scripts.Get("tf_weapon_flaregun")!.Primary.Damage, damage);          // the script's own (:18845)
        Assert.Equal(0f, charge);

        rig.Attack.Tick(103f);
        Assert.Single(rig.Projectile.Fired);                                                    // one press, one flare
    }

    /// <summary>:18840 — the id gate: a plain flamethrower in the hand is no flare gun, so the
    /// scorch's press launches nothing.</summary>
    [Fact]
    public void TheScorchShotRefusesThePlainFlamethrower()
    {
        var rig = Armed(TFClass.Pyro, TFTauntAttack.PyroScorchShotScene);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_flamethrower");

        rig.Attack.Tick(102f);

        Assert.Empty(rig.Projectile.Fired);
    }

    /// <summary>The fists' radial buff (keyed :18025-18033 on the held fists' radial mode, arm
    /// :18585-18611): past its second every living mate within the reach gains 50 health (:18598) and the
    /// heal's own <c>player_healonhit</c> event, healed edict and stock's -1 defindex among its fields
    /// (:18601-18608).</summary>
    [Fact]
    public void TheRadialBuffHealsAHurtMateWithinReach()
    {
        var owners = new FakeOwners();
        var hooks = new AttributeHooks();
        var rig = new TauntRig(TFClass.HeavyWeapons, null, owners, hooks);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_fists");                      // the fists key the arm (:18025)
        owners.Held[rig.Fighter.EntryIndex] = "tf_weapon_fists";
        owners.Items[rig.Fighter.EntryIndex] = FistsMode(1);                                    // FISTTYPE_RADIAL_BUFF (:18028)
        rig.VictimTeam = (int)TFTeam.Red;                                                       // the heavy's own side (:18590)
        rig.Players.All[rig.Victim].Health = 100;
        rig.Players.All[rig.Victim].MaxHealth = 300;
        rig.Engine.Returns("Entities.GetIndex", 3);                                             // the healed mate's edict (:18605)
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(42));                           // :18602
        rig.Engine.Returns("GameEvents.EventFire", true);                                       // :18607
        rig.Attack.Arm("scenes/player/heavy/low/taunt01.vcd", 100f);                           // any taunt scene: the fists key it

        rig.Attack.Tick(101f);                                                                  // the second itself has not passed (:18030)
        Assert.Equal(100, rig.Players.All[rig.Victim].Health);

        rig.Attack.Tick(101.1f);
        Assert.Equal(150, rig.Players.All[rig.Victim].Health);                                  // the fifty whole, headroom to spare (:18598)

        EngineCall amount = Assert.Single(rig.Engine.Calls, c => c.Member == "GameEvents.EventSetInt" && c.ArgAs<string>(1) == "amount");
        Assert.Equal(50, amount.ArgAs<int>(2));                                                 // :18604
        EngineCall entindex = Assert.Single(rig.Engine.Calls, c => c.Member == "GameEvents.EventSetInt" && c.ArgAs<string>(1) == "entindex");
        Assert.Equal(3, entindex.ArgAs<int>(2));                                                // :18605
        EngineCall defIndex = Assert.Single(rig.Engine.Calls, c => c.Member == "GameEvents.EventSetInt" && c.ArgAs<string>(1) == "weapon_def_index");
        Assert.Equal(-1, defIndex.ArgAs<int>(2));                                               // INVALID_ITEM_DEF_INDEX (:18606)
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));                            // :18607
    }

    /// <summary>:18596 — one 1100 units off stands past the squared reach: no health, no heal event.</summary>
    [Fact]
    public void TheRadialBuffSkipsAMatePastItsReach()
    {
        var owners = new FakeOwners();
        var hooks = new AttributeHooks();
        var rig = new TauntRig(TFClass.HeavyWeapons, null, owners, hooks);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_fists");
        owners.Held[rig.Fighter.EntryIndex] = "tf_weapon_fists";
        owners.Items[rig.Fighter.EntryIndex] = FistsMode(1);
        rig.VictimTeam = (int)TFTeam.Red;
        rig.Players.All[rig.Victim].Health = 100;
        rig.Players.All[rig.Victim].MaxHealth = 300;
        rig.VictimOrigin = new Vector(1100f, 0f, 0f);                                  // past the squared reach of :18596
        rig.Engine.Returns("GameEvents.EventCreate", IntPtr.Zero);                    // the rig's own answer: no event to build
        rig.Attack.Arm("scenes/player/heavy/low/taunt01.vcd", 100f);

        rig.Attack.Tick(101.1f);

        Assert.Equal(100, rig.Players.All[rig.Victim].Health);                        // the skip of :18596
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventSetInt"));
    }

    /// <summary>:18590-18592 — stock walks the team's own list: the other side's hurt player stands
    /// outside it and gains nothing.</summary>
    [Fact]
    public void TheRadialBuffSparesTheOtherSide()
    {
        var owners = new FakeOwners();
        var hooks = new AttributeHooks();
        var rig = new TauntRig(TFClass.HeavyWeapons, null, owners, hooks);           // the victim stays the rig's Blue: never the heavy's side
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_fists");
        owners.Held[rig.Fighter.EntryIndex] = "tf_weapon_fists";
        owners.Items[rig.Fighter.EntryIndex] = FistsMode(1);
        rig.Players.All[rig.Victim].Health = 100;
        rig.Players.All[rig.Victim].MaxHealth = 300;
        rig.Attack.Arm("scenes/player/heavy/low/taunt01.vcd", 100f);

        rig.Attack.Tick(101.1f);

        Assert.Equal(100, rig.Players.All[rig.Victim].Health);                        // the enemy gate of :18592
    }

    /// <summary>The fists' arm pays the half-second crit bonus (:18599): half of the 800-point
    /// scale divided across the multiplier range (:9692) rides the meter as one generic event,
    /// and the half-second think weighs it onto the byte — total 80 maps to multiplier 1.5 and
    /// byte 25 (:9562-9566). The bonus rides the arm, not the heal: a press that found no mate
    /// still pays.</summary>
    [Fact]
    public void TheRadialBuffPaysItsHalfToTheCritMeter()
    {
        var owners = new FakeOwners();
        var hooks = new AttributeHooks();
        var rig = new TauntRig(TFClass.HeavyWeapons, null, owners, hooks);
        var ticker = new TFCritMultTicker(rig.Carriers, DeclaredConVars.Fresh<TFDamageEventsTrackForConVar>());
        ticker.Attach(rig.Fighter);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_fists");                      // the fists key the arm (:18025)
        owners.Held[rig.Fighter.EntryIndex] = "tf_weapon_fists";
        owners.Items[rig.Fighter.EntryIndex] = FistsMode(1);                                    // FISTTYPE_RADIAL_BUFF (:18028)
        rig.Attack.Arm("scenes/player/heavy/low/taunt01.vcd", 100f);

        Assert.Equal(0, ticker.CritMultByte);                                     // the empty list's own byte (:9527)
        rig.Attack.Tick(101.1f);                                                  // the buff arm fires (:18585-18611)
        ticker.Tick(105f);                                                        // the half-second think, the event 3.9 s old (:9548-9555)

        Assert.Equal(25, ticker.CritMultByte);                                    // half of 800 over five: mult 1.5, byte (int)25.5 (:9562-9566)
    }

    /// <summary>The armageddon (keyed :17987-17990 at three seconds, arm :18755-18836): every enemy the
    /// sight line reaches inside the hundred takes plasma — the first pick 400 strong with the
    /// armageddon custom (:18798, :18809), each next half again (:18818) — and the draw walks stock's
    /// pick slots backwards, the least of each range (:18800-18822): first, then next.</summary>
    [Fact]
    public void TheArmageddonHalvesAcrossItsEnemiesInDrawOrder()
    {
        var rig = Armed(TFClass.Pyro, TFTauntAttack.PyroArmageddonScene);
        var (second, _) = rig.Players.Add(TFTeam.Blue, TFClass.Scout);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h != rig.Fighter ? (int)TFTeam.Blue : (int)TFTeam.Red);
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h != rig.Fighter);      // both of them enemies (:18772)
        rig.Engine.Returns("WorldQuery.EntitiesInSphere", 2);
        rig.Engine.FillsSphere(rig.Victim, second);
        rig.Trace = new TraceResult(true, 1f, default, default, EntityHandle.None);   // the line reaches whole (:18788)

        rig.Attack.Tick(103.1f);

        Assert.Equal(2, rig.Damage.Hits.Count);
        TauntHit first = rig.Damage.Hits[0];                                           // the first pick (:18800-18804)
        Assert.Equal(rig.Victim, first.Victim);
        Assert.Equal(400f, first.Damage);                                              // :18798
        Assert.Equal(DamageType.Plasma, first.Type);                                   // DMG_PLASMA (:18809)
        Assert.Equal(TFDamageCustom.TauntatkArmageddon, first.Custom);
        Assert.Equal(new Vector(0f, 0f, 0f), first.Position);                          // the pyro's own origin (:18809)

        TauntHit next = rig.Damage.Hits[1];
        Assert.Equal(second, next.Victim);
        Assert.Equal(200f, next.Damage);                                               // half again (:18818)
        Assert.Equal([(0, 1), (0, 0)], rig.Random.DrawnRanges);                        // :18803's draws over the two slots
    }

    /// <summary>:18788-18790 — something other than the victim stops the brush-only line short of whole:
    /// the blocked enemy takes nothing.</summary>
    [Fact]
    public void TheArmageddonMissesWhoeverAWallHides()
    {
        var rig = Armed(TFClass.Pyro, TFTauntAttack.PyroArmageddonScene);
        rig.Trace = new TraceResult(true, 0.5f, default, default, EntityHandle.None);   // stopped short by a wall (:18789)

        rig.Attack.Tick(103.1f);

        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>:18775-18779 — the squared retest stands over the box query's list: one 150 units off is
    /// outside the hundred and takes nothing.</summary>
    [Fact]
    public void TheArmageddonMissesWhoeverIsBeyondTheRadius()
    {
        var rig = Armed(TFClass.Pyro, TFTauntAttack.PyroArmageddonScene);
        rig.VictimOrigin = new Vector(150f, 0f, 0f);        // outside the hundred of :18764

        rig.Attack.Tick(103.1f);

        Assert.Empty(rig.Damage.Hits);
    }

    /// <summary>The medic's own meter for the charge facts: the held charge the component reads and raises, clamped like the setter's (:18929).</summary>
    internal sealed class ChargeMeter : ITFMedigunWeapons
    {
        public float Charge;

        public void TickMedic(int ownerIndex, float now, float frametime) { }
        public void Holster(int ownerIndex, float now) { }
        public void Unholster(int ownerIndex, float now) { }
        public void RemoveMedigun(int ownerIndex, float now) { }
        public float ChargeLevel(int ownerIndex) => Charge;
        public void SetChargeLevel(int ownerIndex, float level) => Charge = Math.Clamp(level, 0f, 1f);

        public bool SubtractCharge(int ownerIndex, float amount)
        {
            Charge = MathF.Max(0f, Charge - MathF.Max(0f, amount));
            return true;
        }

        public void PreserveCharge(int ownerIndex, float amount) { }
        public int HealingTargetIndex(int ownerIndex) => -1;
        public bool IsReleasing(int ownerIndex) => false;
    }

    /// <summary>Whose handle the engine was last asked about, the same read
    /// the rig's own per-call answers make.</summary>
    private static bool ArgIsVictim(TauntRig rig) =>
        rig.Engine.Calls.Count > 0 && rig.Engine.Calls[^1].Args.Length > 0
        && rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == rig.Victim;

    /// <summary>The arm's draw, shared shape with the support rig's
    /// <c>LowestRandom</c>: the least of the range, the asked-for bounds on
    /// record.</summary>
    internal sealed class FixedRandom : IUniformRandom
    {
        public List<(int Min, int Max)> DrawnRanges { get; } = [];

        public int RandomInt(int min, int max)
        {
            DrawnRanges.Add((min, max));
            return min;
        }

        public float RandomFloat(float min, float max) => min;

        public float RandomFloatExp(float min, float max, float exponent) => min;
    }

    /// <summary>The bite counter the eat arm's facts read.</summary>
    private sealed class LunchboxStub : TF2.Server.Weapons.ITFLunchboxEffects
    {
        public List<int> Bites { get; } = [];
        public List<int> Drinks { get; } = [];

        public void LunchboxEat(in WeaponAttackContext attack, TFWeaponScript script) { }

        public void LunchboxDrop(in WeaponAttackContext attack, TFWeaponScript script, int mode) { }

        public void Bite(int playerIndex) => Bites.Add(playerIndex);

        public void Drink(int playerIndex) => Drinks.Add(playerIndex);
    }

    /// <summary>
    /// The launch seam's own record: each <c>Projectile</c> call the taunt arms
    /// make, as the shot's owner, damage and charge ride it. The other two arms
    /// answer nothing.
    /// </summary>
    internal sealed class RecordingProjectile : ITFProjectileEffects
    {
        public List<(int OwnerIndex, float Damage, float Charge)> Fired { get; } = [];

        public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge)
            => Fired.Add((attack.Command.OwnerIndex, shot.Damage, charge));

        public void Detonate(in WeaponAttackContext attack, TFWeaponScript script) { }

        public void ShockAttack(in WeaponAttackContext attack, TFWeaponScript script) { }
    }
}

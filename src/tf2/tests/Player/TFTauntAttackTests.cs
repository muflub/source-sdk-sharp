using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Tests.Entities.Items;
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
                                      Vector? direction = null, Vector forceOrigin = default, bool physicsOnly = false) => 1;
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
                                       Random, Engine);
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
}

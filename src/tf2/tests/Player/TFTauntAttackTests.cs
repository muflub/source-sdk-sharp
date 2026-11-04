using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
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
        public TFWeaponScripts Scripts { get; } = new(new RecordingLog(), WeaponFixtures.ShippedWithClasses());

        /// <summary>The tick's fighter, alive: the fired gate's leg a fact can drop.</summary>
        public bool Alive { set => Players.All[Fighter].Alive = value; }

        /// <summary>What the traces answer: the hit, its fraction and the entity named.</summary>
        public TraceResult Trace;

        /// <summary>Where the victim's collision box sits (its extents are a point).</summary>
        public Vector VictimOrigin { get; set; } = new(64f, 0f, 64f);

        /// <summary>The victim's team, as the engine answers it.</summary>
        public int VictimTeam { get; set; } = (int)TFTeam.Blue;

        public TFTauntAttack Attack { get; }

        public TauntRig(TFClass playerClass)
        {
            (Fighter, _) = Players.Add(TFTeam.Red, playerClass);
            (Victim, _) = Players.Add(TFTeam.Blue, TFClass.HeavyWeapons);
            Players.All[Fighter].Conditions.Add(TFCond.Taunting);

            Damage = new TauntDamage(Scopes);
            Attack = new TFTauntAttack(Carriers, Players, Engine, Engine, Damage, Engine, Engine, Scripts, Scopes, new NoEntities());
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
}

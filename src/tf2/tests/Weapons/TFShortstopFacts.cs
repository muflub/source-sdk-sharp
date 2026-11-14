using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Server.Projectiles;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

using TF2.Tests.Projectiles;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Shortstop's shove (<see cref="TFWeaponBehaviour.SecondaryAttack"/>'s
/// HandgunScoutPrimary arm, stock <c>CTFPistol_ScoutPrimary</c>,
/// tf_weapon_pistol.cpp): the alt-fire that spends no shot, defers its push
/// 0.2 s behind the animation and shoves the first enemy the muzzle's hull
/// sweep meets.
/// </summary>
public sealed class TFShortstopFacts
{
    private const int Attack2 = (int)InputButtons.Attack2;

    /// <summary>One press, then the frame tail past the 0.2 s delay.</summary>
    private static (TFWeaponWorld World, WeaponNetState W, ViewModelNetState Vm) Pressed()
    {
        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.Shipped()));
        var (w, vm) = world.Armed("tf_weapon_handgun_scout_primary", clip: 4, reserve: 36);
        world.Run(w, vm, Attack2, 10f);
        return (world, w, vm);
    }

    [Fact]
    public void The_row_is_the_shortstop_over_the_gun_behaviour()
    {
        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.Shipped()));

        // The seed (TFWeaponTypes' Names()) rows the equipped item; the gun
        // arm owns it and no ammunition secondary stands behind the alt-fire.
        var row = world.Row("tf_weapon_handgun_scout_primary");
        Assert.Equal("tf2.gun", row.Behaviour);
        Assert.Null(row.Secondary);
        Assert.Equal(TFWeaponId.HandgunScoutPrimary, WeaponFixtures.ParseExtra("tf_weapon_handgun_scout_primary").Id);
    }

    [Fact]
    public void The_shove_arms_its_delay_and_refires_without_spending_the_clip()
    {
        var (world, w, vm) = Pressed();

        // The two refires (:109-110) and the deferred push's clock (:111);
        // the shot count untouched — nothing is spent here.
        Assert.Equal(10.2f, w.BehaviourTimeA, 3);
        Assert.Equal(10.6f, w.NextPrimaryAttack, 3);
        Assert.Equal(11.5f, w.NextSecondaryAttack, 3);
        Assert.Equal(4, w.Clip);
        Assert.Contains(TFWeaponBehaviour.ShoveSound, world.Arms.Sounds);
        Assert.Contains(SourceSharp.Shared.Anim.WeaponGestureTrigger.Secondary, world.Arms.Gestures);
    }

    [Fact]
    public void The_pending_shove_fires_once_past_its_delay()
    {
        var (world, w, vm) = Pressed();

        world.Run(w, vm, 0, 10.5f);

        var shove = Assert.Single(world.Effects.Shoves);
        Assert.Equal(("tf_weapon_handgun_scout_primary", TFWeaponWorld.Owner), shove);
    }

    [Fact]
    public void The_cleared_latch_does_not_shove_twice()
    {
        var (world, w, vm) = Pressed();

        world.Run(w, vm, 0, 10.5f);
        world.Run(w, vm, 0, 10.7f);

        Assert.Empty(world.Effects.Shoves.Skip(1));
    }

    [Fact]
    public void The_landed_shove_speaks_the_plain_fire_concept()
    {
        var (world, w, vm) = Pressed();

        world.Run(w, vm, 0, 10.5f);

        // SpeakWeaponFire() with no concept (tf_weapon_pistol.cpp:190).
        Assert.Equal([""], world.Effects.Speeches);
    }

    [Fact]
    public void A_cloaked_scout_does_not_shove()
    {
        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.Shipped()));
        var (w, vm) = world.Armed("tf_weapon_handgun_scout_primary", clip: 4, reserve: 36);
        world.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.Stealthed);

        world.Run(w, vm, Attack2, 10f);
        world.Run(w, vm, 0, 10.5f);

        // CanAttack refuses before the refire (tf_weapon_pistol.cpp:100-101).
        Assert.Empty(world.Effects.Shoves);
        Assert.NotEqual(11.5f, w.NextSecondaryAttack);
    }
}

/// <summary>
/// The shove's landing (<see cref="TFShortstopShove.Shove"/>): the generic
/// pushback impulse (<c>CTFPlayer::ApplyGenericPushbackImpulse</c>,
/// tf_player.cpp:3369-3400) and the point of damage, on the first enemy the
/// candidate walk keeps.
/// </summary>
public sealed class TFShortstopPushFacts
{
    private static readonly EntityHandle Shooter = new(0x0001_0000u | 3);
    private static readonly EntityHandle Victim = new(0x0001_0000u | 5);

    /// <summary>Per-entity answers the walk reads; absent keys answer zero.</summary>
    private readonly Dictionary<EntityHandle, Vector> _origins = new();
    private readonly Dictionary<EntityHandle, int> _teams = new();

    private readonly RecordingEngine _engine = new();
    private readonly FakeConditions _conditions = new();
    private readonly FakeOwners _owners = new();
    private readonly RecordingLog _log = new();
    private readonly TFShortstopShove _shove;
    private readonly CountingLag _lag = new();
    private bool _seated;

    public TFShortstopPushFacts()
    {
        // The walk's own answer: the shooter's team, the victim's another,
        // the victim 100 units down the aim.
        _teams[Shooter] = 2;
        _teams[Victim] = 3;
        _origins[Shooter] = new(0f, 0f, 0f);
        _origins[Victim] = new(100f, 0f, 0f);

        int NextTeam() => LastHandle() is { } h && _teams.TryGetValue(h, out int t) ? t : 2;
        Vector NextOrigin() => LastHandle() is { } h && _origins.TryGetValue(h, out var v) ? v : default;

        // The roster: the shooter in slot 3, the victim in slot 5 once a
        // fact seats it, every other slot empty.
        _engine.ReturnsEachCall("Players.GetHandle", () => _engine.Calls[^1].ArgAs<int>(0) switch
        {
            3 => Shooter,
            5 when _seated => Victim,
            _ => EntityHandle.None,
        });
        _engine.Returns("Entities.IsValid", true);
        _engine.Returns("Entities.IsPlayer", true);
        _engine.Returns("Players.IsAlive", true);
        _engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        _engine.Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f));
        _engine.Returns("Entities.GetWorldSpaceCenter", new Vector(0f, 64f, 0f));
        _engine.ReturnsEachCall("Players.GetTeam", () => NextTeam());
        _engine.ReturnsEachCall("Entities.GetOrigin", () => NextOrigin());
        _shove = new TFShortstopShove(_engine, _engine, _engine, _engine, _engine,
                                      _conditions, _owners, new AttributeHooks(), _lag, _log);
    }

    /// <summary>The handle the last engine ask named, the per-entity reads' key.</summary>
    private EntityHandle? LastHandle() =>
        _engine.Calls.Count > 0 && _engine.Calls[^1].Args[0] is EntityHandle h ? h : null;

    private void Grounded(bool on)
    {
        _engine.Returns("Networking.EntityGetState", on ? EntityState.OnGround : EntityState.None);
        _engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
    }

    private static WeaponAttackContext Attack() =>
        new(default!, null!, null!, 1f, false, null!, new WeaponCommand(1, 5, 3));

    private static TFWeaponScript Script() => WeaponFixtures.ParseExtra("tf_weapon_handgun_scout_primary");

    [Fact]
    public void The_victim_takes_the_push_the_ground_floor_and_the_point()
    {
        Grounded(on: true);
        _seated = true;
        _engine.Returns("WorldQuery.TraceLine", FlameRig.Clear);
        _engine.Returns("WorldQuery.TraceHull", new HullResult(true, false, Victim));

        _shove.Shove(Attack(), Script());

        // 400 along the shooter-to-victim line, the grounded Z lifted to
        // JUMP_MIN_SPEED, the ground flag cleared with the impulse
        // (tf_player.cpp:3384-3393).
        var pushed = _engine.Last("Entities.ApplyVelocityImpulse");
        Assert.NotNull(pushed);
        Assert.Equal(Victim, pushed!.ArgAs<EntityHandle>(0));
        Vector impulse = pushed.ArgAs<Vector>(1);
        Assert.Equal(TFShortstopShove.PushForce, impulse.X, 2);
        Assert.Equal(0f, impulse.Y, 2);
        Assert.Equal(TFAirBlastDefs.JumpMinSpeed, impulse.Z, 2);
        Assert.True(pushed.ArgAs<bool>(2));

        // The point of damage (:174), melee-typed.
        var harm = _engine.Last("Damage.ApplyAt");
        Assert.NotNull(harm);
        Assert.Equal(Victim, harm!.ArgAs<EntityHandle>(0));
        Assert.Equal(Shooter, harm.ArgAs<EntityHandle>(1));
        Assert.Equal(TFShortstopShove.ShoveDamage, harm.ArgAs<float>(2), 4);

        // TF_COND_KNOCKED_INTO_AIR on the victim (:3388).
        Assert.Contains((Victim, TFCond.KnockedIntoAir, TFConditions.Permanent), _conditions.Added);
    }

    [Fact]
    public void The_victims_airblast_vulnerability_multiplies_the_push()
    {
        Grounded(on: true);
        _seated = true;
        _engine.Returns("WorldQuery.TraceLine", FlameRig.Clear);
        _engine.Returns("WorldQuery.TraceHull", new HullResult(true, false, Victim));
        _owners.Managers[Victim.EntryIndex] = ItemWith("airblast_vulnerability_multiplier", 1.5f);

        _shove.Shove(Attack(), Script());

        // ON_OTHER walk as the airblast's (:1326): 1.5 x 400.
        Vector impulse = _engine.Last("Entities.ApplyVelocityImpulse")!.ArgAs<Vector>(1);
        Assert.Equal(1.5f * TFShortstopShove.PushForce, impulse.X, 2);
    }

    [Fact]
    public void An_airborne_victim_keeps_the_push_as_given()
    {
        Grounded(on: false);
        _seated = true;
        _engine.Returns("WorldQuery.TraceLine", FlameRig.Clear);
        _engine.Returns("WorldQuery.TraceHull", new HullResult(true, false, Victim));

        _shove.Shove(Attack(), Script());

        // No ground under them: no floor to lift the Z to (:3385-3387).
        Vector impulse = _engine.Last("Entities.ApplyVelocityImpulse")!.ArgAs<Vector>(1);
        Assert.Equal(0f, impulse.Z, 2);
    }

    [Fact]
    public void A_teammate_is_not_shoved()
    {
        Grounded(on: true);
        _teams[Victim] = 2;
        _seated = true;
        _engine.Returns("WorldQuery.TraceLine", FlameRig.Clear);
        _engine.Returns("WorldQuery.TraceHull", new HullResult(true, false, Victim));

        _shove.Shove(Attack(), Script());

        Assert.Null(_engine.Last("Entities.ApplyVelocityImpulse"));
        Assert.Empty(_conditions.Added);
    }

    [Fact]
    public void A_victim_past_the_128_gate_is_not_shoved()
    {
        Grounded(on: true);
        _origins[Victim] = new(0f, 300f, 0f);
        _seated = true;
        _engine.Returns("WorldQuery.TraceLine", FlameRig.Clear);
        _engine.Returns("WorldQuery.TraceHull", new HullResult(true, false, Victim));

        _shove.Shove(Attack(), Script());

        // Stock's origin-to-origin gate (:147): 300 past 128, out.
        Assert.Null(_engine.Last("Entities.ApplyVelocityImpulse"));
    }

    [Fact]
    public void A_blocked_line_or_a_missed_hull_is_not_shoved()
    {
        Grounded(on: true);
        _seated = true;

        // A wall between: the trace stops short of the victim (:149-151).
        var walled = _engine;
        walled.Returns("WorldQuery.TraceLine", new TraceResult(true, 0.5f, default, default, EntityHandle.None));
        walled.Returns("WorldQuery.TraceHull", new HullResult(true, false, Victim));
        _shove.Shove(Attack(), Script());
        Assert.Null(walled.Last("Entities.ApplyVelocityImpulse"));

        // A hull sweep that ends in the air (:156-161).
        _engine.Clear();
        _engine.Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, default, default, EntityHandle.None));
        _engine.Returns("WorldQuery.TraceHull", new HullResult(false, false, default));
        _shove.Shove(Attack(), Script());
        Assert.Null(_engine.Last("Entities.ApplyVelocityImpulse"));
    }

    /// <summary>An item holding one attribute, the hook facts' shape.</summary>
    private static IAttributeProvider ItemWith(string hook, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(7, "attr " + hook, hook, AttributeDescriptionFormat.Percentage,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    [Fact]
    public void The_push_runs_inside_the_shooters_lag_compensation()
    {
        Grounded(on: true);
        _seated = true;
        _engine.Returns("WorldQuery.TraceLine", FlameRig.Clear);
        _engine.Returns("WorldQuery.TraceHull", new HullResult(true, false, Victim));

        _shove.Shove(Attack(), Script());

        // StartLagCompensation and FinishLagCompensation around the walk
        // (tf_weapon_pistol.cpp:126, :193), once each for the shooter.
        Assert.Equal([Shooter], _lag.Started);
        Assert.Equal([Shooter], _lag.Finished);
    }

    [Fact]
    public void A_vertical_vulnerability_scales_the_floored_rise()
    {
        Grounded(on: true);
        _seated = true;
        _engine.Returns("WorldQuery.TraceLine", FlameRig.Clear);
        _engine.Returns("WorldQuery.TraceHull", new HullResult(true, false, Victim));
        _owners.Managers[Victim.EntryIndex] = ItemWith("airblast_vertical_vulnerability_multiplier", 0.5f);

        _shove.Shove(Attack(), Script());

        // The vertical hook runs on the floored Z (tf_player.cpp:3388-3394).
        var push = _engine.Last("Entities.ApplyVelocityImpulse")!.Value.ArgAs<Vector>(1);
        Assert.Equal(TFAirBlastDefs.JumpMinSpeed * 0.5f, push.Z, 0.01f);
    }

    private sealed class CountingLag : ILagCompensation
    {
        public List<EntityHandle> Started { get; } = [];
        public List<EntityHandle> Finished { get; } = [];
        public void Start(EntityHandle player) => Started.Add(player);
        public void Start(EntityHandle player, in UserCmd cmd) => Started.Add(player);
        public void Finish(EntityHandle player) => Finished.Add(player);
        public bool IsCurrentlyDoingLagCompensation => Started.Count > Finished.Count;
        public void NoteWeaponFired(EntityHandle player) { }
    }
}

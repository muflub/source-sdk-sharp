using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Medigun;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Tests.Combat;
using TF2.Tests.Player;
using TF2.Tests.Projectiles;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Medigun;

/// <summary>
/// The Quick-Fix shares its patient's blast jump (<c>CTFPlayer::OnTakeDamage</c>,
/// <c>tf_player.cpp:9797-9829</c>): the applier tells its self-blast hooks of a
/// Soldier's, Demoman's or Pyro's own airborne blast after the push, and
/// <see cref="TFQuickFixJumpShare"/> throws each Quick-Fix medic healing the
/// jumper along the jumper's velocity, at most 900.
/// </summary>
public sealed class TFQuickFixJumpShareFacts
{
    private static readonly EntityHandle Projectile = new(0x0003_0009u);

    // ---- the applier's tell ----------------------------------------------

    private sealed class RecordingSelfBlast : ITFSelfBlastHook
    {
        public readonly List<EntityHandle> Jumped = new();

        public void SelfBlastJumped(EntityHandle player) => Jumped.Add(player);
    }

    /// <summary>A player of <paramref name="cls"/> at 100, its own rocket at the origin: the self pass reaches him.</summary>
    private static (RecordingSelfBlast Hook, PlayerRig Rig, TFBlastApplier Blast) SelfBlastRig(TFClass cls, EntityState state)
        => SelfBlastRig(cls, state, new ProjectileRig.DirectoryView(), new ProjectileRig.FixedOwners());

    private static (RecordingSelfBlast Hook, PlayerRig Rig, TFBlastApplier Blast) SelfBlastRig(
        TFClass cls, EntityState state, ProjectileRig.DirectoryView directory, ProjectileRig.FixedOwners owners)
    {
        var rig = new PlayerRig().As(cls);
        var hook = new RecordingSelfBlast();
        var blast = new TFBlastApplier(rig.Engine, rig.Engine, rig.Engine, new TFDamageScopes(), new DamageHitGroups(),
            new DamageInflictors(), rig.Carriers, rig.Log, rig.Engine, owners,
            new AttributeHooks(), new ProjectileRig.Resupply(), rig.Engine, rig.Engine,
            directory, rig.Carriers,
            DeclaredConVars.Fresh<TF2.Server.TFPreroundPushFromDamageEnableConVar>(),
            new TF2.Tests.Rules.ListHooks<ITFSelfBlastHook>(hook));

        rig.Engine.Returns("WorldQuery.EntitiesInSphere", 0);
        rig.Engine.Returns("Networking.EntityGetState", state);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetBounds", (default(Vector), default(Vector)));
        rig.Engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 1f, default, default, EntityHandle.None));
        rig.Engine.Returns("Damage.TakeDamageAnswered", 90);
        rig.Engine.ReturnsEachCall("GameEvents.EventCreate", () => new IntPtr(7));
        rig.Engine.Returns("GameEvents.EventFire", true);
        rig.Engine.Returns("Players.GetUserId", 9);
        return (hook, rig, blast);
    }

    private static void OwnRocket(TFBlastApplier blast)
        => blast.Apply(new Vector(0f, 0f, 0f),
                       TFBlast.Of(90f, TFProjectileDefs.RocketRadius, TFWeaponDamageTypes.Of(TFWeaponId.Rocketlauncher)),
                       PlayerRig.Player, Projectile, ignored: Projectile,
                       TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false,
                       selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

    [Fact]
    public void An_airborne_soldiers_own_blast_is_told_to_the_hooks()
    {
        var (hook, _, blast) = SelfBlastRig(TFClass.Soldier, EntityState.None);

        OwnRocket(blast);

        Assert.Equal(new[] { PlayerRig.Player }, hook.Jumped);
    }

    [Fact]
    public void A_grounded_soldiers_own_blast_is_not_told()
    {
        var (hook, _, blast) = SelfBlastRig(TFClass.Soldier, EntityState.OnGround);

        OwnRocket(blast);

        Assert.Empty(hook.Jumped);
    }

    /// <summary>The jump flags are the Soldier's, Demoman's and Pyro's (:8921-8922, :9797): a Heavy's is no jump.</summary>
    [Fact]
    public void An_airborne_heavys_own_blast_is_not_told()
    {
        var (hook, _, blast) = SelfBlastRig(TFClass.HeavyWeapons, EntityState.None);

        OwnRocket(blast);

        Assert.Empty(hook.Jumped);
    }

    /// <summary>The Pyro's flag asks no blast bit (:9797).</summary>
    [Fact]
    public void An_airborne_pyros_own_blast_is_told()
    {
        var (hook, _, blast) = SelfBlastRig(TFClass.Pyro, EntityState.None);

        OwnRocket(blast);

        Assert.Equal(new[] { PlayerRig.Player }, hook.Jumped);
    }

    /// <summary>
    /// A self blast with no own pass, the attacker in the sweep's own sphere:
    /// stock's Caber (<c>CTFStickBomb::Smack</c>'s blast, an airborne Demoman's own
    /// <c>DMG_BLAST</c>).
    /// </summary>
    private static void OwnBlastInTheSweep(PlayerRig rig, TFBlastApplier blast, DamageType bits)
    {
        rig.Engine.FillsSphere(PlayerRig.Player);
        blast.Apply(new Vector(100f, 0f, 0f), TFBlast.Of(90f, TFProjectileDefs.RocketRadius, bits),
                    PlayerRig.Player, Projectile, ignored: Projectile,
                    TFWeaponId.Stickbomb, "tf_weapon_stickbomb", critical: false);
    }

    private static DamageType BlastBits => TFWeaponDamageTypes.Of(TFWeaponId.Rocketlauncher);

    /// <summary>The sweep's hit arm (<c>TFBlastApplier</c>'s delivered push): an airborne Demoman's own blast in the sweep is told.</summary>
    [Fact]
    public void An_airborne_demomans_own_blast_in_the_sweep_is_told()
    {
        var (hook, rig, blast) = SelfBlastRig(TFClass.Demoman, EntityState.None);

        OwnBlastInTheSweep(rig, blast, BlastBits);

        Assert.Equal(new[] { PlayerRig.Player }, hook.Jumped);
    }

    /// <summary>The control: the same blast on the ground is no jump.</summary>
    [Fact]
    public void A_grounded_demomans_own_blast_in_the_sweep_is_not_told()
    {
        var (hook, rig, blast) = SelfBlastRig(TFClass.Demoman, EntityState.OnGround);

        OwnBlastInTheSweep(rig, blast, BlastBits);

        Assert.Empty(hook.Jumped);
    }

    /// <summary>
    /// The sweep's zero-damage arm: an item whose <c>no_self_blast_dmg</c> 2
    /// zeroes the self hit still throws, and the throw is told (stock reaches
    /// :9797 for the Rocket Jumper's harmless jumps too).
    /// </summary>
    [Fact]
    public void A_harmless_own_blast_in_the_sweep_is_told()
    {
        var owners = new ProjectileRig.FixedOwners
        {
            Item = new ProjectileRig().AttributeItem("no_self_blast_dmg", AttributeDescriptionFormat.Additive, 2f),
        };
        var (hook, rig, blast) = SelfBlastRig(TFClass.Demoman, EntityState.None, new ProjectileRig.DirectoryView(), owners);

        OwnBlastInTheSweep(rig, blast, BlastBits);

        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
        Assert.Equal(new[] { PlayerRig.Player }, hook.Jumped);
    }

    /// <summary>The flags ask dry (<c>!(GetFlags() &amp; FL_INWATER)</c>, :8921): an airborne Soldier in water is not told.</summary>
    [Fact]
    public void An_airborne_soldier_in_water_is_not_told()
    {
        var directory = new ProjectileRig.DirectoryView();
        var (hook, rig, blast) = SelfBlastRig(TFClass.Soldier, EntityState.None, directory, new ProjectileRig.FixedOwners());
        var player = new SsPlayer(rig.Engine, rig.Engine, TestServices.Impacts(rig.Engine), TestServices.Fluid(),
                                  TestServices.InfiniteAux(), TestServices.PrintDamage()) { Handle = PlayerRig.Player };
        player.State.BindLocal();
        player.State.WaterLevel = 1;
        directory.Holds(PlayerRig.Player, player);

        OwnRocket(blast);

        Assert.Empty(hook.Jumped);
    }

    /// <summary>The Soldier's flag asks <c>DMG_BLAST</c> (:8921): his own airborne damage without it is not told.</summary>
    [Fact]
    public void An_airborne_soldiers_own_damage_without_the_blast_bit_is_not_told()
    {
        var (hook, rig, blast) = SelfBlastRig(TFClass.Soldier, EntityState.None);

        OwnBlastInTheSweep(rig, blast, DamageType.Bullet);

        Assert.Empty(hook.Jumped);
    }

    /// <summary>The Pyro's flag asks no blast bit (:9797): his own airborne damage without it is told.</summary>
    [Fact]
    public void An_airborne_pyros_own_damage_without_the_blast_bit_is_told()
    {
        var (hook, rig, blast) = SelfBlastRig(TFClass.Pyro, EntityState.None);

        OwnBlastInTheSweep(rig, blast, DamageType.Bullet);

        Assert.Equal(new[] { PlayerRig.Player }, hook.Jumped);
    }

    // ---- the share --------------------------------------------------------

    private const int Jumper = 3;
    private const int QuickFixMedic = 5;
    private const int PlainMedic = 6;

    /// <summary><c>MEDIGUN_QUICKFIX</c>, the Quick-Fix's <c>set_weapon_mode</c> (tf_weapon_medigun.h:23-29).</summary>
    private const int QuickFixMode = 2;

    private static EntityHandle Handle(int index) => new(0x0001_0000u | (uint)index);

    private static AttributeContainer Mode(int mode)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(
            new AttributeDefinition(411, "set_weapon_mode", "set_weapon_mode", AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false, AttributeValueType.Default,
                                    MergedKeys.Block("x")), mode));
        return item;
    }

    /// <summary>The jumper at <paramref name="velocity"/>, healed by a Quick-Fix medic and a stock-medigun medic.</summary>
    private static (TFQuickFixJumpShare Share, RecordingEngine Engine) ShareRig(Vector velocity)
    {
        var engine = RecordingEngine.Default();
        var healing = new CombatRig.HealingSpy();
        healing.Healers[Jumper] = [QuickFixMedic, PlainMedic];
        var owners = new FakeOwners();
        owners.Held[QuickFixMedic] = "tf_weapon_medigun";
        owners.Held[PlainMedic] = "tf_weapon_medigun";
        owners.ByName[QuickFixMedic] = new() { ["tf_weapon_medigun"] = Mode(QuickFixMode) };
        owners.ByName[PlainMedic] = new() { ["tf_weapon_medigun"] = Mode(0) };

        engine.Returns("Entities.GetVelocity", velocity)
              .Returns("Entities.IsPlayer", true)
              .Returns("Players.GetActiveWeapon", "tf_weapon_medigun")
              .ReturnsEachCall("Players.GetHandle", () => engine.Calls[^1].Args[0] is int i ? Handle(i) : EntityHandle.None);

        var share = new TFQuickFixJumpShare(healing, engine, engine, new TFWeaponWorld().Parts.Scripts, owners,
                                            new AttributeHooks());
        return (share, engine);
    }

    /// <summary>The Quick-Fix medic alone is thrown, off the ground, along the jump at its speed.</summary>
    [Fact]
    public void The_quick_fix_medic_is_thrown_along_the_jump()
    {
        var (share, engine) = ShareRig(new Vector(300f, 0f, 400f));

        share.SelfBlastJumped(Handle(Jumper));

        var impulse = Assert.Single(engine.Calls, c => c.Member == "Entities.ApplyVelocityImpulse");
        Assert.Equal(Handle(QuickFixMedic), impulse.Args[0]);
        var thrown = (Vector)impulse.Args[1]!;
        Assert.Equal(300f, thrown.X, 3);
        Assert.Equal(0f, thrown.Y, 3);
        Assert.Equal(400f, thrown.Z, 3);
        Assert.Equal(true, impulse.Args[2]);
    }

    /// <summary>The throw's speed stops at 900 (:9822): a 1500 u/s jump throws the medic at 900 along it.</summary>
    [Fact]
    public void The_throw_stops_at_nine_hundred()
    {
        var (share, engine) = ShareRig(new Vector(0f, 900f, 1200f));

        share.SelfBlastJumped(Handle(Jumper));

        var thrown = (Vector)Assert.Single(engine.Calls, c => c.Member == "Entities.ApplyVelocityImpulse").Args[1]!;
        Assert.Equal(TFQuickFixJumpShare.MaxForce, thrown.Length, 2);
        Assert.Equal(540f, thrown.Y, 2);
        Assert.Equal(720f, thrown.Z, 2);
    }

    /// <summary>A medic whose medigun is out of the hands shares nothing (:9808: the ACTIVE weapon).</summary>
    [Fact]
    public void A_medic_without_his_medigun_out_shares_nothing()
    {
        var (share, engine) = ShareRig(new Vector(300f, 0f, 400f));
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_syringegun_medic");

        share.SelfBlastJumped(Handle(Jumper));

        Assert.Equal(0, engine.CountOf("Entities.ApplyVelocityImpulse"));
    }
}

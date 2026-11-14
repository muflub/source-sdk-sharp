using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Shared;

using TF2.Tests.Weapons;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Player;

/// <summary>
/// The shot's grant and its spend land together: the on-hit refund is a pool
/// grant the damage arms pay INSIDE the command (<c>TFDamageLayer</c>'s
/// <c>GiveAmmo</c>, mid-machine), and the command's settle carries the spend
/// (<c>Arsenal.OnPlayerCommand</c>'s <c>SettlePool</c>) — the reserve the
/// client decodes is the pool the settle made true, spend and grant both.
/// Found in the Widowmaker's HUD of 2026-11-05: the mid-command grant
/// mirrored the not-yet-settled pool over the machine's reserve, the spend
/// vanished from the settle's delta, and the HUD read a shot high (170 to
/// the server's 140) on every kill.
/// </summary>
public sealed class TFWidowmakerSettleTests
{
    private static readonly EntityHandle Shooter = new(0x0001_0003u);
    private static readonly EntityHandle Victim = new(0x0001_0004u);
    private static readonly EntityHandle Weapon = new(0x0003_0001u);
    private static readonly EntityHandle ViewModel = new(0x0003_0002u);

    private const string Shotgun = "tf_weapon_shotgun_primary";
    private static readonly Vector Muzzle = new(0f, 0f, 64f);
    private static readonly Vector HitAt = new(64f, 0f, 64f);

    /// <summary>The banked shot's grant, paid at the engine damage seam the
    /// layer's refund arm stands behind: the whole of the hit, into the metal
    /// — this fact's item's <c>add_onhit_addammo</code> at its hundred.</summary>
    private sealed class RefundingDamage : IEngineDamage
    {
        public Func<EntityHandle, float, int>? Refund;

        /// <summary>The damage the last grant was paid on: the fact's share.</summary>
        public float Dealt;

        public void ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType,
                            Vector force, Vector position)
        {
            Dealt = damage;
            int paid = Refund?.Invoke(attacker, damage) ?? 0;
        }

        public void Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType)
            => ApplyAt(victim, attacker, damage, damageType, default, default);

        public void SetForceServerRagdoll(EntityHandle entity, bool force) { }
        public TakeDamageMode SetTakeDamageMode(EntityHandle entity, TakeDamageMode mode) => mode;
        public void RegisterRules(DamageRuleMasks masks) { }

        public void TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType,
                               Vector? direction = null, Vector forceOrigin = default, bool physicsOnly = false)
        { }

        public int TakeDamageAnswered(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType,
                                      Vector? direction = null, Vector forceOrigin = default, bool physicsOnly = false)
            => 0;
    }

    private static (Arsenal Arsenal, ScriptedEntityRegistry Registry, RefundingDamage Damage) Pressed(int metalGiven = 100)
    {
        var created = new Queue<EntityHandle>([Weapon, ViewModel]);

        RecordingEngine engine = Mock().Returns("Entities.IsValid", true)
            .Returns("Entities.GetIndex", 3)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(32f, 0f, 64f))
            .Returns("Entities.GetAngles", default(Vector))
            .Returns("Entities.GetEyeAngles", default(Vector))
            .Returns("Players.GetShootOrigin", Muzzle)
            .Returns("Players.IsAlive", true)
            .Returns("EntityRender.LookupSequence", -1)
            .Returns("EntityRender.LookupSequenceByActivity", -1)
            .Returns("Clock.Now", 10f)
            .ReturnsEachCall("Entities.Create", () => created.Dequeue())
            .Returns("WorldQuery.TraceLineSurface",
                     new SurfaceTraceResult(true, 0.5f, HitAt, new Vector(-1f, 0f, 0f), Victim, 4, 0))
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(true, 1f, HitAt, Vector.Zero, EntityHandle.None))
            .Returns("WorldQuery.PointContents", Contents.Empty);
        engine.Strict = false;

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = TestServices.Services(out _);
        registry.KeyValue(Shooter, EntityNames.Player, "targetname", "fixture_player");
        registry.Spawn(Weapon, EntityNames.SsWeapon);
        registry.Spawn(ViewModel, EntityNames.PredictedViewModel);

        var world = new TFWeaponWorld();

        // The Widowmaker: the clip none, the metal the pool, thirty the shot,
        // the whole of the hit the refund (the hundred).
        world.Owners.Items[3] = TFWidowmakerTests.Widowed(("add_onhit_addammo", 100f));

        var damage = new RefundingDamage();

        var arsenal = new Arsenal(new RecordingLog(),
                                  engine, engine, engine, engine, engine, engine,   // entities..effects
                                  damage, engine, registry, engine,                 // damage, render, directory, clock
                                  new DamageHitGroups(),
                                  weapons: world.Catalog, ammo: world.Ammo, behaviours: world.Behaviours);

        // The refund, the layer's arm's shape: the hit's whole into the metal.
        damage.Refund = (who, dealt) => arsenal.GiveAmmo(who, (int)dealt, TFAmmoNames.Name(TFAmmo.Metal));

        Assert.True(arsenal.GiveAmmo(Shooter, metalGiven, TFAmmoNames.Name(TFAmmo.Metal)) > 0, "the metal is given");
        Assert.True(arsenal.GiveWeapon(Shooter, Shotgun), "the arsenal gives the shotgun");

        // Past the deploy frame the give made: the next attack fires.
        engine.Returns("Clock.Now", 30f);

        var cmd = new UserCmd { Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Shooter, in cmd, firstTime: true);

        return (arsenal, registry, damage);
    }

    [Fact]
    public void TheShotPaysItsPriceBeforeTheRefundLands()
    {
        var (arsenal, registry, damage) = Pressed();

        // The shot landed and paid its refund: the grant the damage arms made.
        Assert.True(damage.Dealt > 0f, "the shot landed damage");

        int pool = arsenal.PoolOf(Shooter, TFAmmoNames.Name(TFAmmo.Metal), out int metal) ? metal : -1;

        // The hundred-and-one the give left (the metal plus the equip's own
        // round), the shot's thirty from it, the grant onto that: once.
        Assert.Equal(71 + (int)damage.Dealt, pool);

        // The reserve the client decodes is that pool: the machine's spend
        // settled, the grant mirrored — never the pool the grant saw while
        // the spend was still owed.
        var weapon = Assert.IsType<SsWeapon>(registry.Get(Weapon));
        Assert.Equal(pool, weapon.State.Reserve);
    }

    /// <summary>
    /// Stock saves the refund (<c>m_iAmmoToAdd</c>, tf_weaponbase.cpp:5064-5078)
    /// and gives it after the shot's ammo comes off (tf_weaponbase_gun.cpp:387-394),
    /// so a full pool pays the shot and is topped back up. A refund granted first
    /// meets the cap, and the spend then leaves the pool thirty short.
    /// </summary>
    [Fact]
    public void A_full_pool_pays_the_shot_before_the_refund_tops_it_up()
    {
        var (arsenal, _, damage) = Pressed(metalGiven: 100_000);

        Assert.True(damage.Dealt >= 30f, "the shot landed at least its price");

        // Full again: nothing more fits.
        Assert.Equal(0, arsenal.GiveAmmo(Shooter, 100_000, TFAmmoNames.Name(TFAmmo.Metal)));
    }
}

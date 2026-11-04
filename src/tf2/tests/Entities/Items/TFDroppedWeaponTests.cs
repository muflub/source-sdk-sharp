using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Entities.Items;
using TF2.Tests.Projectiles;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Entities.Items;

/// <summary>
/// The dropped weapon itself (<c>CTFDroppedWeapon</c>, tf_dropped_weapon.cpp):
/// the debris spawn and the lifetime's removal think, the snapshot
/// <c>InitDroppedWeapon</c> carries, the two throw arms, and the charge
/// degrade's tick, rebook and pool.
/// </summary>
public sealed class TFDroppedWeaponTests
{
    /// <summary>
    /// The pack rig's planned draws, cycling: the tumble's two jitters, the
    /// band scale and the yaw spin, in the order Init draws them.
    /// </summary>
    private sealed class LoopSeeds : IUniformRandom
    {
        private readonly float[] _fractions = [0.9f, 0.3f, 0.5f, 0.5f];
        private int _at;

        public float RandomFloat(float min, float max) => min + (max - min) * _fractions[_at++ % _fractions.Length];
        public int RandomInt(int min, int max) => min + (int)((max - min) * _fractions[_at++ % _fractions.Length]);

        /// <summary>How many draws the loop has spent, for the draw-count facts.</summary>
        public int Draws => _at;
    }

    private static (TFDroppedWeapon Weapon, RecordingEngine Engine, RecordingEngine Clock, LoopSeeds Seeds) Dropped(float clock = 10f, string? lifetime = null)
    {
        var engine = Mock();
        engine.Returns("Clock.Now", clock)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetEyeAngles", default(Vector))
              .Returns("Entities.GetVelocity", default(Vector))
              .Returns("Entities.GetAngularVelocity", default(Vector))
              .Returns("EntityPhysics.InitPhysics", false);

        var convars = ConVarRegistry.For(UnattachedConsole());
        convars.RegisterAll(typeof(TFDroppedWeapon).Assembly);
        if (lifetime is not null)
            convars.Declared<TFDroppedWeaponLifetimeConVar>().Bind(-1, "tf_dropped_weapon_lifetime", lifetime);

        var seeds = new LoopSeeds();
        var dropped = new TFDroppedWeapon(convars.Declared<TFDroppedWeaponLifetimeConVar>(), seeds, engine);
        dropped.BindEngine(engine);
        dropped.BindAreas(Areas(engine));
        dropped.RegisteredKind = ScriptedEntityKind.Prop;

        var services = Services(out var clockEngine);
        clockEngine.Returns("Clock.Now", clock);
        dropped.Owner = new ScriptedEntityRegistry(TestEngines.Server()) { Services = services };

        return (dropped, engine, clockEngine, seeds);
    }

    private static EntityHandle Whose => new(0x0001_0001u);

    [Fact]
    public void The_lie_is_debris_with_the_lifetime_s_removal_booked()
    {
        var (dropped, engine, _, _) = Dropped();

        dropped.OnSpawn();

        Assert.Equal(CollisionGroup.Debris, engine.Last("EntityPhysics.SetCollisionGroup").ArgAs<CollisionGroup>(1));
        Assert.Equal(30f, dropped.NextThinkDelay("RemoveThink"), 3); // tf_dropped_weapon_lifetime, :131

        dropped.OnThink("RemoveThink"); // SUB_Remove
        Assert.Equal("Entities.Remove", engine.Last("Entities.Remove")!.Value.Member);
    }

    [Fact]
    public void The_lifetime_is_the_convar_read_live()
    {
        var (moved, _, _, _) = Dropped(lifetime: "7");
        moved.OnSpawn();

        Assert.Equal(7f, moved.NextThinkDelay("RemoveThink"), 3);
    }

    [Fact]
    public void The_init_carries_the_clip_the_reserve_the_charge_and_the_skin()
    {
        var (dropped, engine, _, _) = Dropped();
        dropped.OnSpawn();

        dropped.Init(Whose, "tf_weapon_rocketlauncher", clip: 6, ammo: 24, chargeLevel: 0.5f, skin: 1);

        Assert.Equal(Whose, dropped.LastOwner);
        Assert.Equal("tf_weapon_rocketlauncher", dropped.Weapon);
        Assert.Equal(6, dropped.Clip);
        Assert.Equal(24, dropped.Ammo);
        Assert.Equal(0.5f, dropped.ChargeLevel);
        Assert.Equal(1, dropped.Skin);
        Assert.Equal(1, engine.Last("EntityRender.SetSkin").ArgAs<int>(1));
        Assert.Equal(25f, engine.Last("EntityPhysics.SetMass").ArgAs<float>(1), 3); // :561
    }

    [Fact]
    public void A_death_tumble_leaves_at_the_planned_band_and_spin()
    {
        var (dropped, engine, _, seeds) = Dropped();
        dropped.OnSpawn();

        dropped.Init(Whose, "tf_weapon_rocketlauncher", 6, 24);

        // Normalised then scaled: the length IS the draw (0.5 of [100,150]),
        // whatever direction the right/up jitters point (:554-555).
        Assert.Equal(125f, engine.Last("Entities.SetVelocity").ArgAs<Vector>(1).Length, 3);
        Assert.Equal(50f, engine.Last("Entities.SetAngularVelocity").ArgAs<Vector>(1).Y, 3); // :562
        Assert.Equal(4, seeds.Draws); // two jitters, the band, the spin
    }

    [Fact]
    public void A_discard_hurls_it_along_the_thrower_s_view()
    {
        var (dropped, engine, _, _) = Dropped();
        dropped.OnSpawn();

        dropped.Init(Whose, "tf_weapon_rocketlauncher", 6, 24, thrown: true); // :538-545

        var thrown = engine.Last("Entities.SetVelocity").ArgAs<Vector>(1);
        Assert.Equal(250f, thrown.Length, 3); // the arm's scale, :544
        Assert.Equal(0f, thrown.Y, 3);        // eye level, no right/up in this arm
        Assert.True(thrown.X > 0f && thrown.Z > 0f, "forward and up, as the 1.5 z-constant and the view's forward are");
    }

    [Fact]
    public void A_charged_lie_degrades_a_hundredth_a_tenth_and_rebooks()
    {
        var (dropped, _, clock, _) = Dropped();
        dropped.OnSpawn();
        dropped.Init(Whose, "tf_weapon_medigun", 40, 0, chargeLevel: 0.5f); // :598-601

        Assert.Equal(TFDroppedWeapon.DegradeInterval, dropped.NextThinkDelay("ChargeLevelDegradeThink"), 3);

        clock.Returns("Clock.Now", 10.1f);
        dropped.OnThink("ChargeLevelDegradeThink");
        Assert.Equal(0.49f, dropped.ChargeLevel, 5);
        Assert.Equal(TFDroppedWeapon.DegradeInterval, dropped.NextThinkDelay("ChargeLevelDegradeThink"), 3);
    }

    [Fact]
    public void The_pool_at_zero_stops_the_degrade()
    {
        var (dropped, _, clock, _) = Dropped();
        dropped.OnSpawn();
        dropped.Init(Whose, "tf_weapon_medigun", 40, 0, chargeLevel: 0.02f);

        // 0.02 -> 0.01 -> 0.00 (zero is not below zero, stock's own border,
        // :678) -> the negative draw pools at zero and does not rebook.
        float at = 10f;
        for (int tick = 0; tick < 3; tick++)
        {
            at += TFDroppedWeapon.DegradeInterval;
            clock.Returns("Clock.Now", at);
            dropped.OnThink("ChargeLevelDegradeThink");
        }

        Assert.Equal(0f, dropped.ChargeLevel, 5);
        Assert.Equal(0f, dropped.NextThinkDelay("ChargeLevelDegradeThink"), 3); // the stale booking; nothing newer

        // And a pooled lie stays put.
        dropped.OnThink("ChargeLevelDegradeThink");
        Assert.Equal(0f, dropped.ChargeLevel, 5);
    }
}

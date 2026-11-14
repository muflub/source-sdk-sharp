using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Shared;
using TF2.Shared.Player;

using TF2.Tests.Weapons;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Combat;

/// <summary>
/// The shot's multi-damage: stock's <c>CDmgAccumulator</c> over
/// <c>FX_FireBullets</c> (<c>tf_fx_shared.cpp:294</c>, :388-396) — every
/// pellet's trace banks its share on the victim and the shot ends in ONE
/// <c>TakeDamage</c>, the pellets' damages and forces summed
/// (<c>takedamageinfo.cpp:242-248</c>). Found in the mad-milk payback of
/// 2026-11-05: the pellets reached the damage layer one apiece, so the
/// milked payback truncated per pellet — a ten-pellet blast of 60 paid 30
/// where the hit's own 60 pays 36 — and every per-hit effect ran ten times.
/// </summary>
public class TenPelletShotTests
{
    private static readonly EntityHandle Player = new(0x0001_0003u);
    private static readonly EntityHandle Victim = new(0x0001_0004u);

    private const string Scattergun = "tf_weapon_scattergun";
    private static readonly Vector Muzzle = new(0f, 0f, 64f);
    private static readonly Vector HitAt = new(64f, 0f, 64f);

    /// <summary>The pressed shot: the scattergun's ten pellets into one victim.</summary>
    private static (float Damage, float WholeShot) Fired()
    {
        var weapon = new EntityHandle(0x0003_0001u);
        var viewModel = new EntityHandle(0x0003_0002u);
        var created = new Queue<EntityHandle>([weapon, viewModel]);

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
            // Every pellet's trace ends on the victim.
            .Returns("WorldQuery.TraceLineSurface",
                     new SurfaceTraceResult(true, 0.5f, HitAt, new Vector(-1f, 0f, 0f), Victim, 4, 0))
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(true, 1f, HitAt, Vector.Zero, EntityHandle.None))
            .Returns("WorldQuery.PointContents", Contents.Empty);
        engine.Strict = false;

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        registry.Spawn(weapon, EntityNames.SsWeapon);
        registry.Spawn(viewModel, EntityNames.PredictedViewModel);

        var log = new RecordingLog();
        var world = new TFWeaponWorld();
        var scripts = new TFWeaponScripts(log, WeaponFixtures.ShippedWithClasses());

        var arsenal = new Arsenal(log, engine, engine, engine, engine, engine, engine, engine, engine,
                                  registry, engine, new DamageHitGroups(),
                                  weapons: world.Catalog, ammo: world.Ammo, behaviours: world.Behaviours);

        Assert.True(arsenal.GiveWeapon(Player, Scattergun), "the arsenal gives the scattergun");

        // Past the deploy frame the give made: the next attack fires.
        engine.Returns("Clock.Now", 30f);

        var cmd = new UserCmd { Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);

        var applied = engine.AllOf("Damage.ApplyAt").ToList();
        EngineCall hit = Assert.Single(applied);
        Assert.Equal(Victim, hit.ArgAs<EntityHandle>(0));

        // The shot's whole: the script's per-pellet damage times the row's
        // pellets — the ten 6s the behaviour's shot carries.
        float perPellet = scripts.Get(Scattergun)?.Primary.Damage
                          ?? throw new InvalidOperationException("no scattergun script");
        return (hit.ArgAs<float>(2), world.Row(Scattergun).Pellets * perPellet);
    }

    /// <summary>The blast is one hit of the whole shot's damage: the pellets'
    /// sum, once — the milked payback's 60, never ten paybacks of 6.</summary>
    [Fact]
    public void ATenPelletShotLandsAsOneDamage()
    {
        (float damage, float wholeShot) = Fired();

        Assert.True(damage > 0f, "the shot landed damage");
        Assert.Equal(wholeShot, damage, 3);
    }
}

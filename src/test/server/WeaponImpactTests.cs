/*
 * A ROUND THAT LANDS LEAVES A MARK.
 *
 * THE BUG. `Arsenal.FireBullets` -- the port's whole bullet path -- traced,
 * applied damage and, for a player, bled. It never dispatched an impact effect,
 * so a shot into a wall, a floor or a crate left no decal, no flecks, no dust
 * and no impact sound. Lane `live-batch` measured it with the whole firing
 * chain asserted first: a crate at the aim point, `debug-api aim` naming
 * prop_physics, carried rounds 90 -> 63, sprite quads +2115 -- and both effect
 * ledgers flat, temp entities 58 -> 58, impact particles 0 -> 0.
 *
 * IT WAS NEVER A REGRESSION. `git log -S'ImpactEffect' --follow` over
 * Arsenal.cs names no commit at all, across the sixteen that have touched it.
 * The call was never there to lose, so this is the first test of it.
 *
 * AND IT WAS SILENT, which is what makes it the shape plan_entity.md §7 forbids
 * rather than merely a gap: no refusal printed, no counter moved, nothing to
 * read. A test is the only thing that notices.
 *
 * WHAT IS PINNED HERE is the SERVER half -- that a landing round asks, what it
 * asks with, and for what. The client half (something in this build claiming
 * the dispatch name, and resolving the entity by a spelling this client can
 * answer) is `src/sourcesharp/test/impact_dispatch_test.cpp`, and that it
 * actually draws is `tools/fx-impact-gate`, where the native impact ledger goes
 * 0 -> 41 particles on five rounds against 0 -> 0 before.
 *
 * DRIVEN THROUGH THE REAL MACHINE, not through a stub arms object.
 * `ServerWeaponArms` is a private nested class over a private `Carry`, so there
 * is no way to construct one -- and that is the right shape to test anyway:
 * what a live server does is `OnPlayerCommand` with the attack button set, and
 * everything between that and the trace is part of what could break.
 */

using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class WeaponImpactTests
{
    private const uint PlayerHandle = 0x0001_0001;

    private static EntityHandle Player => new(PlayerHandle);

    /// <summary>The crate the round lands in, on the parallel hierarchy.</summary>
    private static EntityHandle Crate => new(0x0002_0007);

    /// <summary>Where the trace says the round stopped.</summary>
    private static Vector Landed => new(64f, 0f, 64f);

    /// <summary>
    /// The surfaceprop the trace found. 13 is not a magic number for its own
    /// sake — it is what a live run read off dm_lockdown's floor, and any value
    /// that is neither 0 nor -1 makes the point: it has to arrive at the effect
    /// unchanged, because the client picks the decal and the impact sound from
    /// the material behind it.
    /// </summary>
    private const int TracedSurfaceProp = 13;

    private const int Attack = (int)InputButtons.Attack;

    /// <summary>
    /// An armed player standing at the origin, looking down +X, with a surface
    /// 64 units in front of them.
    ///
    /// <para>
    /// The SHOOT ORIGIN and the EYE ANGLES are stated because the spread sample
    /// is built from them and the impact's start point is stepped back along
    /// the result; a fixture that left them at zero would make "the start is
    /// behind the end" true by accident rather than by the code.
    /// </para>
    /// </summary>
    private static (Arsenal arsenal, RecordingEngine engine) Armed(
        bool hit = true, EntityHandle? subject = null, int surfaceProp = TracedSurfaceProp,
        string weapon = "weapon_smg1", HitGroup traced = HitGroup.Generic,
        DamageHitGroups? hitGroups = null)
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.SsPlayer)
            .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
            .Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f))
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            // The floor probe a weapon drop takes. Nothing in this file drops a
            // weapon, but the Arsenal reaches it on other paths and an
            // unanswered member is a different failure from the one under test.
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(true, 0.5f, new Vector(0f, 0f, 8f),
                                     new Vector(0f, 0f, 1f), EntityHandle.None))
            .Returns("WorldQuery.TraceLineSurface",
                     new SurfaceTraceResult(hit, hit ? 0.001f : 1f, Landed,
                                            new Vector(-1f, 0f, 0f),
                                            subject ?? Crate,
                                            hit ? surfaceProp : -1,
                                            0, traced))
            .Returns("Clock.Now", 10f);

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);

        registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");

        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);

        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, hitGroups ?? new DamageHitGroups());

        arsenal.GiveWeapon(Player, weapon);

        return (arsenal, engine);
    }

    /// <summary>
    /// One command with the attack button set — and THE CLOCK MOVED FIRST.
    ///
    /// <para>
    /// Not incidental. Deploying a weapon sets <c>NextPrimaryAttack</c> ahead
    /// of the current time, so a trigger pulled at the same instant the weapon
    /// was given is refused by the machine and NOTHING about the impact path is
    /// exercised — nine of these facts went red on a correct tree that way, all
    /// with the message they would print for the defect itself. A fixture whose
    /// silence has two causes cannot tell them apart.
    /// </para>
    /// </summary>
    private static void PullTheTrigger(Arsenal arsenal, RecordingEngine engine)
    {
        engine.Returns("Clock.Now", 30f);

        var cmd = new UserCmd { Buttons = Attack };
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);
    }

    /// <summary>
    /// THE FIXTURE'S OWN PRECONDITION, asserted rather than assumed: the
    /// trigger really was pulled. Every fact below reads an ABSENCE or a value
    /// that only exists if a round was spent, and "the gun did not fire" would
    /// satisfy the absence and crash the value.
    /// </summary>
    private static void AssertARoundWasSpent(RecordingEngine engine)
    {
        Assert.True(engine.Called("Sound.EntityEmitSound"),
            "the weapon must actually have fired — a machine that refused the "
          + "trigger produces the same silence the defect did");
    }

    private static EngineCall? Impact(RecordingEngine engine)
        => engine.Calls.Where(c => c.Member == "Effects.ImpactEffect")
                       .Cast<EngineCall?>()
                       .FirstOrDefault();

    // =====================================================================
    // That it happens at all.
    // =====================================================================

    [Fact]
    public void ARoundThatLandsDispatchesAnImpactEffect()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.True(Impact(engine) is not null,
            "a round that landed must dispatch an impact effect. Stock's "
          + "CBaseEntity::FireBullets calls DoImpactEffect for every pellet "
          + "that lands (baseentity_shared.cpp:1986); this path called nothing, "
          + "and called it silently");
    }

    [Fact]
    public void ARoundThatLandsInNOTHINGDispatchesNoImpact()
    {
        var (arsenal, engine) = Armed(hit: false);

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.True(Impact(engine) is null,
            "a trace that was never stopped has nothing to mark. This is what "
          + "keeps the clause above from being a check that cannot fail");
    }

    // =====================================================================
    // What it carries.
    // =====================================================================

    [Fact]
    public void TheImpactLandsWhereTheTraceSaysTheRoundStopped()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.Equal(Landed, Impact(engine)!.Value.ArgAs<Vector>(1));
    }

    [Fact]
    public void TheImpactCarriesTheSURFACEPROPTheTraceFound()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.True(Impact(engine)!.Value.ArgAs<int>(2) == TracedSurfaceProp,
            "the client chooses the decal AND the impact sound from the "
          + "material behind this number — it is what CBaseEntity::ImpactTrace "
          + "writes into its CEffectData. A server that named none would leave "
          + "a wooden crate taking a concrete decal and cement flecks");
    }

    [Fact]
    public void TheImpactCarriesTheAmmoRowsDamageType()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.Equal(AmmoCatalog.Stock().For("SMG1")!.Value.DamageType,
                     Impact(engine)!.Value.ArgAs<DamageType>(3));
    }

    [Fact]
    public void TheImpactNamesWhatWasHit()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.Equal(Crate, Impact(engine)!.Value.ArgAs<EntityHandle>(4));
    }

    [Fact]
    public void TheImpactStartsBEHINDTheLandingPointSoTheClientCanRetrace()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Vector start = Impact(engine)!.Value.ArgAs<Vector>(0);
        Vector end = Impact(engine)!.Value.ArgAs<Vector>(1);

        Assert.True((end - start).Length > 1f,
            "the two points must not collapse: the client re-traces "
          + "start->origin in fx_impact.cpp's Impact() to find the plane it "
          + "decals, and a zero-length ray finds nothing");

        Assert.True(start.X < end.X,
            "and the start is on the side the shot came FROM — the player is "
          + "at the origin looking down +X, so a start point in front of the "
          + "impact would put the re-trace inside the solid");
    }

    // =====================================================================
    // Which trace it is on, and who it is for.
    // =====================================================================

    [Fact]
    public void TheShotIsOnTheTraceThatAnswersASurface()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.True(engine.Called("WorldQuery.TraceLineSurface"),
            "the narrow trace cannot answer a surfaceprop, and the surfaceprop "
          + "is what the whole impact is chosen from");
    }

    // =====================================================================
    // The push (plan_physics_managed A30): stock's FireBullets fills the
    // damage with CalculateBulletDamageForce, so what it hits is shoved.
    // =====================================================================

    [Fact]
    public void ARoundCarriesItsAmmosForceAlongTheShotToWhereItLanded()
    {
        var (arsenal, engine) = Armed();
        // The fixture ignores IsValid (false), so no other fact here reaches the
        // damage arm; this one must.
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;   // a valid player also animates; that is not this fact

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.ApplyAt");
        Assert.Equal(Crate, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(Player, hit.ArgAs<EntityHandle>(1));

        Vector force = hit.ArgAs<Vector>(4);
        Assert.InRange(force.Length, 667.9f, 668.1f);   // SMG1's DamageForce x phys_pushscale 1
        Assert.True(force.X > 600f, "along the shot: the player looks down +X");
        Assert.Equal(Landed, hit.ArgAs<Vector>(5));

        Assert.False(engine.Called("Damage.Apply"), "the forceless form is not used as well");
    }

    // =====================================================================
    // The hitgroup (tf2 stage 0.8): the trace's tr.hitgroup reaches the
    // victim beside the damage call, as stock hands TraceAttack the trace.
    // =====================================================================

    /// <summary>
    /// Fires one round whose trace stopped on <paramref name="traced"/> at a
    /// valid victim, and answers the hitgroup that was open WHILE the damage
    /// call ran, and the one open after the trigger was released.
    /// </summary>
    private static (HitGroup During, HitGroup After, int Calls) ShootAt(HitGroup traced)
    {
        var hitGroups = new DamageHitGroups();
        var (arsenal, engine) = Armed(traced: traced, hitGroups: hitGroups);
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;   // a valid player also animates; that is not this fact

        HitGroup during = (HitGroup)(-1);
        engine.WhenCalled("Damage.ApplyAt", _ => during = hitGroups.Current);

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        return (during, hitGroups.Current, engine.CountOf("Damage.ApplyAt"));
    }

    [Fact]
    public void ARoundCarriesTheTracesHitgroupIntoTheDamageCall()
    {
        var head = ShootAt(HitGroup.Head);
        var chest = ShootAt(HitGroup.Chest);

        Assert.True(head.Calls == 1 && chest.Calls == 1, "each round reached the damage call once");
        Assert.True(head.During == HitGroup.Head,
            $"a head trace is the head while the victim is hurt (read {head.During})");
        Assert.True(chest.During == HitGroup.Chest,
            $"and a chest trace the chest (read {chest.During}): the value is the trace's, not a constant");
    }

    [Fact]
    public void ARoundOnNoHitboxIsGenericAndTheScopeCloses()
    {
        var plain = ShootAt(HitGroup.Generic);
        var head = ShootAt(HitGroup.Head);

        Assert.True(plain.Calls == 1, "the round reached the damage call");
        Assert.True(plain.During == HitGroup.Generic,
            $"a trace that struck no hitbox hurts as Generic (read {plain.During})");
        Assert.True(head.After == HitGroup.Generic,
            "and the shot's hitgroup does not outlive its damage call: the next "
          + "hit, a blast or a fall, has no trace behind it");
    }

    [Fact]
    public void ASwingCarriesTheMeleeForceForItsDamage()
    {
        // CBaseHLBludgeonWeapon::Hit: CalculateMeleeDamageForce, damage x
        // ImpulseScale( 75, 4 ) along the swing, at the trace's end.
        var (arsenal, engine) = Armed(weapon: "weapon_crowbar");
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;
        var struck = new Vector(40f, 0f, 64f);
        engine.Returns("WorldQuery.TraceLine",
                       new TraceResult(true, 0.5f, struck, new Vector(-1f, 0f, 0f), Crate));

        PullTheTrigger(arsenal, engine);

        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.ApplyAt");
        Assert.Equal(Crate, hit.ArgAs<EntityHandle>(0));
        float damage = hit.ArgAs<float>(2);
        Assert.True(damage > 0f, "the swing did damage");
        Vector force = hit.ArgAs<Vector>(4);
        Assert.InRange(force.X, damage * DamageForceRules.PerDamagePoint * 0.999f, damage * DamageForceRules.PerDamagePoint * 1.001f);
        Assert.Equal(struck, hit.ArgAs<Vector>(5));
    }

    [Fact]
    public void ABulletsForceIsTheAmmoForceAlongTheNormalisedDirection()
    {
        Vector force = DamageForceRules.BulletForce(new Vector(0f, 2f, 0f), 668f);

        Assert.Equal(new Vector(0f, 668f, 0f), force);
        Assert.Equal(new Vector(0f, 1336f, 0f), DamageForceRules.BulletForce(new Vector(0f, 2f, 0f), 668f, pushScale: 2f));
    }

    [Fact]
    public void AMeleeForceIsTheDamageTimesTheSeventyFiveKgFourInchImpulse()
    {
        Vector force = DamageForceRules.MeleeForce(new Vector(3f, 0f, 0f), damage: 25f);

        Assert.Equal(new Vector(25f * DamageForceRules.ImpulseScale(75f, 4f), 0f, 0f), force);
    }

    [Fact]
    public void ARoundIntoTheWORLDDispatchesAnImpactToo()
    {
        // No entity at all — which is what a shot into a wall or a floor is,
        // and it is the COMMON case. Stock dispatches for it: UTIL_ImpactTrace
        // resolves such a trace to worldspawn and CWorld::ImpactTrace answers.
        var (arsenal, engine) = Armed(subject: EntityHandle.None);

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.True(Impact(engine) is not null,
            "an impact must not be gated on naming a subject. The damage arm "
          + "needs one; the mark a bullet leaves does not, and a wall is what "
          + "most rounds hit");
    }

    [Fact]
    public void ARoundIntoTheWORLDStillDamagesNothing()
    {
        var (arsenal, engine) = Armed(subject: EntityHandle.None);

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.False(engine.Called("Damage.Apply"),
            "and the damage arm keeps its own guard — this is the other half "
          + "of the clause above, and without it 'ungate the impact' could be "
          + "read as 'ungate everything'");
    }

    // =====================================================================
    // Per pellet, not per pull.
    // =====================================================================

    [Fact]
    public void EveryPelletOfABuckshotLoadLeavesItsOwnMark()
    {
        var (arsenal, engine) = Armed();
        arsenal.GiveWeapon(Player, "weapon_shotgun");
        arsenal.Switch(Player, "weapon_shotgun");

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        int pellets = WeaponCatalog.Stock().For("weapon_shotgun")!.Value.Pellets;
        int marks = engine.Calls.Count(c => c.Member == "Effects.ImpactEffect");

        Assert.True(marks == pellets,
            $"a shotgun that fired {pellets} pellet(s) into a surface left "
          + $"{marks} mark(s). One per pellet is stock — FireBullets' loop "
          + "calls DoImpactEffect inside it, not after it");
    }
}

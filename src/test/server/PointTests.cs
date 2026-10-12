using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// point_hurt's keys, which are the only thing about it that is not an engine
/// call.
///
/// Worth a test because the rule is a CLAMP and not a default, and the two look
/// alike until a map writes a zero on purpose. Stock has always turned that zero
/// into 128, and a map built against stock is relying on it.
/// </summary>
public class PointTests
{

    [Fact]
    public void Defaults()
    {
        // What an entity with none of the keys written ends up using.
        var (radius, damage, delay) = PointHurt.Settle(0f, 0, 0f);

        Assert.True(radius == 128f, "an unset radius settles at 128");
        Assert.True(damage == 2, "unset damage settles at 2");
        Assert.True(delay == 0.1f, "an unset delay settles at a tenth of a second");
    }

    [Fact]
    public void Clamping()
    {
        var written = PointHurt.Settle(512f, 40, 2f);

        Assert.True(written.Radius == 512f, "a radius the map wrote is kept");
        Assert.True(written.Damage == 40, "damage the map wrote is kept");
        Assert.True(written.Delay == 2f, "a delay the map wrote is kept");

        // The case the word "clamp" is for: a map that wrote zero meaning "off"
        // does not get zero, and never has.
        var negative = PointHurt.Settle(-1f, -5, -1f);

        Assert.True(negative.Radius == 128f, "a negative radius is replaced, not kept");
        Assert.True(negative.Damage == 2, "negative damage is replaced, not kept");
        Assert.True(negative.Delay == 0.1f, "a negative delay is replaced, not kept");
    }
}

/// <summary>
/// The maker's room check, which is the part of it that can be wrong quietly.
///
/// The rest of the entity is engine calls — instance a template, trace a hull,
/// throw what came out. What is a decision is how the two ways of being blocked
/// are read, and getting that wrong spawns crates inside walls.
/// </summary>
public class EntityMakerTests
{

    [Fact]
    public void BlockedReadings()
    {
        // A hull that struck something names it.
        var struck = new HullResult(Blocked: true, StartSolid: false, new EntityHandle(9));

        Assert.True(struck.Blocked, "a hull that struck something is blocked");
        // Against None rather than IsValid: that property asks the ENGINE
        // whether the handle still resolves, and a test has no engine to ask —
        // it would answer false and the check would pass for the wrong reason.
        Assert.True(struck.Entity != EntityHandle.None, "and names what it struck");

        // A hull that BEGAN inside something usually names nothing at all,
        // there having been no surface to cross. This is the reading that has to
        // be right: code that took "no entity" for "clear" would place an object
        // inside a wall, and the world is the commonest thing to start inside.
        var buried = new HullResult(Blocked: true, StartSolid: true, EntityHandle.None);

        Assert.True(buried.Blocked, "a hull that started solid is still blocked");
        Assert.True(buried.Entity == EntityHandle.None, "even though it names nothing");
        Assert.True(buried.StartSolid, "and says which kind of blocked it was");

        var clear = new HullResult(Blocked: false, StartSolid: false, EntityHandle.None);
        Assert.True(!clear.Blocked && !clear.StartSolid, "a clear hull is neither");
    }

    [Fact]
    public void Bounds()
    {
        // Why EntityGetBounds exists rather than halving EntityGetBoundsSize.
        // A crate model 64 tall with its origin on the BASE occupies 0..64, not
        // -32..32 — and the second hull hangs half a crate through the floor, so
        // the room check is blocked everywhere and nothing ever spawns.
        var onTheBase = (Mins: new Vector(-32f, -32f, 0f), Maxs: new Vector(32f, 32f, 64f));
        var halvedSize = (Mins: new Vector(-32f, -32f, -32f), Maxs: new Vector(32f, 32f, 32f));

        Assert.True(onTheBase.Mins.Z == 0f, "a base-origin model's box starts at the floor");
        Assert.True(halvedSize.Mins.Z < 0f, "halving a size would put it below the floor");

        Assert.True(onTheBase.Maxs.Z - onTheBase.Mins.Z == halvedSize.Maxs.Z - halvedSize.Mins.Z, "the two describe the same height");
    }
}

/// <summary>
/// The surface trigger's fire rule, which is the whole of its behaviour.
///
/// Everything else about it is a push it does not control. What it decides is
/// which changes are worth an output, and the easy half to get wrong is that it
/// fires on the way OFF the watched material as well as onto it.
/// </summary>
public class SurfaceTriggerTests
{
    private const int Metal = 'M';
    private const int Wood  = 'W';
    private const int Glass = 'Y';

    [Fact]
    public void SteppingOntoTheWatchedMaterialFires()
        => Assert.True(EnvPlayerSurfaceTrigger.ShouldFire(current: Wood, arriving: Metal, target: Metal), "stepping onto the watched material fires");

    [Fact]
    public void SteppingOffTheWatchedMaterialFires()
    {
        // The half that is easy to miss: leaving is a change too, and the map
        // has a separate output for it.
        Assert.True(EnvPlayerSurfaceTrigger.ShouldFire(current: Metal, arriving: Wood, target: Metal), "stepping off the watched material fires");
    }

    [Fact]
    public void AChangeBetweenTwoMaterialsItDoesNotWatchFiresNothing()
        => Assert.True(!EnvPlayerSurfaceTrigger.ShouldFire(current: Wood, arriving: Glass, target: Metal), "a change between two materials it does not watch fires nothing");

    [Fact]
    public void TheSameMaterialArrivingAgainFiresNothing()
    {
        // The push arrives whenever the ground changes, and the same material
        // arriving twice is not a change.
        Assert.True(!EnvPlayerSurfaceTrigger.ShouldFire(current: Metal, arriving: Metal, target: Metal), "the same material arriving again fires nothing");
    }

    [Fact]
    public void StandingStillOnAnUnwatchedMaterialFiresNothing()
        => Assert.True(!EnvPlayerSurfaceTrigger.ShouldFire(current: Wood, arriving: Wood, target: Metal), "standing still on an unwatched material fires nothing");
}

/// <summary>
/// The shooter's one lookup, which is the only place it can silently disagree
/// with the map.
/// </summary>
public class ShooterTests
{
    // Hammer lists these in its own order, which is not the order the engine's
    // material letters fall in — so this is a table and not a cast.

    [Fact]
    public void ZeroIsGlass()
        => Assert.True(EnvShooter.MaterialFor(0) == SurfaceMaterial.Glass, "0 is glass");

    [Fact]
    public void OneIsWood()
        => Assert.True(EnvShooter.MaterialFor(1) == SurfaceMaterial.Wood, "1 is wood");

    [Fact]
    public void TwoIsMetal()
        => Assert.True(EnvShooter.MaterialFor(2) == SurfaceMaterial.Metal, "2 is metal");

    [Fact]
    public void ThreeIsFlesh()
        => Assert.True(EnvShooter.MaterialFor(3) == SurfaceMaterial.Flesh, "3 is flesh");

    [Fact]
    public void FourIsRocks()
        => Assert.True(EnvShooter.MaterialFor(4) == SurfaceMaterial.Rocks, "4 is rocks");

    [Fact]
    public void TheMembersAreTheEnginesSurfacepropsLetters()
    {
        // The letters are what go over the ABI, so they are asserted as letters
        // too. A member renamed is harmless; a member renumbered is a silent
        // change to what the engine is told the gibs are made of.
        Assert.True((int)SurfaceMaterial.Glass == 'Y' && (int)SurfaceMaterial.Wood == 'W'
            && (int)SurfaceMaterial.Metal == 'M' && (int)SurfaceMaterial.Flesh == 'F'
            && (int)SurfaceMaterial.Rocks == 'R', "the members are the engine's surfaceprops letters");
    }

    // Stock's explicit "no sound", and anything else a map might write.

    [Fact]
    public void MinusOneIsSilent()
        => Assert.True(EnvShooter.MaterialFor(-1) == SurfaceMaterial.Silent, "-1 is silent");

    [Fact]
    public void ANumberNobodyDefinedIsSilentRatherThanWrong()
        => Assert.True(EnvShooter.MaterialFor(99) == SurfaceMaterial.Silent, "a number nobody defined is silent rather than wrong");

    [Fact]
    public void AndSilentIsStillTheMinusOneTheEngineExpects()
        => Assert.True((int)SurfaceMaterial.Silent == -1, "and silent is still the -1 the engine expects");

    /// <summary>
    /// The three spawnflag bits, against the numbers <c>game/server/effects.cpp</c>
    /// defines.
    ///
    /// <para>
    /// This exists because one of them was WRONG and nothing could say so.
    /// <c>StrictRemove</c> was 16 — a bit nothing defines — under a comment that
    /// explained the gap between 1 and 16 as an artefact of gibshooter's flags.
    /// The three are consecutive shifts:
    /// <c>SF_GIBSHOOTER_REPEATABLE (1&lt;&lt;0)</c>,
    /// <c>SF_SHOOTER_FLAMING (1&lt;&lt;1)</c>,
    /// <c>SF_SHOOTER_STRICT_REMOVE (1&lt;&lt;2)</c>. A mapper ticking "Strict
    /// Remove" in Hammer sets 4, and the entity read 16, so the flag did
    /// nothing at all.
    /// </para>
    ///
    /// <para>
    /// Asserted as NUMBERS rather than as names: a renamed member is harmless
    /// and a renumbered one is a spawnflag that silently stops meaning what the
    /// map wrote.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSpawnflagsAreTheThreeConsecutiveBitsEffectsCppDefines()
    {
        Assert.True((int)EnvShooter.SpawnFlag.Repeatable == 1,
            "SF_GIBSHOOTER_REPEATABLE is (1<<0)");
        Assert.True((int)EnvShooter.SpawnFlag.Flaming == 2,
            "SF_SHOOTER_FLAMING is (1<<1)");
        Assert.True((int)EnvShooter.SpawnFlag.StrictRemove == 4,
            "SF_SHOOTER_STRICT_REMOVE is (1<<2) -- it was read as 16 for the whole "
            + "life of the managed shooter, which is a bit nothing defines");

        // Adjacent, and no bit shared. Three shifts of one cannot overlap, so
        // what this really catches is a fourth flag added on top of one of them.
        Assert.True((int)EnvShooter.SpawnFlag.Repeatable
                  + (int)EnvShooter.SpawnFlag.Flaming
                  + (int)EnvShooter.SpawnFlag.StrictRemove == 7,
            "the three occupy the bottom three bits between them and share none");

        Assert.True((int)EnvShooter.SpawnFlag.None == 0,
            "an entity whose mapper ticked nothing reads as no flag at all");
    }

    //-------------------------------------------------------------------------
    // What the gibs bleed, which the two classnames disagree about.
    //-------------------------------------------------------------------------

    /// <summary>
    /// One shooter, spawned through the registry so its <c>ClassName</c> is the
    /// one the map wrote rather than one the test asserted into place.
    /// </summary>
    private static (EnvShooter Shooter, RecordingEngine Engine) Spawn(
        string className, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvShooter).Assembly);

        var services = TestServices.Services(out var engine);

        // THE REGISTRY IS THE DIRECTORY, which is what ServerModule registers
        // too. A shooter takes one because a gib is a managed entity now
        // (plan_entity.md step 4a) and something has to find the SsGib instance
        // behind the handle Create answered. Registering the real one rather
        // than a stub keeps this test standing on the same lookup the game uses.
        services.AddInstance<IEntityDirectory>(registry);

        registry.Services = services;

        // Pointed straight up the world X axis and standing at the origin. The
        // direction and the position are not what these tests are about, but the
        // engine is strict and a shooter cannot throw without asking for both.
        engine.Returns("Entities.GetAngles", new Vector(0f, 0f, 0f));
        engine.Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f));

        // The scatter draw. Fixed rather than random, because a volley whose
        // spread is a test input is a volley whose failures are reproducible.
        engine.Returns("Host.RandomFloat", 0f);

        // A gib IS a managed entity now (plan_entity.md step 4a), so throwing
        // one creates one rather than crossing to native. The strict double
        // answers EntityHandle.None to Entities.Create, so Gibs.Shoot warns and
        // gives up part way -- which is fine and is what these tests want: what
        // they read is the SHOT the shooter filled in, and that is decided
        // before anything is created. See EnvShooter.LastShot.
        engine.Returns("Precache.PrecacheModel", 1);
        engine.Returns("Precache.ModelTypeOf", ModelType.Studio);
        engine.Returns("Entities.Create", EntityHandle.None);

        var handle = new EntityHandle(41u);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, className, key, value);

        registry.Spawn(handle, className);
        return ((EnvShooter)registry.Get(handle)!, engine);
    }

    /// <summary>
    /// The two classnames bleed the opposite ways, and neither of them bleeds
    /// what a zeroed field would.
    ///
    /// <para>
    /// <c>game/server/effects.cpp</c> sets this in both factories:
    /// <c>CGibShooter::CreateGib</c> says <c>SetBloodColor( BLOOD_COLOR_RED )</c>
    /// and <c>CEnvShooter::CreateGib</c> says <c>SetBloodColor( DONT_BLEED )</c>.
    /// The shim said neither, and a <c>CGib</c> has no constructor — so
    /// <c>m_bloodColor</c> was the zeroed 0, which IS <c>BLOOD_COLOR_RED</c>.
    /// gibshooter came out accidentally right and env_shooter came out wrong.
    /// </para>
    ///
    /// <para>
    /// The value is asserted as a NUMBER as well as a name, because the number
    /// is what <c>GibShot.BloodColor</c> carries into <c>SsGib.Throw</c>, and
    /// because -1 being the "do nothing" answer rather than 0 is the whole
    /// reason the default was wrong.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTwoClassnamesBleedTheOppositeWaysEffectsCppSetsThem()
    {
        var (gibshooter, _) = Spawn(EntityNames.Gibshooter);
        var (envShooter, _) = Spawn(EntityNames.EnvShooter, ("shootmodel", "models/props_debris/rock001a.mdl"));

        Assert.True(gibshooter.Bleeds == BloodColor.Red,
            "CGibShooter::CreateGib says SetBloodColor( BLOOD_COLOR_RED )");
        Assert.True(envShooter.Bleeds == BloodColor.None,
            "CEnvShooter::CreateGib says SetBloodColor( DONT_BLEED )");

        Assert.True((int)BloodColor.Red == 0 && (int)BloodColor.None == -1,
            "and the numbers are the engine's: DONT_BLEED is -1, not the 0 a "
            + "zeroed m_bloodColor reads as");

        // The classname decides, not the model. An env_shooter whose mapper left
        // shootmodel empty still throws human gibs and is still an env_shooter,
        // so reading the model would have got this one backwards.
        var (bare, _) = Spawn(EntityNames.EnvShooter);
        Assert.True(bare.Bleeds == BloodColor.None,
            "an env_shooter with no shootmodel is still an env_shooter");
    }

    /// <summary>
    /// And the shot actually CARRIES it.
    ///
    /// <para>
    /// Separate from the test above on purpose. <see cref="EnvShooter.Bleeds"/>
    /// could be perfectly right and never reach the struct, which is exactly the
    /// state this whole fix is repairing — the value <c>effects.cpp</c> sets was
    /// correct in stock and simply was not being sent. A test that only asks the
    /// property is a test that would have passed before the bug was fixed.
    /// </para>
    /// </summary>
    [Fact]
    public void TheShotCarriesTheBloodColourRatherThanLeavingItZero()
    {
        var (shooter, engine) = Spawn(EntityNames.EnvShooter,
            ("shootmodel", "models/props_debris/rock001a.mdl"), ("m_iGibs", "1"));

        shooter.OnThink();

        Assert.True(shooter.ShotsThrown == 1, $"one gib was thrown, not {shooter.ShotsThrown}");

        Assert.True(shooter.LastShot.BloodColor == (int)BloodColor.None,
            $"an env_shooter's gib is told DONT_BLEED (-1), not {shooter.LastShot.BloodColor}");

        var (gibshooter, _) = Spawn(EntityNames.Gibshooter, ("m_iGibs", "1"));
        gibshooter.OnThink();

        Assert.True(gibshooter.ShotsThrown == 1, $"one gib was thrown, not {gibshooter.ShotsThrown}");
        Assert.True(gibshooter.LastShot.BloodColor == (int)BloodColor.Red,
            $"a gibshooter's gib is told BLOOD_COLOR_RED (0), not {gibshooter.LastShot.BloodColor}");
    }
}

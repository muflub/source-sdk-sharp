using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// env_fog_controller, tested where the decisions are.
///
/// <para>
/// Three things in this entity can be wrong quietly. The transition commits
/// only the channels an input armed, so a mask handled wrongly snaps a colour
/// nobody asked to change — and to whatever the uninitialised LerpTo held,
/// which for a colour is black. The fog direction is the NEGATED facing, and a
/// sign dropped there points the second colour the wrong way round with no
/// error anywhere. And the mask has to CLEAR after committing, or a second
/// transition re-commits the first one's channels.
/// </para>
/// </summary>
public class EnvFogControllerTests
{
    private static readonly EntityHandle Ent = new(0x0059_0001u);

    /// <summary>
    /// Spawns one, with the two engine reads it makes stubbed.
    ///
    /// <para>
    /// `spawnflags` and `angles` are NOT keyvalues as far as a scripted entity
    /// is concerned: SpawnFlags and SpawnAngles both go across to the engine,
    /// which in a test process has no entity to ask. Stubbing them is the
    /// point of the strict double — the first version of these tests wrote them
    /// as map keys, and the fog direction and master-flag checks both failed,
    /// which is exactly the report that was wanted.
    /// </para>
    /// </summary>
    private static (ScriptedEntityRegistry Registry, EnvFogController Fog) SpawnWith(
        int spawnFlags, Vector spawnAngles, params (string Key, string Value)[] keys)
    {
        var services = Services(out var engine);
        engine.Ignores("WorldQuery.GetArea");
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        engine.Returns("Entities.GetSpawnAngles", spawnAngles);

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvFogController).Assembly);
        registry.Services = services;

        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, EntityNames.EnvFogController, key, value);

        registry.Spawn(Ent, EntityNames.EnvFogController);
        return (registry, (EnvFogController)registry.Get(Ent)!);
    }

    private static (ScriptedEntityRegistry Registry, EnvFogController Fog) SpawnWith(
        params (string Key, string Value)[] keys)
        => SpawnWith(0, default, keys);

    private static EnvFogController Spawn(params (string Key, string Value)[] keys)
        => SpawnWith(keys).Fog;

    [Fact]
    public void TheMapsKeysAreWhatTheEntityStartsAt()
    {
        var (registry, fog) = SpawnWith(("fogenable", "1"),
                        ("fogstart", "512"),
                        ("fogend", "2048"),
                        ("fogmaxdensity", "0.75"),
                        ("farz", "4096"),
                        ("fogcolor", "10 20 30"));

        Assert.True(fog.Enabled, "a map that says fogenable 1 gets fog on");
        Assert.True(fog.Start == 512f, "fogstart is where the fog begins");
        Assert.True(fog.End == 2048f, "fogend is where it is fully opaque");
        Assert.True(fog.MaxDensity == 0.75f, "fogmaxdensity is how opaque that is");
        Assert.True(fog.FarZ == 4096f, "farz is the far clip plane");
        Assert.True(fog.ColourPrimary == (10u | (20u << 8) | (30u << 16) | (255u << 24)),
                    "fogcolor packs R into the lowest byte with a full alpha");
    }

    /// <summary>
    /// A map with no fogmaxdensity must not get zero: zero density is fog that
    /// never appears, and every such map would silently lose its fog.
    /// </summary>
    [Fact]
    public void AMapThatNamesNoMaxDensityGetsFullyOpaqueRatherThanInvisible()
    {
        Assert.True(Spawn().MaxDensity == 1f, "an unstated fogmaxdensity is one, not zero");
    }

    /// <summary>
    /// Stock's `dirPrimary *= -1.0f`. Yaw 0 faces +X, so the fog direction is
    /// -X: fogcolor2 is what you see looking INTO the light, which is the
    /// opposite of where the entity points.
    /// </summary>
    [Fact]
    public void TheFogDirectionIsTheNegatedFacingRatherThanTheFacing()
    {
        var (registry, fog) = SpawnWith(0, new Vector(0f, 0f, 0f), ("use_angles", "1"));

        Assert.True(MathF.Abs(fog.DirPrimary.X - -1f) < 0.001f,
                    "a fog controller facing +X puts the fog direction at -X");
        Assert.True(MathF.Abs(fog.DirPrimary.Y) < 0.001f, "and nothing on Y");
    }

    /// <summary>Without use_angles the key wins and the facing is ignored.</summary>
    [Fact]
    public void WithoutUseAnglesTheMapsFogdirIsLeftAlone()
    {
        var (registry, fog) = SpawnWith(0, new Vector(0f, 0f, 0f), ("fogdir", "0 1 0"));

        Assert.True(MathF.Abs(fog.DirPrimary.Y - 1f) < 0.001f,
                    "a map that writes fogdir and not use_angles keeps its fogdir");
    }

    //-------------------------------------------------------------------------
    // The transition.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnArmedChannelCommitsAndAnUnarmedOneDoesNot()
    {
        var (registry, fog) = SpawnWith(("fogstart", "100"), ("fogend", "900"), ("fogcolor", "1 2 3"));

        registry.Input(Ent, "SetStartDistLerpTo", "400", 400f, EntityHandle.None);
        fog.CommitTransition();

        Assert.True(fog.Start == 400f, "the armed start distance moves to its target");
        Assert.True(fog.End == 900f, "the end distance nobody armed does not move");
        Assert.True(fog.ColourPrimary == (1u | (2u << 8) | (3u << 16) | (255u << 24)),
                    "and neither does the colour nobody armed");
    }

    /// <summary>
    /// Nothing is committed before the transition is asked for. Stock's inputs
    /// arm a target and wait; a SetColorLerpTo that took effect immediately
    /// would make the entity's whole delay mechanism invisible.
    /// </summary>
    [Fact]
    public void ArmingAChannelDoesNotChangeItYet()
    {
        var (registry, fog) = SpawnWith(("fogstart", "100"));

        registry.Input(Ent, "SetStartDistLerpTo", "400", 400f, EntityHandle.None);

        Assert.True(fog.Start == 100f, "arming a transition does not itself move anything");
    }

    /// <summary>
    /// The mask must clear, or the next transition re-commits this one's
    /// channels — a fog that keeps snapping back to an old colour every time
    /// anything else transitions.
    /// </summary>
    [Fact]
    public void CommittingDisarmsSoASecondTransitionCarriesNothingOver()
    {
        var (registry, fog) = SpawnWith(("fogstart", "100"), ("fogend", "900"));

        registry.Input(Ent, "SetStartDistLerpTo", "400", 400f, EntityHandle.None);
        fog.CommitTransition();

        registry.Input(Ent, "SetEndDistLerpTo", "1500", 1500f, EntityHandle.None);
        registry.Input(Ent, "SetStartDist", "50", 50f, EntityHandle.None);
        fog.CommitTransition();

        Assert.True(fog.End == 1500f, "the second transition's own channel commits");
        Assert.True(fog.Start == 50f,
                    "the first transition's channel is not re-committed over a later write");
    }

    /// <summary>
    /// Committing a transition that armed only the START must not take the
    /// colour with it.
    ///
    /// <para>
    /// The first version of this test could not fail, and finding that out is
    /// why it is written the way it is. It spawned with a colour, armed only
    /// the end distance, committed, and asserted the colour was unchanged —
    /// but the LerpTo colours are SEEDED to the live colour at spawn, so a
    /// commit that ignored the mask entirely wrote the same value back and the
    /// assertion passed. Mutating `ColourPrimary = _colourPrimaryLerpTo` to run
    /// unconditionally left the suite green.
    /// </para>
    ///
    /// <para>
    /// So the seed has to be made stale first: arm a colour, commit it, then
    /// write a DIFFERENT colour immediately. Now the LerpTo and the live value
    /// disagree, and only a commit that respects the mask leaves the live one
    /// standing.
    /// </para>
    /// </summary>
    [Fact]
    public void ATransitionThatArmsNoColourLeavesTheColourAloneRatherThanOverwritingIt()
    {
        var (registry, fog) = SpawnWith(("fogcolor", "200 100 50"), ("fogcolor2", "9 8 7"));

        // Make the stored LerpTo colours differ from the live ones.
        registry.Input(Ent, "SetColorLerpTo", "1 1 1", 0f, EntityHandle.None);
        registry.Input(Ent, "SetColorSecondaryLerpTo", "2 2 2", 0f, EntityHandle.None);
        fog.CommitTransition();

        registry.Input(Ent, "SetColor", "200 100 50", 0f, EntityHandle.None);
        registry.Input(Ent, "SetColorSecondary", "9 8 7", 0f, EntityHandle.None);

        // A transition that arms only a distance.
        registry.Input(Ent, "SetEndDistLerpTo", "2000", 2000f, EntityHandle.None);
        fog.CommitTransition();

        Assert.True(fog.ColourPrimary == (200u | (100u << 8) | (50u << 16) | (255u << 24)),
                    "an unarmed primary colour survives a transition");
        Assert.True(fog.ColourSecondary == (9u | (8u << 8) | (7u << 16) | (255u << 24)),
                    "and so does an unarmed secondary");
    }

    //-------------------------------------------------------------------------
    // Immediate inputs.
    //-------------------------------------------------------------------------

    [Fact]
    public void TurnOffAndTurnOnMoveTheEnabledState()
    {
        var (registry, fog) = SpawnWith(("fogenable", "1"));

        registry.Input(Ent, "TurnOff", "", 0f, EntityHandle.None);
        Assert.True(!fog.Enabled, "TurnOff turns the fog off");

        registry.Input(Ent, "TurnOn", "", 0f, EntityHandle.None);
        Assert.True(fog.Enabled, "TurnOn turns it back on");
    }

    [Fact]
    public void SetColorReplacesThePrimaryColourImmediately()
    {
        var (registry, fog) = SpawnWith(("fogcolor", "1 1 1"));

        registry.Input(Ent, "SetColor", "255 128 64", 0f, EntityHandle.None);

        Assert.True(fog.ColourPrimary == (255u | (128u << 8) | (64u << 16) | (255u << 24)),
                    "SetColor is immediate, unlike SetColorLerpTo");
    }

    /// <summary>
    /// The master flag is spawnflag 1. It decides which controller a map with
    /// several of them means, so reading the wrong bit picks the wrong fog.
    /// </summary>
    [Fact]
    public void TheMasterSpawnflagIsBitZero()
    {
        Assert.True(SpawnWith(1, default).Fog.IsMaster, "spawnflag 1 is the master flag");
        Assert.True(!SpawnWith(2, default).Fog.IsMaster, "and spawnflag 2 is not");
        Assert.True(!SpawnWith(0, default).Fog.IsMaster,
                    "a controller with no spawnflags is not the master");
    }
}

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two sky entities, tested at the level that has decisions in it.
///
/// <para>
/// Neither draws anything, and the parts that could be wrong are both quiet.
/// A fog colour parsed a byte out of order gives a mapper the wrong sky and no
/// error; a three-component colour read as having zero alpha gives a map fog
/// that never appears; the six skybox faces named in the wrong order load six
/// real materials and hang them on the wrong sides of the cube.
/// </para>
/// </summary>
public class SkyTests
{
    private static readonly EntityHandle Ent = new(0x0058_0001u);

    private static (ScriptedEntityRegistry, T) SpawnWith<T>(string className,
        params (string Key, string Value)[] keys) where T : ScriptedEntity
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(SkyCamera).Assembly);
        registry.Services = Ignoring("WorldQuery.GetArea", "Entities.GetOrigin");

        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, className, key, value);

        registry.Spawn(Ent, className);
        return (registry, (T)registry.Get(Ent)!);
    }

    /// <summary>
    /// The packing has to match the engine's color32, which is R in the lowest
    /// byte. Every component is checked separately because a rotated word still
    /// round-trips through a single-component test.
    /// </summary>
    [Fact]
    public void ColourParsing()
    {
        uint c = SkyCamera.ParseColour("1 2 3 4");
        Assert.True((c & 0xFF) == 1, "red is the low byte");
        Assert.True(((c >> 8) & 0xFF) == 2, "green is the second");
        Assert.True(((c >> 16) & 0xFF) == 3, "blue is the third");
        Assert.True(((c >> 24) & 0xFF) == 4, "alpha is the high byte");

        // The three-component form is what Hammer writes for a fog colour, and
        // a zero alpha here would make every such map's fog invisible.
        uint rgb = SkyCamera.ParseColour("255 128 0");
        Assert.True(((rgb >> 24) & 0xFF) == 255, "a three-component colour is opaque");
        Assert.True((rgb & 0xFF) == 255 && ((rgb >> 8) & 0xFF) == 128, "and keeps its components");

        // Map data is not trusted input. Saturating rather than wrapping is the
        // difference between a bright sky and a black one.
        Assert.True((SkyCamera.ParseColour("300 0 0") & 0xFF) == 255, "a component above 255 saturates");
        Assert.True((SkyCamera.ParseColour("-5 0 0") & 0xFF) == 0, "a negative component clamps to zero");
        Assert.True((SkyCamera.ParseColour("banana 0 0") & 0xFF) == 0, "rubbish parses as black");
        Assert.True(SkyCamera.ParseColour("") == 0xFF000000u, "an empty colour is opaque black");
    }

    /// <summary>
    /// The face order the engine expects, which is neither alphabetical nor
    /// axis-major — getting it wrong puts the ground on the ceiling.
    /// </summary>
    [Fact]
    public void SkyboxFaces()
    {
        var names = SkyboxSwapper.MaterialNames("sky_day01_01");

        Assert.True(names.Length == 6, "a skybox is six materials");
        Assert.True(names[0] == "skybox/sky_day01_01rt", "the first face is right");
        Assert.True(names[3] == "skybox/sky_day01_01ft", "the fourth is front");
        Assert.True(names[5] == "skybox/sky_day01_01dn", "the last is down");
    }

    /// <summary>
    /// The camera's own keys. Scale and max density both matter as DEFAULTS
    /// rather than as parsing: stock sets max density from a constructor and not
    /// from a keyvalue, so a map that omits it wants one rather than zero, and a
    /// zero there is fog that never draws.
    /// </summary>
    [Fact]
    public void CameraDefaults()
    {
        var (_, plain) = SpawnWith<SkyCamera>(EntityNames.SkyCamera);

        Assert.True(plain.Params.Scale == 16, "a sky camera with no keys scales by 16");
        Assert.True(plain.Params.FogMaxDensity == 1f, "and its fog can reach full density");
        Assert.True(plain.Params.FogEnable == 0, "and its fog is off");

        var (_, fogged) = SpawnWith<SkyCamera>(EntityNames.SkyCamera,
            ("scale", "32"), ("fogenable", "1"), ("fogblend", "1"),
            ("fogcolor", "10 20 30"), ("fogcolor2", "40 50 60"),
            ("fogstart", "500"), ("fogend", "2000"), ("fogmaxdensity", "0.5"),
            ("fogdir", "0 1 0"));

        Assert.True(fogged.Params.Scale == 32, "the camera takes the map's scale");
        Assert.True(fogged.Params.FogStart == 500f && fogged.Params.FogEnd == 2000f, "and its fog range");
        Assert.True(fogged.Params.FogMaxDensity == 0.5f, "and its density");
        Assert.True((fogged.Params.FogColorPrimary & 0xFF) == 10
              && (fogged.Params.FogColorSecondary & 0xFF) == 40, "and both fog colours, apart");

        // With use_angles unset the direction is the key verbatim, which is the
        // half that does not need an engine to answer.
        Assert.True(fogged.Params.FogDirY == 1f && fogged.Params.FogDirX == 0f, "fogdir is taken literally when use_angles is unset");
    }
}

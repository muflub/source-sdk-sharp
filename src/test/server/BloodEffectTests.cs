using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Blood at a point, decided in C#: stock's UTIL_BloodStream, UTIL_BloodDrips
/// (and through it UTIL_BloodImpact and UTIL_Smoke) and UTIL_BloodSpray.
/// </summary>
public class BloodEffectTests
{
    private static readonly Vector At = new(10f, 20f, 30f);
    private static readonly Vector Up = new(0f, 0f, 1f);

    private sealed class Console(Dictionary<string, string> values) : IEngineConsole
    {
        public void ExecuteServerCommand(string command) => throw new NotSupportedException();
        public string? FindConVarValue(string name) => values.GetValueOrDefault(name);
        public string GetConVarValue(int id) => throw new NotSupportedException();
        public void Msg(string message) { }
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => throw new NotSupportedException();
        public int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null) => throw new NotSupportedException();
        public void SendClientCommand(EntityHandle player, string command) => throw new NotSupportedException();
        public void SetConVarValue(int id, string value) => throw new NotSupportedException();
        public void SetConVarValueByName(string name, string value) => throw new NotSupportedException();
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }

    private sealed class HighestRandom : IUniformRandom
    {
        public float RandomFloat(float min, float max) => max;
        public int RandomInt(int min, int max) => max;
    }

    private sealed class World : IEffectsWorld
    {
        public ServerTrace? Trace(Vector start, Vector end, uint mask) => null;
        public void StaticDecal(Vector originInEntitySpace, int decalIndex, int entityIndex, int modelIndex, bool lowPriority) { }
        public int PrecacheModel(string name) => name == "sprites/steam1.vmt" ? 42 : -1;
        public int EntityIndex(EntityHandle entity) => -1;
        public Span<byte> Block(EntityHandle entity) => Span<byte>.Empty;
        public (string ClassName, string Name) Names(EntityHandle entity) => ("", "");
    }

    private static (EffectsOverTempEnts Effects, EffectsRewiringTests.RecordingTempEnts Temps) Build(
        bool humanBlood = true, bool alienBlood = true, IUniformRandom? random = null)
    {
        var engine = new RecordingEngine();
        var temps = new EffectsRewiringTests.RecordingTempEnts();
        var console = new Console(new Dictionary<string, string>
        {
            ["violence_hblood"] = humanBlood ? "1" : "0",
            ["violence_ablood"] = alienBlood ? "1" : "0",
        });
        var effects = new EffectsOverTempEnts(engine, temps, engine, engine, new World(), console,
                                              random ?? new DefaultForwarders.LowestRandom(), _ => { });
        return (effects, temps);
    }

    [Fact]
    public void A_stream_is_a_capped_blood_stream_in_stocks_colour_to_the_pvs()
    {
        var (effects, temps) = Build();

        effects.BloodEffect(At, Up, BloodColor.Red, 300f, stream: true);

        EffectsRewiringTests.TempEntCall call = temps.Only("BloodStream");
        Assert.Equal(TeFilters.Pvs(At), call.Filter);
        Assert.Equal(new Color32(247, 63, 14, 255), call.Arg<Color32>(2));
        Assert.Equal(255, call.Arg<int>(3));
    }

    [Fact]
    public void Drips_are_a_blood_impact_scaled_up_in_multiplayer()
    {
        var (effects, temps) = Build();

        effects.BloodEffect(At, Up, BloodColor.Yellow, 20f);

        EffectsRewiringTests.TempEntCall call = temps.Only("DispatchEffectByName");
        Assert.Equal("bloodimpact", call.Arg<string>(0));
        Assert.Equal(TeFilters.Pas(At), call.Filter);
        TeEffectData data = call.Arg<TeEffectData>(1);
        Assert.Equal(100f, data.Scale);
        Assert.Equal((byte)BloodColor.Yellow, data.Color);
        Assert.Equal(Up, data.Normal);
    }

    [Fact]
    public void Drips_of_no_amount_make_nothing()
    {
        var (effects, temps) = Build();

        effects.BloodEffect(At, Up, BloodColor.Red, 0f);

        Assert.Equal(0, temps.Count);
    }

    [Fact]
    public void Mechanical_blood_sparks_and_smokes_on_the_high_roll()
    {
        var (effects, temps) = Build(random: new HighestRandom());

        effects.BloodEffect(At, Up, (BloodColor)3, 10f);

        Assert.Equal(1, temps.CountOf("Sparks"));
        EffectsRewiringTests.TempEntCall smoke = temps.Only("Smoke");
        Assert.Equal(42, smoke.Arg<int>(1));
        Assert.Equal(1.5f, smoke.Arg<float>(2), 3);
        Assert.Equal(10, smoke.Arg<int>(3));
        Assert.Equal(0, temps.CountOf("DispatchEffectByName"));
    }

    [Fact]
    public void Mechanical_blood_on_the_low_roll_only_sparks()
    {
        var (effects, temps) = Build();

        effects.BloodEffect(At, Up, (BloodColor)3, 10f);

        Assert.Equal(1, temps.CountOf("Sparks"));
        Assert.Equal(0, temps.CountOf("Smoke"));
    }

    [Fact]
    public void Spray_flags_add_a_blood_spray_after_the_drips()
    {
        var (effects, temps) = Build();

        effects.BloodEffect(At, Up, BloodColor.Red, 4f, sprayFlags: BloodSpray.Gore | BloodSpray.Cloud);

        var dispatches = temps.Calls.Where(c => c.Member == "DispatchEffectByName").ToList();
        Assert.Equal(["bloodimpact", "bloodspray"], dispatches.Select(c => c.Arg<string>(0)));
        TeEffectData spray = dispatches[1].Arg<TeEffectData>(1);
        Assert.Equal((int)(BloodSpray.Gore | BloodSpray.Cloud), spray.Flags);
        Assert.Equal(4f, spray.Scale);
    }

    [Theory]
    [InlineData(BloodColor.Red, false, true)]
    [InlineData(BloodColor.Yellow, true, false)]
    public void Each_colour_reads_its_own_violence_convar(BloodColor color, bool humanBlood, bool alienBlood)
    {
        var (effects, temps) = Build(humanBlood, alienBlood);

        effects.BloodEffect(At, Up, color, 10f, stream: true);
        effects.BloodEffect(At, Up, color, 10f);

        Assert.Equal(0, temps.Count);
    }

    [Fact]
    public void Dont_bleed_makes_nothing_even_with_spray_flags()
    {
        var (effects, temps) = Build();

        effects.BloodEffect(At, Up, BloodColor.None, 10f, sprayFlags: BloodSpray.Gore);

        Assert.Equal(0, temps.Count);
    }
}

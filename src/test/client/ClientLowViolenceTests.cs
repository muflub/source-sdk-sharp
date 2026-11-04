using Xunit;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Shared;

namespace SourceSharp.Tests;

/// <summary>
/// UTIL_IsLowViolence (util_shared.cpp), decided in C#: any of the four legacy
/// violence_* convars off, else the engine's answer.
/// </summary>
public class ClientLowViolenceTests
{
    private static readonly string[] Four = ["violence_hblood", "violence_ablood", "violence_hgibs", "violence_agibs"];

    private sealed class Engine(bool low) : IViolenceSettings
    {
        public int Asked { get; private set; }

        public bool EngineSaysLowViolence
        {
            get
            {
                Asked++;
                return low;
            }
        }
    }

    private static ClientLowViolence Low(Dictionary<string, string> values, IViolenceSettings engine)
        => new(DeclaredConVars.Holding<ViolenceHBloodConVar>(values["violence_hblood"]), DeclaredConVars.Holding<ViolenceABloodConVar>(values["violence_ablood"]),
               DeclaredConVars.Holding<ViolenceHGibsConVar>(values["violence_hgibs"]), DeclaredConVars.Holding<ViolenceAGibsConVar>(values["violence_agibs"]), engine);

    private static Dictionary<string, string> AllOn() => Four.ToDictionary(n => n, _ => "1");

    [Theory]
    [InlineData("violence_hblood")]
    [InlineData("violence_ablood")]
    [InlineData("violence_hgibs")]
    [InlineData("violence_agibs")]
    public void Any_convar_off_is_low_and_the_engine_is_not_asked(string off)
    {
        var values = AllOn();
        values[off] = "0";
        var engine = new Engine(low: false);

        Assert.True(Low(values, engine).IsLowViolence);
        Assert.Equal(0, engine.Asked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void With_all_four_on_the_engine_decides(bool engineLow)
    {
        var engine = new Engine(engineLow);

        Assert.Equal(engineLow, Low(AllOn(), engine).IsLowViolence);
        Assert.Equal(1, engine.Asked);
    }

    [Fact]
    public void A_fraction_truncates_as_GetInt_does()
    {
        var values = AllOn();
        values["violence_hblood"] = "0.5";

        Assert.True(Low(values, new Engine(low: false)).IsLowViolence);
    }
}

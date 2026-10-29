using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What kind of prop data a model has: stock's PARSE_* answer out of
/// CBaseProp::ParsePropData, CPropData::ParsePropFromKV and ParsePropFromBase
/// (props.cpp, props_shared.cpp). CBaseProp::Spawn deletes a prop by it, so a
/// model's kind decides whether a map's prop exists at all.
/// </summary>
public class PropDataKindTests
{
    private const string File = @"
""PropData""
{
    ""Wooden.Small""  { ""health"" ""30"" }
    ""Static.Ok""     { ""health"" ""10"" ""allowstatic"" ""1"" }
    ""Via.Static""    { ""base"" ""Static.Ok"" }
}
";

    private static PropDataParse Kind(string? text, string? file = File)
    {
        var engine = TestServices.Fake();
        engine.Returns("Files.ReadGameFile", file);
        var props = new PropData(new RecordingLog(), engine, engine, engine);
        props.Load();
        return props.ModelPropDataKind(text);
    }

    [Fact]
    public void AModelWithNoKeyValuesHasNoData() => Assert.Equal(PropDataParse.NoData, Kind(null));

    [Fact]
    public void KeyValuesWithoutAPropDataSectionAreNoData()
        => Assert.Equal(PropDataParse.NoData, Kind("mdlkeyvalue { particles { } }"));

    [Fact]
    public void KeyValuesThatDoNotParseAreNoData()
        => Assert.Equal(PropDataParse.NoData, Kind("mdlkeyvalue { prop_data { "));

    [Fact]
    public void APropDataSectionIsAPhysicsPropsData()
        => Assert.Equal(PropDataParse.Succeeded, Kind("mdlkeyvalue { prop_data { health 40 } }"));

    [Fact]
    public void AllowStaticLetsItBeStaticToo()
        => Assert.Equal(PropDataParse.SucceededAllowedStatic, Kind("mdlkeyvalue { prop_data { allowstatic 1 } }"));

    [Fact]
    public void ABaseThatExistsIsFollowed()
        => Assert.Equal(PropDataParse.Succeeded, Kind("mdlkeyvalue { prop_data { base Wooden.Small } }"));

    [Fact]
    public void AllowStaticIsInheritedFromTheBaseChain()
        => Assert.Equal(PropDataParse.SucceededAllowedStatic, Kind("mdlkeyvalue { prop_data { base Via.Static } }"));

    [Fact]
    public void ABaseTheFileDoesNotHaveIsBadData()
        => Assert.Equal(PropDataParse.BadData, Kind("mdlkeyvalue { prop_data { base No.Such.Base } }"));

    [Fact]
    public void ABaseWithNoPropDataFileLoadedIsNoData()
    {
        // ParsePropFromBase: "if ( !m_bPropDataLoaded ) return PARSE_FAILED_NO_DATA;"
        Assert.Equal(PropDataParse.NoData, Kind("mdlkeyvalue { prop_data { base Wooden.Small } }", file: null));
    }
}

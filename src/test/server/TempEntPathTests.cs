using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>A temp entity is sent per bullet impact: nothing on the way down reads the metadata.</summary>
public class TempEntPathTests
{
    [Fact]
    public void Nothing_on_a_temp_entity_s_send_path_reads_the_metadata()
    {
        var (walked, reflective) = TempEntPathCensus.From(
            TempEntPathCensus.Methods(typeof(TempEnts)).Concat(TempEntPathCensus.Methods(typeof(TempEntState), "ToBlob")));

        Assert.Empty(reflective);

        Assert.Contains("SourceSharp.Server.TempEnts.Sparks", walked);
        Assert.Contains("SourceSharp.Abi.TempEntState.ToBlob", walked);
    }
}

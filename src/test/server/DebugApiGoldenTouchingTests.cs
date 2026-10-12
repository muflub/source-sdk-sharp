using SourceSharp.Abi;
using Debug.Api;

using Xunit;

using static SourceSharp.Tests.DebugApiGolden;

namespace SourceSharp.Tests;

/// <summary>
/// Golden JSON for /entity/touching (b5's route, c722b04d), now that it reads
/// the touch directory through IDebugWorld. The bodies are the ones c722b04d's
/// handler writes; DebugApiTests keeps b5's three facts beside them.
/// </summary>
public partial class DebugApiGoldenTouchingTests
{

    [Fact]
    public void Touching_renders_the_entity_and_each_linked_index()
    {
        using var server = DebugApiServer.ForTest(Hooks());
        Assert.Equal("{\"index\":5,\"touching\":[7,9]}", Pumped(server, "/entity/touching?index=5").Body);
    }

    [Fact]
    public void Touching_with_no_index_and_with_no_directory_render_exactly()
    {
        using var server = DebugApiServer.ForTest(Hooks());
        Assert.Equal("{\"error\":\"index required\"}", Get(server, "/entity/touching").Body);

        using var none = DebugApiServer.ForTest(TestDebugHooks.Server());
        Assert.Equal("{\"error\":\"no touch directory\"}", Get(none, "/entity/touching?index=5").Body);
    }
}

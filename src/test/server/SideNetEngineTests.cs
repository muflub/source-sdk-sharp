using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_abi_hotreload D8, the networking half of the movement and client
/// cluster: the payload reader and the standalone publish called the static
/// <c>Engine</c>, which answered for whichever side was active. Each now takes
/// its side's own areas, and these facts hand two engines to two callers and
/// show each call lands on the caller's own.
/// </summary>
public class SideNetEngineTests
{
    private static readonly EntityHandle Prop = new(0x0042_0007u);

    [Fact]
    public void APayloadReaderWalksItsOwnSidesEntities()
    {
        var mine = new RecordingEngine()
            .Returns("Entities.FindAllByClassname", new[] { Prop })
            .Returns("Entities.GetIndex", 7)
            .Returns("Entities.GetClassName", "prop_physics")
            .Returns("Networking.CheckPayloadAgreement", default(PayloadAgreement));

        // Strict and unstubbed: any call that reached it would throw.
        var theirs = new RecordingEngine();

        var reader = new NetPayloadReader(mine, mine);
        _ = new NetPayloadReader(theirs, theirs);

        NetEntityBlob blob = reader.Read(7);

        Assert.True(blob.Found, "the reader found the entity on its own side");
        Assert.Equal("prop_physics", blob.ClassName);
        Assert.Equal(Prop.Value, blob.Handle);
        Assert.Equal("*", mine.Calls.Single(c => c.Member == "Entities.FindAllByClassname").ArgAs<string>(0));
        Assert.Equal(1, mine.CountOf("Networking.GetEntityBlob"));
        Assert.Equal(1, mine.CountOf("Networking.CheckPayloadAgreement"));
        Assert.Empty(theirs.Calls);
    }

    [Fact]
    public void APayloadReaderAnswersMissingForAnIndexItsSideLacks()
    {
        var mine = new RecordingEngine()
            .Returns("Entities.FindAllByClassname", new[] { Prop })
            .Returns("Entities.GetIndex", 7);

        NetEntityBlob blob = new NetPayloadReader(mine, mine).Read(8);

        Assert.False(blob.Found);
        Assert.Equal(0, mine.CountOf("Networking.GetEntityBlob"));
    }

    [Fact]
    public void TheHierarchyIsDeclaredThroughThePublishingSidesNetworking()
    {
        var mine = new RecordingEngine().Returns("Networking.NetHierarchyDeclare", 3);
        var theirs = new RecordingEngine();

        // Nothing hosted, so the only engine call is the declaration.
        StandaloneNetStates.PublishAll(mine, "Server", typeof(WorldNetState).Assembly,
                                       _ => false, new NetTypeRefusals(), new RecordingLog());

        Assert.Equal(1, mine.CountOf("Networking.NetHierarchyDeclare"));
        Assert.Empty(theirs.Calls);
    }

    [Fact]
    public void TheWorldIsDeclaredThroughThePublishingSidesNetworking()
    {
        var mine = new RecordingEngine().Returns("Networking.WorldNetDeclare", 5);
        var theirs = new RecordingEngine().Returns("Networking.WorldNetDeclare", 9);

        int words = WorldNet.Publish(mine, "Client", new NetTypeRefusals(), new RecordingLog());

        Assert.Equal(5, words);
        Assert.Equal(1, mine.CountOf("Networking.WorldNetDeclare"));
        Assert.Equal(0, theirs.CountOf("Networking.WorldNetDeclare"));
    }
}

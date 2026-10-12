using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The protocol manifest (plan_side_isolation §2.1): one hash per list over
/// its keys, compared at join so a declaration only one side compiles is
/// named.
/// </summary>
public class ProtocolManifestTests
{
    [Fact]
    public void The_hash_is_over_the_set_of_keys_not_their_order()
    {
        Assert.Equal(ProtocolManifest.Hash(["b", "a", "c"]), ProtocolManifest.Hash(["a", "b", "c"]));
    }

    [Fact]
    public void Keys_are_separated_so_a_split_key_hashes_differently()
    {
        Assert.NotEqual(ProtocolManifest.Hash(["ab", "c"]), ProtocolManifest.Hash(["a", "bc"]));
    }

    [Fact]
    public void A_missing_key_changes_the_hash()
    {
        Assert.NotEqual(ProtocolManifest.Hash(["a", "b"]), ProtocolManifest.Hash(["a"]));
    }

    [Fact]
    public void The_shared_assembly_declares_something_in_every_list()
    {
        // The stimulus has to reach the subject: a manifest of three empty
        // lists would agree with any other side's empty lists forever.
        var manifest = ProtocolManifest.Of(TestServices.ServerGame, new RecordingLog());
        uint empty = ProtocolManifest.Hash([]);

        Assert.NotEqual(empty, manifest.StringTables);
        Assert.NotEqual(empty, manifest.TempEnts);
        Assert.NotEqual(empty, manifest.NetStates);
    }

    [Fact]
    public void Differences_names_each_list_that_differs()
    {
        var mine = new ProtocolManifest(1, 2, 3);

        Assert.Empty(mine.Differences(1, 2, 3));
        Assert.Equal(["string tables", "networked states"], mine.Differences(9, 2, 9));
    }
}

using SourceSharp.Host.Contracts;
using SourceSharp.Host.Sdk;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Sheets;
using TF2.Descent.Server;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>What HostLink hands the SDK for a hero's sheet, and what it hands the game from a lease.</summary>
public sealed class HostLinkSheetTests
{
    private static readonly OpaquePayload Leased = new([1, 2, 3], 2);

    [Fact]
    public void A_checkpoint_sends_the_games_sheet_and_evidence_with_their_schemas()
    {
        var rules = new TF2DescentRules();
        var sheet = rules.NewCharacter("soldier", "s", hardcore: false);
        var evidence = rules.Evidence.Write(new CheckpointProof(12, CheckpointProof.None.Kills));

        var (data, proof) = HostLink.Payloads(new SheetCheckpoint(sheet, evidence), Leased);

        Assert.Equal(sheet.Data, data.Data);
        Assert.Equal((uint)SheetCodec.Schema, data.SchemaVersion);
        Assert.NotNull(proof);
        Assert.Equal(evidence.Data, proof.Data);
        Assert.Equal((uint)EvidenceCodec.Schema, proof.SchemaVersion);
    }

    [Fact]
    public void A_checkpoint_without_a_sheet_sends_the_leases_own_and_no_evidence()
    {
        var (data, proof) = HostLink.Payloads(null, Leased);

        Assert.Same(Leased, data);
        Assert.Null(proof);
    }

    [Fact]
    public void The_join_hands_the_game_the_leased_sheet_with_its_schema()
    {
        var sheet = HostLink.SheetOf(Leased);

        Assert.Equal(Leased.Data, sheet.Data);
        Assert.Equal(2, sheet.SchemaVersion);
    }
}

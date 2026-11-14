using System;
using System.Linq;
using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A body keeps the look it died in (plan_bots_teams_tf2 A.8, ruled
/// 2026-11-02). The client's body takes its model from its own entity and its
/// team skin from the team the server stamped on it at the death, never from
/// the player it belonged to, so an owner who swaps sides afterwards — and a
/// client that first sees the body after the swap — leaves it as it was. The
/// team skin is TF's (<c>C_TFRagdoll</c>); an HL2MP body, which no class was
/// stamped on, wears its model's own skin as stock's <c>C_HL2MPRagdoll</c>.
/// </summary>
public class RagdollTeamSkinTests
{
    private static readonly EntityHandle Body = new(99u);

    private sealed class NoViewers : IClientStandalones
    {
        public System.Collections.Generic.IReadOnlyList<ClientStandaloneEntity> Live() => [];
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private static (ClientSsRagdoll Body, RecordingEngine Engine) Arrive(byte team, byte playerClass)
    {
        var engine = new RecordingEngine();
        engine.Returns("EntityRender.GetModelName", "models/combine_soldier.mdl")
              .Returns("EntityRender.LookupSequenceByActivity", -1)
              .Returns("EntityRender.BecomeRagdoll", true)
              .Returns("EntityRender.RagdollState", RagdollState.Present)
              .Returns("Entities.IsValid", true);

        var body = new ClientSsRagdoll(engine, engine, engine, new NoViewers(), new RecordingRagdolls(engine));
        body.BindEngine(engine, engine);
        body.State.BindLocal();
        body.State.PlayerIndex = -1;
        body.State.Team = team;
        body.State.Class = playerClass;
        body.Handle = Body;

        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        return (body, engine);
    }

    private static int[] Skins(RecordingEngine engine)
        => [.. engine.AllOf("EntityRender.SetSkin").Select(c => c.ArgAs<int>(1))];

    [Fact]
    public void ATfBodyWearsTheTeamStampedOnItAndKeepsItThroughLaterUpdates()
    {
        var (body, engine) = Arrive(team: 3, playerClass: 3);

        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.Equal([1], Skins(engine));
        Assert.False(engine.Called("EntityRender.SetModel"), "the body's model is its own entity's, never re-set from its owner");
    }

    [Fact]
    public void AClientThatFirstSeesARedBodyLaterSeesRed()
    {
        var (_, engine) = Arrive(team: 2, playerClass: 7);

        Assert.Equal([0], Skins(engine));
    }

    [Fact]
    public void AnHl2mpBodyKeepsItsModelsOwnSkinOnAnyTeam()
    {
        foreach (byte team in new byte[] { 0, 2, 3 })
        {
            var (_, engine) = Arrive(team, playerClass: 0);

            Assert.True(Skins(engine).Length == 0, $"team {team}: an HL2MP body wears its model's skin, as the player did");
        }
    }
}

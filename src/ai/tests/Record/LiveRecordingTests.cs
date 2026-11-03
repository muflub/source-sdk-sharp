using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Tests.Hl2mpNav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// §2.6, §8.2's determinism over a live session (Phase 6 step 2): dm_lockdown
/// recorded with <c>ai_record</c> while four bots (hard, normal, easy and a
/// Headhunter 10 / Discipline 10 custom) played on the pool's four workers
/// beside an idle human, committed under <c>recordings/live/</c>. Its brains,
/// rebuilt over the recorded world and this tree's dm_lockdown graph, give the
/// recording's intents byte for byte: inline, and on 1, 2 and 4 worker threads.
/// </summary>
public sealed class LiveRecordingTests
{
    private const string Map = "dm_lockdown";

    public static string Recording => SourceSharp.Tests.RepoLayout.Repo("src", "ai", "tests", "recordings", "live", "dm_lockdown-4bots.airec");

    private static readonly Lazy<(NavGraph Graph, NavSight Sight)> Nav = new(() =>
    {
        byte[] bsp = File.ReadAllBytes(PerTreeNav.BspPath(Map));
        Nav3dReader reader = Nav3dReader.Open(File.ReadAllBytes(PerTreeNav.NavPath(Map)));
        MapEntities entities = MapEntities.Read(bsp);
        DerivedNav nav = NavGraphFromNav3d.Derive(new Nav3dNavSource(reader), Hl2mpNavNumbers.Stock(reader.JumpHeight), NavEdgeCosts.Default, entities.Hurt);
        return (nav.Graph, NavSight.Build(nav.Graph, new Nav3dVoxelSpace(reader)));
    });

    internal static AiReplayResult Replay(IAiTransport transport)
    {
        using FileStream f = File.OpenRead(Recording);
        return AiReplayer.ReplayWithRecordedWorld(f, transport, _ => (Nav.Value.Graph, Nav.Value.Sight), setup => new Hl2mpDecider(setup));
    }

    [PerTreeNavFact(Map)]
    public void TheLiveSessionReplaysInlineToItsIntents()
    {
        AiReplayResult r = Replay(new InlineTransport());
        Assert.StartsWith(Map, r.Header.Map);
        Assert.True(r.RecordedIntentCount > 1000, $"the control: {r.RecordedIntentCount} intents recorded");
        Assert.Equal(r.RecordedIntentCount, r.ReplayedIntentCount);
        Assert.True(r.Identical, "inline: the intents differ from the live session's");
    }

    [PerTreeNavFact(Map)]
    public void TheLiveSessionReplaysAtOneTwoAndFourThreadsToItsIntents()
    {
        foreach (int threads in new[] { 1, 2, 4 })
        {
            using var pool = new AiWorkerPool();
            pool.Start(threads);
            AiReplayResult r = Replay(new PooledTransport(pool));
            Assert.Equal(r.RecordedIntentCount, r.ReplayedIntentCount);
            Assert.True(r.Identical, $"{threads} thread(s): the intents differ from the live session's");
        }
    }
}

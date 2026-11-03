using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Replay;
using SourceSharp.Ai.Tests.Hl2mpNav;
using SkiaSharp;
using Xunit;

namespace SourceSharp.Ai.Tests.Inspect;

/// <summary>
/// <c>ai-replay heat</c> (plan_hl2mp_ai §7.1): a recording replayed with the
/// brains remembering over the course's graph; while the opponent is seen
/// there is no heat, once sight is lost the bot's belief is drawn and spreads.
/// </summary>
public sealed class HeatTests
{
    private static readonly ActorId Bot = new(3, 1), Them = new(2, 0x4000_0001);

    private static (DerivedNav Nav, List<HeatFrame> Frames) Replayed()
    {
        DerivedNav nav = NavGraphFromNav3d.Derive(new Nav3dNavSource(NavFixtures.Course().Open()), Hl2mpNavNumbers.Stock(56f), NavEdgeCosts.Default, []);
        var stream = new MemoryStream();
        var rec = new AiRecorder(stream, new AiRecordingHeader(0x5EED, "course", 0, 0, 8, 4));
        var session = new AiSession(new InlineTransport(), rec, 8, 4);
        session.AddBot(Bot, 1, new byte[10]);
        var at = new AiVector(80, 260, 17);
        for (int t = 1; t <= 300; t++)
        {
            if (t == 1)
                session.Push(AiEvent.SightGained(Bot, new Tick(t), Them, at, default, 1f));
            if (t == 60)
                session.Push(AiEvent.SightLost(Bot, new Tick(t), Them, at, new AiVector(150, 0, 0)));
            AiSnapshot s = session.Snapshots.BeginWrite(new Tick(t));
            PerceivedView v = s.Add(Bot, new SelfState(new AiVector(40, 40, 17), default, default, 100, 0, -1, 0, 0, SelfFlags.Alive));
            if (t < 60)
                v.AddSighted(new ActorFacts(Them, at, default, -1, ActorActivity.None, 1f));
            session.Publish(s);
        }

        stream.Position = 0;
        var world = new MemoryWorld(new MapKnowledge("course", [], [], new WeaponKnowledge([])), nav.Graph, 0.015f, id => id.ToString());
        return (nav, HeatPng.Collect(stream, world, 30));
    }

    [Fact]
    public void NoHeatWhileSeenThenABeliefThatSpreads()
    {
        (DerivedNav nav, List<HeatFrame> frames) = Replayed();
        Assert.All(frames.Where(f => f.Tick < 60), f => Assert.Empty(f.Mass));
        HeatFrame early = frames.First(f => f.Tick >= 60 && f.Mass.Count > 0), late = frames.Last();
        Assert.True(late.Mass.Count > early.Mass.Count, $"the belief spreads: {early.Mass.Count} cells at {early.Tick}, {late.Mass.Count} at {late.Tick}");
        Assert.Equal(1f, late.Mass.Values.Sum(), 2);

        using SKBitmap png = SKBitmap.Decode(HeatPng.Render(nav, late, 0.5f));
        int hot = 0;
        for (int y = 0; y < png.Height; y++)
        {
            for (int x = 0; x < png.Width; x++)
            {
                if (png.GetPixel(x, y).Red > 120)
                    hot++;
            }
        }

        Assert.True(hot > 0, "the frame draws the heat");
    }
}

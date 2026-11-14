using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ragdolls;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The body's fade (c_tf_player.cpp:1512-1553): nothing is seen of a corpse
/// whose <c>cl_ragdoll_fade_time</c> is up — the alpha runs down at 600 a
/// second from the death time the arrival armed, and the last frame leaves it
/// at nought, hidden and never removed. An earlier frame leaves it drawn.
/// </summary>
public class RagdollFadeTests
{
    private static readonly EntityHandle Body = new(0x0005_0022u);

    private static (RagdollFades Fades, RecordingEngine Engine) Rig(float now, string fadeTime = "15")
    {
        var engine = Fake();
        engine.Strict = false;
        engine.Returns("Clock.Now", now);
        var time = DeclaredConVars.Holding<SourceSharp.Client.Ragdolls.RagdollFadeTimeConVar>(fadeTime);
        return (new RagdollFades(engine, engine, time), engine);
    }

    private static int Blends(RecordingEngine engine, out int lastAlpha)
    {
        var blends = engine.Calls.Where(c => c.Member == "EntityRender.SetBlending").ToList();
        lastAlpha = blends.Count > 0 ? blends[^1].ArgAs<int>(3) : -1;
        return blends.Count;
    }

    [Fact]
    public void The_frame_before_the_death_time_leaves_the_body_drawn()
    {
        var (fades, engine) = Rig(now: 10f);
        fades.Begin(Body);

        fades.ClientThink(0.016f);

        Assert.Equal(1, fades.Live);
        Assert.Equal(0, Blends(engine, out _));
    }

    [Fact]
    public void The_fade_runs_at_six_hundred_a_second_once_the_time_is_up()
    {
        var (fades, engine) = Rig(now: 10f);   // the death time is 25
        fades.Begin(Body);

        engine.Returns("Clock.Now", 25.1f);
        fades.ClientThink(0.1f);

        Assert.Equal(1, fades.Live);
        Assert.Equal(1, Blends(engine, out int alpha));

        Assert.Equal(195, alpha);   // 255 - 600 * 0.1
    }

    [Fact]
    public void The_fade_ends_at_alpha_nought_and_never_removes()
    {
        var (fades, engine) = Rig(now: 10f);
        fades.Begin(Body);

        engine.Returns("Clock.Now", 25.1f);
        fades.ClientThink(0.5f);    // 255 - 300: past nought, clamped

        Assert.Equal(0, fades.Live);
        Assert.Equal(1, Blends(engine, out int alpha));

        Assert.Equal(0, alpha);

        // And the body is still in the world: the next frame blends nothing,
        // because there is nothing left to fade. Removal was never this
        // service's to do — EndFadeOut hides (c_tf_player.cpp:1634-1660).
        fades.ClientThink(0.016f);

        Assert.Equal(1, Blends(engine, out _));
    }

    [Fact]
    public void An_end_takes_the_body_out_of_the_fade()
    {
        var (fades, engine) = Rig(now: 10f);
        fades.Begin(Body);
        fades.End(Body);

        engine.Returns("Clock.Now", 25.1f);
        fades.ClientThink(0.016f);

        Assert.Equal(0, fades.Live);
        Assert.Equal(0, Blends(engine, out _));
    }

    [Fact]
    public void The_fade_arms_once_no_matter_how_often_it_is_asked()
    {
        var (fades, engine) = Rig(now: 10f);
        fades.Begin(Body);

        // A later update (the same body, served again) must not re-arm: the
        // death time stays the first arrival's.
        engine.Returns("Clock.Now", 12f);
        fades.Begin(Body);

        Assert.Equal(1, fades.Live);

        engine.Returns("Clock.Now", 25.1f);
        fades.ClientThink(0.1f);

        // One entry, not two — and at the FIRST arrival's death time, 25, not
        // a re-armed 27.
        Assert.Equal(1, Blends(engine, out int alpha));

        Assert.Equal(195, alpha);
    }
}

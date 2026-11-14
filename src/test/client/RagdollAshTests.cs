using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ragdolls;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The ashing corpse's half-second (c_tf_player.cpp:1435-1465): the frame
/// that passes <see cref="FlameRules.AshDissolveSeconds"/> hides the body —
/// the dissolve's own alpha-nought blending — and stops the ash particle by
/// its slot, once; an earlier frame leaves both alone.
/// </summary>
public class RagdollAshTests
{
    private static readonly EntityHandle Body = new(0x0005_0021u);

    private static (RagdollAsh Ash, RecordingEngine Engine) Rig(float now)
    {
        var engine = Fake();
        engine.Strict = false;
        engine.Returns("Clock.Now", now);
        return (new RagdollAsh(engine, engine, engine), engine);
    }

    private static int? BlendCalls(RecordingEngine engine, out int stops)
    {
        stops = engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStopSlot");
        return engine.Calls.Count(c => c.Member == "EntityRender.SetBlending" && c.ArgAs<int>(3) == 0);
    }

    [Fact]
    public void The_frame_inside_the_half_second_leaves_the_body_drawn()
    {
        var (ash, engine) = Rig(10.2f);
        ash.Begin(Body, 10f);

        ash.ClientThink(0.016f);

        Assert.Equal(1, ash.Live);
        Assert.Equal(0, BlendCalls(engine, out int stops));
        Assert.Equal(0, stops);
    }

    [Fact]
    public void The_frame_past_the_half_second_hides_the_body_and_stops_the_ash()
    {
        var (ash, engine) = Rig(10.6f);
        ash.Begin(Body, 10f);

        ash.ClientThink(0.016f);

        Assert.Equal(0, ash.Live);
        Assert.Equal(1, BlendCalls(engine, out int stops));
        Assert.Equal(1, stops);
        var stop = engine.Calls.Last(c => c.Member == "Effects.ParticleSystemStopSlot");

        Assert.Equal(RagdollAsh.CorpseSlot, stop.ArgAs<int>(1));
    }

    [Fact]
    public void The_hide_is_once_and_an_end_ends_it_early()
    {
        var (ash, engine) = Rig(11f);
        ash.Begin(Body, 10f);
        ash.Begin(Body, 10f);
        Assert.Equal(1, ash.Live);

        ash.ClientThink(0.016f);
        ash.ClientThink(0.016f);

        Assert.Equal(1, BlendCalls(engine, out int stops));   // the second frame hid nothing anew
        Assert.Equal(1, stops);

        var (fresh, _) = Rig(11f);
        fresh.Begin(Body, 10f);
        fresh.End(Body);
        fresh.ClientThink(0.016f);

        Assert.Equal(0, fresh.Live);
    }
}

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ragdolls;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The ice statue's hold (stock's m_frozenTimer, c_tf_player.cpp:741,
/// :1007-1031): the frame that passes the hold's end thaws the body —
/// <see cref="IClientRagdolls.Thaw"/> — once; an earlier frame, a second
/// Begin and an End leave the one hold as it stood.
/// </summary>
public class RagdollIceTests
{
    private static readonly EntityHandle Body = new(0x0005_0022u);

    /// <summary>One roll, named: the hold's length is the point.</summary>
    private sealed class FixedRandom(float value) : IUniformRandom
    {
        public float Roll { get; set; } = value;

        public float RandomFloat(float min, float max) => Roll;

        public int RandomInt(int min, int max) => min;

        public float RandomFloatExp(float min, float max, float exponent) => min;
    }

    private static (RagdollIce Ice, RecordingRagdolls Ragdolls) Rig(float now, float roll)
    {
        RecordingEngine engine = Fake();
        engine.Strict = false;
        engine.Returns("Clock.Now", now);
        var ragdolls = new RecordingRagdolls(engine);
        return (new RagdollIce(ragdolls, new FixedRandom(roll), engine), ragdolls);
    }

    [Fact]
    public void The_frame_inside_the_hold_leaves_the_statue_standing()
    {
        var (ice, ragdolls) = Rig(10f, roll: 1f);   // thaw at 11
        ice.Begin(Body, 10f);

        ice.ClientThink(0.016f);

        Assert.Equal(1, ice.Frozen);
        Assert.Equal(0, ragdolls.Thawed);
    }

    [Fact]
    public void The_frame_past_the_hold_thaws_the_body_once()
    {
        var (ice, ragdolls) = Rig(11.1f, roll: 1f); // thaw at 11
        ice.Begin(Body, 10f);

        ice.ClientThink(0.016f);

        Assert.Equal(0, ice.Frozen);
        Assert.Equal(1, ragdolls.Thawed);
        Assert.Equal(Body, ragdolls.ThawedWho);
    }

    [Fact]
    public void The_hold_is_taken_once_and_an_end_ends_it_early()
    {
        var (ice, ragdolls) = Rig(11.1f, roll: 1f);
        ice.Begin(Body, 10f);
        ice.Begin(Body, 10f);
        Assert.Equal(1, ice.Frozen);

        ice.ClientThink(0.016f);
        ice.ClientThink(0.016f);

        Assert.Equal(1, ragdolls.Thawed);

        var (fresh, freshRagdolls) = Rig(11.1f, roll: 1f);
        fresh.Begin(Body, 10f);
        fresh.End(Body);
        fresh.ClientThink(0.016f);

        Assert.Equal(0, fresh.Frozen);
        Assert.Equal(0, freshRagdolls.Thawed);
    }
}

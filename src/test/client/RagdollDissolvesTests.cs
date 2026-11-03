using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Ragdolls;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>C_EntityDissolve on a client ragdoll: its colour, its alpha and its lift, frame by frame.</summary>
public class RagdollDissolvesTests
{
    private static readonly EntityHandle Body = new(0x0005_0020u);

    private sealed class Blocks : IStandaloneBlocks
    {
        public readonly byte[] Block_ = new byte[EngineBlock.SizeBytes];
        public Span<byte> Block(EntityHandle entity) => Block_;
    }

    private sealed class NoParticles : IDissolveParticles
    {
        public void Emit(string material, List<SimpleParticle> particles, Vector sortOrigin) { }
    }

    private static (RagdollDissolves Dissolves, Blocks Blocks, RecordingEngine Engine) Rig(float now)
    {
        var engine = Fake();
        engine.Strict = false;
        engine.Returns("Clock.Now", now);
        var blocks = new Blocks();
        // ModelIndex 0 stops the sparks: they need a model, and these facts are about the fade.
        return (new RagdollDissolves(new NoClientRagdolls(), blocks, null!, engine, new ViewSlots(), new ScriptedRandom(),
                                     new NoParticles(), engine, () => 600f), blocks, engine);
    }

    private static byte TintOf(Blocks blocks)
        => System.Runtime.InteropServices.MemoryMarshal.Read<EngineBlock>(blocks.Block_).Client.RenderColorR;

    [Theory]
    [InlineData(10f, 255, 255)]    // just begun
    [InlineData(10.5f, 127, 255)]  // half way to black
    [InlineData(11f, 0, 255)]      // black
    [InlineData(11.95f, 0, 127)]   // vanishing
    public void Each_frame_sets_its_colour_and_alpha(float now, int tint, int alpha)
    {
        var (dissolves, blocks, engine) = Rig(now);
        dissolves.Begin(Body, 10f);

        dissolves.ClientThink(0.016f);

        Assert.Equal(tint, TintOf(blocks));
        Assert.Equal(alpha, engine.Calls.Last(c => c.Member == "EntityRender.SetBlending").ArgAs<int>(3));
    }

    [Fact]
    public void It_stops_when_it_is_done()
    {
        var (dissolves, _, _) = Rig(12.5f);
        dissolves.Begin(Body, 10f);
        dissolves.ClientThink(0.016f);
        Assert.Equal(0, dissolves.Live);
    }

    [Fact]
    public void A_body_begun_twice_is_one_dissolve_and_its_end_ends_it()
    {
        var (dissolves, _, _) = Rig(10f);
        dissolves.Begin(Body, 10f);
        dissolves.Begin(Body, 10f);
        Assert.Equal(1, dissolves.Live);

        dissolves.End(Body);
        Assert.Equal(0, dissolves.Live);
    }
}

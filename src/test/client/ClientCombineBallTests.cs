using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The AR2's ball as drawn: C_PropCombineBall::DrawModel for a ball never held.</summary>
public class ClientCombineBallTests
{
    private static readonly EntityHandle Ball = new(0x0005_0011u);

    private static (ClientCombineBall Ball, QuadRecorder Quads, RecordingEngine Engine) Rig(bool emit = true)
    {
        var engine = Fake();
        engine.Strict = false;
        engine.Returns("Clock.Now", 2f).Returns("Entities.GetOrigin", new Vector(50f, 0f, 0f));
        var quads = new QuadRecorder();
        var ball = new ClientCombineBall(engine, quads, new ViewSlots(), new ScriptedRandom(), engine, engine) { Handle = Ball };
        ball.State.BindLocal();
        ball.State.Emit = emit;
        ball.State.Radius = 10f;
        return (ball, quads, engine);
    }

    /// <summary>DrawFlicker, then the body, and never the model: DrawModel answers for it.</summary>
    [Fact]
    public void It_draws_the_glow_then_the_body()
    {
        var (ball, quads, _) = Rig();

        Assert.Equal(1, ball.OnDraw());

        Assert.Equal([ClientCombineBall.FlickerMaterial, ClientCombineBall.BodyMaterial],
                     quads.Draws.Select(d => quads.MaterialName(d.Material)));
        Assert.All(quads.Draws, d => Assert.Equal(4, d.Vertices.Length));
    }

    [Fact]
    public void Past_its_end_it_draws_nothing()
    {
        var (ball, quads, _) = Rig(emit: false);
        Assert.Equal(0, ball.OnDraw());
        Assert.Empty(quads.Draws);
    }

    /// <summary>The cull's reach must hold the flicker's largest, radius * 2.5 at the largest radius.</summary>
    [Fact]
    public void It_asks_to_draw_itself_in_the_translucent_pass()
    {
        var (ball, _, engine) = Rig();

        ball.OnDataChanged(default, DataUpdateType.Created);

        Assert.True(engine.Called("EntityRender.SetCustomDraw"));
        var blending = engine.Calls.Single(c => c.Member == "EntityRender.SetBlending");
        Assert.True(blending.ArgAs<bool>(1));
    }

    [Theory]
    [InlineData(0f, 10f)]
    [InlineData(MathF.PI / 50f, 11f)]   // sin( now * 25 ) at its top
    public void The_body_breathes_a_unit_about_its_radius(float now, float scale)
        => Assert.Equal(scale, ClientCombineBall.BodyScale(10f, now), 3);

    /// <summary>DrawHaloOriented: at no roll it is DrawHalo, a quarter turn swaps the view's axes.</summary>
    [Fact]
    public void An_oriented_halo_turns_on_the_view()
    {
        var view = new ViewSlots().View();
        Span<FxVertex> plain = stackalloc FxVertex[4], none = stackalloc FxVertex[4], turned = stackalloc FxVertex[4];
        EffectQuads.Halo(plain, Vector.Zero, 8f, new Vector(1f, 1f, 1f), view);
        EffectQuads.HaloOriented(none, Vector.Zero, 8f, new Vector(1f, 1f, 1f), 0f, view);
        EffectQuads.HaloOriented(turned, Vector.Zero, 8f, new Vector(1f, 1f, 1f), MathF.PI / 2f, view);

        for (int i = 0; i < 4; i++)
            Assert.True((plain[i].Position - none[i].Position).Length < 0.001f, $"vertex {i}");

        // Turned a quarter: where -up-right was, is now +... of the other axis.
        Assert.True((turned[0].Position - (view.Right * 8f - view.Up * 8f)).Length < 0.001f, $"{turned[0].Position}");
    }
}

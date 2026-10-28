using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// C_WeaponStunStick's client draw (shared/hl2mp/weapon_stunstick.cpp),
/// held to stock's numbers: the two-pass group; in the first person the
/// head's glow (0.4 and 22 swinging, faded otherwise) at the centre
/// attachment and a point at each of attachments 0 to 18 (0.05 to 0.5, 4 to
/// 5), fading over 0.25 s to a tenth once a swing ends; a lightning beam one
/// swinging frame in four; in the third person the head's glow, its blue
/// flare and one frame in six a tesla arc.
/// </summary>
public class StunStickDrawEffectTests
{
    private const int IdleSequence = 0, SwingSequence = 4;
    private static readonly Vector Head = new(20, 0, 50);

    private sealed class Lift : IViewModelAttachmentFormat
    {
        public Vector ForWorldPass(EntityHandle entity, Vector attachment) => attachment + new Vector(0, 0, 100);
    }

    private static (StunStickDrawEffect Effect, WeaponEffectRig Rig) Rig()
    {
        var rig = new WeaponEffectRig(EntityNames.WeaponStunstick);
        for (int i = 1; i <= 19; i++)
            rig.Models.At(rig.ViewModel.Handle, i, new Vector(10, i, 0));
        rig.Models.At(rig.Weapon.Handle, 1, Head, new Vector(0, 0, 0));
        rig.Models.Activities[(rig.ViewModel.Handle.Value, "ACT_VM_HITCENTER")] = SwingSequence;
        rig.Models.Activities[(rig.ViewModel.Handle.Value, "ACT_VM_IDLE")] = IdleSequence;
        rig.ViewModel.Present(IdleSequence, 0, 1f);
        rig.Now = 100f;

        var effect = new StunStickDrawEffect(rig.Quads, rig.View, rig.Models, rig.Beams, rig.Random, new Lift());
        return (effect, rig);
    }

    private static void Swing(WeaponEffectRig rig) => rig.ViewModel.Present(SwingSequence, 1, 1f);

    private static float HaloScale(FxVertex[] quad) => (quad[2].Position - quad[0].Position).Length / (2f * MathF.Sqrt(2f));

    [Fact]
    public void TheStickAnswersTheTwoPassGroup()
        => Assert.True(Rig().Effect.TwoPass);

    [Fact]
    public void ASwingIsTheViewmodelPlayingASwingActivity()
    {
        var (effect, rig) = Rig();
        Assert.False(effect.InSwing(rig.FirstPerson()));

        Swing(rig);

        Assert.True(effect.InSwing(rig.FirstPerson()));
    }

    [Fact]
    public void AStickNotDrawnThroughTheViewmodelIsAlwaysSwinging()
    {
        var (effect, rig) = Rig();

        Assert.True(effect.InSwing(rig.ThirdPerson()));
    }

    [Fact]
    public void AtRestEachPointIsATenthOfASwingsAndTheCentreThatDoesNotAnswerIsNotDrawn()
    {
        // A stick made in the first person looked "sparkrear" up on w_stunbaton, which has none: 0.
        var (effect, rig) = Rig();

        effect.ViewModelDrawn(rig.FirstPerson());

        var points = rig.Drawn(StunStickDrawEffect.GlowMaterialNoZ);
        Assert.Equal(18, points.Count);
        Assert.Equal(new Vector(10, 1, 0), QuadMath.Centre(points[0]));
        Assert.Equal(0.275f * 0.1f, points[0][0].R, 4);
        Assert.Equal(4.5f * 0.1f, HaloScale(points[0]), 3);
        Assert.False(rig.Quads.Draws[0].ByteColor);
    }

    [Fact]
    public void EachOfTheNineteenPointsDrawsItsBrightnessThenItsSize()
    {
        var (effect, rig) = Rig();

        effect.ViewModelDrawn(rig.FirstPerson());

        Assert.Equal(38, rig.Random.Asked.Count);
        Assert.Equal((false, 0.05f, 0.5f), rig.Random.Asked[0]);
        Assert.Equal((false, 4f, 5f), rig.Random.Asked[1]);
    }

    [Fact]
    public void SwingingTheCentreGlowsAt22AndThePointsAtFullStrength()
    {
        var (effect, rig) = Rig();
        rig.Models.Named(rig.Weapon.Handle, StunStickDrawEffect.CoreAttachmentName, 19);
        Swing(rig);

        effect.ViewModelDrawn(rig.FirstPerson());

        var drawn = rig.Drawn(StunStickDrawEffect.GlowMaterialNoZ);
        Assert.Equal(19, drawn.Count);
        Assert.Equal(new Vector(10, 19, 0), QuadMath.Centre(drawn[0]));
        Assert.Equal(22f, HaloScale(drawn[0]), 3);
        Assert.Equal(0.4f, drawn[0][0].R, 4);
        Assert.Equal(0.275f, drawn[1][0].R, 4);
        Assert.Equal(4.5f, HaloScale(drawn[1]), 3);
    }

    [Fact]
    public void TheEndOfASwingFadesOverAQuarterSecond()
    {
        var (effect, rig) = Rig();
        rig.Models.Named(rig.Weapon.Handle, StunStickDrawEffect.CoreAttachmentName, 19);
        Swing(rig);
        effect.Think(rig.FirstPerson() with { FrameTime = 0f });

        rig.ViewModel.Present(IdleSequence, 2, 1f);
        rig.Now = 200f;
        effect.Think(rig.FirstPerson());
        rig.Now = 200.125f;
        effect.ViewModelDrawn(rig.FirstPerson());

        // Halfway: 1 -> 0.1 is 0.55, of the centre's 0.4.
        var centre = rig.Drawn(StunStickDrawEffect.GlowMaterialNoZ)[0];
        Assert.Equal(0.4f * 0.55f, centre[0].R, 4);
        Assert.Equal(20f, HaloScale(centre), 3);
    }

    [Fact]
    public void ASwingingFrameStrikesALightningBeamOneTimeInFour()
    {
        var (effect, rig) = Rig();
        Swing(rig);
        rig.Random.Values.Enqueue(0);     // RandomInt(0, 3): strike
        rig.Random.Values.Enqueue(7);     // RandomInt(0, 15): the attachment
        rig.Random.Values.Enqueue(1);     // RandomVector(-8, 8)
        rig.Random.Values.Enqueue(2);
        rig.Random.Values.Enqueue(3);
        rig.Random.Values.Enqueue(1.5f);  // width
        rig.Random.Values.Enqueue(20f);   // amplitude

        effect.Think(rig.FirstPerson() with { FrameTime = 1f / 60f });

        BeamInfo beam = Assert.Single(rig.BeamsMade());
        Assert.Equal((rig.ViewModel.Handle, 7, EntityHandle.None, -1), (beam.StartEnt, beam.StartAttachment, beam.EndEnt, beam.EndAttachment));
        Assert.Equal(new Vector(10 + 1, 7 + 2, 100 + 3), beam.End);
        Assert.Equal(("sprites/lgtning.vmt", 0.05f, 1.5f, 0f, 20f, 255f, 16), (beam.ModelName, beam.Life, beam.Width, beam.EndWidth,
                                                                         beam.Amplitude, beam.Brightness, beam.Segments));
        Assert.Equal(BeamFlags.None, beam.Flags);
        Assert.Equal(new[] { (true, 0f, 3f), (true, 0f, 15f) }, rig.Random.Asked.Take(2).ToArray());
    }

    [Fact]
    public void TheOtherThreeFramesStrikeNothing()
    {
        var (effect, rig) = Rig();
        Swing(rig);
        rig.Random.Values.Enqueue(1);

        effect.Think(rig.FirstPerson() with { FrameTime = 1f / 60f });

        Assert.Empty(rig.BeamsMade());
    }

    [Fact]
    public void APausedFrameStrikesNothing()
    {
        var (effect, rig) = Rig();
        Swing(rig);

        effect.Think(rig.FirstPerson() with { FrameTime = 0f });

        Assert.Empty(rig.BeamsMade());
        Assert.Empty(rig.Random.Asked);
    }

    [Fact]
    public void AtRestNoBeamIsStruck()
    {
        var (effect, rig) = Rig();

        effect.Think(rig.FirstPerson() with { FrameTime = 1f / 60f });

        Assert.Empty(rig.BeamsMade());
    }

    [Fact]
    public void InTheThirdPersonTheHeadGlowsWithABlueFlareOverIt()
    {
        var (effect, rig) = Rig();
        rig.Random.Values.Enqueue(0.7f);  // the flare's brightness
        rig.Random.Values.Enqueue(5f);    // its size
        rig.Random.Values.Enqueue(3);     // RandomInt(0, 5): no arc

        Assert.Equal(1, effect.DrawModel(rig.ThirdPerson() with { FrameTime = 1f / 60f }, unchecked((int)0x80000009)));

        var glow = Assert.Single(rig.Drawn(StunStickDrawEffect.GlowMaterial));
        Assert.Equal((Head, 22f, 0.4f), (QuadMath.Centre(glow), HaloScale(glow), glow[0].R));
        var flare = Assert.Single(rig.Drawn(StunStickDrawEffect.GlowMaterial2));
        Assert.Equal((Head, 5f, 0.7f), (QuadMath.Centre(flare), HaloScale(flare), flare[0].R));
        Assert.Empty(rig.BeamsMade("Beams.CreateBeamPoints(BeamInfo)"));
    }

    [Fact]
    public void OneThirdPersonFrameInSixArcsATeslaBeamOffTheHead()
    {
        var (effect, rig) = Rig();
        rig.Random.Values.Enqueue(0.7f);
        rig.Random.Values.Enqueue(5f);
        rig.Random.Values.Enqueue(0);     // RandomInt(0, 5): arc
        rig.Random.Values.Enqueue(-2);    // RandomVector(-12, 8)
        rig.Random.Values.Enqueue(0);
        rig.Random.Values.Enqueue(2);
        rig.Random.Values.Enqueue(2f);    // width
        rig.Random.Values.Enqueue(1.5f);  // amplitude

        effect.DrawModel(rig.ThirdPerson() with { FrameTime = 1f / 60f }, unchecked((int)0x80000009));

        BeamInfo arc = Assert.Single(rig.BeamsMade("Beams.CreateBeamPoints(BeamInfo)"));
        Assert.Equal((BeamType.Tesla, Head - new Vector(1, 0, 0), Head + new Vector(2, 4, 6)), (arc.Type, arc.Start, arc.End));
        Assert.Equal(("sprites/lgtning.vmt", 0.01f, 2f, 1.5f, 16, BeamFlags.ShadeOut),
                     (arc.ModelName, arc.Life, arc.Width, arc.Amplitude, arc.Segments, arc.Flags));
    }

    [Fact]
    public void TheLocalCarriersWorldModelDrawsNothingInTheFirstPerson()
    {
        var (effect, rig) = Rig();

        Assert.Equal(0, effect.DrawModel(rig.FirstPerson(), unchecked((int)0x80000009)));
        Assert.Empty(rig.Quads.Draws);
    }
}

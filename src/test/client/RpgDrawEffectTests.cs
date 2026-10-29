using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// CWeaponRPG's client draw (shared/hl2mp/weapon_rpg.cpp: DrawEffects,
/// InitBeam, ViewModelDrawn), held to stock's numbers: while the laser guides,
/// RPG_LASER_SPRITE at the viewmodel's attachment 2, 8 units across give or
/// take 2, white; and once, with ammunition left, a beam from attachment 2 to
/// 3 of RPG_BEAM_SPRITE_NOZ, 2 wide, red, four segments, forever and shading
/// out, whose fade is 128 units times the trace's fraction and whose
/// brightness is drawn from 128 to 200 each frame.
/// </summary>
public class RpgDrawEffectTests
{
    private static readonly Vector Laser = new(10, -4, -3);

    private static (RpgDrawEffect Effect, WeaponEffectRig Rig) Rig(bool guiding = true, bool hidden = false, int ammo = 3)
    {
        var rig = new WeaponEffectRig(EntityNames.WeaponRpg);
        int special = RpgState.Set(RpgState.Set(0, RpgState.Guiding, guiding), RpgState.HideGuiding, hidden);
        rig.Weapon.State.Special = special;
        rig.Weapon.State.Reserve = ammo;
        rig.Models.At(rig.ViewModel.Handle, RpgDrawEffect.GuideAttachment, Laser, new Vector(0, 0, 0));
        rig.Engine.Returns("WorldQuery.ClientTraceLine",
                           new ClientTraceResult(true, 0.25f, Laser + new Vector(32, 0, 0), new Vector(-1, 0, 0), 0, 0, 0, 0));

        var effect = new RpgDrawEffect(rig.Quads, rig.View, rig.Models, rig.Engine, rig.Beams, rig.Random);
        return (effect, rig);
    }

    [Fact]
    public void TheGlowIsTheLaserSpriteAtTheViewmodelsLaserAttachment()
    {
        var (effect, rig) = Rig();
        rig.Random.Values.Enqueue(1.5f);

        effect.ViewModelDrawn(rig.FirstPerson());

        FxVertexAssert.Single(rig.Drawn(RpgDrawEffect.LaserSprite), out var quad);
        Assert.Equal(Laser, QuadMath.Centre(quad));
        Assert.Equal(9.5f, QuadMath.Width(quad), 3);
        Assert.All(quad, v => Assert.Equal((255f, 255f, 255f, 255f), (v.R, v.G, v.B, v.A)));
    }

    [Fact]
    public void TheGlowsSizeIsDrawnFromEightGiveOrTakeTwo()
    {
        var (effect, rig) = Rig();

        effect.ViewModelDrawn(rig.FirstPerson());

        Assert.Equal((false, -2f, 2f), rig.Random.Asked[0]);
    }

    [Fact]
    public void ALaserThatIsOffDrawsNothing()
    {
        var (effect, rig) = Rig(guiding: false);

        effect.ViewModelDrawn(rig.FirstPerson());

        Assert.Empty(rig.Quads.Draws);
        Assert.Empty(rig.BeamsMade());
    }

    [Fact]
    public void AHiddenLaserDrawsNothing()
    {
        var (effect, rig) = Rig(hidden: true);

        effect.ViewModelDrawn(rig.FirstPerson());

        Assert.Empty(rig.Quads.Draws);
    }

    [Fact]
    public void TheBeamIsInitBeamsFirstPersonBeam()
    {
        var (effect, rig) = Rig();

        effect.ViewModelDrawn(rig.FirstPerson());

        BeamInfo beam = Assert.Single(rig.BeamsMade());
        Assert.Equal((rig.ViewModel.Handle, 2, rig.ViewModel.Handle, 3, "effects/laser1_noz.vmt"),
                     (beam.StartEnt, beam.StartAttachment, beam.EndEnt, beam.EndAttachment, beam.ModelName));
        Assert.Equal((2f, 2f, 0f, 0f, 1f, 30f, 4), (beam.Width, beam.EndWidth, beam.Life, beam.Amplitude, beam.Speed,
                                                    beam.FrameRate, beam.Segments));
        Assert.Equal((255f, 0f, 0f), (beam.Red, beam.Green, beam.Blue));
        Assert.Equal(BeamFlags.Forever | BeamFlags.ShadeOut, beam.Flags);
        Assert.True(beam.Renderable);
    }

    [Fact]
    public void TheBeamIsMadeOnce()
    {
        var (effect, rig) = Rig();

        effect.ViewModelDrawn(rig.FirstPerson());
        effect.ViewModelDrawn(rig.FirstPerson());

        Assert.Single(rig.BeamsMade());
    }

    [Fact]
    public void NoBeamIsMadeWithNothingLeftToFireButTheGlowStillDraws()
    {
        var (effect, rig) = Rig(ammo: 0);

        effect.ViewModelDrawn(rig.FirstPerson());

        Assert.Empty(rig.BeamsMade());
        Assert.Single(rig.Drawn(RpgDrawEffect.LaserSprite));
    }

    [Fact]
    public void TheBeamFadesOverTheTracesRunAndFlickers()
    {
        var (effect, rig) = Rig();
        rig.Random.Values.Enqueue(0f);     // the glow's size
        rig.Random.Values.Enqueue(150f);   // the beam's brightness

        effect.ViewModelDrawn(rig.FirstPerson());

        Beam beam = effect.BeamOf(rig.Weapon.Handle)!;
        Assert.Equal(128f * 0.25f, beam.FadeLength);
        Assert.Equal(150f, beam.Brightness);
        Assert.Equal((true, 128f, 200f), rig.Random.Asked[1]);
    }

    [Fact]
    public void TheRunIsTracedFromTheGlowAlongTheAttachmentPastTheOwner()
    {
        var (effect, rig) = Rig();

        effect.ViewModelDrawn(rig.FirstPerson());

        var call = rig.Engine.Last("WorldQuery.ClientTraceLine");
        Assert.NotNull(call);
        Assert.Equal((Laser, Laser + new Vector(128, 0, 0), TraceMask.Shot, TraceFilter.Simple, WeaponEffectRig.Local),
                     ((Vector)call.Value.Args[0]!, (Vector)call.Value.Args[1]!, (TraceMask)call.Value.Args[2]!,
                      (TraceFilter)call.Value.Args[3]!, (int)call.Value.Args[4]!));
    }

    [Fact]
    public void TurningTheLaserOffDarkensTheBeam()
    {
        var (effect, rig) = Rig();
        effect.ViewModelDrawn(rig.FirstPerson());

        rig.Weapon.State.Special = 0;
        effect.ViewModelDrawn(rig.FirstPerson());

        Assert.Equal(0f, effect.BeamOf(rig.Weapon.Handle)!.Brightness);
    }

    [Fact]
    public void AWeaponThatStopsBeingAnRpgLetsItsBeamDie()
    {
        var (effect, rig) = Rig();
        effect.ViewModelDrawn(rig.FirstPerson());
        Beam beam = effect.BeamOf(rig.Weapon.Handle)!;

        effect.Forget(rig.Weapon.Handle);

        Assert.Equal(BeamFlags.None, beam.Flags);
        Assert.True(beam.Die < 0f);
        Assert.Null(effect.BeamOf(rig.Weapon.Handle));
    }

    [Fact]
    public void TheCarriedRpgIsNotTwoPassAndDrawsNothingBesideItsModel()
    {
        var (effect, rig) = Rig();

        Assert.False(effect.TwoPass);
        Assert.Equal(-1, effect.DrawModel(rig.ThirdPerson(), unchecked((int)0x80000009)));
        Assert.Empty(rig.Quads.Draws);
    }
}

/// <summary>Single-quad assertions over recorded draws.</summary>
internal static class FxVertexAssert
{
    public static void Single(IReadOnlyList<SourceSharp.Client.Interop.FxVertex[]> draws,
                              out SourceSharp.Client.Interop.FxVertex[] quad)
    {
        quad = Assert.Single(draws);
        Assert.Equal(4, quad.Length);
    }
}

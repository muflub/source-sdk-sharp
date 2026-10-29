using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Interop;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// C_WeaponPhysCannon's client draw and motor (shared/hl2mp/
/// weapon_physcannon.cpp), held to stock's numbers: StartEffects' sprites
/// and attachments, each DoEffect's moves of their scales and alphas, the
/// beams a hold makes, the blast a launch makes, and the hold sound's ramps.
/// </summary>
public class PhysCannonDrawEffectTests
{
    private const float T0 = 10f;

    private sealed class Sounds : IClientSoundController
    {
        public readonly List<string> Calls = [];

        public void Create(int patch) => Calls.Add($"create {patch}");
        public void Destroy(int patch) => Calls.Add($"destroy {patch}");
        public void Play(int patch, float volume, float pitch, float startTime) => Calls.Add($"play {patch} {volume} {pitch}");
        public float PlayEnvelope(int patch, int command, ReadOnlySpan<float> points, int count) => throw new InvalidOperationException();
        public void ChangePitch(int patch, float target, float deltaTime) => Calls.Add($"pitch {patch} {target} {deltaTime}");
        public void ChangeVolume(int patch, float target, float deltaTime) => Calls.Add($"volume {patch} {target} {deltaTime}");
        public float GetPitch(int patch) => throw new InvalidOperationException();
        public float GetVolume(int patch) => throw new InvalidOperationException();
        public void FadeOut(int patch, float deltaTime, bool destroyOnFadeout) => Calls.Add($"fade {patch} {deltaTime} {destroyOnFadeout}");
        public void CommandAdd(int patch, float executeDeltaTime, int command, float commandTime, float value) => throw new InvalidOperationException();
        public void CommandClear(int patch) => throw new InvalidOperationException();
        public void SystemReset() => throw new InvalidOperationException();
        public void Shutdown(int patch) => throw new InvalidOperationException();
    }

    private static readonly string[] WorldNames = ["core", "fork1m", "fork1t", "fork2m", "fork2t", "fork3m", "fork3t"];

    private static (PhysCannonDrawEffect Effect, WeaponEffectRig Rig, Sounds Sounds) Rig()
    {
        var rig = new WeaponEffectRig(EntityNames.WeaponPhyscannon);
        for (int i = 1; i <= 7; i++)
        {
            rig.Models.At(rig.ViewModel.Handle, i, new Vector(10, i, 0));
            rig.Models.At(rig.Weapon.Handle, i, new Vector(50, i, 0));
            rig.Models.Named(rig.Weapon.Handle, WorldNames[i - 1], i);
        }
        rig.Engine.Returns("Sound.SoundPatchCreate", 77);
        rig.Now = T0;

        var sounds = new Sounds();
        var effect = new PhysCannonDrawEffect(rig.Quads, rig.View, rig.Models, rig.Beams, sounds, rig.Engine, rig.Random);
        return (effect, rig, sounds);
    }

    private static void Think(PhysCannonDrawEffect effect, WeaponEffectRig rig, PhysCannonEffect state, bool firstPerson = true,
                              bool open = false)
    {
        rig.Weapon.State.CannonEffect = (int)state;
        rig.Weapon.State.CannonOpen = open;
        effect.Think(firstPerson ? rig.FirstPerson() : rig.ThirdPerson());
    }

    private static float Size(FxVertex[] quad) => QuadMath.Width(quad);

    [Fact]
    public void TheGunAnswersTheTwoPassGroup()
        => Assert.True(Rig().Effect.TwoPass);

    [Fact]
    public void StartingInTheFirstPersonLooksTheGlowsUpByTheViewmodelsNamesOnTheWorldModel()
    {
        var (effect, rig, _) = Rig();

        Think(effect, rig, PhysCannonEffect.None);

        var p = effect.CannonOf(rig.Weapon.Handle)!.Parameters;
        Assert.Equal([0, 2, 3, 0, 4, 5], p.Skip(PhysCannonDrawEffect.Glow1).Take(6).Select(x => x.Attachment));
        Assert.Equal([3, 5, 7], p.Skip(PhysCannonDrawEffect.EndCap1).Select(x => x.Attachment));
        Assert.Equal((1, 1), (p[PhysCannonDrawEffect.Core].Attachment, p[PhysCannonDrawEffect.Blast].Attachment));
    }

    [Fact]
    public void StartingInTheThirdPersonLooksTheGlowsUpByTheWorldModelsNames()
    {
        var (effect, rig, _) = Rig();

        Think(effect, rig, PhysCannonEffect.None, firstPerson: false);

        var p = effect.CannonOf(rig.Weapon.Handle)!.Parameters;
        Assert.Equal([2, 3, 4, 5, 6, 7], p.Skip(PhysCannonDrawEffect.Glow1).Take(6).Select(x => x.Attachment));
    }

    [Fact]
    public void ReadyInTheFirstPersonIsTheCoreAt14And128AndTheGlowsFlickerSmall()
    {
        var (effect, rig, _) = Rig();
        Think(effect, rig, PhysCannonEffect.Ready);

        rig.Now = T0 + 0.5f;
        effect.ViewModelDrawn(rig.FirstPerson());

        var core = Assert.Single(rig.Drawn(PhysCannonDrawEffect.CenterGlow));
        Assert.Equal((new Vector(10, 1, 0), 14f, 128f), (QuadMath.Centre(core), MathF.Round(Size(core), 3), core[0].A));
        Assert.Equal((255f, 255f, 255f), (core[0].R, core[0].G, core[0].B));

        // Six glows, two of whose names (fork1b, fork2b) the world model lacks.
        var glows = rig.Drawn(PhysCannonDrawEffect.GlowSprite);
        Assert.Equal([new Vector(10, 2, 0), new Vector(10, 3, 0), new Vector(10, 4, 0), new Vector(10, 5, 0)],
                     glows.Select(QuadMath.Centre));
        // DoEffectIdle's every frame: 0.05 to 0.075 of 128 across (8 at the
        // middle the scripted stream answers) and 24 to 32 alpha (28).
        Assert.All(glows, g => Assert.Equal((8f, 255f, 128f, 0f, 28f), (MathF.Round(Size(g), 3), g[0].R, g[0].G, g[0].B, g[0].A)));
        Assert.Empty(rig.Drawn(PhysCannonDrawEffect.EndCapSprite));
        Assert.Empty(rig.Drawn(PhysCannonDrawEffect.BlastSprite));
    }

    [Fact]
    public void TheCoreGrowsFromWhereItIsOverAFifthOfASecond()
    {
        var (effect, rig, _) = Rig();
        Think(effect, rig, PhysCannonEffect.Ready);

        rig.Now = T0 + 0.1f;
        effect.ViewModelDrawn(rig.FirstPerson());

        // StartEffects left it at 0; Ready takes it to 14 over 0.2 s.
        Assert.Equal(7f, Size(Assert.Single(rig.Drawn(PhysCannonDrawEffect.CenterGlow))), 3);
    }

    [Fact]
    public void ReadyInTheThirdPersonFadesTheCoreOutAndDrawsTheGlowsOnTheWorldModel()
    {
        var (effect, rig, _) = Rig();
        Think(effect, rig, PhysCannonEffect.Ready, firstPerson: false);

        rig.Now = T0 + 0.5f;
        Assert.Equal(1, effect.DrawModel(rig.ThirdPerson(), unchecked((int)0x80000009)));

        Assert.Empty(rig.Drawn(PhysCannonDrawEffect.CenterGlow));
        Assert.Equal(6, rig.Drawn(PhysCannonDrawEffect.GlowSprite).Count);
        Assert.Equal(new Vector(50, 2, 0), QuadMath.Centre(rig.Drawn(PhysCannonDrawEffect.GlowSprite)[0]));
    }

    [Fact]
    public void HoldingInTheFirstPersonBrightensTheCoreLightsTwoEndCapsAndMakesTwoBeams()
    {
        var (effect, rig, _) = Rig();
        Think(effect, rig, PhysCannonEffect.Holding);

        rig.Now = T0 + 0.5f;
        effect.ViewModelDrawn(rig.FirstPerson());

        var core = Assert.Single(rig.Drawn(PhysCannonDrawEffect.CenterGlow));
        Assert.Equal((16f, 255f), (Size(core), core[0].A));
        Assert.Equal([new Vector(10, 3, 0), new Vector(10, 5, 0)], rig.Drawn(PhysCannonDrawEffect.EndCapSprite).Select(QuadMath.Centre));
        Assert.All(rig.Drawn(PhysCannonDrawEffect.EndCapSprite), c => Assert.Equal((4f, 227f), (MathF.Round(Size(c), 3), c[0].A)));

        var made = rig.BeamsMade();
        Assert.Equal([(3, 1), (5, 1)], made.Select(b => (b.StartAttachment, b.EndAttachment)));
        Assert.All(made, b => Assert.Equal((rig.ViewModel.Handle, rig.ViewModel.Handle, "sprites/orangelight1_noz.vmt", 0f, 4f),
                                           (b.StartEnt, b.EndEnt, b.ModelName, b.Width, b.EndWidth)));
        Assert.All(made, b => Assert.Equal((16f, 150f, 30f, 8, BeamFlags.Forever), (b.Amplitude, b.Speed, b.FrameRate, b.Segments, b.Flags)));
    }

    /// <summary>
    /// The local gun's effect follows its prediction: stock's client ran
    /// DoEffect from the predicted gun, so the claws' change shows the frame
    /// the replay made it, before the server's word of it is decoded.
    /// </summary>
    [Fact]
    public void ThePredictedStateDrivesTheEffectBeforeTheDecodedOneMoves()
    {
        var (effect, rig, _) = Rig();
        Think(effect, rig, PhysCannonEffect.Ready);
        Assert.Empty(rig.BeamsMade());

        var predicted = new WeaponNetState();
        predicted.BindLocal();
        predicted.CannonEffect = (int)PhysCannonEffect.Holding;
        effect.Think(rig.FirstPerson() with { Predicted = predicted });

        Assert.Equal((int)PhysCannonEffect.Ready, rig.Weapon.State.CannonEffect);
        Assert.Equal(2, rig.BeamsMade().Count);
    }

    [Fact]
    public void HoldingInTheThirdPersonMakesThreeBeamsOnTheWorldModel()
    {
        var (effect, rig, _) = Rig();
        Think(effect, rig, PhysCannonEffect.Holding, firstPerson: false);

        var made = rig.BeamsMade();
        Assert.Equal([3, 5, 7], made.Select(b => b.StartAttachment));
        Assert.All(made, b => Assert.Equal((rig.Weapon.Handle, "sprites/orangelight1.vmt", 0.5f, 2f),
                                           (b.StartEnt, b.ModelName, b.Width, b.EndWidth)));
    }

    [Fact]
    public void EveryFrameTheGlowsAndEndCapsAreDrawnFromTheIdleRanges()
    {
        var (effect, rig, _) = Rig();

        Think(effect, rig, PhysCannonEffect.Ready);

        // Six glows (size, alpha) then three end caps (size, alpha).
        Assert.Equal(Enumerable.Repeat(new[] { (false, 0.075f, 0.05f), (true, 24f, 32f) }, 6).SelectMany(x => x)
                         .Concat(Enumerable.Repeat(new[] { (false, 3f, 5f), (true, 200f, 255f) }, 3).SelectMany(x => x)),
                     rig.Random.Asked);
    }

    [Fact]
    public void TheBeamsAreLitOnlyWhileTheGunHolds()
    {
        var (effect, rig, _) = Rig();
        Think(effect, rig, PhysCannonEffect.Holding);
        Beam beam = effect.CannonOf(rig.Weapon.Handle)!.Beams[0]!;
        Assert.Equal(255f, beam.Brightness);

        Think(effect, rig, PhysCannonEffect.Ready);

        Assert.Equal(0f, beam.Brightness);
    }

    [Fact]
    public void ALaunchBlastsFrom8To64InATenthWhileFadingOverAFifth()
    {
        var (effect, rig, _) = Rig();
        Think(effect, rig, PhysCannonEffect.Launch);

        rig.Now = T0 + 0.05f;
        effect.ViewModelDrawn(rig.FirstPerson());

        var blast = Assert.Single(rig.Drawn(PhysCannonDrawEffect.BlastSprite));
        Assert.Equal((36f, 191f), (Size(blast), blast[0].A));
    }

    [Fact]
    public void NoneDrawsNothingAndDarkensTheBeams()
    {
        var (effect, rig, sounds) = Rig();
        Think(effect, rig, PhysCannonEffect.Holding);
        Think(effect, rig, PhysCannonEffect.None);

        effect.ViewModelDrawn(rig.FirstPerson());

        Assert.Empty(rig.Quads.Draws);
        Assert.All(effect.CannonOf(rig.Weapon.Handle)!.Beams.Take(2), b => Assert.Equal(0f, b!.Brightness));
        Assert.Equal("fade 77 0.1 False", sounds.Calls[^1]);
    }

    [Fact]
    public void HoldingWithTheClawsOpenRunsTheMotorUp()
    {
        var (effect, rig, sounds) = Rig();

        Think(effect, rig, PhysCannonEffect.Holding, open: true);

        var create = rig.Engine.Last("Sound.SoundPatchCreate");
        Assert.NotNull(create);
        Assert.Equal((rig.Weapon.Handle, 6, "Weapon_PhysCannon.HoldSound", 75),
                     ((EntityHandle)create.Value.Args[0]!, (int)create.Value.Args[1]!, (string)create.Value.Args[2]!, (int)create.Value.Args[3]!));
        Assert.Equal(["play 77 0 50", "pitch 77 100 0.5", "volume 77 0.8 0.5"], sounds.Calls);
    }

    [Fact]
    public void HoldingWithTheClawsShutLeavesTheMotorAlone()
    {
        var (effect, rig, sounds) = Rig();

        Think(effect, rig, PhysCannonEffect.Holding);

        Assert.Empty(sounds.Calls);
    }

    [Fact]
    public void ReadyRunsTheMotorDownOverASecond()
    {
        var (effect, rig, sounds) = Rig();

        Think(effect, rig, PhysCannonEffect.Ready);

        Assert.Equal(["volume 77 0 1", "pitch 77 50 1"], sounds.Calls);
    }

    [Fact]
    public void ClosingTheClawsRunsTheMotorDown()
    {
        var (effect, rig, sounds) = Rig();
        Think(effect, rig, PhysCannonEffect.Holding, open: true);
        sounds.Calls.Clear();

        Think(effect, rig, PhysCannonEffect.Holding, open: false);

        Assert.Equal(["volume 77 0 1", "pitch 77 50 1"], sounds.Calls);
    }

    [Fact]
    public void AStateIsActedOnOnceAsItChanges()
    {
        var (effect, rig, sounds) = Rig();

        Think(effect, rig, PhysCannonEffect.Ready);
        Think(effect, rig, PhysCannonEffect.Ready);

        Assert.Equal(2, sounds.Calls.Count);
    }

    [Fact]
    public void AGunThatGoesTakesItsBeamsAndItsMotor()
    {
        var (effect, rig, sounds) = Rig();
        Think(effect, rig, PhysCannonEffect.Holding, open: true);
        Beam beam = effect.CannonOf(rig.Weapon.Handle)!.Beams[0]!;

        effect.Forget(rig.Weapon.Handle);

        Assert.Equal((BeamFlags.None, true), (beam.Flags, beam.Die < 0));
        Assert.Equal("destroy 77", sounds.Calls[^1]);
        Assert.NotNull(rig.Engine.Last("Sound.SoundPatchFree"));
        Assert.Null(effect.CannonOf(rig.Weapon.Handle));
    }

    [Fact]
    public void TheLocalCarriersWorldModelDrawsNothingInTheFirstPerson()
    {
        var (effect, rig, _) = Rig();
        Think(effect, rig, PhysCannonEffect.Ready);

        Assert.Equal(0, effect.DrawModel(rig.FirstPerson(), unchecked((int)0x80000009)));
        Assert.Empty(rig.Quads.Draws);
    }
}

/// <summary>CInterpolatedValue (fx_interpvalue.cpp).</summary>
public class InterpolatedValueTests
{
    [Fact]
    public void ALinearValueIsItsStartBeforeAndItsEndAfter()
    {
        var v = new InterpolatedValue();
        v.Init(2f, 10f, 1f, now: 5f);

        Assert.Equal((2f, 6f, 10f), (v.Interp(4f), v.Interp(5.5f), v.Interp(7f)));
    }

    [Fact]
    public void ASplineEasesInAndOut()
    {
        var v = new InterpolatedValue();
        v.Init(0f, 1f, 1f, now: 0f, InterpolatedValue.Kind.Spline);

        Assert.Equal(0.5f, v.Interp(0.5f), 4);
        Assert.True(v.Interp(0.25f) < 0.25f);
    }

    [Fact]
    public void FromCurrentStartsWhereTheValueIs()
    {
        var v = new InterpolatedValue();
        v.Init(0f, 10f, 1f, now: 0f);

        v.InitFromCurrent(0f, 1f, now: 0.5f);

        Assert.Equal((5f, 2.5f), (v.Interp(0.5f), v.Interp(1.0f)));
    }

    [Fact]
    public void NoTimeIsTheEndAtOnce()
    {
        var v = new InterpolatedValue();
        v.Init(0f, 10f, 0f, now: 3f);

        Assert.Equal(10f, v.Interp(3f));
    }
}

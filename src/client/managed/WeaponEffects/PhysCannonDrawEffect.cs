//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client.WeaponEffects;

/// <summary>
/// <c>C_WeaponPhysCannon</c>'s client draw and motor (shared/hl2mp/
/// weapon_physcannon.cpp: <c>StartEffects</c>, <c>DoEffect*</c>,
/// <c>DrawEffects</c>, <c>DrawEffectSprite</c>, <c>GetEffectParameters</c>,
/// <c>ViewModelDrawn</c>, <c>DrawModel</c>, <c>GetMotorSound</c>,
/// <c>CPhysCannonEffect</c>, <c>CPhysCannonEffectBeam</c>), on the two-pass
/// group its <c>GetRenderGroup</c> answers.
///
/// <para>
/// ELEVEN SPRITES. The core (<c>orangecore1</c>) and the blast
/// (<c>orangecore2</c>) at attachment 1, six glows (<c>glow04_noz</c>, orange)
/// and three end caps (<c>orangeflare1</c>), each with a scale and an alpha
/// that move between values over a fraction of a second as the gun's effect
/// state changes (ready: the core 14 across at 128 in the first person, the
/// glows 51 at 64; holding: the core 16 at 255, the glows 64, the end caps
/// on and two or three beams from the tines to the muzzle; a launch: the
/// blast from 8 to 64 across while it fades over 0.2 s). Drawn after the
/// viewmodel in the first person and in the world model's translucent pass
/// otherwise.
/// </para>
///
/// <para>
/// THE STATE. Stock's client runs <c>DoEffect</c> from its own prediction and
/// again whenever the networked <c>m_EffectState</c> differs from the one it
/// last ran (<c>OnDataChanged</c>'s parity check); here the parity check is
/// the whole of it, once a frame, over the weapon's decoded state. Closing the
/// claws (the networked <c>m_bOpen</c> going false) ramps the motor down, as
/// <c>CloseElements</c> does.
/// </para>
///
/// <para>
/// WHICH ATTACHMENTS. As stock: each sprite's attachment is looked up by name
/// on the WEAPON's model (w_physics) when the effects start, from the
/// first-person list if the gun is drawn through the viewmodel then, and the
/// third-person list otherwise; in the first person that index is asked of the
/// viewmodel (v_physcannon), whose tines are numbered differently. A sprite
/// whose attachment does not answer is not drawn (stock draws it at the eye,
/// unseen).
/// </para>
///
/// <para>
/// AND EVERY FRAME, <c>DoEffectIdle</c> (stock's <c>ClientThink</c>): the
/// glows flicker at 6.4 to 9.6 across and 24 to 32 alpha and the end caps at
/// 3 to 5 across and 200 to 255, whatever the last state set them to, so what
/// a state's change of them shows is only its visibility.
/// </para>
///
/// <para>
/// THE MOTOR: <c>Weapon_PhysCannon.HoldSound</c> on the weapon, made the first
/// time an effect asks for it; up to 0.8 and pitch 100 over half a second
/// while holding with the claws open, down to silence and pitch 50 over a
/// second when ready or closed, faded out in a tenth when the effects stop.
/// </para>
/// </summary>
public sealed class PhysCannonDrawEffect(IFxRender fx, IPixelVisSlots view, IEngineEntityRender render,
                                         IClientBeams beams, IClientSoundController sounds, IEngineSound engineSound,
                                         IUniformRandom random)
    : IWeaponDrawEffect
{
    /// <summary>EffectType_t.</summary>
    public const int Core = 0, Blast = 1, Glow1 = 2, EndCap1 = 8, Parameters = 11;

    /// <summary>NUM_GLOW_SPRITES, NUM_ENDCAP_SPRITES, NUM_PHYSCANNON_BEAMS.</summary>
    public const int GlowSprites = 6, EndCapSprites = 3, BeamCount = 3;

    /// <summary>SPRITE_SCALE.</summary>
    public const float SpriteScale = 128.0f;

    /// <summary>The sprites' materials, by name.</summary>
    public const string CenterGlow = "sprites/orangecore1", BlastSprite = "sprites/orangecore2",
                        GlowSprite = "sprites/glow04_noz", EndCapSprite = "sprites/orangeflare1";

    /// <summary>PHYSCANNON_BEAM_SPRITE and PHYSCANNON_BEAM_SPRITE_NOZ.</summary>
    public const string BeamSprite = "sprites/orangelight1.vmt", BeamSpriteNoZ = "sprites/orangelight1_noz.vmt";

    /// <summary>The motor's loop.</summary>
    public const string HoldSound = "Weapon_PhysCannon.HoldSound";

    /// <summary>CHAN_STATIC, and ATTN_NORM as a sound level (ATTN_TO_SNDLVL: 50 + 20 / 0.8).</summary>
    public const int ChannelStatic = 6, SoundLevelNorm = 75;

    /// <summary>StartEffects' attachment names.</summary>
    public static readonly ImmutableArray<string> GlowNames = ["fork1b", "fork1m", "fork1t", "fork2b", "fork2m", "fork2t"];

    /// <summary>StartEffects' attachment names for a gun drawn in the world.</summary>
    public static readonly ImmutableArray<string> GlowNamesThirdPerson = ["fork1m", "fork1t", "fork2m", "fork2t", "fork3m", "fork3t"];

    /// <summary>StartEffects' end cap attachment names.</summary>
    public static readonly ImmutableArray<string> EndCapNames = ["fork1t", "fork2t", "fork3t"];

    /// <summary><c>CPhysCannonEffect</c>.</summary>
    public sealed class Parameter
    {
        public readonly InterpolatedValue Alpha = new();
        public readonly InterpolatedValue Scale = new();
        public Vector Color = new(255, 255, 255);
        public bool Visible = true;
        public int Attachment = -1;
        public string? Material;
    }

    /// <summary>One gun's client state.</summary>
    public sealed class Cannon
    {
        public readonly Parameter[] Parameters = Enumerable.Range(0, PhysCannonDrawEffect.Parameters).Select(_ => new Parameter()).ToArray();
        public readonly Beam?[] Beams = new Beam?[BeamCount];
        public bool Started;
        public int OldEffect = (int)PhysCannonEffect.None;
        public bool OldOpen;
        public int Motor;
    }

    private readonly Dictionary<EntityHandle, Cannon> _cannons = [];
    private readonly Dictionary<string, int> _materials = new(StringComparer.Ordinal);

    public string WeaponName => EntityNames.WeaponPhyscannon;

    /// <summary>C_WeaponPhysCannon::GetRenderGroup: RENDER_GROUP_TWOPASS.</summary>
    public bool TwoPass => true;

    /// <summary>A gun's state, for the facts.</summary>
    internal Cannon? CannonOf(EntityHandle weapon) => _cannons.GetValueOrDefault(weapon);

    private int Material(string name)
    {
        if (!_materials.TryGetValue(name, out int id))
        {
            id = fx.MaterialFind(name);
            _materials[name] = id;
        }
        return id;
    }

    /// <summary>DestroyEffects and StopLoopingSounds: the beams go and the motor with them.</summary>
    public void Forget(EntityHandle weapon)
    {
        if (!_cannons.Remove(weapon, out Cannon? cannon))
            return;

        foreach (Beam? beam in cannon.Beams)
        {
            if (beam is not null)
                Release(beam);
        }

        if (cannon.Motor != 0)
        {
            sounds.Destroy(cannon.Motor);
            engineSound.SoundPatchFree(cannon.Motor);
        }
    }

    private static void Release(Beam beam)
    {
        beam.Flags = 0;
        beam.Die = -1f;
    }

    /// <summary>
    /// ClientThink and OnDataChanged's parity checks: the effects start the
    /// first time the gun is seen, then each change of the networked effect
    /// state runs its DoEffect, and the claws closing ramp the motor down.
    /// </summary>
    public void Think(in WeaponDrawContext context)
    {
        Cannon cannon = CannonFor(context);
        WeaponNetState state = context.State;

        if (cannon.OldOpen && !state.CannonOpen)
            MotorDown(context, cannon);
        cannon.OldOpen = state.CannonOpen;

        if (state.CannonEffect != cannon.OldEffect)
        {
            DoEffect(context, cannon, (PhysCannonEffect)state.CannonEffect);
            cannon.OldEffect = state.CannonEffect;
        }

        // ClientThink, every frame.
        DoEffectIdle(cannon, (PhysCannonEffect)state.CannonEffect, context.Now);
    }

    /// <summary>
    /// CWeaponPhysCannon::DoEffectIdle, every frame from ClientThink: the
    /// glows flicker small and faint and the end caps small and bright,
    /// whatever the last DoEffect set them to; the beams are off unless the
    /// gun holds something.
    /// </summary>
    private void DoEffectIdle(Cannon cannon, PhysCannonEffect effect, float now)
    {
        Parameter[] p = cannon.Parameters;

        for (int i = Glow1; i < Glow1 + GlowSprites; i++)
        {
            p[i].Scale.SetAbsolute(random.RandomFloat(0.075f, 0.05f) * SpriteScale, now);
            p[i].Alpha.SetAbsolute(random.RandomInt(24, 32), now);
        }

        for (int i = EndCap1; i < EndCap1 + EndCapSprites; i++)
        {
            p[i].Scale.SetAbsolute(random.RandomFloat(3, 5), now);
            p[i].Alpha.SetAbsolute(random.RandomInt(200, 255), now);
        }

        if (effect != PhysCannonEffect.Holding)
        {
            foreach (Beam? beam in cannon.Beams)
            {
                if (beam is not null)
                    beam.Brightness = 0.0f;
            }
        }
    }

    private Cannon CannonFor(in WeaponDrawContext context)
    {
        if (!_cannons.TryGetValue(context.Weapon.Handle, out Cannon? cannon))
            _cannons[context.Weapon.Handle] = cannon = new Cannon();

        if (!cannon.Started)
            StartEffects(context, cannon);

        return cannon;
    }

    /// <summary>CWeaponPhysCannon::StartEffects.</summary>
    private void StartEffects(in WeaponDrawContext context, Cannon cannon)
    {
        cannon.Started = true;
        float now = context.Now;
        Parameter[] p = cannon.Parameters;
        EntityHandle weapon = context.Weapon.Handle;

        // Core.
        p[Core].Scale.Init(0.0f, 1.0f, 0.1f, now);
        p[Core].Alpha.Init(255.0f, 255.0f, 0.1f, now);
        p[Core].Attachment = 1;
        p[Core].Material = CenterGlow;

        // Blast.
        p[Blast].Scale.Init(0.0f, 1.0f, 0.1f, now);
        p[Blast].Alpha.Init(255.0f, 255.0f, 0.1f, now);
        p[Blast].Attachment = 1;
        p[Blast].Visible = false;
        p[Blast].Material = BlastSprite;

        // Glows: different for different views.
        ImmutableArray<string> glowNames = context.FirstPerson ? GlowNames : GlowNamesThirdPerson;
        for (int i = 0; i < GlowSprites; i++)
        {
            Parameter glow = p[Glow1 + i];
            glow.Scale.SetAbsolute(0.05f * SpriteScale, now);
            glow.Alpha.SetAbsolute(64.0f, now);
            glow.Attachment = render.LookupAttachment(weapon, glowNames[i]);
            glow.Color = new Vector(255, 128, 0);
            glow.Material = GlowSprite;
        }

        // End caps.
        for (int i = 0; i < EndCapSprites; i++)
        {
            Parameter cap = p[EndCap1 + i];
            cap.Scale.SetAbsolute(0.05f * SpriteScale, now);
            cap.Alpha.SetAbsolute(255.0f, now);
            cap.Attachment = render.LookupAttachment(weapon, EndCapNames[i]);
            cap.Visible = false;
            cap.Material = EndCapSprite;
        }
    }

    private void DoEffect(in WeaponDrawContext context, Cannon cannon, PhysCannonEffect effect)
    {
        switch (effect)
        {
            case PhysCannonEffect.Closed: DoEffectClosed(cannon); break;
            case PhysCannonEffect.Ready: DoEffectReady(context, cannon); break;
            case PhysCannonEffect.Holding: DoEffectHolding(context, cannon); break;
            case PhysCannonEffect.Launch: DoEffectLaunch(context, cannon); break;
            default: DoEffectNone(context, cannon); break;
        }
    }

    private static void DoEffectClosed(Cannon cannon)
    {
        // Turn off the end-caps.
        for (int i = EndCap1; i < EndCap1 + EndCapSprites; i++)
            cannon.Parameters[i].Visible = false;
    }

    private void DoEffectReady(in WeaponDrawContext context, Cannon cannon)
    {
        float now = context.Now;
        Parameter[] p = cannon.Parameters;

        if (context.FirstPerson)
        {
            p[Core].Scale.InitFromCurrent(14.0f, 0.2f, now);
            p[Core].Alpha.InitFromCurrent(128.0f, 0.2f, now);
        }
        else
        {
            p[Core].Scale.InitFromCurrent(8.0f, 0.2f, now);
            p[Core].Alpha.InitFromCurrent(0.0f, 0.2f, now);
        }
        p[Core].Visible = true;

        for (int i = Glow1; i < Glow1 + GlowSprites; i++)
        {
            p[i].Scale.InitFromCurrent(0.4f * SpriteScale, 0.2f, now);
            p[i].Alpha.InitFromCurrent(64.0f, 0.2f, now);
            p[i].Visible = true;
        }

        for (int i = EndCap1; i < EndCap1 + EndCapSprites; i++)
            p[i].Visible = false;

        MotorDown(context, cannon);
    }

    /// <summary>The motor down to silence and pitch 50 over a second (DoEffectReady, CloseElements).</summary>
    private void MotorDown(in WeaponDrawContext context, Cannon cannon)
    {
        int motor = Motor(context, cannon);
        if (motor == 0)
            return;

        sounds.ChangeVolume(motor, 0.0f, 1.0f);
        sounds.ChangePitch(motor, 50, 1.0f);
    }

    private void DoEffectHolding(in WeaponDrawContext context, Cannon cannon)
    {
        float now = context.Now;
        Parameter[] p = cannon.Parameters;
        bool firstPerson = context.FirstPerson && context.ViewModel is not null;

        p[Core].Scale.InitFromCurrent(firstPerson ? 16.0f : 14.0f, 0.2f, now);
        p[Core].Alpha.InitFromCurrent(255.0f, 0.1f, now);
        p[Core].Visible = true;

        // Prepare for scale up.
        p[Blast].Visible = false;

        for (int i = Glow1; i < Glow1 + GlowSprites; i++)
        {
            p[i].Scale.InitFromCurrent(0.5f * SpriteScale, 0.2f, now);
            p[i].Alpha.InitFromCurrent(64.0f, 0.2f, now);
            p[i].Visible = true;
        }

        // The last end cap is left off for the first person.
        int caps = firstPerson ? EndCapSprites - 1 : EndCapSprites;
        for (int i = EndCap1; i < EndCap1 + caps; i++)
            p[i].Visible = true;

        // The beams, from the tines to the muzzle.
        EntityHandle weapon = context.Weapon.Handle;
        EntityHandle beamEntity = firstPerson ? context.ViewModel!.Handle : weapon;
        int beamCount = firstPerson ? 2 : 3;
        for (int i = 0; i < beamCount; i++)
        {
            cannon.Beams[i] ??= beams.CreateBeamEntPoint(
                EffectBeam(render.LookupAttachment(weapon, EndCapNames[i]), 1, beamEntity, firstPerson));
            if (cannon.Beams[i] is { } beam)
                beam.Brightness = 255.0f;
        }

        if (context.State.CannonOpen)
        {
            int motor = Motor(context, cannon);
            if (motor != 0)
            {
                sounds.Play(motor, 0.0f, 50, 0f);
                sounds.ChangePitch(motor, 100, 0.5f);
                sounds.ChangeVolume(motor, 0.8f, 0.5f);
            }
        }
    }

    private static void DoEffectLaunch(in WeaponDrawContext context, Cannon cannon)
    {
        // Turn on the blast sprite and scale. (The impact itself is the
        // server's PhyscannonImpact dispatch.)
        Parameter blast = cannon.Parameters[Blast];
        blast.Scale.Init(8.0f, 64.0f, 0.1f, context.Now);
        blast.Alpha.Init(255.0f, 0.0f, 0.2f, context.Now);
        blast.Visible = true;
    }

    private void DoEffectNone(in WeaponDrawContext context, Cannon cannon)
    {
        Parameter[] p = cannon.Parameters;
        p[Core].Visible = false;
        p[Blast].Visible = false;

        for (int i = Glow1; i < Glow1 + GlowSprites; i++)
            p[i].Visible = false;

        for (int i = EndCap1; i < EndCap1 + EndCapSprites; i++)
            p[i].Visible = false;

        foreach (Beam? beam in cannon.Beams)
        {
            if (beam is not null)
                beam.Brightness = 0.0f;
        }

        int motor = Motor(context, cannon);
        if (motor != 0)
            sounds.FadeOut(motor, 0.1f, false);
    }

    /// <summary>GetMotorSound: made the first time it is asked for.</summary>
    private int Motor(in WeaponDrawContext context, Cannon cannon)
    {
        if (cannon.Motor == 0)
            cannon.Motor = engineSound.SoundPatchCreate(context.Weapon.Handle, ChannelStatic, HoldSound, SoundLevelNorm);
        return cannon.Motor;
    }

    /// <summary>CPhysCannonEffectBeam::Init's BeamInfo_t.</summary>
    public static BeamInfo EffectBeam(int startAttachment, int endAttachment, EntityHandle entity, bool firstPerson) => new()
    {
        StartEnt = entity,
        StartAttachment = startAttachment,
        EndEnt = entity,
        EndAttachment = endAttachment,
        Type = BeamType.Points,
        Start = Vector.Zero,
        End = Vector.Zero,
        ModelName = firstPerson ? BeamSpriteNoZ : BeamSprite,
        HaloScale = 0.0f,
        Life = 0.0f,
        Width = firstPerson ? 0.0f : 0.5f,
        EndWidth = firstPerson ? 4.0f : 2.0f,
        FadeLength = 0.0f,
        Amplitude = 16,
        Brightness = 255.0f,
        Speed = 150.0f,
        StartFrame = 0,
        FrameRate = 30.0f,
        Red = 255.0f,
        Green = 255.0f,
        Blue = 255.0f,
        Segments = 8,
        Renderable = true,
        Flags = BeamFlags.Forever,
    };

    /// <summary>ViewModelDrawn: the effects, after the viewmodel.</summary>
    public void ViewModelDrawn(in WeaponDrawContext context) => DrawEffects(context);

    /// <summary>
    /// DrawModel's translucent arm: the effects, and the model is not drawn
    /// again. The local carrier's gun drawn through the viewmodel is not drawn
    /// here (stock's ShouldDraw refuses its world model).
    /// </summary>
    public int DrawModel(in WeaponDrawContext context, int flags)
    {
        if (context.FirstPerson)
            return 0;

        DrawEffects(context);
        return 1;
    }

    /// <summary>CWeaponPhysCannon::DrawEffects: the core, the blast, the glows, the end caps.</summary>
    private void DrawEffects(in WeaponDrawContext context)
    {
        if (!_cannons.TryGetValue(context.Weapon.Handle, out Cannon? cannon) || !cannon.Started)
            return;

        CurrentViewState eye = view.View();
        for (int i = 0; i < Parameters; i++)
            DrawEffectSprite(context, cannon.Parameters[i], eye);
    }

    /// <summary>DrawEffectSprite over GetEffectParameters.</summary>
    private void DrawEffectSprite(in WeaponDrawContext context, Parameter effect, in CurrentViewState eye)
    {
        // Don't draw invisible effects.
        if (!effect.Visible || effect.Material is null)
            return;

        float alpha = effect.Alpha.Interp(context.Now);
        float scale = effect.Scale.Interp(context.Now);
        int a = (int)alpha;

        // The viewmodel's attachment in the first person (back in its own
        // field of view: this build's), the world model's otherwise.
        EntityHandle entity = context.FirstPerson && context.ViewModel is { } viewModel ? viewModel.Handle : context.Weapon.Handle;
        if (!render.GetAttachment(entity, effect.Attachment, out Vector origin, out _))
            return;

        // Don't render fully translucent objects.
        if (a <= 0)
            return;

        Span<FxVertex> quad = stackalloc FxVertex[4];
        EffectQuads.Sprite(quad, origin, scale, scale, (byte)(int)effect.Color.X, (byte)(int)effect.Color.Y,
                           (byte)(int)effect.Color.Z, (byte)a, eye);
        fx.DrawQuads(Material(effect.Material), quad, byteColor: true);
    }
}

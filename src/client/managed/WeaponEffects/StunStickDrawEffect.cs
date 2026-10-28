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
/// <c>C_WeaponStunStick</c>'s client draw (shared/hl2mp/weapon_stunstick.cpp:
/// <c>DrawFirstPersonEffects</c>, <c>DrawThirdPersonEffects</c>,
/// <c>ClientThink</c>, <c>InSwing</c>, <c>SetupAttachmentPoints</c>), on the
/// two-pass group stock's <c>GetRenderGroup</c> answers.
///
/// <para>
/// FIRST PERSON, after the viewmodel: a glow about the head at the centre
/// attachment and a bright point at each of attachments 0 to 18, in
/// <c>light_glow02_add_noz</c>. Swinging, the glow is 0.4 and 22 across and
/// the points 0.05 to 0.5 bright, 4 to 5 across; once a swing ends they fade
/// over a quarter of a second to a tenth of that, where they rest. While it
/// swings, one frame in four strikes a lightning beam from a random one of
/// attachments 0 to 15 to a point within 8 units of it.
/// </para>
///
/// <para>
/// THIRD PERSON, in the carried model's translucent pass: a glow 22 across at
/// 0.4 in <c>light_glow02_add</c> at the head (the world model's attachment
/// 1), a flickering <c>blueflare1</c> 4 to 6 across over it, and one frame in
/// six a tesla arc off the head. Stock's <c>InSwing</c> answers "swinging"
/// for every weapon not drawn through the viewmodel (its FIXME), so this is
/// what everyone else's stick always shows.
/// </para>
///
/// <para>
/// WHICH ATTACHMENTS. Stock looks the centre up on the WEAPON's model when the
/// weapon is created: "sparkrear" for a weapon made in the first person, which
/// its world model (w_stunbaton) does not have, so the lookup answers 0, and
/// attachment 1 otherwise. It then asks the VIEWMODEL for that index and for
/// 0 to 18 directly. Attachment 0 is no attachment: stock's answer is the
/// viewmodel's origin, the eye, where a halo cannot be seen, and a draw here
/// whose attachment does not answer draws nothing.
/// </para>
/// </summary>
public sealed class StunStickDrawEffect(IFxRender fx, IPixelVisSlots view, IEngineEntityRender render,
                                        IClientBeams beams, IUniformRandom random,
                                        IViewModelAttachmentFormat viewModels) : IWeaponDrawEffect
{
    /// <summary>STUNSTICK_BEAM_MATERIAL.</summary>
    public const string BeamMaterial = "sprites/lgtning.vmt";

    /// <summary>STUNSTICK_GLOW_MATERIAL.</summary>
    public const string GlowMaterial = "sprites/light_glow02_add";

    /// <summary>STUNSTICK_GLOW_MATERIAL2.</summary>
    public const string GlowMaterial2 = "effects/blueflare1";

    /// <summary>STUNSTICK_GLOW_MATERIAL_NOZ.</summary>
    public const string GlowMaterialNoZ = "sprites/light_glow02_add_noz";

    /// <summary>BEAM_ATTACH_CORE_NAME.</summary>
    public const string CoreAttachmentName = "sparkrear";

    /// <summary>NUM_BEAM_ATTACHMENTS.</summary>
    public const int BeamAttachments = 9;

    /// <summary>FADE_DURATION.</summary>
    public const float FadeDuration = 0.25f;

    /// <summary>The swing activities InSwing names (the bludgeon's primary and secondary, and the two misses).</summary>
    public static readonly ImmutableArray<string> SwingActivities = ["ACT_VM_HITCENTER", "ACT_VM_HITCENTER2", "ACT_VM_MISSCENTER", "ACT_VM_MISSCENTER2"];

    /// <summary>A stick's own client state: m_bSwungLastFrame and m_flFadeTime.</summary>
    private sealed class Stick
    {
        public bool SwungLastFrame;
        public float FadeTime = FadeDuration;
    }

    private readonly Dictionary<EntityHandle, Stick> _sticks = [];
    private readonly Dictionary<string, int> _materials = new(StringComparer.Ordinal);

    public string WeaponName => EntityNames.WeaponStunstick;

    /// <summary>C_WeaponStunStick::GetRenderGroup: RENDER_GROUP_TWOPASS.</summary>
    public bool TwoPass => true;

    private Stick StickOf(EntityHandle weapon)
    {
        if (!_sticks.TryGetValue(weapon, out Stick? stick))
            _sticks[weapon] = stick = new Stick();
        return stick;
    }

    public void Forget(EntityHandle weapon) => _sticks.Remove(weapon);

    private int Material(string name)
    {
        if (!_materials.TryGetValue(name, out int id))
        {
            id = fx.MaterialFind(name);
            _materials[name] = id;
        }
        return id;
    }

    /// <summary>
    /// <c>InSwing</c>: the viewmodel's sequence is one of the swing
    /// activities', and always for a stick not drawn through the viewmodel.
    /// </summary>
    public bool InSwing(in WeaponDrawContext context)
    {
        if (!context.FirstPerson || context.ViewModel is not { } viewModel)
            return true;

        int sequence = viewModel.ShownSequence;
        if (sequence < 0)
            return false;

        foreach (string activity in SwingActivities)
        {
            if (render.LookupSequenceByActivity(viewModel.Handle, activity) == sequence)
                return true;
        }

        return false;
    }

    /// <summary>C_WeaponStunStick::ClientThink.</summary>
    public void Think(in WeaponDrawContext context)
    {
        Stick stick = StickOf(context.Weapon.Handle);

        if (!InSwing(context))
        {
            if (stick.SwungLastFrame)
            {
                // Start fading.
                stick.FadeTime = context.Now;
                stick.SwungLastFrame = false;
            }

            return;
        }

        // Remember if we were swinging last frame.
        stick.SwungLastFrame = true;

        if (!context.FirstPerson || context.ViewModel is not { } viewModel)
            return;

        // Update our effects.
        if (context.FrameTime != 0.0f && random.RandomInt(0, 3) == 0)
        {
            int attachment = random.RandomInt(0, 15);

            // UTIL_GetWeaponAttachment, then back into the world's field of view.
            if (!render.GetAttachment(viewModel.Handle, attachment, out Vector origin, out _))
                return;

            Vector end = viewModels.ForWorldPass(viewModel.Handle, origin) + RandomVector(-8, 8);

            beams.CreateBeamEntPoint(new BeamInfo
            {
                Start = Vector.Zero,
                StartEnt = viewModel.Handle,
                StartAttachment = attachment,
                EndEnt = EntityHandle.None,
                EndAttachment = -1,
                End = end,
                ModelName = BeamMaterial,
                HaloScale = 0.0f,
                Life = 0.05f,
                Width = random.RandomFloat(1.0f, 2.0f),
                EndWidth = 0,
                FadeLength = 0.0f,
                Amplitude = random.RandomFloat(16, 32),
                Brightness = 255.0f,
                Speed = 0.0f,
                StartFrame = 0,
                FrameRate = 1.0f,
                Red = 255.0f,
                Green = 255.0f,
                Blue = 255.0f,
                Segments = 16,
                Renderable = true,
                Flags = 0,
            });
        }
    }

    private Vector RandomVector(float min, float max)
        => new(random.RandomFloat(min, max), random.RandomFloat(min, max), random.RandomFloat(min, max));

    /// <summary>ViewModelDrawn: the first-person effects.</summary>
    public void ViewModelDrawn(in WeaponDrawContext context)
    {
        if (context.ViewModel is not { } viewModel)
            return;

        DrawFirstPersonEffects(context, viewModel);
    }

    /// <summary>
    /// DrawModel's translucent arm: the effects, and the model is not drawn
    /// again. A stick drawn through the viewmodel is not drawn here at all
    /// (stock's <c>ShouldDraw</c> refuses the local carrier's world model).
    /// </summary>
    public int DrawModel(in WeaponDrawContext context, int flags)
    {
        if (context.FirstPerson)
            return 0;

        DrawThirdPersonEffects(context);
        return 1;
    }

    private void Halo(int material, Vector origin, float scale, float brightness, in CurrentViewState eye)
    {
        Span<FxVertex> quad = stackalloc FxVertex[4];
        EffectQuads.Halo(quad, origin, scale, new Vector(brightness, brightness, brightness), eye);
        fx.DrawQuads(material, quad, byteColor: false);
    }

    /// <summary>C_WeaponStunStick::DrawFirstPersonEffects.</summary>
    private void DrawFirstPersonEffects(in WeaponDrawContext context, ClientSsViewModel viewModel)
    {
        int material = Material(GlowMaterialNoZ);
        CurrentViewState eye = view.View();
        Stick stick = StickOf(context.Weapon.Handle);
        bool swinging = InSwing(context);

        // Find where we are in the fade.
        float fadeAmount = RemapValClamped(context.Now, stick.FadeTime, stick.FadeTime + FadeDuration, 1.0f, 0.1f);

        // Get bright when swung.
        float color = swinging ? 0.4f : 0.4f * fadeAmount;
        float scale = swinging ? 22.0f : 20.0f;

        // An all-encompassing glow around the entire head, at the centre
        // attachment stock looked up on the weapon's own model.
        int centre = render.LookupAttachment(context.Weapon.Handle, CoreAttachmentName);
        if (color > 0.0f && render.GetAttachment(viewModel.Handle, centre, out Vector core, out _))
            Halo(material, core, scale, color, eye);

        // Bright points at each attachment location.
        for (int i = 0; i < (BeamAttachments * 2) + 1; i++)
        {
            if (swinging)
            {
                color = random.RandomFloat(0.05f, 0.5f);
                scale = random.RandomFloat(4.0f, 5.0f);
            }
            else
            {
                color = random.RandomFloat(0.05f, 0.5f) * fadeAmount;
                scale = random.RandomFloat(4.0f, 5.0f) * fadeAmount;
            }

            if (color > 0.0f && render.GetAttachment(viewModel.Handle, i, out Vector point, out _))
                Halo(material, point, scale, color, eye);
        }
    }

    /// <summary>C_WeaponStunStick::DrawThirdPersonEffects (InSwing is always true here).</summary>
    private void DrawThirdPersonEffects(in WeaponDrawContext context)
    {
        CurrentViewState eye = view.View();
        EntityHandle weapon = context.Weapon.Handle;

        // An all-encompassing glow around the entire head: the world model's head.
        if (!render.GetAttachment(weapon, 1, out Vector head, out Vector angles))
            return;

        Halo(Material(GlowMaterial), head, 22.0f, 0.4f, eye);

        float flare = random.RandomFloat(0.6f, 0.8f);
        float flareScale = random.RandomFloat(4.0f, 6.0f);
        Halo(Material(GlowMaterial2), head, flareScale, flare, eye);

        // Update our effects.
        if (context.FrameTime != 0.0f && random.RandomInt(0, 5) == 0)
        {
            Vector forward = Angles.Forward(angles);
            Vector start = head - forward * 1.0f;
            Vector offset = RandomVector(-12, 8) + new Vector(4, 4, 4);

            beams.CreateBeamPoints(new BeamInfo
            {
                Start = start,
                End = head + offset,
                StartEnt = weapon,
                EndEnt = weapon,
                StartAttachment = 1,
                EndAttachment = -1,
                Type = BeamType.Tesla,
                ModelName = BeamMaterial,
                HaloScale = 0.0f,
                Life = 0.01f,
                Width = random.RandomFloat(1.0f, 3.0f),
                EndWidth = 0,
                FadeLength = 0.0f,
                Amplitude = random.RandomFloat(1, 2),
                Brightness = 255.0f,
                Speed = 0.0f,
                StartFrame = 0,
                FrameRate = 1.0f,
                Red = 255.0f,
                Green = 255.0f,
                Blue = 255.0f,
                Segments = 16,
                Renderable = true,
                Flags = BeamFlags.ShadeOut,
            });
        }
    }

    /// <summary>RemapValClamped.</summary>
    private static float RemapValClamped(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;

        float cVal = (val - a) / (b - a);
        cVal = Math.Clamp(cVal, 0.0f, 1.0f);
        return c + (d - c) * cVal;
    }
}

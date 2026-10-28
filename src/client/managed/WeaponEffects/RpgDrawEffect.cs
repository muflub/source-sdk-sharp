//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client.WeaponEffects;

/// <summary>
/// <c>CWeaponRPG</c>'s client draw (shared/hl2mp/weapon_rpg.cpp,
/// <c>DrawEffects</c>, <c>InitBeam</c>, <c>ViewModelDrawn</c>): while the
/// laser guides, a red glow at the viewmodel's "laser" attachment, drawn in
/// the viewmodel pass, and a red beam from "laser" to "laser_end" that the
/// beam system draws in the world's translucent pass.
///
/// <para>
/// The glow is <c>RPG_LASER_SPRITE</c> eight units across, give or take two,
/// white; the beam <c>RPG_BEAM_SPRITE_NOZ</c>, two wide, shading out over the
/// length of a 128-unit trace from the glow along the attachment's forward,
/// at a brightness drawn from 128 to 200 each frame. The beam is made once,
/// on the first guiding frame with ammunition left, and kept dark while the
/// laser is off.
/// </para>
///
/// <para>
/// <b>No third-person draw, as stock.</b> Stock's <c>DrawModel</c> draws the
/// effects only for <c>STUDIO_TRANSPARENCY</c>, and the carried RPG never gets
/// it: its world model (w_rocket_launcher, one opaque VertexLitGeneric
/// material) is not translucent, and <c>CWeaponRPG::IsTranslucent</c> overrides
/// nothing (the render group asks <c>IsTransparent</c>). So the RPG answers
/// the opaque group and draws nothing beside its world model.
/// </para>
/// </summary>
public sealed class RpgDrawEffect(IFxRender fx, IPixelVisSlots view, IEngineEntityRender render,
                                  IEngineWorldQuery world, IClientBeams beams, IUniformRandom random) : IWeaponDrawEffect
{
    /// <summary>RPG_LASER_SPRITE, as a material name.</summary>
    public const string LaserSprite = "sprites/redglow1";

    /// <summary>RPG_BEAM_SPRITE_NOZ.</summary>
    public const string BeamSpriteNoZ = "effects/laser1_noz.vmt";

    /// <summary>RPG_BEAM_SPRITE.</summary>
    public const string BeamSprite = "effects/laser1.vmt";

    /// <summary>RPG_GUIDE_ATTACHMENT and RPG_GUIDE_TARGET_ATTACHMENT, on the viewmodel.</summary>
    public const int GuideAttachment = 2, GuideTargetAttachment = 3;

    /// <summary>RPG_LASER_BEAM_LENGTH.</summary>
    public const float LaserBeamLength = 128f;

    private readonly Dictionary<EntityHandle, Beam> _beams = [];
    private int _sprite;

    public string WeaponName => EntityNames.WeaponRpg;

    public bool TwoPass => false;

    public void Think(in WeaponDrawContext context) { }

    public void ViewModelDrawn(in WeaponDrawContext context) => DrawEffects(context);

    // Never asked: the RPG is not in the two-pass group (see the class remarks).
    public int DrawModel(in WeaponDrawContext context, int flags) => -1;

    /// <summary>The weapon is no longer an RPG: its beam goes, as a beam whose entity went does.</summary>
    public void Forget(EntityHandle weapon)
    {
        if (_beams.Remove(weapon, out Beam? beam))
            Release(beam);
    }

    /// <summary>CPhysCannonEffectBeam::Release's shape: no flags, dead a second ago.</summary>
    private static void Release(Beam beam)
    {
        beam.Flags = 0;
        beam.Die = -1f;
    }

    /// <summary>The beam this weapon keeps, for the facts.</summary>
    internal Beam? BeamOf(EntityHandle weapon) => _beams.GetValueOrDefault(weapon);

    /// <summary><c>CWeaponRPG::DrawEffects</c>, in the first person.</summary>
    private void DrawEffects(in WeaponDrawContext context)
    {
        int special = context.Weapon.State.Special;
        Beam? beam = _beams.GetValueOrDefault(context.Weapon.Handle);

        // Must be guiding and not hidden.
        if (!RpgState.Has(special, RpgState.Guiding) || RpgState.Has(special, RpgState.HideGuiding))
        {
            if (beam is not null)
                beam.Brightness = 0;
            return;
        }

        if (context.ViewModel is not { } viewModel)
            return;

        if (_sprite == 0)
            _sprite = fx.MaterialFind(LaserSprite);

        float scale = 8.0f + random.RandomFloat(-2.0f, 2.0f);

        // GetWeaponAttachment: the viewmodel's, back in its own field of view,
        // which is this build's attachment as the skeleton places it.
        render.GetAttachment(viewModel.Handle, GuideAttachment, out Vector attachment, out Vector angles);
        Vector dir = Angles.Forward(angles);

        // Draw the sprite.
        if (_sprite != 0)
        {
            Span<FxVertex> quad = stackalloc FxVertex[4];
            EffectQuads.Sprite(quad, attachment, scale, scale, 255, 255, 255, 255, view.View());
            fx.DrawQuads(_sprite, quad, byteColor: true);
        }

        // The beam's run.
        ClientTraceResult tr = world.ClientTraceLine(attachment, attachment + dir * LaserBeamLength, TraceMask.Shot,
                                                     TraceFilter.Simple, context.Owner);

        beam ??= InitBeam(context.Weapon, viewModel);

        if (beam is not null)
        {
            beam.FadeLength = LaserBeamLength * tr.Fraction;
            beam.Brightness = random.RandomInt(128, 200);
        }
    }

    /// <summary><c>CWeaponRPG::InitBeam</c>, first person: once, and only with ammunition left.</summary>
    private Beam? InitBeam(ClientSsWeapon weapon, ClientSsViewModel viewModel)
    {
        if (weapon.State.Reserve <= 0)
            return null;

        Beam? beam = beams.CreateBeamEntPoint(FirstPersonBeam(viewModel.Handle));
        if (beam is not null)
            _beams[weapon.Handle] = beam;
        return beam;
    }

    /// <summary>InitBeam's BeamInfo_t for the viewmodel.</summary>
    public static BeamInfo FirstPersonBeam(EntityHandle viewModel) => new()
    {
        StartEnt = viewModel,
        EndEnt = viewModel,
        Type = BeamType.Points,
        Start = Vector.Zero,
        End = Vector.Zero,
        ModelName = BeamSpriteNoZ,
        HaloScale = 0.0f,
        Life = 0.0f,
        Width = 2.0f,
        EndWidth = 2.0f,
        StartAttachment = GuideAttachment,
        EndAttachment = GuideTargetAttachment,
        FadeLength = 0.0f,
        Amplitude = 0,
        Brightness = 255.0f,
        Speed = 1.0f,
        StartFrame = 0,
        FrameRate = 30.0f,
        Red = 255.0f,
        Green = 0.0f,
        Blue = 0.0f,
        Segments = 4,
        Renderable = true,
        Flags = BeamFlags.Forever | BeamFlags.ShadeOut,
    };
}

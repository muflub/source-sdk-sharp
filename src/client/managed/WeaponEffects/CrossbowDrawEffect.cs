//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client.WeaponEffects;

/// <summary>
/// <c>CWeaponCrossbow</c>'s charger (shared/hl2mp/weapon_crossbow.cpp:
/// <c>CreateChargerEffects</c>, <c>SetChargerState</c>, <c>DoLoadEffect</c>)
/// and the <c>CrossbowLoad</c> client effect (c_weapon_crossbow.cpp,
/// <c>CrosshairLoadCallback</c>), for the local carrier.
///
/// <para>
/// THE CHARGER: stock's server made an env_sprite, <c>light_glow02_noz</c>,
/// additive orange (255 128 0, no dissipation), hung on the viewmodel's
/// "bolt_start" attachment, made dark and 0.1 large; the charge brightens it
/// to 32 over half a second at 0.025, the bolt being ready to 80 over a second
/// at 0.1, and a shot or a holster puts it out. The state follows the model
/// events the weapon schedules (<c>CrossbowBehaviour</c>'s pending
/// <see cref="WeaponNetState.AnimEvent"/>): the charge's event handled is
/// "charging", every event handled with a bolt in is "ready", a bolt gone is
/// "discharged".
/// </para>
///
/// <para>
/// THE LOAD: each shot and each reload's first event (<c>DoLoadEffect</c>)
/// strikes an electric spark at the viewmodel's "spark" attachment, where
/// the world's projection draws it (<c>CrossbowLoad</c>), and stock's server
/// hung a <c>blueflare1</c> there, 0.2 large and 128 bright, faded out over a
/// quarter second from a hundredth of a second after it was made.
/// </para>
///
/// <para>
/// DRAWN IN THE WORLD'S TRANSLUCENT PASS, as stock's two sprites were: they
/// were entities, drawn there at the attachment converted to the world's field
/// of view (C_BaseViewModel's GetAttachment), so the viewmodel drew over them.
/// Here each bow holds a renderable of its own in that pass
/// (<see cref="IClientTranslucentRenderables"/>), moved to the bolt's tip each
/// frame the carrier sees it in the first person, which only the carrier ever
/// does: stock's sprites, on the viewmodel, were sent to nobody else
/// (CSprite::ShouldTransmit asks the viewmodel's).
/// </para>
/// </summary>
public sealed class CrossbowDrawEffect(IClientSpriteDraw sprites, IEngineEntityRender render, IEngineEntities entities,
                                       IClientBeamExecutor models, IClientSparks sparks, IViewModelAttachmentFormat viewModels,
                                       IClientTranslucentRenderables translucent) : IWeaponDrawEffect
{
    /// <summary>CROSSBOW_GLOW_SPRITE and CROSSBOW_GLOW_SPRITE2.</summary>
    public const string GlowSprite = "sprites/light_glow02_noz.vmt", BlastSprite = "sprites/blueflare1.vmt";

    /// <summary>BOLT_TIP_ATTACHMENT, and the load effect's attachment.</summary>
    public const int BoltTipAttachment = 2, SparkAttachment = 1;

    /// <summary>EVENT_WEAPON_THROW, THROW2 and THROW3: the charger's three model events.</summary>
    public const int StartLoadEvent = 3005, StartChargeEvent = 3013, ReadyEvent = 3016;

    /// <summary>ChargerState_t.</summary>
    public enum Charger
    {
        StartLoad,
        StartCharge,
        Ready,
        Discharge,
        Off,
    }

    /// <summary>One crossbow's charger sprite and its last load flare.</summary>
    public sealed class Bow
    {
        public Charger State = Charger.Off;
        public readonly SpriteRamp Ramp = new();
        public float Scale = 0.1f, ScaleTime;
        public int Brightness;
        public float BrightnessTime;
        public bool On;
        public int LastClip = -1;
        public float BlastAt = float.NegativeInfinity;

        // The renderable the two sprites are drawn by, and what its draw reads:
        // whether the carrier saw the bow in the first person this frame, its
        // viewmodel and the frame's clock.
        public int Renderable = -1;
        public bool Visible;
        public EntityHandle ViewModel;
        public float Now;
    }

    /// <summary>Half the renderable's box: both sprites at their largest fit inside.</summary>
    private const float RenderableExtent = 32f;

    private readonly Dictionary<EntityHandle, Bow> _bows = [];

    public string WeaponName => EntityNames.WeaponCrossbow;

    public bool TwoPass => false;

    /// <summary>A bow's state, for the facts.</summary>
    internal Bow? BowOf(EntityHandle weapon) => _bows.GetValueOrDefault(weapon);

    public void Forget(EntityHandle weapon)
    {
        if (_bows.Remove(weapon, out Bow? bow) && bow.Renderable >= 0)
            translucent.Remove(bow.Renderable);
    }

    public int DrawModel(in WeaponDrawContext context, int flags) => -1;

    /// <summary>
    /// The charger state the weapon's scheduled model events say it is in:
    /// what <c>SetChargerState</c> was last called with.
    /// </summary>
    public static Charger StateOf(WeaponNetState weapon, Charger was) => weapon.AnimEvent switch
    {
        // An empty bow's draw (stock's ACT_CROSSBOW_DRAW_UNLOADED) charges nothing.
        ReadyEvent when weapon.Clip > 0 || weapon.InReload => Charger.StartCharge,
        ReadyEvent => was,
        StartChargeEvent when weapon.InReload => Charger.StartLoad,
        StartChargeEvent or StartLoadEvent => was,
        _ => weapon.Clip > 0 ? Charger.Ready : was == Charger.Off ? Charger.Off : Charger.Discharge,
    };

    /// <summary>Once a frame: the charger follows the events, and a shot or a load flares.</summary>
    public void Think(in WeaponDrawContext context)
    {
        if (!context.FirstPerson || context.ViewModel is not { } viewModel)
        {
            if (_bows.TryGetValue(context.Weapon.Handle, out Bow? unseen))
                unseen.Visible = false;
            return;
        }

        if (!_bows.TryGetValue(context.Weapon.Handle, out Bow? bow))
        {
            _bows[context.Weapon.Handle] = bow = new Bow();
            bow.Ramp.Created(bow.Scale, bow.Brightness);
            bow.LastClip = context.Weapon.State.Clip;
        }

        bow.Visible = true;
        bow.ViewModel = viewModel.Handle;
        bow.Now = context.Now;
        Place(bow);

        WeaponNetState weapon = context.Weapon.State;

        // FireBolt: DoLoadEffect, then the discharge.
        if (weapon.Clip < bow.LastClip)
            DoLoadEffect(context, viewModel, bow);
        bow.LastClip = weapon.Clip;

        Charger next = StateOf(weapon, bow.State);
        if (next != bow.State)
            SetChargerState(context, viewModel, bow, next);
    }

    /// <summary>CWeaponCrossbow::SetChargerState.</summary>
    private void SetChargerState(in WeaponDrawContext context, ClientSsViewModel viewModel, Bow bow, Charger state)
    {
        bow.State = state;
        switch (state)
        {
            case Charger.StartLoad:
                // Shoot some sparks (the sound is the server's event's).
                DoLoadEffect(context, viewModel, bow);
                break;

            case Charger.StartCharge:
                Set(bow, brightness: 32, brightnessTime: 0.5f, scale: 0.025f, scaleTime: 0.5f, on: true, context.Now);
                break;

            case Charger.Ready:
                Set(bow, brightness: 80, brightnessTime: 1.0f, scale: 0.1f, scaleTime: 0.5f, on: true, context.Now);
                break;

            default:
                Set(bow, brightness: 0, brightnessTime: 0f, scale: bow.Scale, scaleTime: bow.ScaleTime, on: false, context.Now);
                break;
        }
    }

    private static void Set(Bow bow, int brightness, float brightnessTime, float scale, float scaleTime, bool on, float now)
    {
        bow.Brightness = brightness;
        bow.BrightnessTime = brightnessTime;
        bow.Scale = scale;
        bow.ScaleTime = scaleTime;
        bow.On = on;
        bow.Ramp.Think(now, scale, brightness);
    }

    /// <summary>
    /// DoLoadEffect: the CrossbowLoad spark at the "spark" attachment in the
    /// world's field of view, and the blue flare hung there.
    /// </summary>
    private void DoLoadEffect(in WeaponDrawContext context, ClientSsViewModel viewModel, Bow bow)
    {
        if (render.GetAttachment(viewModel.Handle, SparkAttachment, out Vector spark, out _))
            sparks.ElectricSpark(viewModels.ForWorldPass(viewModel.Handle, spark), 1, 1, null);

        bow.BlastAt = context.Now;
    }

    /// <summary>The flare's brightness: 128, then FadeAndDie( 0.25 ) from a hundredth of a second on.</summary>
    public static int BlastBrightness(float sinceMade)
    {
        const float FadeFrom = 0.01f, Fade = 0.25f;
        if (sinceMade < 0f || sinceMade > FadeFrom + Fade)
            return 0;
        if (sinceMade <= FadeFrom)
            return 128;
        return (int)(128 * (1f - (sinceMade - FadeFrom) / Fade));
    }

    /// <summary>Stock's crossbow draws nothing after its viewmodel: its sprites are the world pass's.</summary>
    public void ViewModelDrawn(in WeaponDrawContext context)
    {
    }

    /// <summary>The bow's renderable at the bolt's tip as the world's pass sees it, made the first time.</summary>
    private void Place(Bow bow)
    {
        Vector origin = WorldPassAttachment(bow.ViewModel, BoltTipAttachment, out Vector tip) ? tip : entities.GetOrigin(bow.ViewModel);
        var extent = new Vector(RenderableExtent, RenderableExtent, RenderableExtent);

        if (bow.Renderable < 0)
            bow.Renderable = translucent.Add(origin, extent * -1f, extent, () => DrawSprites(bow));
        else
            translucent.Changed(bow.Renderable, origin, extent * -1f, extent);
    }

    /// <summary>The two sprites, in the world's translucent pass, as large as stock's entities drew them.</summary>
    internal void DrawSprites(Bow bow)
    {
        if (!bow.Visible)
            return;

        if (bow.On)
        {
            float scale = bow.Ramp.Scale(bow.Now, bow.Scale, bow.ScaleTime);
            int brightness = bow.Ramp.Brightness(bow.Now, bow.Brightness, bow.BrightnessTime);
            if (brightness > 0)
                Draw(bow.ViewModel, BoltTipAttachment, GlowSprite, 255, 128, 0, brightness, scale,
                     ClientSpriteRenderRules.RenderFxNoDissipation);
        }

        int blast = BlastBrightness(bow.Now - bow.BlastAt);
        if (blast > 0)
            Draw(bow.ViewModel, SparkAttachment, BlastSprite, 255, 255, 255, blast, 0.2f, 0);
    }

    /// <summary>A viewmodel attachment where the world's field of view puts it.</summary>
    private bool WorldPassAttachment(EntityHandle viewModel, int attachment, out Vector at)
    {
        at = default;
        if (!render.GetAttachment(viewModel, attachment, out Vector raw, out _))
            return false;
        at = viewModels.ForWorldPass(viewModel, raw);
        return true;
    }

    private void Draw(EntityHandle viewModel, int attachment, string sprite, int r, int g, int b, int a, float scale,
                      int renderFx)
    {
        // The sprite's index in the networked model table (the server precached it).
        int model = models.ModelIndex(sprite);
        if (model <= 0 || !WorldPassAttachment(viewModel, attachment, out Vector at))
            return;

        // Drawn at the viewmodel's origin plus the way to its attachment.
        Vector offset = at - entities.GetOrigin(viewModel);
        sprites.DrawSpriteOffset(viewModel, offset, model, 0f, (int)RenderMode.TransAdd, renderFx,
                                 r, g, b, a, scale, worldSpaceScale: false, hdrColorScale: 1.0f, glowProxySize: 0f);
    }
}

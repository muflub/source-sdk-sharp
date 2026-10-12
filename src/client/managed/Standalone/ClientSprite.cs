//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Effects;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client halves of env_sprite, env_sprite_oriented and env_glow.
///
/// <para>
/// Like <see cref="ClientBeam"/> this is short and is not optional for that
/// reason: a class with networked state gets a table on a side only if that
/// side has an entity class to host it, and a server table with no match here
/// stops the join with "No matching RecvTable for SendTable 'DT_env_sprite'"
/// rather than with a missing sprite.
/// </para>
///
/// <para>
/// It is also the entity's whole client presence. Stock's env_sprite draws
/// because the server networks a <c>CSprite</c> and the engine builds the
/// <c>C_Sprite</c> paired with <c>DT_Sprite</c>; a managed entity is never on
/// the wire as that class — it arrives through one of the six tier tables in
/// <c>generic_nettable.h</c> — so <c>DT_Sprite</c> stops being sent the moment
/// the classname moves and nothing on this side would ever be asked to draw.
/// </para>
///
/// <para>
/// UNLIKE the steam jet, the embers, the six trails and the smokestack, no
/// stock client OBJECT is kept alive behind this. Those five SIMULATE — a puff
/// spawner, an ember drifting, a trail's point history — and the object is the
/// only way to keep the simulation. A sprite simulates nothing:
/// <c>C_SpriteRenderer::DrawSprite</c> is a pure function of the values passed
/// to it, and every one of those values is a field this class already holds
/// decoded. See <c>shell_sprite_render.cpp</c>.
/// </para>
/// </summary>
public abstract class ClientSpriteBase : ClientStandaloneEntity
{
    public ClientSpriteBase(IEngineEntityRender render, IEngineEffects effects, IClientSpriteDraw sprites)
        : base(render)
    {
        Effects = effects;
        Sprites = sprites;
    }

    /// <summary>the small effects.</summary>
    protected readonly IEngineEffects Effects;

    /// <summary>
    /// The sprite draw, planned on this side (ClientSpriteHost): the plan costs
    /// no crossing back into managed from inside this entity's own draw.
    /// </summary>
    protected readonly IClientSpriteDraw Sprites;

    /// <summary>The three classnames' tables, read through one interface.</summary>
    protected abstract ISpriteNetState Sprite { get; }

    /// <summary>
    /// The box the renderer culls this sprite by, and how it is blended.
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        var s = Sprite;

        // Stock's C_Sprite::GetRenderBounds, and the halving is stock's too: the
        // box is the sprite's own extent, so half of it either side of the
        // origin.
        //
        // NOT the entity's model bounds, which is what the shell would use — a
        // sprite's model is a picture with no geometry, so its bounds are zero
        // and the leaf system would cull it by a point. That is the failure that
        // reads as "the sprite vanishes when its centre leaves the screen".
        float half = s.Scale * 0.5f;

        // A normalised scale is a MULTIPLE of the sprite's own size, so the size
        // has to come back into it to get world units. Native knows the size;
        // this side does not, so a normalised sprite is boxed by its scale alone
        // and padded generously rather than guessed at precisely. A box that is
        // a little large draws when it need not; one that is a little small
        // vanishes at the edge of the screen.
        if (!s.WorldSpaceScale)
            half *= MaxSpriteSizeGuess;

        half = MathF.Max(half, 1f);

        var mins = new Vector(-half, -half, -half);
        var maxs = new Vector(half, half, half);

        Render.SetCustomDraw(Handle, mins, maxs);

        // AFTER SetCustomDraw, and on THIS side.
        //
        // After, because the first SetCustomDraw is what puts a modelless
        // entity into the renderer's list at all, and a renderable inserted
        // before it can draw is one the shell warns about.
        //
        // On this side because the slot is a CLIENT one — StandaloneSetBlending
        // is wired inside the gamesystem's `#ifdef CLIENT_DLL` — so the same
        // call from the server half is a null function pointer and Engine.cs
        // skips it silently. That was not theoretical: it is what this port did
        // first, and the symptom was that additive sprites drew and the two GLOW
        // modes did not, because the entity stayed in the opaque list where
        // DrawNoZBufferTranslucentRenderables never looks. Nothing was logged.
        //
        // Pushed on every update rather than only the first, because the three
        // answers all come from state a map can change: ColorBlueValue does not
        // touch them, but a mod that networked a render mode would.
        Render.SetBlending(Handle,
                           translucent: true,
                           ignoreZ: SpriteRules.IgnoresDepth(s.RenderMode),
                           alpha: s.Brightness);
    }

    /// <summary>
    /// The largest sprite this build ships, in units, used to turn a normalised
    /// scale into a bounding box.
    ///
    /// <para>
    /// A bound rather than a measurement, because the real number lives on the
    /// loaded model and this side has no call that answers it. 256 covers every
    /// .spr in the tree with room to spare, and the cost of being generous is a
    /// renderable asked to draw slightly more often than it need be — which is
    /// the harmless direction. See docs/divergences.md.
    /// </para>
    /// </summary>
    private const float MaxSpriteSizeGuess = 256f;

    /// <summary>
    /// One sprite, this frame.
    ///
    /// <para>
    /// Every value here was decoded from the entity's own networked state, which
    /// is why there is no renderer object behind this class: there is nothing to
    /// read out of native memory, because the state already arrived as
    /// properties.
    /// </para>
    ///
    /// <para>
    /// WHERE it is and WHICH WAY IT FACES are the exceptions, and are not
    /// passed. Native reads both off the entity, because what it holds is the
    /// INTERPOLATED place for the frame being drawn — and because this side
    /// cannot answer at all: <c>EntityGetOrigin</c> is a server-side ABI slot
    /// and returns a silent zero vector here. That is not hypothetical; it is
    /// what this class did first, and every sprite in dm_lockdown drew at the
    /// world origin.
    /// </para>
    /// </summary>
    public override int OnDraw()
    {
        var s = Sprite;

        if (!s.Visible || s.ModelIndex <= 0)
            return 0;

        return Sprites.DrawSprite(Handle, s.ModelIndex,
                                  s.Frame, s.RenderMode, s.RenderFx,
                                  s.Red, s.Green, s.Blue, s.Brightness,
                                  s.Scale, s.WorldSpaceScale,
                                  s.HdrColorScale, s.GlowProxySize);
    }

    /// <summary>The same sprite, planned in the view's batch (C7 item 7) and carried out at its DrawModel.</summary>
    public override int OnDrawBatched()
    {
        var s = Sprite;

        if (!s.Visible || s.ModelIndex <= 0)
            return 0;

        return Sprites.PrepareSprite(Handle, s.ModelIndex,
                                     s.Frame, s.RenderMode, s.RenderFx,
                                     s.Red, s.Green, s.Blue, s.Brightness,
                                     s.Scale, s.WorldSpaceScale,
                                     s.HdrColorScale, s.GlowProxySize);
    }
}

/// <summary>env_sprite: the name every one of dm_lockdown's twenty-three uses.</summary>
[ClientStandaloneEntity(EntityNames.EnvSprite)]
public sealed class ClientSprite : ClientSpriteBase
{
    public ClientSprite(IEngineEntityRender render, IEngineEffects effects, IClientSpriteDraw sprites)
        : base(render, effects, sprites)
    {
    }

    public override string ClassName => EntityNames.EnvSprite;

    [NetState] public EnvSpriteNetState State = new();

    protected override ISpriteNetState Sprite => State;
}

/// <summary>env_sprite_oriented: the same sprite, spawned facing the other way.</summary>
[ClientStandaloneEntity(EntityNames.EnvSpriteOriented)]
public sealed class ClientSpriteOriented : ClientSpriteBase
{
    public ClientSpriteOriented(IEngineEntityRender render, IEngineEffects effects, IClientSpriteDraw sprites)
        : base(render, effects, sprites)
    {
    }

    public override string ClassName => EntityNames.EnvSpriteOriented;

    [NetState] public EnvSpriteOrientedNetState State = new();

    protected override ISpriteNetState Sprite => State;
}

/// <summary>env_glow: stock's own back-compatibility alias for env_sprite.</summary>
[ClientStandaloneEntity(EntityNames.EnvGlow)]
public sealed class ClientGlow : ClientSpriteBase
{
    public ClientGlow(IEngineEntityRender render, IEngineEffects effects, IClientSpriteDraw sprites)
        : base(render, effects, sprites)
    {
    }

    public override string ClassName => EntityNames.EnvGlow;

    [NetState] public EnvGlowNetState State = new();

    protected override ISpriteNetState Sprite => State;
}

/// <summary>
/// The client half of env_spritetrail.
///
/// <para>
/// The one member of the sprite family that does NOT draw itself. Its sibling
/// classes hand eleven numbers to <c>C_SpriteRenderer::DrawSprite</c> and are
/// done; a trail is a ring of up to 256 points laid down over time, and that
/// ring has to be owned by something between frames.
/// </para>
///
/// <para>
/// It used to be owned by the stock <c>C_SpriteTrail</c>, which native built
/// client-side and this class kept fed. It is not any more: <c>C_SpriteTrail</c>
/// paired on a ServerClass name nothing in this build sends, its only
/// constructor call anywhere was our own shell, and both stock files have left
/// the client build. The ribbon lives on a <c>CShellRenderable</c> in
/// <c>shell_sprite_render.cpp</c> — the de-entified base <c>C_PointWorldText</c>
/// already sits on — which is a renderable in the client leaf system and
/// nothing else.
/// </para>
///
/// <para>
/// Nothing changes on this side, and that is the point: this class still draws
/// nothing here and still declares no custom draw, because the object native
/// makes has its own DrawModel either way. What this side owns is the DECISIONS
/// — six numbers about the ribbon and five about the sprite it is made of.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvSpritetrail)]
public sealed class ClientSpriteTrail : ClientStandaloneEntity
{
    public ClientSpriteTrail(IEngineEntityRender render, IEngineEffects effects, ClientSpriteTrails trails)
        : base(render)
    {
        Effects = effects;
        _trails = trails;
    }

    /// <summary>the small effects.</summary>
    private readonly IEngineEffects Effects;

    // The ribbon itself (SpriteTrail): its ring lives on this side, ticked and
    // drawn by native's renderable through IManagedFxClient.
    private readonly ClientSpriteTrails _trails;

    public override string ClassName => EntityNames.EnvSpritetrail;

    [NetState] public EnvSpriteTrailNetState State = new();

    /// <summary>
    /// Everything the map decided, on every update.
    ///
    /// <para>
    /// The position is not among it. Native sweeps every trail in PreRender,
    /// because a trail is by definition something that FOLLOWS and
    /// <c>OnDataChanged</c> fires on the blob — a managed entity's origin is a
    /// real prop outside it, so one that only MOVED delivers no update at all.
    /// </para>
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        var s = State;

        _trails.Update(Handle, new SpriteTrailParams
        {
            ModelIndex = s.ModelIndex,
            LifeTime = s.LifeTime,
            StartWidth = s.StartWidth,
            EndWidth = s.EndWidth,
            StartWidthVariance = s.StartWidthVariance,
            TextureRes = s.TextureRes,
            MinFadeLength = s.MinFadeLength,
            RenderMode = s.RenderMode,
            R = s.Red,
            G = s.Green,
            B = s.Blue,
            Brightness = s.Brightness,
            Visible = s.Visible,
        });

        // Native's renderable: made on the first update, refused for a render
        // mode outside RenderMode_t, switched on and off by the map.
        Effects.SpriteTrailUpdate(Handle, s.RenderMode, s.Visible);
    }

    /// <summary>The trail goes when the entity does.</summary>
    public override void OnRemoved()
    {
        _trails.Remove(Handle);
        Effects.SpriteTrailDestroy(Handle);
    }
}

//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a sprite tells the client: which sprite, how big, how bright, what
/// colour, which frame, and whether it is drawn at all.
///
/// <para>
/// Declared once and used by all THREE of stock's classnames through
/// <see cref="EnvSpriteNetState"/>, <see cref="EnvSpriteOrientedNetState"/> and
/// <see cref="EnvGlowNetState"/>, which exist only because a table is keyed by
/// classname and stock registers one class — <c>CSprite</c> — under three of
/// them. The FIELDS are here, in one place, so the three tables cannot drift
/// apart; the same shape <see cref="ISteamJetNetState"/> and
/// <see cref="IDustNetState"/> have.
/// </para>
///
/// <para>
/// STOCK'S <c>DT_Sprite</c> HAS THIRTEEN PROPS AND THIS HAS TWELVE. What is
/// here and what is not was settled by reading
/// <c>IMPLEMENT_NETWORKCLASS_ALIASED( Sprite, DT_Sprite )</c> prop by prop
/// rather than by guessing, because two of them were the reason this family was
/// refused four times:
/// </para>
///
/// <list type="bullet">
/// <item>
/// <c>SendPropEHandle( m_hAttachedToEntity )</c> and <c>m_nAttachment</c> —
/// NOT here, and they carry nothing in this build. The only thing that ever
/// writes them is <c>CSprite::SetAttachment</c>, which no map key and no input
/// reaches; every caller of it is under <c>hl2/</c>, and
/// <c>sdk_csharp_release.ninja</c> compiles one file out of that directory.
/// A map-placed sprite has had a null handle and a zero attachment in stock
/// too. See docs/divergences.md.
/// </item>
/// <item>
/// <c>m_flScaleTime</c> and <c>m_flBrightnessTime</c> — NOT here, and the same
/// reasoning. They are the DURATION of a ramp, and only the two-argument
/// <c>SetScale( scale, time )</c> and <c>SetBrightness( level, time )</c> ever
/// set one. A map's "SetScale" input is a <c>DEFINE_INPUT</c> straight onto the
/// scale with no duration; the two-argument forms are called from
/// <c>FadeAndDie</c> and its friends, which nothing compiled here calls. Both
/// are zero for every sprite a map can place, and the client's
/// <c>GetRenderScale</c>/<c>GetRenderBrightness</c> then return the plain value.
/// </item>
/// <item>
/// <c>m_flSpriteFramerate</c> — not here either, because it is a decision
/// rather than an appearance: the SERVER advances the frame and networks the
/// frame. Stock networks the rate as well and the client does nothing with it.
/// </item>
/// </list>
///
/// <para>
/// What is here that stock does not network is <see cref="ModelIndex"/>,
/// <see cref="RenderMode"/>, <see cref="RenderFx"/> and the colour. Stock gets
/// all four free from <c>CBaseEntity</c>'s own send table, which the parallel
/// hierarchy does not have — its entities carry a model index and an origin and
/// nothing else. So they are carried explicitly, which is what every other
/// standalone class does.
/// </para>
/// </summary>
public interface ISpriteNetState
{
    /// <summary>The precached .spr or .vmt, as an index both sides agree on.</summary>
    int ModelIndex { get; set; }

    /// <summary>
    /// How big, and whether that is a multiplier or a measurement — see
    /// <see cref="WorldSpaceScale"/>. The mapper's "scale" key, clamped to
    /// stock's 0..64.
    /// </summary>
    float Scale { get; set; }

    /// <summary>
    /// Which frame of an animated sprite is showing. Advanced by the SERVER, so
    /// that what debug-api reports and what is drawn are the same number.
    /// </summary>
    float Frame { get; set; }

    /// <summary>
    /// The vertex alpha the sprite's quad is built with, 0..255. The mapper's
    /// "renderamt" — stock copies it into <c>m_nBrightness</c> in Spawn and then
    /// networks both, one as the mesh colour and one as the fx blend.
    /// </summary>
    int Brightness { get; set; }

    /// <summary>
    /// Which of Source's blend modes. 5 is kRenderTransAdd and 9 is
    /// kRenderWorldGlow, which between them are every sprite dm_lockdown places.
    /// </summary>
    int RenderMode { get; set; }

    /// <summary>The mapper's "renderfx" — pulses and fades the renderer applies on top.</summary>
    int RenderFx { get; set; }

    byte Red { get; set; }
    byte Green { get; set; }
    byte Blue { get; set; }

    /// <summary>
    /// How big the occlusion proxy is that the glow modes fade against, in
    /// world units. Stock's constructor default is 2, and dm_lockdown's sprites
    /// set 2 or 4.
    /// </summary>
    float GlowProxySize { get; set; }

    /// <summary>
    /// Multiplies the material's <c>$HDRCOLORSCALE</c>. Stock's constructor
    /// default is 1 — one of the values a managed field would otherwise start at
    /// zero and leave the sprite black under HDR.
    /// </summary>
    float HdrColorScale { get; set; }

    /// <summary>
    /// Whether <see cref="Scale"/> is world units rather than a multiple of the
    /// sprite's own size. No map key sets it; carried because stock networks it.
    /// </summary>
    bool WorldSpaceScale { get; set; }

    /// <summary>Drawn at all. Stock's EF_NODRAW, driven by ShowSprite/HideSprite/ToggleSprite.</summary>
    bool Visible { get; set; }
}

/// <summary>
/// The rules about a sprite that BOTH halves need, and that neither owns.
///
/// <para>
/// One rule so far. It lives here rather than on either entity class because
/// the server writes the render mode and the client acts on it, and a copy on
/// each side is two places for one fact to drift.
/// </para>
/// </summary>
public static class SpriteRules
{
    /// <summary>
    /// Stock's <c>C_BaseEntity::IgnoresZBuffer</c>, which is the two GLOW modes
    /// and nothing else.
    ///
    /// <para>
    /// It decides which of the translucent renderer's two passes a sprite is
    /// drawn in, and getting it wrong is invisible in a test and obvious in a
    /// corridor: a glow that answers false is drawn depth-tested and disappears
    /// behind the very geometry it is meant to bloom over.
    /// </para>
    /// </summary>
    public static bool IgnoresDepth(int renderMode)
        => renderMode == (int)RenderMode.Glow || renderMode == (int)RenderMode.WorldGlow;
}

/// <summary>env_sprite's table. See <see cref="ISpriteNetState"/> for what the fields mean.</summary>
[StandaloneNetState(EntityNames.EnvSprite)]
public partial class EnvSpriteNetState : ISpriteNetState
{
    public partial int ModelIndex { get; set; }
    public partial float Scale { get; set; }
    public partial float Frame { get; set; }

    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int Brightness { get; set; }
    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int RenderMode { get; set; }
    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int RenderFx { get; set; }

    [NetField(Bits = 8)] public partial byte Red { get; set; }
    [NetField(Bits = 8)] public partial byte Green { get; set; }
    [NetField(Bits = 8)] public partial byte Blue { get; set; }

    public partial float GlowProxySize { get; set; }
    public partial float HdrColorScale { get; set; }

    [NetField(Bits = 1)] public partial bool WorldSpaceScale { get; set; }
    [NetField(Bits = 1)] public partial bool Visible { get; set; }
}

/// <summary>env_sprite_oriented's table. The same fields; see <see cref="ISpriteNetState"/>.</summary>
[StandaloneNetState(EntityNames.EnvSpriteOriented)]
public partial class EnvSpriteOrientedNetState : ISpriteNetState
{
    public partial int ModelIndex { get; set; }
    public partial float Scale { get; set; }
    public partial float Frame { get; set; }

    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int Brightness { get; set; }
    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int RenderMode { get; set; }
    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int RenderFx { get; set; }

    [NetField(Bits = 8)] public partial byte Red { get; set; }
    [NetField(Bits = 8)] public partial byte Green { get; set; }
    [NetField(Bits = 8)] public partial byte Blue { get; set; }

    public partial float GlowProxySize { get; set; }
    public partial float HdrColorScale { get; set; }

    [NetField(Bits = 1)] public partial bool WorldSpaceScale { get; set; }
    [NetField(Bits = 1)] public partial bool Visible { get; set; }
}

/// <summary>env_glow's table. The same fields; see <see cref="ISpriteNetState"/>.</summary>
[StandaloneNetState(EntityNames.EnvGlow)]
public partial class EnvGlowNetState : ISpriteNetState
{
    public partial int ModelIndex { get; set; }
    public partial float Scale { get; set; }
    public partial float Frame { get; set; }

    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int Brightness { get; set; }
    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int RenderMode { get; set; }
    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int RenderFx { get; set; }

    [NetField(Bits = 8)] public partial byte Red { get; set; }
    [NetField(Bits = 8)] public partial byte Green { get; set; }
    [NetField(Bits = 8)] public partial byte Blue { get; set; }

    public partial float GlowProxySize { get; set; }
    public partial float HdrColorScale { get; set; }

    [NetField(Bits = 1)] public partial bool WorldSpaceScale { get; set; }
    [NetField(Bits = 1)] public partial bool Visible { get; set; }
}

/// <summary>
/// env_spritetrail's table: a sprite's, plus the six numbers that make it a
/// TRAIL.
///
/// <para>
/// Stock's <c>DT_SpriteTrail</c> is exactly this shape — <c>CSpriteTrail</c>
/// derives from <c>CSprite</c>, so its send table inherits <c>DT_Sprite</c> and
/// adds eight props of its own. Two of those eight are the skybox pair, which
/// is not carried: <c>SetSkybox</c> is called from nothing this build compiles,
/// and the client only reads them to notice they CHANGED.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvSpritetrail)]
public partial class EnvSpriteTrailNetState : ISpriteNetState
{
    public partial int ModelIndex { get; set; }
    public partial float Scale { get; set; }
    public partial float Frame { get; set; }

    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int Brightness { get; set; }
    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int RenderMode { get; set; }
    [NetField(NetFieldKind.UInt, Bits = 8)] public partial int RenderFx { get; set; }

    [NetField(Bits = 8)] public partial byte Red { get; set; }
    [NetField(Bits = 8)] public partial byte Green { get; set; }
    [NetField(Bits = 8)] public partial byte Blue { get; set; }

    public partial float GlowProxySize { get; set; }
    public partial float HdrColorScale { get; set; }

    [NetField(Bits = 1)] public partial bool WorldSpaceScale { get; set; }
    [NetField(Bits = 1)] public partial bool Visible { get; set; }

    /// <summary>How long a segment of the trail survives after it is laid down.</summary>
    public partial float LifeTime { get; set; }

    /// <summary>How wide the trail is at the entity.</summary>
    public partial float StartWidth { get; set; }

    /// <summary>
    /// How wide it is at the far end. NEGATIVE means "the same as the start",
    /// and −1 is what stock's CONSTRUCTOR sets — not its Spawn, and not a
    /// keyfield default. A managed field left at zero would give a trail that
    /// tapers to nothing where stock's keeps its width.
    /// </summary>
    public partial float EndWidth { get; set; }

    /// <summary>How much the width of each segment is allowed to wobble. Nothing map-facing sets it.</summary>
    public partial float StartWidthVariance { get; set; }

    /// <summary>Texels per inch along the trail. Zero, as stock leaves it, samples one column.</summary>
    public partial float TextureRes { get; set; }

    /// <summary>How many units of the tail are spent fading out.</summary>
    public partial float MinFadeLength { get; set; }
}

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What worldspawn tells the client: the level's bounds, its water height, the
/// two occlusion areas, the two prop screen widths, the detail sprite material,
/// and the two flags.
///
/// <para>
/// THESE ARE STOCK'S <c>DT_WORLD</c>, PROP FOR PROP. Ten of them, in the order
/// <c>IMPLEMENT_SERVERCLASS_ST(CWorld, DT_WORLD)</c> listed them, because that
/// is the order <c>c_world.cpp</c>'s receive table listed them in too and the
/// point of one declaration is that neither side gets to choose. What has moved
/// is WHERE they are declared, not what they are.
/// </para>
///
/// <para>
/// EVERY ONE IS READ BY THE CLIENT'S RENDERER, which is why the port that moved
/// them had to be two-sided. The bounds drive the <c>WorldDims</c> material
/// proxy and the client's own effects; the occlusion areas and the prop widths
/// are handed to <c>engine->SetOcclusionParameters</c> and
/// <c>modelinfo->SetLevelScreenFadeRange</c> the moment they arrive;
/// <c>DetailSpriteMaterial</c> is what the client's <c>DetailPropSystem</c> builds every
/// detail sprite out of; <c>StartDark</c> fades the screen up on the first
/// frame. If one of them stopped arriving the symptom would be a rendering
/// difference and not a crash.
/// </para>
///
/// <para>
/// NOT ON THE PARALLEL HIERARCHY, and that flag is the only thing about this
/// declaration that is unusual. worldspawn is edict 0, it owns brush model 0,
/// every trace that hits nothing hits it, and the engine names it by INDEX
/// rather than by handle on both sides — so it is its own class on each, and
/// each allocates its own blob against its own base. See
/// <see cref="StandaloneNetStateAttribute.OnHierarchy"/> for what the flag
/// changes, which is one piece of arithmetic and nothing else.
/// </para>
///
/// <para>
/// AND IT IS ON NO HIERARCHY AT ALL NOW, which this paragraph used to get half
/// right. It said the world "stays a <c>CBaseEntity</c>"; that was true of both
/// sides when it was written, then step 4g took <c>CWorld</c> off
/// <c>CBaseEntity</c> and step 5e took <c>C_World</c> off <c>C_BaseEntity</c>.
/// Neither landed on the parallel hierarchy — a standing owner instruction
/// refuses that specifically — so both are their own standalone classes and
/// both partition arms answer NULL for them. <b>Nothing in this declaration
/// changed for it</b>, and that is the point worth recording: the wire is
/// neutral to which hierarchy the two native objects sit on, because the table
/// is built from THIS file on both sides and each side resolves its props
/// against its own <c>sizeof</c>.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.Worldspawn, OnHierarchy = false)]
public partial class WorldNetState
{
    /// <summary>
    /// How far the water surface moves, for the wave effect the client's water
    /// material applies.
    ///
    /// <para>
    /// Zero for the life of this build, and carried anyway. The only thing that
    /// ever writes it is <c>CBaseEntity::SetWaveHeight</c> from a water volume's
    /// spawn, and no water entity is compiled here — but stock networks it, the
    /// client has a getter for it, and a prop that is dropped because it is
    /// currently zero is a prop somebody has to re-derive the day a water brush
    /// comes back.
    /// </para>
    /// </summary>
    public partial float WaveHeight { get; set; }

    /// <summary>
    /// The level's minimum corner, from the map's <c>world_mins</c> key.
    ///
    /// <para>
    /// Stock sends this <c>SPROP_COORD</c> — quantised to the engine's coord
    /// encoding — and it is sent unscaled here, which is the ONE observable
    /// difference this port makes to the wire. Unscaled is the more accurate of
    /// the two, and its consumers are a material proxy and a lifetime
    /// calculation rather than anything that compares against a server value.
    /// </para>
    /// </summary>
    public partial Vector WorldMins { get; set; }

    /// <summary>The level's maximum corner, from the map's <c>world_maxs</c> key.</summary>
    public partial Vector WorldMaxs { get; set; }

    /// <summary>
    /// The map's <c>startdark</c> key: the client fades up from black on the
    /// first frame after it arrives.
    /// </summary>
    public partial bool StartDark { get; set; }

    /// <summary>
    /// The map's <c>maxoccludeearea</c>: an occludee bigger than this fraction
    /// of the screen is never occluded away. Handed straight to
    /// <c>engine->SetOcclusionParameters</c>.
    /// </summary>
    public partial float MaxOccludeeArea { get; set; }

    /// <summary>
    /// The map's <c>minoccluderarea</c>: an occluder smaller than this fraction
    /// of the screen does not occlude.
    /// </summary>
    public partial float MinOccluderArea { get; set; }

    /// <summary>
    /// The map's <c>maxpropscreenwidth</c>, the far end of the level's own prop
    /// fade range. <c>modelinfo->SetLevelScreenFadeRange</c> takes it.
    /// </summary>
    public partial float MaxPropScreenSpaceWidth { get; set; }

    /// <summary>The map's <c>minpropscreenwidth</c>, the near end of the same range.</summary>
    public partial float MinPropScreenSpaceWidth { get; set; }

    /// <summary>
    /// The map's <c>detailmaterial</c>: the material every detail sprite in the
    /// level is drawn with.
    ///
    /// <para>
    /// 256 BYTES BECAUSE THAT IS WHAT THE CLIENT COULD ALWAYS HOLD.
    /// <c>C_World::MAX_DETAIL_SPRITE_MATERIAL_NAME_LENGTH</c> is 256 and stock's
    /// <c>SendPropStringT</c> allowed 512 — <c>DT_MAX_STRING_BUFFERSIZE</c> —
    /// so anything past 256 was truncated on arrival and always had been.
    /// Declaring the buffer the client actually has makes the truncation happen
    /// where it is written down rather than where nobody looks.
    /// </para>
    ///
    /// <para>
    /// AND IT IS A PLAIN STRING, which is worth saying because <c>string_t</c>
    /// has misled this project before. <c>SendPropStringT</c> is
    /// <c>SendPropString( name, offset, 512, 0, SendProxy_StringT_To_String )</c>
    /// and that proxy is two lines: <c>pOut->m_pString = (char *)STRING( str )</c>.
    /// The pooled handle never reaches the wire; what crosses is the characters.
    /// </para>
    /// </summary>
    [NetString(256)] public partial string DetailSpriteMaterial { get; set; }

    /// <summary>
    /// The map's <c>coldworld</c>: the client's breath particle system asks it
    /// whether the player's breath should show.
    /// </summary>
    public partial bool ColdWorld { get; set; }
}

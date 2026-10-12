using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// info_lighting_relative: a pair of places, and the transform between them.
///
/// <para>
/// A mapper who has built a room in one corner of the map and copied it to
/// another has a problem: the props in the copy are lit by the lightmap of
/// wherever they now stand, which may be nothing like the original. This entity
/// is the fix. It marks a place, it names a LANDMARK — a second place — and the
/// difference between the two is a transform. A prop pointed at it with the
/// <c>lightingoriginhack</c> key is then lit as though it stood at the landmark
/// instead of where it really is.
/// </para>
///
/// <para>
/// WHY THIS IS A MARKER AND NOT A NETWORKED ENTITY, which is the whole of the
/// decision and is measured rather than assumed. Stock's
/// <c>CInfoLightingRelative</c> networks one EHandle to a client half whose only
/// job is <c>GetLightingOffset</c>, and the only caller of THAT is
/// <c>C_BaseAnimating</c>'s render setup, for an entity whose
/// <c>m_hLightingOriginRelative</c> is set. In this build no such entity can
/// exist: <c>m_hLightingOriginRelative</c> is written only by a stock server
/// <c>CBaseAnimating</c>, and neither shipping map places one — every prop,
/// item and weapon on both maps is served by the managed hierarchy. So the wire
/// half of stock's design carries a value that nothing in this build reads, and
/// the transmit-forcing in stock's <c>Activate</c> and <c>SetTransmit</c> exists
/// only to keep the landmark entity alive on the client so that client half
/// could read its transform.
/// </para>
///
/// <para>
/// WHAT THE SHIPPING MAP ACTUALLY CONTAINS, since "nothing reads it" is a claim
/// worth pinning. dm_lockdown places two of these and no other map places any:
/// </para>
///
/// <list type="bullet">
/// <item>
/// <c>wall2_lighting_rel</c> — has NO <c>LightingLandmark</c> key and is named
/// by nothing. Stock resolves its handle to null and its client half returns
/// the identity matrix.
/// </item>
/// <item>
/// <c>sink_lighting</c> — its <c>LightingLandmark</c> is <c>sink_lighting</c>,
/// which is its own targetname. A transform from a place to itself is the
/// identity matrix, so stock's client half returns the identity here too.
/// </item>
/// </list>
///
/// <para>
/// The six entities that name <c>sink_lighting</c> through
/// <c>lightingoriginhack</c> are <c>prop_physics_respawnable</c> sinks — managed
/// entities, which never had the key. So the offset both instances describe is
/// the identity, and the consumer that would apply it is not in this build. See
/// docs/divergences.md.
/// </para>
///
/// <para>
/// What is kept is everything a MAP can observe: the classname exists so the two
/// entities spawn and the entity count is unchanged, the origin is a place other
/// entities can be aimed at by name, the landmark key is read and resolved, and
/// a landmark that names nothing still warns the mapper exactly as stock did.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.InfoLightingRelative)]
public sealed class InfoLightingRelative : ScriptedEntity
{
    /// <summary>What a map may set on this entity.</summary>
    [ScriptVars] public InfoLightingRelativeVars Vars = new();

    /// <summary>
    /// The entity this one is measured against, once the map has finished
    /// loading. <see cref="EntityHandle.None"/> when no landmark was named, or
    /// when the name found nothing — both of which stock also treats as "no
    /// offset".
    ///
    /// <para>
    /// Initialised to <c>None</c> and NOT left at the default, which is the
    /// difference between "nothing" and "the world": a defaulted
    /// <see cref="EntityHandle"/> holds 0, and entity 0 is <c>worldspawn</c>.
    /// The engine's own miss value is <c>INVALID_EHANDLE_INDEX</c>, which is
    /// what <c>SourceSharp_FindEntityByName</c> returns and what this compares
    /// against.
    /// </para>
    /// </summary>
    public EntityHandle Landmark { get; private set; } = EntityHandle.None;

    /// <summary>Whether a landmark was named AND found.</summary>
    public bool HasLandmark => Landmark != EntityHandle.None;

    public override void OnSpawn()
    {
        // The landmark is another entity and at spawn the map is still being
        // read, so it may not exist yet. Deferred to the first think, which is
        // what every other class here that resolves a name does, and which is
        // where stock's Activate sat for the same reason.
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        if (Vars.LightingLandmark.Length == 0)
            return;

        Landmark = Entities.FindByName(Vars.LightingLandmark);

        // Stock's DevWarning, kept verbatim in substance: a mapper who has
        // mistyped a landmark name gets told, and gets told which entity did it.
        //
        // Compared against None rather than asked IsValid, because IsValid is a
        // question for the ENGINE about a handle that may have gone stale, and
        // this one was answered a line ago. The miss value is the contract of
        // the lookup itself.
        if (!HasLandmark)
            Log.Warning($"[SourceSharp] {ClassName}: could not find lighting " +
                        $"landmark '{Vars.LightingLandmark}'!\n");
    }
}

/// <summary>
/// The map keys <see cref="InfoLightingRelative"/> declares.
/// </summary>
[ScriptVars]
public partial class InfoLightingRelativeVars
{
    /// <summary>
    /// The targetname of the entity whose place this one stands in for. Stock
    /// spells the key exactly this way and a map file carries that spelling, so
    /// it is not renamed to something tidier.
    /// </summary>
    public string LightingLandmark { get; set; } = "";
}

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_fire_trail: a trail of flame and smoke that follows whatever it is on.
///
/// <para>
/// The cheapest of the six classes in <c>smoke_trail.cpp</c>, and the one that
/// shows the family's shape with nothing else in the way. Stock's server half is
/// a send table of two props, a datadesc of the same two, a precache of seven
/// materials, and a <c>FollowEntity</c> that only the C++ creators in
/// <c>hl2/</c> ever called. There is no <c>Spawn</c>, no input and no keyvalue
/// at all: everything a fire trail does is on the client, where
/// <c>C_FireTrail::Update</c> fills the gap between where the entity was last
/// frame and where it is now with flamelets.
/// </para>
///
/// <para>
/// It needed a client half written before the classname could move, for the
/// reason the whole particle and trail family turns on: stock's trail draws
/// because the server networks a <c>CFireTrail</c> and the engine builds the
/// <c>C_FireTrail</c> paired with <c>DT_FireTrail</c>. A managed entity is never
/// on the wire as that class — it arrives through one of the six tier tables in
/// <c>generic_nettable.h</c> — so moving the name alone would have delivered the
/// entity perfectly and drawn nothing. See
/// <see cref="Client.Standalone.ClientFireTrail"/> and
/// its sim in SourceSharp.Client.Effects (lane c2-sims).
/// </para>
///
/// <para>
/// And it is the first of the family that had to be CHECKED rather than
/// assumed: every one of these six is created in stock by a static
/// <c>CreateXxxTrail</c> that goes through <c>CreateEntityByName</c> and then
/// <c>dynamic_cast</c>s the result, which a managed entity would fail. Every one
/// of those creators is in <c>hl2/</c> or in <c>shared/hl2mp/weapon_rpg.cpp</c>,
/// and <c>sdk_csharp_release.ninja</c> says neither is handed to this build's
/// compiler; the one caller outside them,
/// <c>CBaseAnimating::HandleAnimEvent</c>, is behind <c>HL2_EPISODIC</c>, which
/// this build does not define. So nothing in this build creates one and only a
/// map can.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvFireTrail, ScriptedEntityKind.Effect)]
public sealed class EnvFireTrail : ScriptedEntity
{
    public EnvFireTrail(IEnginePrecache precache)
    {
        Precache = precache;
    }

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;

    [NetState] public FireTrailNetState State = new();

    /// <summary>
    /// Stock's <c>CFireTrail::Precache</c>, unchanged, and nothing else.
    ///
    /// Five flamelets and two particle sheets, and the client picks between them
    /// per particle. They are asked for here rather than on the client because a
    /// material a map may need has to be in the level's string table before the
    /// level runs, which is a server fact.
    ///
    /// <para>
    /// Stock has a <c>Precache</c> and no <c>Spawn</c>; here both are the one
    /// hook, because the precache is all a spawn would have done. Both networked
    /// fields keep the zero a fresh <c>CFireTrail</c> has, and zero is the
    /// meaningful value for both — no attachment, and no end to the trail.
    /// </para>
    /// </summary>
    public override void OnSpawn()
    {
        Precache.PrecacheMaterial("sprites/flamelet1");
        Precache.PrecacheMaterial("sprites/flamelet2");
        Precache.PrecacheMaterial("sprites/flamelet3");
        Precache.PrecacheMaterial("sprites/flamelet4");
        Precache.PrecacheMaterial("sprites/flamelet5");
        Precache.PrecacheMaterial("particle/particle_smokegrenade");
        Precache.PrecacheMaterial("particle/particle_noisesphere");
    }
}

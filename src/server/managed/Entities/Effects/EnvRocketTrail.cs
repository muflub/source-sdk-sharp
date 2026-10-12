using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_rockettrail: smoke with a flare at the nozzle.
///
/// <para>
/// The third of the smoke-trail shape and the one with NO map keys at all.
/// <c>RocketTrail</c>'s datadesc is fifteen <c>DEFINE_FIELD</c>s and not one
/// <c>DEFINE_KEYFIELD</c>, and unlike its two twins it has no <c>KeyValue</c>
/// override either. So a map can place one and change nothing about it, and
/// every value it networks is whatever <c>RocketTrail::RocketTrail</c> set.
/// </para>
///
/// <para>
/// Which makes the constructor the entire entity, and makes this class a list
/// of stock's numbers rather than a vars class. There is deliberately no
/// <c>[ScriptVars]</c> here: declaring keys the entity does not have would put
/// them in the inventory and invite a mapper to write one that does nothing.
/// </para>
///
/// <para>
/// It differs from <see cref="EnvSmokeTrail"/> by two fields in each direction:
/// no directed speeds, and a flare scale and a damaged flag instead. Only a C++
/// caller ever set the last two — <c>weapon_rpg.cpp</c> raises Damaged when the
/// missile is shot down — and none of those callers is compiled here.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvRockettrail, ScriptedEntityKind.Effect)]
public sealed class EnvRocketTrail : ScriptedEntity
{
    [NetState] public RocketTrailNetState State = new();

    /// <summary>
    /// Stock's constructor, written out, because there is nothing else.
    ///
    /// <para>
    /// A managed field starts at zero and stock's starts at these, and the
    /// difference is not cosmetic: a trail with a spawn rate of zero emits
    /// nothing while reporting every field correctly through
    /// <c>debug-api payload</c>. That is the silent failure this method exists to
    /// prevent.
    /// </para>
    /// </summary>
    public override void OnSpawn()
    {
        State.SpawnRate        = 10f;
        State.StartColor       = new(0.5f, 0.5f, 0.5f);
        State.EndColor         = new(0f, 0f, 0f);
        State.Opacity          = 0.5f;
        State.ParticleLifetime = 5f;
        State.MinSpeed         = 2f;
        State.MaxSpeed         = 4f;
        State.StartSize        = 35f;
        State.EndSize          = 55f;
        State.SpawnRadius      = 2f;
        State.FlareScale       = 1.5f;
        State.Emit             = true;

        // Zero rather than never-stop is what stock's constructor leaves, and
        // zero IS never-stop in this field. Written out because a reader
        // otherwise has to know that to know the trail does not die at once.
        State.StopEmitTime = 0f;

        // MINUS ONE, which is C_RocketTrail's own default and not the zero its
        // three neighbours use. Both mean "no attachment" — GetAimEntOrigin
        // only consults one when the index is above zero — and this is stock's
        // value rather than a tidied one.
        State.Attachment = -1;

        // Only weapon_rpg.cpp ever raised this, when a missile was shot down,
        // and that file is not compiled here.
        State.Damaged = false;
    }
}

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_sporetrail: a thin trail of embers, meant to hang off a moving model.
///
/// <para>
/// Like <see cref="EnvRocketTrail"/> it has no map keys — <c>SporeTrail</c>'s
/// datadesc is seven <c>DEFINE_FIELD</c>s and no <c>KeyValue</c> override — so
/// the constructor is the whole entity and this class is stock's numbers
/// written out.
/// </para>
///
/// <para>
/// AND IT IS INERT WHEN A MAP PLACES ONE, in stock as much as here. Two
/// independent reasons, both stock's:
/// </para>
///
/// <para>
/// The first is <c>m_bEmit</c>. <c>SporeTrail::SporeTrail</c> sets six fields
/// and does not touch that one; entity memory arrives zeroed, so it networks
/// false, and <c>C_SporeTrail</c> draws and simulates nothing while it is. The
/// class has no key, no input and no <c>Spawn</c>, so nothing a map or a player
/// can do will raise it — only <c>CreateSporeTrail</c> followed by a C++ caller
/// writing the member, and every one of those callers is in <c>hl2/</c>.
/// (Compare <c>SporeExplosion</c>, whose constructor DOES set it and whose
/// <c>Spawn</c> then clears it, which reads as deliberate where this reads as
/// forgotten.)
/// </para>
///
/// <para>
/// The second is where it would draw if it did. <c>C_SporeTrail::StartRender</c>
/// multiplies the effect matrix by <c>m_mAttachmentMatrix</c>, and nothing fills
/// that but <c>GetAimEntOrigin</c> — reached only for an entity with a move
/// parent. A stock env_sporetrail with no parent draws through an
/// uninitialised matrix. That one IS repaired here: the client entity
/// (<see cref="Client.Standalone.ClientSporeTrail"/>) builds the matrix from the entity's own place each frame, so
/// a standalone one would draw where it stands. See docs/divergences.md.
/// </para>
///
/// <para>
/// Porting it costs nothing that was working, because nothing in this build
/// creates one: <c>CreateSporeTrail</c>'s callers are all in <c>hl2/</c>, which
/// <c>sdk_csharp_release.ninja</c> shows the compiler is not handed.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvSporetrail, ScriptedEntityKind.Effect)]
public sealed class EnvSporeTrail : ScriptedEntity
{
    [NetState] public SporeTrailNetState State = new();

    /// <summary>Stock's constructor, written out, because there is nothing else.</summary>
    public override void OnSpawn()
    {
        State.EndColor         = new(0f, 0f, 0f);
        State.SpawnRate        = 100f;
        State.ParticleLifetime = 1f;
        State.StartSize        = 1f;
        State.EndSize          = 0f;
        State.SpawnRadius      = 16f;

        // FALSE, and written out rather than left to the field's own zero,
        // because it is the one value here that decides whether anything is
        // drawn and a reader should not have to guess that it was meant. Stock
        // leaves it false too — see the class remarks.
        State.Emit = false;
    }
}

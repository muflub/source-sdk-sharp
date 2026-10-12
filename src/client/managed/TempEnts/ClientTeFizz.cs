//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>CTEFizz</c>, played. Bubbles rising off a submerged brush entity — the
/// fizz on a body of water, or off something dropped into one.
///
/// <para>
/// The whole of <c>c_te_fizz.cpp</c>'s <c>C_TEFizz::PostDataUpdate</c>:
/// </para>
/// <code>
/// C_BaseEntity *pEnt = cl_entitylist-&gt;GetEnt( m_nEntity );
/// if (pEnt != NULL)
/// {
///     tempents-&gt;FizzEffect( pEnt, m_nModelIndex, m_nDensity, m_nCurrent );
/// }
/// </code>
///
/// <para>
/// <b>THIS IS BEHAVIOUR LEDGER ROW C6, AND IT IS CLOSED HERE.</b> The row pairs
/// with S1: lane D restored the server's <c>Fizz</c> and it has sent correctly
/// ever since, and every one of them was decoded and dropped on arrival — first
/// silently, at stock's <c>pEnt != NULL</c> test, because
/// <c>cl_entitylist-&gt;GetEnt</c> answers a <c>C_BaseEntity *</c> and therefore
/// NULL for everything on the parallel hierarchy; then, after C-Phase 5 ported
/// the class, out loud once per level. It now fizzes.
/// </para>
///
/// <para>
/// <b>WHAT IT TOOK, because the row was bigger than its own wording.</b>
/// "Resolve the index through the managed entity list" was necessary and NOT
/// sufficient. <c>CTempEnts::FizzEffect</c> reads TWO things off the entity:
/// </para>
/// <list type="number">
/// <item><description>
/// <c>modelinfo-&gt;GetModelBounds( pent-&gt;GetModel(), mins, maxs )</c> — the
/// bounds of the entity's own MODEL, which is the volume the bubbles are spread
/// across. <b>Not the collision bounds.</b>
/// <see cref="IEngineEntities.GetBounds"/> answers the collideable's box, and on
/// <c>C_StandaloneEntity</c> that box is a symmetric ±16 cube fixed in the
/// constructor and never networked, so substituting it would rise every fizz
/// out of a 32-unit cube and look like a working effect.
/// </description></item>
/// <item><description>
/// <c>pent-&gt;GetLocalAngles()[1]</c> — the entity's YAW, which becomes the
/// horizontal drift the "current" carries the bubbles along.
/// </description></item>
/// </list>
/// <para>
/// Both come back from one slot,
/// <see cref="IEngineWorldQuery.ClientEntityModelBounds"/>, which resolves
/// through <c>cl_entitylist-&gt;GetClientEntity</c> — an <c>IClientEntity</c>
/// for BOTH hierarchies, where stock's <c>GetEnt</c> answered for one.
/// </para>
///
/// <para>
/// <b>THE MANAGED REGISTRY WAS CONSIDERED AND IS NOT WHAT THIS USES.</b>
/// <c>ClientStandaloneRegistry.GetByIndex</c> answers for the standalone
/// hierarchy only, so a fizz aimed at an SDK client entity would have got a
/// zero box and a zero yaw with nothing said — stock's own failure with the
/// hierarchies swapped. The native resolve has no such half.
/// </para>
///
/// <para>
/// <b>WHAT IS STILL AVOIDED, and it is not this class.</b> The row's title
/// names <c>SourceSharp_BubbleEffect</c>, the server-side substitution:
/// <c>EnvBubbles</c> sends <c>te-&gt;Bubbles</c> with the corners on the wire
/// because a fizz used to arrive at nothing. That substitution is now
/// UNBLOCKED and has not been flipped here — flipping it is a live effect
/// changing which temp entity it sends, and this lane cannot join a game to
/// confirm the first burst lands in the water. It is named in
/// <c>docs/te-ledger.md</c> and pinned by
/// <c>TempEntFizzTests.TheServerSideSubstitutionIsStillNamedAndStillTracked</c>
/// so it cannot go quiet.
/// </para>
/// </summary>
[ClientTempEnt("CTEFizz")]
public sealed class ClientTeFizz : ClientTempEnt<TeFizz>, IClientTempEntLevel
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly TempEntSpawns _spawns;
    private readonly IEngineWorldQuery _world;

    public ClientTeFizz(ILog log, TempEntSpawns spawns, IEngineWorldQuery world)
    {

        _log = log;
        _spawns = spawns;
        _world = world;
    }

    private bool _said;

    public void LevelInit()
    {
        _said = false;
    }

    public override void Play(TeFizz te)
    {
        // STOCK'S `pEnt != NULL`, AND ITS `!pent->GetModel()`, IN ONE ANSWER.
        // They are two guards in two functions in stock -- PostDataUpdate's and
        // FizzEffect's first line -- and both mean the same thing here: there
        // is no entity to measure the column against.
        if (!_world.ClientEntityModelBounds(te.Entity, out Vector mins, out Vector maxs,
                                            out Vector angles))
        {
            NothingToFizzOn(te.Entity);
            return;
        }

        // [1] is stock's own index into the QAngle: PITCH, YAW, ROLL, so the
        // yaw. Written as .Y rather than as an index for the same reason
        // everything else here names its field.
        _spawns.FizzEffect(mins, maxs, angles.Y, te.ModelIndex, te.Density, te.Current);
    }

    /// <summary>
    /// The index named no entity, or named one with no model.
    ///
    /// <para>
    /// STOCK IS SILENT HERE AND THIS IS NOT, and the difference is deliberate
    /// rather than an oversight of the port's own rule about not adding noise.
    /// For four hundred commits this exact silence WAS behaviour ledger row C6
    /// — every fizz in the build arrived, found nothing and emitted nothing,
    /// and nothing anywhere said so. Saying it once per level is what stops the
    /// row from being able to come back unnoticed if a later change breaks the
    /// resolve again.
    /// </para>
    ///
    /// <para>
    /// ONCE PER LEVEL, not once per event: a fizzing volume sends these
    /// continuously and a per-event line would bury itself.
    /// </para>
    /// </summary>
    private void NothingToFizzOn(int entity)
    {
        if (_said)
            return;

        _said = true;

        _log.Warning(
            "[SourceSharp/Client] CTEFizz arrived for entity " + entity + ", which this "
          + "client cannot measure a bubble column against — no such entity, or one with "
          + "no model. Stock returns in silence here (c_te_fizz.cpp's pEnt != NULL, and "
          + "CTempEnts::FizzEffect's !pent->GetModel()); this says it because that same "
          + "silence WAS behaviour ledger row C6, which is now closed. Said once per "
          + "level.\n");
    }
}

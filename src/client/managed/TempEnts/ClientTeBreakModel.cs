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
/// <c>CTEBreakModel</c>, played. A box of gibs: a crate, a barrel or a pane
/// coming apart into pieces that tumble, bounce, fade and share one lighting
/// origin.
///
/// <para>
/// The whole of <c>c_te_breakmodel.cpp</c>'s
/// <c>C_TEBreakModel::PostDataUpdate</c>:
/// </para>
/// <code>
/// tempents-&gt;BreakModel( m_vecOrigin, m_angRotation, m_vecSize, m_vecVelocity,
///                      m_nRandomization, m_fTime, m_nCount, m_nModelIndex, m_nFlags );
/// </code>
///
/// <para>
/// <b>THE ARGUMENT ORDER IS NOT THE TABLE ORDER, AND THE MISMATCH IS EXACTLY
/// THE KIND THAT COMPILES.</b> The send table declares model index, then
/// randomization, then count; the helper takes randomization, time, count,
/// model index. Two of those four are ints in adjacent positions, so a
/// transposition would build cleanly and produce a plausible number of gibs of
/// the wrong model — or, the other way, the right model in a count taken from a
/// spread. Every argument to the call below is named for that reason.
/// </para>
///
/// <para>
/// <b>NOTHING SENDS THIS TEMP ENTITY IN THIS BUILD</b>, and saying so is worth
/// more than pretending otherwise. <c>ITempEnts.BreakModel</c> has no caller in
/// <c>SourceSharp.Server</c> — the survey measured it — so
/// <c>TE_BreakModel</c> is never on the wire, and the pool has been at zero
/// allocations for the whole of this phase. That is a fact about the SENDER and
/// not about this class: the table pairs, the blob decodes, and the moment
/// something sends one the gibs appear. <c>cs_cl_te_break</c> beside the
/// forward in <c>shell_te_fwd_pool2.cpp</c> is what drives it in the meantime,
/// through the managed writer and this player rather than at the helper.
/// </para>
///
/// <para>
/// NO LEDGER ROW. The lighting group and the draw helper both still run —
/// <c>CBreakableHelper</c> is native and reached through
/// <see cref="TempEntSpawn.BreakHelper"/>, because its only reader is a
/// <c>Vector *</c> the renderer holds and a managed list cannot supply one.
/// </para>
/// </summary>
[ClientTempEnt("CTEBreakModel")]
public sealed class ClientTeBreakModel : ClientTempEnt<TeBreakModel>
{
    private readonly TempEntSpawns _spawns;

    public ClientTeBreakModel(TempEntSpawns spawns)
    {
        _spawns = spawns;
    }

    public override void Play(TeBreakModel te)
    {
        _spawns.BreakModel(
            pos: te.Origin,
            angles: te.Rotation,
            size: te.Size,
            dir: te.Velocity,

            // Stock's parameter is called `random` and the prop is called
            // m_nRandomization; it is how far each gib's velocity may stray
            // from the shared direction, in units per second, and it is applied
            // symmetrically in X and Y and one-sided upward in Z.
            randRange: te.Randomization,

            // m_fTime is a LIFETIME, not a timestamp. The helper adds it to
            // now and then adds a further 0-1 seconds so a crate's gibs do not
            // all vanish on one frame.
            life: te.Time,

            count: te.Count,
            modelIndex: te.ModelIndex,
            flags: te.Flags);
    }
}

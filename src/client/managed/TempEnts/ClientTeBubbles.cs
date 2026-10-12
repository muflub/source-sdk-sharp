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
/// <c>CTEBubbles</c>, played. A box fills with bubbles that wobble upward and
/// pop at a stated height above its floor.
///
/// <para>
/// The whole of <c>c_te_bubbles.cpp</c>'s <c>C_TEBubbles::PostDataUpdate</c>:
/// </para>
/// <code>
/// tempents-&gt;Bubbles( m_vecMins, m_vecMaxs, m_fHeight, m_nModelIndex, m_nCount, m_fSpeed );
/// </code>
///
/// <para>
/// <b><c>m_fHeight</c> IS NOT A Z COORDINATE</b>, and this is the one thing about
/// this effect that is easy to get wrong in a way nothing would catch. The
/// helper computes each bubble's lifetime as
/// <c>(height - (origin.z - mins.z)) / zspeed</c> — a height RELATIVE to the
/// box's floor, so a bubble spawned near the top of the box has less of it left
/// to travel. Reading it as a world Z would give bubbles in a low box lifetimes
/// of several minutes and bubbles in a high one negative ones, and neither
/// looks like a bug in the receiving code. Its sibling
/// <see cref="ClientTeBubbleTrail"/> carries an ABSOLUTE water height in the
/// same position, which is what makes the confusion available.
/// </para>
///
/// <para>
/// NO LEDGER ROW. Nothing was guarded out: the native class is evicted whole and
/// the same helper runs, in C#.
/// </para>
/// </summary>
[ClientTempEnt("CTEBubbles")]
public sealed class ClientTeBubbles : ClientTempEnt<TeBubbles>
{
    private readonly TempEntSpawns _spawns;

    public ClientTeBubbles(TempEntSpawns spawns)
    {
        _spawns = spawns;
    }

    public override void Play(TeBubbles te)
    {
        _spawns.Bubbles(te.Mins, te.Maxs, te.Height, te.ModelIndex, te.Count, te.Speed);
    }
}

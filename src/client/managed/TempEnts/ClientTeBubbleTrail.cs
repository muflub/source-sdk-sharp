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
/// <c>CTEBubbleTrail</c>, played. Bubbles scattered along a line, rising to a
/// water surface.
///
/// <para>
/// The whole of <c>c_te_bubbletrail.cpp</c>'s
/// <c>C_TEBubbleTrail::PostDataUpdate</c>:
/// </para>
/// <code>
/// tempents-&gt;BubbleTrail( m_vecMins, m_vecMaxs, m_flWaterZ, m_nModelIndex, m_nCount, m_fSpeed );
/// </code>
///
/// <para>
/// <b>THE TWO VECTORS ARE A SEGMENT, NOT A BOX,</b> and only their NAMES say
/// otherwise. The send table calls them <c>m_vecMins</c> and <c>m_vecMaxs</c>
/// — the same names <see cref="ClientTeBubbles"/> uses for a real bounding box
/// — and the helper's own parameters are called <c>start</c> and <c>end</c> and
/// are handed to a lerp. A reader who trusts the wire names here will look for
/// a box that is not there. The names are kept because they are what the server
/// sends and what stock's table declares; three spellings of one prop is how a
/// pairing fails.
/// </para>
///
/// <para>
/// <c>m_flWaterZ</c> IS an absolute world height, unlike its sibling's
/// <c>m_fHeight</c>. See <see cref="ClientTeBubbles"/>.
/// </para>
///
/// <para>
/// NO LEDGER ROW.
/// </para>
/// </summary>
[ClientTempEnt("CTEBubbleTrail")]
public sealed class ClientTeBubbleTrail : ClientTempEnt<TeBubbleTrail>
{
    private readonly TempEntSpawns _spawns;

    public ClientTeBubbleTrail(TempEntSpawns spawns)
    {
        _spawns = spawns;
    }

    public override void Play(TeBubbleTrail te)
    {
        // start and end. See the class note on why they are named for a box.

        _spawns.BubbleTrail(te.Mins, te.Maxs, te.WaterZ, te.ModelIndex, te.Count, te.Speed);
    }
}

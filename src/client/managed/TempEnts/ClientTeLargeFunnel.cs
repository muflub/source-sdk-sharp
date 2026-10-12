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
/// <c>CTELargeFunnel</c>, played — 484 flare particles on a 24-unit grid, either
/// falling in towards the origin or blowing up out of it.
///
/// <para>
/// <c>c_te_largefunnel.cpp</c>'s <c>PostDataUpdate</c> is one call to
/// <c>CreateFunnel</c>, so the whole of the file is either the decode (here) or
/// the emitter (<see cref="Effects.LargeFunnel"/>, lane c5-particles).
/// </para>
///
/// <para>
/// <c>m_nModelIndex</c> IS DECODED AND NOT USED, and that is stock's own
/// behaviour rather than an omission here. <c>C_TELargeFunnel</c> stores it and
/// draws with <c>sprites/flare6</c> regardless; the prop stays on the wire
/// because the server's table is stock's and 2E is explicit that this is not a
/// place to tidy up. It is read here so that a reader of this class sees the
/// same fields the table carries, and so that the read is exercised — a field
/// nothing ever reads is a field whose offset nothing ever checks.
/// </para>
///
/// <para>
/// <c>m_nReversed</c> IS TRUTH-TESTED, NOT COMPARED WITH 1. Stock writes
/// <c>if ( m_nReversed )</c> and the prop is two bits wide, so 2 and 3 are
/// reversed funnels too. The value is passed down as it arrived and the backend
/// does the same test.
/// </para>
/// </summary>
[ClientTempEnt("CTELargeFunnel")]
public sealed class ClientTeLargeFunnel : ClientTempEnt<TeLargeFunnel>
{
    private readonly Effects.ILargeFunnel _funnel;

    public ClientTeLargeFunnel(Effects.ILargeFunnel funnel)
    {
        _funnel = funnel;
    }

    public override void Play(TeLargeFunnel te)
    {
        // Read and deliberately unused; see the note above. Kept as a local
        // rather than discarded at the call site so the reason has somewhere to
        // live.
        _ = te.ModelIndex;

        _funnel.Funnel(te.Origin, te.Reversed);
    }
}

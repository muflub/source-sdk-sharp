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
/// <c>CTEImpact</c>, played. <b>STOCK DRAWS NOTHING HERE</b>, and this class
/// exists to say that on the record rather than to look like an effect the port
/// lost — the same shape, and the same reason, as
/// <see cref="ClientTeBeamSpline"/>.
///
/// <para>
/// The whole of <c>c_te_impact.cpp</c>'s <c>C_TEImpact::PostDataUpdate</c> —
/// not an excerpt, the entire body:
/// </para>
/// <code>
/// VPROF( "C_TEImpact::PostDataUpdate" );
/// </code>
///
/// <para>
/// And the rest of that file is the same: <c>Precache()</c> is a
/// <c>//TODO: Precache all materials/sounds used by impacts here</c> and
/// nothing, <c>PlayImpactSound( trace_t &amp; )</c> is empty, and
/// <c>PerformCustomEffects( trace_t &amp;, Vector &amp; )</c> is empty. There is
/// no <c>FX_</c> call, no <c>g_pEffects</c> call and no
/// <c>DispatchEffect</c> anywhere in it. Two of those three empty methods are
/// <c>virtual</c> with no override and no caller in this build.
/// </para>
///
/// <para>
/// <b>SO THE PORT DOES NOT INVENT ONE.</b> An impact at a point with a normal
/// and a surface type is exactly the argument list <c>FX_Impact</c> wants, and
/// wiring it would look like finishing the job. It would be this port adding
/// behaviour stock does not have, which is the mirror image of closing a
/// behaviour-ledger row by deleting the call that exposed it. Neither is
/// allowed.
/// </para>
///
/// <para>
/// <b>THIS IS NOT LEDGER ROW C7, AND CONFLATING THE TWO IS EASY.</b> C7 was
/// <c>DispatchEffect("Impact")</c> — a NAME on <c>CTEEffectDispatch</c>'s
/// string table, whose client callback existed only in <c>tf/</c>, <c>hl2/</c>
/// and <c>sdk/</c> and therefore never fired here
/// (<see cref="ClientTeEffectDispatch"/>); lane <c>fx-impact</c> registered one
/// in <c>shell_impact_effects.cpp</c> and closed it. This is <c>CTEImpact</c>,
/// a table of its own, whose client half has been empty in stock since it was
/// written and is empty in Valve's tree today. One of them was a build's worth
/// of missing registrations; the other is a class Valve left unfinished — and
/// <b>C7 closing does not close this</b>, which is exactly the confusion this
/// paragraph exists to prevent.
/// </para>
///
/// <para>
/// <b>ROW S2's OTHER HALF.</b> The server side of <c>DT_TEImpact</c> was
/// restored by lane B — <c>te_impact.cpp</c> is excluded from this build, so
/// <c>CTEImpact</c> had no sender at all while <c>C_TEImpact</c> stayed
/// registered on this side. Registering the managed client half is what makes
/// the pairing REAL in both directions; the row was already marked restored and
/// this does not change its state. See <see cref="Shared.TeImpact"/>.
/// </para>
///
/// <para>
/// <b><c>m_ucFlags</c> IS NOT READ, AND CANNOT BE.</b> Stock's RECV table
/// declares a fourth prop the SEND table has never had, so
/// <c>C_TEImpact::m_ucFlags</c> keeps its constructor's zero forever — the
/// engine matches props by name and one with no sender is never written. The
/// managed declaration reproduces the send table, which has three props, so
/// there is no <c>m_ucFlags</c> field in the blob to read. Asking for one would
/// produce the reader's "has no field" warning on every impact.
/// </para>
///
/// <para>
/// The decode is still REAL, and that is the point of porting a class that
/// draws nothing: the three props are read, kept, and counted, so
/// <c>cs_cl_te_report</c> shows the class as played with the numbers it carried
/// rather than as <c>NO PLAYER</c> — which is what a lane that never landed
/// looks like from the console.
/// </para>
/// </summary>
[ClientTempEnt("CTEImpact")]
public sealed class ClientTeImpact : ClientTempEnt<TeImpact>
{
    /// <summary>Where the last impact landed.</summary>
    public Vector Origin { get; private set; }

    /// <summary>The surface normal it carried. A COORD vector, not a normal.</summary>
    public Vector Normal { get; private set; }

    /// <summary>
    /// <c>m_iType</c> — the surface-property index the client would turn into an
    /// effect, if this build's client did anything with it.
    /// </summary>
    public int Type { get; private set; }

    /// <summary>How many impacts have arrived. Stock counts none, and draws none.</summary>
    public int Received { get; private set; }

    public override void Play(TeImpact te)
    {
        Origin = te.Origin;
        Normal = te.Normal;

        // UNSIGNED: thirty-two bits with SPROP_UNSIGNED, stock's own width.
        Type = te.Type;

        Received++;

        // AND NOTHING ELSE, which is stock's whole PostDataUpdate. There is not
        // even a DevMsg here to reproduce -- CTEBeamSpline at least had one.
    }
}

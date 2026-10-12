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
/// <c>CTEBeamSpline</c>, played. <b>STOCK DRAWS NOTHING HERE, AND THIS CLASS
/// EXISTS TO SAY SO RATHER THAN TO LOOK LIKE A CLASS THE PORT LOST.</b>
///
/// <para>
/// The whole of <c>c_te_beamspline.cpp</c>'s
/// <c>C_TEBeamSpline::PostDataUpdate</c> — not an excerpt, the whole body:
/// </para>
/// <code>
/// VPROF( "C_TEBeamSpline::PostDataUpdate" );
/// DevMsg( 1, "Beam spline with %i points received\n", m_nPoints );
/// </code>
///
/// <para>
/// There is no <c>beams->CreateBeam*</c> call, no particle spawn and no
/// <c>g_pEffects</c> call anywhere in that file. Its sibling
/// <c>TE_BeamSpline</c> — the client MAKING one for itself rather than
/// receiving one — is the same shape: <c>DevMsg( 1, "Beam spline with %i
/// points invoked\n", points )</c> and nothing else. Sixteen networked
/// coordinates arrive and Valve prints a line. That is the effect.
/// </para>
///
/// <para>
/// <b>SO THE PORT DOES NOT INVENT A DRAWING PATH.</b> Sixteen COORD points
/// through <c>beams->CreateBeamPoints</c> would look plausible, would be a
/// visible improvement, and would be this port doing something stock does not
/// — which is the same class of error as closing a behaviour-ledger row by
/// deleting the call that exposed it, with the sign flipped. The rule both
/// directions share is that this port reproduces stock's behaviour and records
/// where it cannot, and neither half of that licenses adding behaviour.
/// </para>
///
/// <para>
/// <b>AND THE PORT DOES NOT DROP THE CLASS EITHER</b>, which is the failure a
/// reader should be able to rule out at a glance. <c>CTEBeamSpline</c> is one
/// of the forty-four. It registers, it pairs, it receives, its blob decodes,
/// and <see cref="ClientTempEnts.Played"/> counts it — so
/// <c>cs_cl_te_report</c> shows it as "played by C#" with the sixteen points
/// read back, exactly like a class that draws. Without a managed player it
/// would instead print "NO PLAYER" and count as DROPPED, which is the state
/// this file removes and which is indistinguishable from a lane that never
/// arrived. <b>An effect that does nothing and an effect nobody wrote look the
/// same from the console; they must not look the same in the code.</b>
/// </para>
///
/// <para>
/// <b>THE DECODE IS REAL AND IS THE POINT OF PORTING IT.</b>
/// <c>DT_TEBeamSpline</c> is the only array in all forty-four tables (plan 3.3)
/// and the widest blob in the inventory — 139 of the 192 bytes
/// the engine's per-event data cap allows — so it is the one class that
/// exercises <see cref="TempEntReader.VectorAt"/>, the element stride, and the
/// two-props-one-name shape <c>SendPropArray</c> expands to. All sixteen
/// elements are read, because with a NULL length proxy all sixteen are SENT
/// whatever <c>m_nPoints</c> says; reading only <c>m_nPoints</c> of them would
/// leave the far end of the array unchecked, which is precisely where a stride
/// error hides. <see cref="Points"/> and <see cref="PointCount"/> keep what
/// arrived, so a test — and <c>cs_cl_te_report</c> — can assert the numbers
/// rather than trust that something happened.
/// </para>
///
/// <para>
/// <c>NOBASE</c>, so it nests nothing and collides with nothing: this class
/// could move on its own the day its file was evicted (plan 2E-bis).
/// </para>
/// </summary>
[ClientTempEnt("CTEBeamSpline")]
public sealed class ClientTeBeamSpline : ClientTempEnt<TeBeamSpline>
{
    /// <summary>
    /// <c>MAX_SPLINE_POINTS</c>. Taken from the shared declaration rather than
    /// restated, because the declaration is the one place the sixteen is
    /// allowed to live on this side — see <see cref="TeBeamSpline"/> for why it
    /// is a literal there and a symbol here.
    /// </summary>
    public const int MaxSplinePoints = TeBeamSpline.MaxSplinePoints;

    private readonly IEngineConsole _console;
    private readonly Vector[] _points = new Vector[MaxSplinePoints];

    public ClientTeBeamSpline(IEngineConsole console)
    {
        _console = console;
    }

    /// <summary>
    /// <c>m_nPoints</c> from the last spline that arrived — how many of the
    /// sixteen the sender meant. Five bits unsigned, so 0..31: one bit wider
    /// than sixteen points need, which is stock's number and stays stock's.
    /// </summary>
    public int PointCount { get; private set; }

    /// <summary>
    /// The last spline's sixteen points, ALL of them, in wire order. See the
    /// class note: the array has no length proxy, so all sixteen cross the wire
    /// on every send and a reader that stopped at <see cref="PointCount"/>
    /// would never look at the elements a stride error corrupts.
    /// </summary>
    public ReadOnlySpan<Vector> Points => _points;

    /// <summary>How many splines this client has received. Stock counts none.</summary>
    public int Received { get; private set; }

    public override void Play(TeBeamSpline te)
    {
        // THE COUNT AND ALL SIXTEEN POINTS IN ONE READ. m_nPoints is five bits
        // UNSIGNED and the run is a DPT_Array at a stride of twelve; both facts
        // are the declaration's, and TempEntDeclarationReader is the one place
        // that reads an [InlineArray] element by element rather than stopping at
        // element zero, which is what reflection does if nothing makes it walk.

        PointCount = te.Points;

        for (int i = 0; i < MaxSplinePoints; i++)
            _points[i] = te.Spline[i];

        Received++;

        // AND THEN STOCK'S ONE LINE, spelt exactly as stock spells it. DevMsg's
        // first argument is a developer LEVEL, and the managed side has no
        // DevMsg -- so the level is the condition, read from the engine's own
        // `developer` convar rather than reinvented as a mod convar of ours. At
        // developer 0 this class is silent, which is stock's behaviour on the
        // console it is compared against.
        if (DeveloperLevel() >= 1)
            _console.Msg($"Beam spline with {PointCount} points received\n");
    }

    /// <summary>
    /// The engine's <c>developer</c> convar as a number, or zero if it cannot be
    /// read. Zero is the right failure: it means "say nothing", and a class
    /// whose entire behaviour is a diagnostic must not become louder than stock
    /// because a lookup failed.
    /// </summary>
    private int DeveloperLevel()
    {
        string? value = _console.FindConVarValue("developer");

        return int.TryParse(value, out int level) ? level : 0;
    }
}

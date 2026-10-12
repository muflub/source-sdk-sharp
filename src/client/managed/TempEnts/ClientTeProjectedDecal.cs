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
/// <c>CTEProjectedDecal</c>, played. A decal thrown along a facing rather than
/// stamped at a point — which is what an <c>info_projecteddecal</c> is.
///
/// <para>
/// The whole of <c>c_te_projecteddecal.cpp</c>'s <c>TE_ProjectDecal</c>:
/// </para>
/// <code>
/// trace_t tr;
/// Vector fwd;
/// AngleVectors( *angles, &amp;fwd );
/// Vector endpos;
/// VectorMA( *pos, distance, fwd, endpos );
/// CTraceFilterHitAll traceFilter;
/// UTIL_TraceLine( *pos, endpos, MASK_ALL, &amp;traceFilter, &amp;tr );
/// if ( tr.fraction == 1.0f )
///     return;
/// C_BaseEntity* ent = TraceEntity( tr );
/// Assert( ent );
/// int hitbox = tr.hitbox;
/// if ( tr.hitbox != 0 )
///     staticpropmgr-&gt;AddDecalToStaticProp( *pos, endpos, hitbox - 1, index, false, tr );
/// else
///     ent-&gt;AddDecal( *pos, endpos, endpos, hitbox, index, false, tr );  // WAS LEDGER ROW C3
/// </code>
///
/// <para>
/// BEHAVIOUR LEDGER ROW C3 IS CLOSED HERE, and it was CLOSED rather than
/// dropped: the <c>else</c> still runs, on the same arguments, and now ends in
/// a decal. Stock's own comment on that branch is <i>"Only decal the world +
/// brush models"</i>, which is why the row was bigger than C2's and must never
/// have been sized from it — an <c>info_projecteddecal</c> aimed at ordinary
/// map geometry lands THERE, with a hitbox of zero and an entity index of zero,
/// and the world is a brush model. <see cref="ClientEntityDecal.AddDecal"/> is
/// the managed <c>AddDecal</c> that has both arms.
/// </para>
///
/// <para>
/// THE CLASS THE CLIENT TRACE WAS ADDED FOR. Everything above the branch is a
/// decision, and the decision is made from <c>tr.hitbox</c> and from nothing
/// else — so a managed player without a trace has no way to know which arm it
/// is in. Before lane CX the client had no trace at all: the ABI's
/// <c>TraceLine</c> is wired inside <c>#ifndef CLIENT_DLL</c>, and its outs
/// carry no hitbox even on the side that has it.
/// <see cref="IEngineWorldQuery.ClientTraceLine"/> is the slot that answers
/// both.
/// </para>
///
/// <para>
/// A NARROW SLOT THAT TRACED AND DECALLED IN ONE NATIVE CALL WOULD HAVE BEEN
/// SHORTER AND WRONG. It would have put the <c>fraction == 1</c> early return
/// and the arm selection back into C++ — the decode and the decision this whole
/// port exists to move — and it would have carried ledger row C3's dropped arm
/// into SourceSharp's own code, where no <c>#ifndef</c> marks it and no scanner
/// finds it. The general trace pays for itself the first time anything else on
/// this client wants to know what a line ran into.
/// </para>
///
/// <para>
/// <c>MASK_ALL</c> AND NOT MASK_SHOT. Stock traces everything, and a decal
/// projected through a narrower mask lands on a different surface — or on
/// nothing, and returns. <see cref="TraceMask.All"/> exists for this call.
/// </para>
///
/// <para>
/// <c>TraceEntity( tr )</c> and its <c>Assert</c> did not come across, and did
/// not need to: the only use of that pointer is the <c>AddDecal</c> in the arm
/// below, and the trace's entity INDEX reaches it. An index rather than a
/// handle because reading a client handle back through the engine's table is
/// ledger row C8 — and MORE than the native arm could manage, which is why
/// stock's <c>Assert( ent )</c> is not ported as one: <c>TraceEntity</c>
/// answers NULL off the SDK hierarchy in this build, so the assert would fire
/// on a trace that had hit something real.
/// </para>
///
/// <para>
/// WHAT WAS LOST WITH THE FILE: <c>RecordProjectDecal</c>, the tool-recording
/// KeyValues message, <c>ToolsEnabled()</c>-gated and unreachable in this
/// build. docs/divergences.md has it. Stock's <c>Precache()</c> was empty.
/// </para>
/// </summary>
[ClientTempEnt("CTEProjectedDecal")]
public sealed class ClientTeProjectedDecal : ClientTempEnt<TeProjectedDecal>
{
    private readonly IEngineEffects _effects;
    private readonly IEngineWorldQuery _world;

    public ClientTeProjectedDecal(IEngineEffects effects, IEngineWorldQuery world)
    {
        _effects = effects;
        _world = world;
    }

    public override void Play(TeProjectedDecal te)
    {
        // A QAngles prop, which is three floats of blob like a vector and a
        // different ENCODING on the wire — see TeProjectedDecal for why that
        // distinction is the one the whole table turns on.

        // AngleVectors then VectorMA, which is stock's two lines. Angles.Vectors
        // is this build's port of AngleVectors and is arithmetic with no engine
        // state in it, so it is done here rather than across the boundary.
        Vector forward = Angles.Forward(te.Rotation);
        Vector endpos  = te.Origin + forward * te.Distance;

        ClientTraceResult tr = _world.ClientTraceLine(te.Origin, endpos, TraceMask.All);

        // Stock's first test, and it returns on it. A projection that reached
        // nothing paints nothing -- not a decal at the origin.
        if (tr.Fraction == 1.0f)
            return;

        int hitbox = tr.Hitbox;

        // Non-zero means static prop number hitbox - 1. The subtraction lives
        // beside the test that reads the same convention.
        if (hitbox != 0)
        {
            _effects.ClientDecalStaticProp(te.Origin, endpos, hitbox - 1, te.Index);
            return;
        }

        // Stock's else, whose own comment is "Only decal the world + brush
        // models" -- and which is the ORDINARY case for an
        // info_projecteddecal, not a corner. Behaviour ledger row C3 was open
        // for it.
        //
        // THE CENTRE IS endpos AND NOT tr.endpos. Stock passes the whole aimed
        // segment, `*pos, endpos, endpos`, rather than the part of it the trace
        // traversed -- the same measurement the static prop arm above makes,
        // and the reason AddDecal's third argument exists at all.
        ClientEntityDecal.AddDecal(_world, _effects, tr.EntityIndex,
                                   rayStart: te.Origin, rayEnd: endpos,
                                   decalCenter: endpos, decalIndex: te.Index);
    }
}

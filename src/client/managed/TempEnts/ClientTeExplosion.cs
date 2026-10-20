//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>CTEExplosion</c>, played. The largest of the seven particle-system temp
/// entities and the one that carries a behaviour ledger row.
///
/// <para>
/// <c>c_te_explosion.cpp</c>'s <c>PostDataUpdate</c> is four steps and three of
/// them are decisions, which is why this class reads like stock's function
/// rather than like one call:
/// </para>
/// <code>
/// RecordExplosion();                                    // tools only
/// AffectRagdolls();                                     // LEDGER ROW C1
/// if ( UTIL_PointContents( m_vecOrigin ) &amp; CONTENTS_WATER )
/// { WaterExplosionEffect().Create( ... ); return; }     // and NO fireball
/// if ( !( m_nFlags &amp; TE_EXPLFLAG_NOFIREBALL ) ) { ...overlay... }
/// BaseExplosionEffect().Create( ... );
/// </code>
///
/// <para>
/// THE WATER TEST IS ASKED ON THIS SIDE, as stock asks it. It would have been
/// easy to fold it into the backend and hand C# one <c>ClientExplosion</c>
/// slot; that would have moved the decision back into C++ and left this class
/// with nothing but a decode. It is asked here instead, which is what made a
/// CLIENT arm of <c>PointContents</c> necessary — that slot had been
/// server-only for the whole port, so managed client code asking what was at a
/// point got <c>Contents.Empty</c> and no error. See
/// <c>shell_worldquery_client.cpp</c>.
/// </para>
///
/// <para>
/// THE FIREBALL IS SKIPPED UNDERWATER, and not because a flag says so — stock
/// <c>return</c>s out of the water branch before reaching it. An
/// <c>if/else if/else</c> here would draw the overlay on top of a splash. The
/// early return is kept as an early return.
/// </para>
///
/// <para>
/// BEHAVIOUR LEDGER ROW C1 (plan_tmp.md Part 1) IS CLOSED HERE, and it is
/// closed by the push being real rather than by the call going away.
/// <see cref="AffectRagdolls"/> below is stock's
/// <c>CRagdollExplosionEnumerator</c> — the sphere, the per-body force falloff,
/// the <c>MASK_SHOT_HULL</c> line that decides whether a wall was in the way,
/// and the impulse <c>C_ClientRagdoll::ImpactTrace</c>'s <c>DMG_BLAST</c> arm
/// builds — transcribed, with two ABI slots under it for the two facts C# has
/// no reach for: whether an entity has a jointed body and where the SOLVER says
/// it is, and the one <c>ApplyForceCenter</c> that pushes it.
/// </para>
///
/// <para>
/// IT IS NOT DRIVEN FROM THE NATIVE ENUMERATOR, which is what the row predicted
/// it would be, and the reason is worth having in one place. That enumerator is
/// a sphere query over <c>PARTITION_CLIENT_RESPONSIVE_EDICTS</c>, and the
/// parallel hierarchy never joins the client spatial partition at all —
/// <c>shell_standalone_client.h</c> opens by saying so, <i>"no edict to own, no
/// spatial partition to join"</i>. Driving it would have enumerated an empty
/// set, which is the "looks like the capability half works" failure the row
/// rejected, arriving by a different road. The managed path walks the managed
/// entity list, which is where the bodies this build can push actually are.
/// </para>
///
/// <para>
/// NOT PORTED: <c>RecordExplosion</c>, the tool-recording KeyValues message.
/// SFM is dead (user ruling 2026-10-19). <c>RenderParticles</c> and
/// <c>SimulateParticles</c> went too and were empty and
/// <c>RemoveAllParticles()</c> respectively — the class inherited a particle
/// system it never put a particle in.
/// </para>
/// </summary>
[ClientTempEnt("CTEExplosion")]
public sealed class ClientTeExplosion : ClientTempEnt<TeExplosion>, IClientTempEntLevel
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    /// <summary><c>TE_EXPLFLAG_NOFIREBALL</c>, tempentity.h:20.</summary>
    private const int NoFireball = 0x40;

    /// <summary>
    /// <c>C_ClientRagdoll::ImpactTrace</c>'s <c>dir *= 500</c>
    /// (<c>c_baseanimating.cpp:377</c>) — stock's own comment on the line is
    /// <i>"adjust impact strenght"</i>, and 500 is what the blast arm uses. The
    /// other arm's 4000 is for a bullet and is not this path's.
    /// </summary>
    private const float BlastImpulse = 500f;

    /// <summary>
    /// The 32 units the enumerator drops the blast centre by before handing the
    /// trace on — <c>c_te_explosion.cpp:108</c>, <i>"move expolsion center a bit
    /// down, so things fly higher"</i>. Stock's spelling of the comment
    /// included.
    /// </summary>
    private const float BlastCentreDrop = 32f;

    private readonly IEngineWorldQuery _world;
    private readonly IClientStandalones _standalones;
    private readonly Ragdolls.IClientRagdolls _ragdolls;
    private readonly IEngineEntities _entities;

    private bool _saidABodyRefusedThePush;

    /// <summary>
    /// How many bodies this client has pushed since load, and how many
    /// explosions did any pushing.
    ///
    /// <para>
    /// KEPT, for the reason <see cref="ClientTempEnts.LastPlayed"/> is kept:
    /// "the push is wired" and "the push happened" are not the same claim, and
    /// row C1 was open long enough that the second one is worth being able to
    /// read off a live client rather than inferred from an effect that draws
    /// either way. <c>cs_cl_te_report</c> is where it surfaces.
    /// </para>
    /// </summary>
    public int RagdollsPushed { get; private set; }

    /// <inheritdoc cref="RagdollsPushed"/>
    public int BlastsThatPushed { get; private set; }

    // fx_explosion.cpp's effects, C# since lane c5-particles (ExplosionEffects).
    private readonly Effects.IClientExplosions _explosions;

    // The fireball flash, a warp overlay the managed glow overlays hold (C7).
    private readonly IExplosionFireball _fireball;

    public ClientTeExplosion(ILog log, IEngineWorldQuery world,
                             IClientStandalones standalones, Ragdolls.IClientRagdolls ragdolls,
                             IEngineEntities entities, Effects.IClientExplosions explosions, IExplosionFireball fireball)
    {
        _explosions = explosions;
        _fireball = fireball;

        _log = log;
        _world = world;
        _standalones = standalones;
        _ragdolls = ragdolls;
        _entities = entities;
    }

    public void LevelInit()
    {
        _saidABodyRefusedThePush = false;
    }

    public override void Play(TeExplosion te)
    {
        // The four the drawing reads. m_nMagnitude is the FORCE the explosion
        // effect is built from and is 32 bits unsigned on the wire; it is also
        // half of what AffectRagdolls would have needed.

        // Decoded and used only by stock's tool recording, which did not come
        // across. Read anyway: they are on the wire because the server's table
        // is stock's (2E), and a field nothing ever reads is a field whose
        // offset nothing ever checks.
        _ = te.ModelIndex;
        _ = te.FrameRate;
        _ = te.Normal;
        _ = te.MaterialType;

        // FIRST, as stock does it: before the water test, and unconditionally
        // in the sense that stock's own guard on it is inside the function.
        AffectRagdolls(te.Origin, te.Radius, te.Magnitude);

        // Stock's own test, on THIS side, in this side's copy of the BSP.
        // Water only — slime is its own contents bit and stock does not test
        // it, which means an explosion in slime is a fireball. That is stock's
        // behaviour and is kept.
        if ((_world.PointContents(te.Origin) & Contents.Water) != 0)
        {
            _explosions.Create(Effects.ExplosionKind.Water, te.Origin, te.Magnitude, te.Scale, te.Flags);

            // AND NOTHING ELSE. Stock returns here, so an underwater explosion
            // has no fireball overlay and no land explosion behind the splash.
            return;
        }

        if ((te.Flags & NoFireball) == 0)
            _fireball.Fireball(te.Origin);

        _explosions.Create(Effects.ExplosionKind.Base, te.Origin, te.Magnitude, te.Scale, te.Flags);
    }

    /// <summary>
    /// The blast push on ragdolls — BEHAVIOUR LEDGER ROW C1, closed.
    ///
    /// <para>
    /// This is <c>C_TEExplosion::AffectRagdolls</c> and the whole of
    /// <c>CRagdollExplosionEnumerator</c> in one method, which is where they
    /// belong: stock split them only because a spatial partition query needs a
    /// callback object, and this side enumerates a list.
    /// </para>
    ///
    /// <para>
    /// STOCK'S GUARD IS KEPT AS STOCK'S GUARD: a radius of zero or a magnitude
    /// of zero is an explosion that pushes nothing, and neither was ever the
    /// gap. <c>m_bShouldAffectRagdolls</c> is the third clause of that guard and
    /// is not here, because it is not on the wire and its one false caller is
    /// the KeyValues replay — see the note at the end of this method.
    /// </para>
    ///
    /// <para>
    /// THE ORDER IS STOCK'S ORDER, and each step is a decision that was made on
    /// the client and still is: the sphere, the body-made-this-tick skip
    /// (<c>WasRagdollCreatedOnCurrentTick</c> — a body made this tick already
    /// has its creation force in it), the linear falloff, the
    /// <c>force &lt;= 1</c> cut, the <c>MASK_SHOT_HULL</c> line of sight, and
    /// only then the impulse.
    /// </para>
    /// </summary>
    private void AffectRagdolls(Vector origin, int radius, int magnitude)
    {
        if (radius == 0 || magnitude == 0)
            return;

        int pushed = 0;

        foreach (Standalone.ClientStandaloneEntity entity in _standalones.Live())
        {
            // ONE CROSSING PER ENTITY, answering both facts about the same
            // object at the same instant. The origin is the SOLVER's, not the
            // networked one: after an entity becomes a ragdoll nothing
            // networked about it moves again, so its collision origin is where
            // it was standing when it fell over.
            RagdollState state = _ragdolls.State(entity.Handle, out Vector position);

            if ((state & RagdollState.Present) == 0)
                continue;

            // Stock's EnumElement, second clause: if the ragdoll was created on
            // this tick then the forces were already applied.
            if ((state & RagdollState.MadeThisTick) != 0)
                continue;

            Vector dir = position - origin;
            float dist = dir.Length;

            // THE SPHERE, WHICH STOCK GOT FOR FREE AND THIS DOES NOT.
            // ::partition->EnumerateElementsInSphere only ever handed the
            // enumerator bodies inside the radius; walking a list hands over
            // all of them, so the test that the query was doing is done here.
            // It is the same test — the falloff below would give a body outside
            // the radius a NEGATIVE force and the force <= 1 cut would drop it
            // — but leaving it to that would trace a line to every ragdoll on
            // the map per explosion to reach the same answer.
            if (dist > radius)
                continue;

            dir = dir.Normalized;

            float force = magnitude - ((magnitude / (float)radius) * dist);

            if (force <= 1f)
                continue;

            ClientTraceResult trace =
                _world.ClientTraceLine(origin, position, TraceMask.ShotHull, TraceFilter.Simple);

            // "DID THE LINE REACH ANYTHING BUT THIS BODY", stock's own test,
            // and it is kept although on this hierarchy it CANNOT currently be
            // false in the interesting direction. A C_StandaloneEntity answers
            // SOLID_NONE / FSOLID_NOT_SOLID and never joins the client spatial
            // partition, so enginetrace cannot stop on one: a fraction below 1
            // here is always something else in the way. Kept rather than
            // simplified to `trace.Fraction < 1f`, because the day this
            // hierarchy becomes traceable the simplification would silently
            // stop every body being pushed by the blast that reached it — and
            // that failure would look like a physics bug, not like a deleted
            // clause.
            if (trace.Fraction < 1f && trace.EntityIndex != _entities.GetIndex(entity.Handle))
                continue;

            // Stock's arithmetic, spelled as stock spells it rather than
            // folded. `dir` is scaled by the force; the trace's start is moved
            // so that end - start IS that vector; the centre is then dropped so
            // things fly higher. C_ClientRagdoll::ImpactTrace reads end - start
            // back out and multiplies by 500, so the two steps have to survive
            // as two steps for the drop to be in the impulse at all.
            dir *= force;

            Vector start = trace.End - dir;
            start.Z -= BlastCentreDrop;

            Vector impulse = (trace.End - start) * BlastImpulse;

            if (!_ragdolls.ApplyForceCenter(entity.Handle, impulse))
            {
                // A BODY THAT PASSED EVERY TEST AND WAS NOT PUSHED. The only
                // way here is a ragdoll_t whose element zero has no physics
                // object, which nothing in this build produces — RagdollCreate
                // is refused unless listCount came back above zero. Said out
                // loud once per level anyway, because a push that silently does
                // not happen is exactly the shape row C1 spent this long being.
                if (!_saidABodyRefusedThePush)
                {
                    _saidABodyRefusedThePush = true;

                    _log.Warning(
                        "[SourceSharp/Client] a blast at " +
                        $"({origin.X:0.#} {origin.Y:0.#} {origin.Z:0.#}) reached a jointed " +
                        "body and the solver refused the impulse, so that body did not move. " +
                        "Behaviour ledger row C1 is closed and this is not that row coming " +
                        "back: it is a ragdoll whose root element carries no physics object, " +
                        "which RagdollCreate is not supposed to produce. Said once per " +
                        "level.\n");
                }

                continue;
            }

            pushed++;
        }

        RagdollsPushed += pushed;

        if (pushed > 0)
            BlastsThatPushed++;
    }

    //-------------------------------------------------------------------------
    // TWO THINGS THIS METHOD DOES NOT DO, BOTH OF THEM DELIBERATE, AND CLOSING
    // ROW C1 IS WHAT MAKES THEM WORTH WRITING DOWN.
    //
    // 1. m_bShouldAffectRagdolls, WHICH USED TO BE FREE TO DROP AND IS NOT ANY
    //    MORE. It is stock's third guard clause and it is not a wire field --
    //    stock set it on the singleton directly, which is what "major hack to
    //    access singleton object for doing this event" meant -- so
    //    shell_te_fwd_cp.cpp drops it at the forwarding boundary, and said at
    //    the time that it "only ever suppressed AffectRagdolls, which is
    //    BEHAVIOUR LEDGER ROW C1 and does not push anything on this client
    //    anyway". THAT SECOND HALF IS NO LONGER TRUE. Its only false caller is
    //    TE_Explosion's KeyValues overload, whose whole purpose is not to push
    //    ragdolls twice on tool PLAYBACK -- and that overload is reached from
    //    exactly one place, C_TempEntsSystem::TriggerTempEntity in c_te.cpp,
    //    which is the client half of behaviour ledger row S5: the tool
    //    framework's replay, refused out loud in this build and unreachable
    //    without a tool attached. So the clause has no live false caller today
    //    and the day one arrives it is a wire field or a forwarding argument,
    //    not a silently missing guard. Written here rather than left in the
    //    forward's comment, because the forward's reason for dropping it has
    //    changed and only this file can say so.
    //
    // 2. C_BaseEntity::ImpactTrace FOR A BODY THAT IS NOT JOINTED. Stock's
    //    enumerator calls ImpactTrace on everything it found, and the virtual
    //    lands on C_ClientRagdoll's blast arm only for a ragdoll; for anything
    //    else it lands on C_BaseEntity::ImpactTrace, which builds a CEffectData
    //    and calls DispatchEffect( "Impact", data ). THAT IS BEHAVIOUR LEDGER
    //    ROW C7. That effect did not play for the whole life of this build --
    //    DECLARE_CLIENT_EFFECT("Impact") existed only in tf/, hl2/ and sdk/,
    //    none of which is compiled here, so the client answered "effect
    //    'Impact' not found on client" -- and dispatching it per non-body per
    //    blast would have been hundreds of not-found lines a second.
    //
    //    ROW C7 IS CLOSED NOW (lane `fx-impact`, which registers the name in
    //    shell_impact_effects.cpp), SO THE REASON THIS ARM IS STILL NOT WIRED
    //    HAS CHANGED AND IS WORTH STATING: it would now DRAW rather than log,
    //    and what it would draw is an impact per entity per blast, which is a
    //    performance and a look question rather than a missing-registration
    //    one. That is a decision for whoever owns the blast, with a picture to
    //    look at -- which is more than could be said before.
    //-------------------------------------------------------------------------
}

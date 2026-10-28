using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// The managed temp entity API — the server writes one, the client plays one.
///
/// <para>
/// DELIBERATELY SHAPED AS STOCK'S <c>ITempEntsSystem</c> rather than as
/// something nicer (plan_tmp.md Part 8). The call sites being rewritten were
/// written against that signature and the C++ they are checked against uses it;
/// redesigning while re-implementing gives a behaviour difference two candidate
/// explanations instead of one.
/// </para>
///
/// <para>
/// A METHOD IS ADDED WHEN ITS TABLE IS, and never before: a method with no
/// implementation behind it is a promise that lapses. S-Phase 1 landed
/// <c>Sparks</c>; the lanes add theirs as their tables land — one per stock
/// <c>ITempEntsSystem</c> method, plus the two restored ones. The six decals
/// below are lane B's, and <see cref="Fizz"/> is lane D's — the one method here
/// that is a BEHAVIOUR LEDGER ROW rather than a rewiring, because the effect has
/// no route at all today and this interface is where it comes back.
/// </para>
/// </summary>
public interface ITempEnts
{
    /// <summary>
    /// A shower of sparks. <c>te_sparks.cpp</c>'s <c>TE_Sparks</c>, exactly:
    /// stock takes <c>const Vector *pDir</c> and substitutes
    /// <c>vec3_origin</c> for a null, which is "no particular direction" rather
    /// than a direction of zero length that has to be special-cased downstream.
    /// </summary>
    void Sparks(TeFilter filter, float delay, Vector position,
                int magnitude, int trailLength, Vector? direction);

    /// <summary>A jet of blood: <c>te_bloodstream.cpp</c>'s <c>TE_BloodStream</c>.</summary>
    void BloodStream(TeFilter filter, float delay, Vector origin, Vector direction, Color32 color, int amount);

    /// <summary>A puff of smoke: <c>te_smoke.cpp</c>'s <c>TE_Smoke</c>.</summary>
    void Smoke(TeFilter filter, float delay, Vector origin, int modelIndex, float scale, int frameRate);

    /// <summary>
    /// Sparks off metal: <c>te_metalsparks.cpp</c>'s <c>TE_MetalSparks</c>,
    /// what stock's server-side <c>IEffects::MetalSparks</c> sends (a scrape's
    /// <c>PhysFrictionEffect</c>, plan_physics_managed B2.2).
    /// </summary>
    void MetalSparks(TeFilter filter, float delay, Vector position, Vector direction);

    /// <summary>
    /// A round off an energy shield. <c>te_energysplash.cpp</c>'s
    /// <c>TE_EnergySplash</c>, exactly: a point, a surface normal, and whether
    /// the client plays the bigger version.
    ///
    /// <para>
    /// It is here because C-Phase 1 proves the RECEIVING half with this class
    /// (see <c>SourceSharp.Client/TempEnts/ClientTeEnergySplash.cs</c> for why
    /// it and not Sparks), and the receiving half cannot be proved without a
    /// sender that goes through <see cref="TempEntWriter"/> rather than through
    /// the native forward. The native chain — <c>g_pEffects->EnergySplash</c>
    /// to <c>te->EnergySplash</c> to lane D's shim — reaches the same table and
    /// is unchanged.
    /// </para>
    /// </summary>
    void EnergySplash(TeFilter filter, float delay, Vector position,
                      Vector direction, bool explosive);

    /// <summary>
    /// A sprayed decal on brushwork. <c>te_bspdecal.cpp</c>'s
    /// <c>TE_BSPDecal</c>: a point, the edict it belongs to (0 for the world)
    /// and an engine decal index.
    /// </summary>
    void BSPDecal(TeFilter filter, float delay, Vector position, int entity, int index);

    /// <summary>
    /// A decal thrown along a facing — <c>info_projecteddecal</c>.
    ///
    /// <para>
    /// The angles are a VALUE and not a nullable, unlike <c>Sparks</c>'
    /// direction: stock's <c>TE_ProjectDecal</c> dereferences its
    /// <c>const QAngle *</c> without checking, so there has never been a "no
    /// particular facing" for this one.
    /// </para>
    /// </summary>
    void ProjectDecal(TeFilter filter, float delay, Vector position, QAngle angles,
                      float distance, int index);

    /// <summary>
    /// The bullet-hole decal. Two points, because the client re-traces from
    /// <paramref name="start"/> to <paramref name="position"/> to find the
    /// surface — passing them the other way round puts the mark on the
    /// shooter's side of the wall.
    /// </summary>
    void Decal(TeFilter filter, float delay, Vector position, Vector start,
               int entity, int hitbox, int index);

    /// <summary>
    /// A footprint. <paramref name="right"/> is NORMALISED here, which is
    /// <c>te_footprintdecal.cpp:87</c>'s own
    /// <c>VectorNormalize( m_vecDirection )</c> moved with the behaviour: the
    /// client builds the print's basis from it and an unnormalised one stretches
    /// the decal.
    /// </summary>
    void FootprintDecal(TeFilter filter, float delay, Vector origin, Vector right,
                        int entity, int index, byte materialType);

    /// <summary>A player's spray logo. The player is a SLOT, not an edict.</summary>
    void PlayerDecal(TeFilter filter, float delay, Vector position, int player, int entity);

    /// <summary>A decal on world brushwork: no entity, no trace, no hitbox.</summary>
    void WorldDecal(TeFilter filter, float delay, Vector position, int index);

    /// <summary>
    /// Bubbles fizzing out of a brush. <c>te_fizz.cpp</c>'s <c>TE_Fizz</c> —
    /// and BEHAVIOUR LEDGER ROW S1 (plan_tmp.md Part 1.1), which is the reason
    /// this one signature is not stock's.
    ///
    /// <para>
    /// Stock's <c>ITempEntsSystem::Fizz</c> takes a <c>const CBaseEntity *</c>
    /// and <c>TE_Fizz</c> takes a <c>const IServerEntity *</c>. Neither survives
    /// <c>CBaseEntity</c> becoming an alias for <c>IGameEntity</c>, which
    /// <c>igameentity.h</c> keeps off <c>IServerEntity</c> on purpose — so
    /// <c>te.cpp:377</c> already carries the <c>#ifndef</c> that deletes the
    /// call, and the effect has no managed route at all.
    /// </para>
    ///
    /// <para>
    /// AND IT NEVER NEEDED ONE. <c>DT_TEFizz</c> sends <c>m_nEntity</c>, an
    /// edict index, and <c>TE_Fizz</c> takes the pointer only to run
    /// <c>ENTINDEX</c> on it. An <c>int entIndex</c> is the whole of what the
    /// wire carries, so that is what this takes. THE DEVIATION IS THE ROW.
    /// </para>
    ///
    /// <para>
    /// THE CLIENT HALF IS ROW C6 AND HAS NOT LANDED. Until it does,
    /// <c>c_te_fizz.cpp</c> resolves the index through
    /// <c>cl_entitylist-&gt;GetEnt</c>, which answers NULL for every entity on
    /// the parallel hierarchy — so what this sends arrives and is dropped, with
    /// no diagnostic on either side. That is tracked, not silent, and it is not
    /// fixed from this side.
    /// </para>
    /// </summary>
    void Fizz(TeFilter filter, float delay, int entIndex,
              int modelIndex, int density, int current);

    /// <summary>
    /// Bubbles rising out of a box of water. <c>te_bubbles.cpp</c>'s
    /// <c>TE_Bubbles</c>: the corners themselves, the height they rise, the
    /// sprite, how many and how fast (plan_server_native_v2 T4f: the managed
    /// BubbleEffect, where SourceSharp_BubbleEffect called the native forward).
    /// </summary>
    void Bubbles(TeFilter filter, float delay, Vector mins, Vector maxs, float height,
                 int modelIndex, int count, float speed);

    /// <summary>
    /// A wake of bubbles from one point to another, up to a water surface.
    /// <c>te_bubbletrail.cpp</c>'s <c>TE_BubbleTrail</c>: the ends, the
    /// surface's world Z, the sprite, how many (at most BUBBLE_TRAIL_MAX_COUNT,
    /// 255) and how fast.
    /// </summary>
    void BubbleTrail(TeFilter filter, float delay, Vector from, Vector to, float waterZ,
                     int modelIndex, int count, float speed);
    // ------------------------------------------------------------------
    // Lane F's three ledger rows. The lane's other three classes —
    // BeamSpline, ClientProjectile and MuzzleFlash — keep their native
    // TE_X() entry points and are not here yet, exactly as lanes A and C
    // left theirs: plan Part 8's rewiring is one agent's job and adding a
    // managed method with no managed caller ahead of it is a promise that
    // lapses. These three are here because they have no OTHER path.
    // ------------------------------------------------------------------

    /// <summary>
    /// A generic impact at a point. <b>LEDGER ROW S2</b>, and this method IS
    /// the restoration.
    ///
    /// <para>
    /// <c>te_impact.cpp</c> is excluded from this build and
    /// <c>ITempEntsSystem</c> has no <c>Impact</c> member at all, so there is no
    /// native entry point to keep: nothing has been able to send
    /// <c>DT_TEImpact</c> since the weapon and NPC evictions, while
    /// <c>c_te_impact.cpp</c>'s <c>C_TEImpact</c> has stayed registered and
    /// waiting. This is the only way to fire one.
    /// </para>
    ///
    /// <para>
    /// <paramref name="type"/> is a surface-property index the client turns
    /// into an effect, and <paramref name="normal"/> is a COORD vector rather
    /// than an <c>SPROP_NORMAL</c> one — see <see cref="TeImpact"/> for why that
    /// is stock's choice and not an oversight.
    /// </para>
    /// </summary>
    void Impact(TeFilter filter, float delay, Vector position, Vector normal, int type);

    /// <summary>
    /// The gauss gun's discharge burst. <b>LEDGER ROW S3</b>, restored
    /// <b>unconditionally</b>.
    ///
    /// <para>
    /// Stock's <c>ITempEntsSystem::GaussExplosion</c> has a body only under
    /// <c>#ifdef HL2_DLL</c>, which this build does not define, and
    /// <c>hl2/te_gaussexplosion.cpp</c> is excluded on top of that — so the
    /// effect has been off twice over. A managed method has no <c>#ifdef</c>:
    /// this one has a body on every target, which is the whole of what "restored
    /// unconditionally" means.
    /// </para>
    /// </summary>
    void GaussExplosion(TeFilter filter, float delay, Vector position, Vector direction, int type);

    /// <summary>
    /// The puff of dust a physics object throws up. <b>LEDGER ROW S6</b>, and
    /// the one row here that was never a gap: it fires today through
    /// <c>IEffects::Dust</c> and five live callers, and it fires after the port
    /// through the same chain.
    ///
    /// <para>
    /// This is plan Part 8's named destination for <c>DustEffect</c> — a real
    /// <c>DT_TEDust</c> and <b>not</b> a <c>DispatchEffect("dust")</c>.
    /// <c>C_TEDust</c> is registered and waiting; substituting a dispatch would
    /// leave it unpaired and play a different effect.
    /// </para>
    ///
    /// <para>
    /// <paramref name="direction"/> must be a UNIT vector, which is what
    /// <c>TE_Dust</c> asserts: it is sent as four bits per component over an
    /// explicit [-1, 1], so a longer one is clamped by the encoder and arrives
    /// pointing somewhere else rather than being rejected.
    /// </para>
    /// </summary>
    void Dust(TeFilter filter, float delay, Vector position, Vector direction,
              float size, float speed);
    // ------------------------------------------------------------------
    // Physical debris (plan Part 6, lane E). Two of the six have live
    // callers in this build and both are behind IEngineEffects:
    // ExplosionEffect and FunnelEffect.
    // ------------------------------------------------------------------

    /// <summary>
    /// A model smashed into gibs. <c>te_breakmodel.cpp</c>'s
    /// <c>TE_BreakModel</c>, whose every geometric argument is a REFERENCE in
    /// stock — there has never been a "no particular position" or "no
    /// particular facing" for this one, so none of them is nullable here.
    /// </summary>
    void BreakModel(TeFilter filter, float delay, Vector position, QAngle angles,
                    Vector size, Vector velocity, int modelIndex, int randomization,
                    int count, float time, int flags);

    /// <summary>
    /// A prop handed to the client to simulate and break on its own.
    ///
    /// <para>
    /// THE ARGUMENT ORDER IS STOCK'S, model and skin first, though the table
    /// sends them sixth and seventh. Reordering it to match the wire would give
    /// a behaviour difference two candidate explanations instead of one, which
    /// is the whole reason this interface is shaped like
    /// <c>ITempEntsSystem</c>.
    /// </para>
    /// </summary>
    void PhysicsProp(TeFilter filter, float delay, int modelIndex, int skin,
                     Vector position, QAngle angles, Vector velocity, int flags, int effects);

    /// <summary>
    /// The fireball. <paramref name="normal"/> is nullable because stock's is a
    /// pointer with a NULL default, and a null means <c>(0,0,1)</c> — UP —
    /// rather than a zero-length normal (<c>te_explosion.cpp:126</c>).
    /// <paramref name="materialType"/> defaults to <c>'C'</c> for concrete, as
    /// stock's declaration does.
    /// </summary>
    void Explosion(TeFilter filter, float delay, Vector position, int modelIndex,
                   float scale, int frameRate, int flags, int radius, int magnitude,
                   Vector? normal = null, byte materialType = (byte)'C');

    /// <summary>
    /// Glass or tile coming apart, with the force that did it and the colours
    /// of both faces. Six separate colour bytes, which is stock's own shape and
    /// not a triple: they arrive as six props named
    /// <c>m_uchFrontColor[0..2]</c> and <c>m_uchBackColor[0..2]</c>.
    /// </summary>
    void ShatterSurface(TeFilter filter, float delay, Vector position, QAngle angles,
                        Vector force, Vector forcePosition,
                        float width, float height, float shardSize,
                        ShatterSurfaceType surfaceType,
                        int frontR, int frontG, int frontB,
                        int backR, int backG, int backB);

    /// <summary>
    /// The cone of sprites that pours into or out of a point.
    /// <paramref name="reversed"/> is an <c>int</c> and not a <c>bool</c>
    /// because stock's is, and because the prop is two bits wide rather than
    /// one.
    /// </summary>
    void LargeFunnel(TeFilter filter, float delay, Vector position, int modelIndex, int reversed);

    /// <summary>
    /// Drop everything stuck to a player. The player is a SLOT, not an edict —
    /// five bits.
    /// </summary>
    void KillPlayerAttachments(TeFilter filter, float delay, int player);
    void DispatchEffect(TeFilter filter, float delay, int effectNameIndex, in TeEffectData data);

    // ------------------------------------------------------------------
    // Lane S5's two, and both are here because PART 8's REWIRING NEEDS
    // THEM rather than because a table landed. The rule at the top of this
    // file is that a method is added when its table is; the corollary the
    // earlier lanes worked to is that a method with no managed CALLER is a
    // promise that lapses, which is why lane F left MuzzleFlash on its
    // native TE_MuzzleFlash forward and lane S4 left DispatchEffect taking
    // an index. Part 8 is the commit that supplies the callers.
    // ------------------------------------------------------------------

    /// <summary>
    /// The flash at the end of a barrel. <c>te_muzzleflash.cpp</c>'s
    /// <c>TE_MuzzleFlash</c>, and stock's <c>ITempEntsSystem::MuzzleFlash</c>
    /// signature exactly — a position, a facing, a size and a type.
    ///
    /// <para>
    /// EVERY ARGUMENT IS A REFERENCE IN STOCK, so none of them is nullable
    /// here: there has never been a "no particular position" or "no particular
    /// facing" for this one, unlike <see cref="Sparks"/>' direction.
    /// </para>
    ///
    /// <para>
    /// It is the reason this effect could cross when its neighbours could not.
    /// Every other row in the particle/trail/smoke family delivers itself by
    /// BEING a networked entity whose stock client class runs an emitter, and a
    /// managed entity never appears on the wire as that class. This one is a
    /// temp entity carrying a position and a set of angles which the client
    /// reads straight out of the message — no entity index, nothing to resolve,
    /// nothing that can be NULL for a managed entity.
    /// (<c>shell_worldquery.cpp</c>'s <c>SourceSharp_MuzzleFlashEffect</c>,
    /// whose note this is.)
    /// </para>
    /// </summary>
    void MuzzleFlash(TeFilter filter, float delay, Vector start, QAngle angles,
                     float scale, int type);

    /// <summary>
    /// The same dispatch, BY NAME.
    ///
    /// <para>
    /// The index-taking overload above is kept and is not deprecated: the
    /// native forwards in <c>shell_te_fwd_s4.cpp</c> call it with an index they
    /// have already resolved, and <c>TE_DispatchEffect</c> is still the last
    /// link of every native <c>DispatchEffect()</c> in the build.
    /// </para>
    ///
    /// <para>
    /// THE NUMBERING IS STILL THE ENGINE'S. This overload does not number
    /// anything; it asks <c>IEngineEffects.EffectDispatchIndex</c>, which is
    /// <c>g_pStringTableEffectDispatch->AddString</c> and the only numbering
    /// there is. A managed table of names would be a SECOND numbering of one
    /// the engine owns, and the symptom of the two disagreeing is not a missing
    /// effect but the WRONG one — which reads as a bug in that effect rather
    /// than in this port.
    /// </para>
    ///
    /// <para>
    /// A NAME NO CLIENT EFFECT CLAIMS IS STILL SENT, deliberately. The client
    /// says <c>"effect '%s' not found on client"</c> and plays nothing
    /// (<c>c_te_effect_dispatch.cpp:86</c>), which is how BEHAVIOUR LEDGER ROW
    /// C7 is visible at all: <c>DispatchEffect("Impact")</c> has never played in
    /// this build because <c>DECLARE_CLIENT_EFFECT("Impact")</c> lives only in
    /// <c>tf/</c>, <c>hl2/</c> and <c>sdk/</c>, none of them compiled here.
    /// Substituting a name that DOES resolve would turn a tracked gap into an
    /// untracked one.
    /// </para>
    /// </summary>
    void DispatchEffect(TeFilter filter, float delay, string effectName, in TeEffectData data);
}

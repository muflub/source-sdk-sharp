using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// The client's half of <see cref="EngineSide"/> (plan_shim_areas_split phase 7).
///
/// <para>
/// Each side compiles its own <see cref="EngineSide"/> since phase 3, so what
/// differs by side is said once, here and in the server's file, rather than
/// chosen at run time: a slot both sides serve is called on
/// <see cref="OwnShims"/>, which is the client's container on the client and
/// the server's on the server. The two containers carry those slots under the
/// same names, so the shared source compiles against either. No <c>#if</c>:
/// the side is the assembly.
/// </para>
///
/// <para>
/// And every member that reaches only the client's areas: they live here, in
/// the client's project, rather than in the shared file, where a server's
/// build compiled them only to answer a default (review of phase 7). The
/// bindings that call them are split the same way (EngineBinding.Client.cs).
/// </para>
/// </summary>
internal sealed unsafe partial class EngineSide
{
    /// <summary>The side's own attached areas, the client's; throws before the attach.</summary>
    internal ClientShimAreas OwnShims => ClientShims;

    /// <summary>Whether the side's own areas are attached.</summary>
    internal bool OwnShimsAttached => ClientAttached;

    /// <summary>Attaches the side's own areas: the client's shared bases, from LoadSideV2.</summary>
    internal bool AttachOwnShims(ShimRoot root, out string why) => AttachClientShims(root, out why);

    /// <summary>
    /// The model precache the side's break models use: none, as the client's
    /// slot was null, so a missing model answers -1.
    /// </summary>
    private Func<string, int>? OwnModelPrecache => null;

    private ClientShimAreas? _clientShims;

    /// <summary>Whether the client's areas are attached.</summary>
    internal bool ClientAttached => _clientShims is not null;

    /// <summary>The client's attached areas; throws before the attach.</summary>
    internal ClientShimAreas ClientShims => _clientShims ?? throw new InvalidOperationException(
        "an engine slot was called on the client before its shim areas were attached");

    /// <summary>Binds the client's shared bases; the client calls this from LoadSideV2.</summary>
    internal bool AttachClientShims(ShimRoot root, out string why)
    {
        if (!ClientShimAreas.TryAcquire(root, out var areas, out why))
            return false;
        _clientShims = areas;
        _root = root;
        return true;
    }

    /// <summary>
    /// A round off an energy shield, where managed code decided one should be.
    ///
    /// <para>
    /// CLIENT ONLY, and it is the END of a ported effect rather than the
    /// beginning: the decode and the decision are C#'s, and this is
    /// <c>g_pEffects->EnergySplash</c> — the one line
    /// <c>C_TEEnergySplash::PostDataUpdate</c> was.
    /// </para>
    /// </summary>
    public unsafe void ClientEnergySplash(Vector position, Vector direction,
                                                 bool explosive)
    {
        if (!s_Initialized || !ClientAttached)
            return;

        float* pPos = stackalloc float[3];
        pPos[0] = position.X;
        pPos[1] = position.Y;
        pPos[2] = position.Z;

        float* pDir = stackalloc float[3];
        pDir[0] = direction.X;
        pDir[1] = direction.Y;
        pDir[2] = direction.Z;

        ClientShims.ClientEnergySplash(pPos, pDir, explosive ? 1 : 0);
    }

    //-------------------------------------------------------------------------
    // C-PHASE 2 — the rest of Part 4's client backends.
    //
    // Fifteen drawing primitives. Each one is the LAST LINE of a c_te_*.cpp and
    // nothing else: the decode of the blob and the decision to draw are managed
    // code's, and what is behind these is the renderer this port never intended
    // to move.
    //
    // (The six beam creators left with the managed beam system: the temp
    // entity players call IClientBeams in C# directly.)
    //-------------------------------------------------------------------------

    /// <summary>
    /// Electric sparks. <c>g_pEffects-&gt;Sparks</c>, which is
    /// <c>FX_ElectricSpark</c>.
    ///
    /// <para>
    /// The direction is OPTIONAL and stays optional: no direction is a spark
    /// with no preferred one, which is a different effect from a spark aimed at
    /// the origin. Pass <c>null</c>, not <c>Vector.Zero</c>.
    /// </para>
    /// </summary>
    public unsafe void ClientSparks(Vector position, int magnitude, int trailLength,
                                           Vector? direction = null)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        if (direction is Vector dir)
        {
            float* pDir = stackalloc float[3];
            pDir[0] = dir.X; pDir[1] = dir.Y; pDir[2] = dir.Z;

            ClientShims.ClientSparks(pPos, magnitude, trailLength, pDir);
            return;
        }

        ClientShims.ClientSparks(pPos, magnitude, trailLength, null);
    }

    /// <summary>
    /// A ricochet: metal sparks AND the sound. <c>g_pEffects-&gt;Ricochet</c>.
    /// </summary>
    public unsafe void ClientRicochet(Vector position, Vector direction)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        float* pDir = stackalloc float[3];
        pDir[0] = direction.X; pDir[1] = direction.Y; pDir[2] = direction.Z;

        ClientShims.ClientRicochet(pPos, pDir);
    }

    /// <summary>
    /// The same sparks with NO SOUND, which is the whole difference from
    /// <see cref="ClientRicochet"/> and is stock's own comment on the
    /// interface. <c>g_pEffects-&gt;MetalSparks</c>, which is
    /// <c>FX_MetalSpark</c>.
    /// </summary>
    public unsafe void ClientMetalSparks(Vector position, Vector direction)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        float* pDir = stackalloc float[3];
        pDir[0] = direction.X; pDir[1] = direction.Y; pDir[2] = direction.Z;

        ClientShims.ClientMetalSparks(pPos, pDir);
    }

    /// <summary>
    /// Smoke sprites. <c>g_pEffects-&gt;Smoke</c>, which is <c>FX_Smoke</c>.
    ///
    /// <para>
    /// <paramref name="scale"/> is the TENFOLD one. Stock's two callers both
    /// pass <c>m_fScale * 10.0f</c> under a comment saying "the number passed
    /// down is 10 times smaller", and that multiply is part of the decode — so
    /// it belongs to the caller, not to this call.
    /// </para>
    /// </summary>
    public unsafe void ClientSmoke(Vector origin, int modelIndex,
                                          float scale, float frameRate)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pOrigin = stackalloc float[3];
        pOrigin[0] = origin.X; pOrigin[1] = origin.Y; pOrigin[2] = origin.Z;

        ClientShims.ClientSmoke(pOrigin, modelIndex, scale, frameRate);
    }

    /// <summary>
    /// A muzzle flash. <c>g_pEffects-&gt;MuzzleFlash</c>, which switches on the
    /// type over <c>FX_MuzzleEffect</c>, <c>FX_GunshipMuzzleEffect</c> and
    /// <c>FX_StriderMuzzleEffect</c> and prints a line for anything else —
    /// stock's own behaviour, left alone rather than turned into a silent
    /// default.
    /// </summary>
    public unsafe void ClientMuzzleFlash(Vector origin, Vector angles,
                                                float scale, int type)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pOrigin = stackalloc float[3];
        pOrigin[0] = origin.X; pOrigin[1] = origin.Y; pOrigin[2] = origin.Z;

        float* pAngles = stackalloc float[3];
        pAngles[0] = angles.X; pAngles[1] = angles.Y; pAngles[2] = angles.Z;

        ClientShims.ClientMuzzleFlash(pOrigin, pAngles, scale, type);
    }

    /// <summary>
    /// A decal on the world, a brush model or a static prop.
    /// <c>effects-&gt;DecalShoot</c>, the ENGINE's decal system.
    ///
    /// <para>
    /// The entity is an EDICT INDEX and is resolved on the native side, because
    /// the call wants the entity's <c>model_t*</c>, abs origin and abs angles
    /// and a <c>model_t*</c> is not a thing that crosses this boundary. Entity
    /// 0 is the world, which is what a world or BSP decal asks for.
    /// </para>
    ///
    /// <para>
    /// <paramref name="sAxis"/> is the decal's S axis — a footprint's facing.
    /// Omit it and the engine picks one, which is what the world and BSP decals
    /// do. <c>r_decals</c> is honoured natively, once, where stock honours it.
    /// </para>
    ///
    /// <para>
    /// This does NOT close ledger rows C2 or C3. Those are decals on ENTITIES
    /// and the world/brush arm of the PROJECTED decal, both of which go through
    /// <c>C_BaseEntity::AddDecal</c> — a call this hierarchy has no version of
    /// and this slot is not.
    /// </para>
    /// </summary>
    public unsafe void ClientDecalShoot(int decalIndex, int entIndex, Vector position,
                                               Vector? sAxis = null, int flags = 0)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        if (sAxis is Vector axis)
        {
            float* pAxis = stackalloc float[3];
            pAxis[0] = axis.X; pAxis[1] = axis.Y; pAxis[2] = axis.Z;

            ClientShims.ClientDecalShoot(decalIndex, entIndex, pPos, pAxis, flags);
            return;
        }

        ClientShims.ClientDecalShoot(decalIndex, entIndex, pPos, null, flags);
    }

    /// <summary>
    /// A player's spray logo. <c>effects-&gt;PlayerDecalShoot</c>.
    ///
    /// <para>
    /// Three numbers here and everything else on the far side, which is not a
    /// thin wrapper being lazy: between the wire fields and the call sit a
    /// <c>player_info_t</c> out of the engine, a custom-file hash, a download
    /// check, a material lookup and a texture-variable swap — every one of them
    /// a pointer type that cannot cross this boundary.
    /// </para>
    /// </summary>
    public unsafe void ClientPlayerDecalShoot(Vector position, int player, int entIndex)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        ClientShims.ClientPlayerDecalShoot(pPos, player, entIndex);
    }

    /// <summary>
    /// A decal's index from its name, or -1 for a name the engine does not
    /// know. <c>effects-&gt;Draw_DecalIndexFromName</c>.
    ///
    /// <para>
    /// The one backend in this family that ANSWERS rather than draws. A
    /// received temp entity never needs it — the wire carries the index — but a
    /// client making a decal for itself does.
    /// </para>
    /// </summary>
    public unsafe int ClientDecalIndexFromName(string name)
    {
        if (!s_Initialized || !ClientAttached || name.Length == 0) return -1;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { return ClientShims.ClientDecalIndexFromName(p); }
    }

    /// <summary>
    /// A decal on a STATIC PROP. <c>staticpropmgr-&gt;AddDecalToStaticProp</c>.
    ///
    /// <para>
    /// ONE call for two classes: <c>CTEDecal</c>'s world-with-a-hitbox arm and
    /// <c>CTEProjectedDecal</c>'s post-trace one. Stock hands both of them an
    /// uninitialised <c>trace_t</c> and the interface's own comment says the
    /// call "returns point of decal in trace_t", so nothing about the trace
    /// that chose the prop has to survive into the decal.
    /// </para>
    ///
    /// <para>
    /// <paramref name="staticPropIndex"/> is stock's <c>hitbox - 1</c>, applied
    /// by the caller. The engine numbers a prop's hitbox from one so that zero
    /// can mean "not a static prop", and unpacking that belongs beside the
    /// <c>hitbox != 0</c> test that reads it — which is a decision, and
    /// decisions are C#'s. A negative index is refused natively.
    /// </para>
    ///
    /// <para>
    /// This does NOT close ledger rows C2 or C3, and it is not the arm either
    /// of them names. Both rows are about <c>C_BaseEntity::AddDecal</c>; this
    /// is the static-prop arm, which never had an entity in it and has drawn
    /// all along.
    /// </para>
    /// </summary>
    public unsafe void ClientDecalStaticProp(Vector start, Vector end,
                                                    int staticPropIndex, int decalIndex)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pStart = stackalloc float[3];
        pStart[0] = start.X; pStart[1] = start.Y; pStart[2] = start.Z;

        float* pEnd = stackalloc float[3];
        pEnd[0] = end.X; pEnd[1] = end.Y; pEnd[2] = end.Z;

        ClientShims.ClientDecalStaticProp(pStart, pEnd, staticPropIndex, decalIndex);
    }

    /// <summary>
    /// Whether this client is running with gore turned down.
    /// <c>UTIL_IsLowViolence</c>.
    ///
    /// <para>
    /// NOT FOUR CONVAR READS. <c>violence_hblood</c> and its three siblings are
    /// all reachable through <see cref="FindConVarValue"/>, and stock's own
    /// comment above them says they are legacy and that "the engine is the
    /// final arbiter of violence settings". The arbiter is
    /// <c>engine-&gt;IsLowViolence()</c>, which managed code cannot reach at
    /// all — so a C# copy of this rule would have been the four terms that do
    /// not decide anything and a missing fifth that does.
    /// </para>
    /// </summary>
    public unsafe bool ClientIsLowViolence()
    {
        if (!s_Initialized || !ClientAttached) return false;

        return ClientShims.ClientIsLowViolence() != 0;
    }

    /// <summary>
    /// A decal's NAME from its index, or the empty string for an index that
    /// names nothing. <c>decalsystem-&gt;GetDecalNameForIndex</c>.
    ///
    /// <para>
    /// NOT THE INVERSE of <see cref="ClientDecalIndexFromName"/>, and reading it
    /// as one is the trap. That asks the ENGINE's precached decal table
    /// (<c>effects-&gt;Draw_DecalIndexFromName</c>); this asks the GAME's decal
    /// emitter system, loaded from <c>scripts/decals_subrect.txt</c> and keyed
    /// on each entry's precache index. The two overlap and are not one table,
    /// and stock reads each in its own place —
    /// <c>c_te_worlddecal.cpp</c>'s low-violence test reads this one.
    /// </para>
    /// </summary>
    public unsafe string ClientDecalNameFromIndex(int decalIndex)
    {
        if (!s_Initialized || !ClientAttached) return string.Empty;

        // 260, which is what every other string slot on this ABI gives itself.
        // A decal name is a material path.
        const int bufSize = 260;
        byte* buf = stackalloc byte[bufSize];

        return ClientShims.ClientDecalNameFromIndex(decalIndex, buf, bufSize) > 0 ? FromUtf8(buf) : string.Empty;
    }

    /// <summary>
    /// What a client entity's model IS, by entity index —
    /// <c>modelinfo-&gt;GetModelType</c> of whatever that index resolves to.
    /// <c>null</c> for no such entity.
    ///
    /// <para>
    /// NULL IS NOT <see cref="Abi.ModelType.Bad"/>, and folding the two together
    /// would delete stock's own line. <c>TE_Decal</c> reads
    /// <c>if ( ( ent = cl_entitylist-&gt;GetEnt( entity ) ) == NULL ) return;</c>
    /// above the call, and <c>mod_bad</c> is a real answer about an entity that
    /// is really there — a caller that could not tell them apart would either
    /// warn about a missing decal path for entities that do not exist, or draw
    /// nothing for entities that do.
    /// </para>
    ///
    /// <para>
    /// NOT <see cref="ModelTypeOf"/> WITH ANOTHER NUMBER. That takes a PRECACHE
    /// index and answers about a file; this takes an ENTITY index and answers
    /// about what that entity is wearing. Managed code cannot bridge them: a
    /// <c>model_t*</c> does not cross this boundary, and this side's entity
    /// list holds entities C# never precached.
    /// </para>
    /// </summary>
    public unsafe ModelType? ClientEntityModelType(int entIndex)
    {
        if (!s_Initialized || !ClientAttached) return null;

        int answer = ClientShims.ClientEntityModelType(entIndex);

        return answer < 0 ? null : (ModelType)answer;
    }

    /// <summary>
    /// A decal on a STUDIO model — <c>C_BaseEntity::AddStudioDecal</c>, which
    /// is <c>modelrender-&gt;AddDecal</c> on the entity's own model instance.
    /// BEHAVIOUR LEDGER ROWS C2 AND C3's studio arm.
    ///
    /// <para>
    /// THE TWO POINTS AND NOT A RAY. Stock builds the <c>Ray_t</c> inside
    /// <c>AddDecal</c> and then widens its DELTA by a tenth
    /// (<c>ray.m_Delta *= 1.1f</c>, under its own "bloat a little bit so we get
    /// the intersection"). Both stay native: bloating the endpoint here and
    /// letting <c>Ray_t::Init</c> subtract would hand the engine
    /// <c>(start + d * 1.1f) - start</c>, which is not <c>d * 1.1f</c>.
    /// </para>
    ///
    /// <para>
    /// No hitbox: <c>AddStudioDecal</c> takes one and never reads it. No LOD
    /// limit: both of stock's call sites take <c>ADDDECAL_TO_ALL_LODS</c> by
    /// default. <c>r_drawmodeldecals</c> is checked natively, because that is
    /// where stock checks it and it is a renderer setting.
    /// </para>
    /// </summary>
    public unsafe void ClientDecalStudioModel(int entIndex, Vector rayStart, Vector rayEnd,
                                                     int decalIndex)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pStart = stackalloc float[3];
        pStart[0] = rayStart.X; pStart[1] = rayStart.Y; pStart[2] = rayStart.Z;

        float* pEnd = stackalloc float[3];
        pEnd[0] = rayEnd.X; pEnd[1] = rayEnd.Y; pEnd[2] = rayEnd.Z;

        ClientShims.ClientDecalStudioModel(entIndex, pStart, pEnd, decalIndex);
    }

    /// <summary>
    /// A decal on a BRUSH model, the world included —
    /// <c>C_BaseEntity::AddBrushModelDecal</c>, which is one
    /// <c>effects-&gt;DecalShoot</c>. BEHAVIOUR LEDGER ROWS C2 AND C3's brush
    /// arm, and the one an ordinary <c>info_projecteddecal</c> takes.
    ///
    /// <para>
    /// NO RAY. With <c>doTrace</c> false the ray reaches nothing in that
    /// function; what places the decal is <paramref name="decalCenter"/>, which
    /// is stock's THIRD argument to <c>AddDecal</c> — <c>*pos</c> at
    /// <c>c_te_decal.cpp</c>'s call site and <c>endpos</c> at
    /// <c>c_te_projecteddecal.cpp</c>'s, and not the same vector as the
    /// segment's end in the second.
    /// </para>
    ///
    /// <para>
    /// NOT <see cref="ClientDecalShoot"/> WITH SHORTER ARGUMENTS, though both
    /// end in the same engine call: that one checks <c>r_decals</c> because all
    /// three of ITS stock callers check it on the line before, and neither of
    /// this one's does.
    /// </para>
    /// </summary>
    public unsafe void ClientDecalBrushModel(int entIndex, Vector decalCenter,
                                                    int decalIndex)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pCenter = stackalloc float[3];
        pCenter[0] = decalCenter.X; pCenter[1] = decalCenter.Y; pCenter[2] = decalCenter.Z;

        ClientShims.ClientDecalBrushModel(entIndex, pCenter, decalIndex);
    }

    /// <summary>
    /// A CLIENT entity's MODEL bounds and its angles, BY INDEX. False when the
    /// index names no entity, or names one with no model.
    ///
    /// <para>
    /// BEHAVIOUR LEDGER ROW C6's native half, and the two reads are stock's:
    /// <c>CTempEnts::FizzEffect</c> asks
    /// <c>modelinfo-&gt;GetModelBounds( pent-&gt;GetModel(), … )</c> for the
    /// volume the bubbles are spread across and <c>GetLocalAngles()[1]</c> for
    /// the direction the current drags them, and nothing else about the entity.
    /// </para>
    ///
    /// <para>
    /// <b>THE MODEL'S BOUNDS, NOT THE COLLISION BOX.</b>
    /// <see cref="EntityGetBounds"/> answers the collideable's rectangle, which
    /// on <c>C_StandaloneEntity</c> is a symmetric ±16 cube set once in the
    /// constructor and never networked — so it would put every fizz in a
    /// 32-unit cube. The model's bounds are the slab the mapper drew, in the
    /// model's own frame, which for a brush model is world space already.
    /// </para>
    ///
    /// <para>
    /// BY INDEX, and the native arm resolves through
    /// <c>cl_entitylist-&gt;GetClientEntity</c>, which answers for BOTH
    /// hierarchies. Stock's <c>cl_entitylist-&gt;GetEnt</c> answered only the
    /// SDK one, which is why every fizz in this build was dropped in silence.
    /// </para>
    /// </summary>
    public unsafe bool ClientEntityModelBounds(int entityIndex,
                                                      out Vector mins, out Vector maxs,
                                                      out Vector angles)
    {
        mins = Vector.Zero;
        maxs = Vector.Zero;
        angles = Vector.Zero;

        if (!s_Initialized || !ClientAttached) return false;

        float* lo = stackalloc float[3];
        float* hi = stackalloc float[3];
        float* pyr = stackalloc float[3];

        if (ClientShims.ClientEntityModelBounds(entityIndex, lo, hi, pyr) == 0) return false;

        mins = new Vector(lo[0], lo[1], lo[2]);
        maxs = new Vector(hi[0], hi[1], hi[2]);
        angles = new Vector(pyr[0], pyr[1], pyr[2]);
        return true;
    }

    //-------------------------------------------------------------------------
    // THE PARTICLE-SYSTEM TEMP ENTITIES (C-Phase 3, lane CP).
    //
    // Eight named primitives, not one ClientParticleTeSpawn(kind, params): the
    // seven TEs on DT_TEParticleSystem share a base TABLE and nothing else.
    // Sparks needs none of them — ClientSparks above already is its primitive,
    // which is why there are eight for seven.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The fireball's white-hot flash: one <c>CExplosionOverlay</c>, which
    /// grows and fades over a tenth of a second.
    /// </summary>
    public unsafe void ClientExplosionFireball(Vector origin)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* pOrigin = stackalloc float[3];
        pOrigin[0] = origin.X; pOrigin[1] = origin.Y; pOrigin[2] = origin.Z;

        ClientShims.ClientExplosionFireball(pOrigin);
    }

    // ---- the legacy temp entity pool (plan_tmp.md Part 5). Client only ----
    //
    // c_te_legacytempents.cpp's CTempEnts, whose BOOKKEEPING is
    // ClientTempEntPool.cs: the active list, the free list, the priority
    // classes, the eviction, the lifetime and the update loop. These six reach
    // ONE pooled C_LocalTempEntity, addressed by SLOT.
    //
    // EVERY ONE ANSWERS FALSE FOR AN EMPTY SLOT rather than throwing, and that
    // is what the managed pool's own recovery is built on: a slot whose object
    // has gone -- because a reservation never got one, or because native tore
    // the level down under it -- is dropped from the list on the next pass
    // instead of being a fault.

    /// <summary>
    /// How many tempents this build's pool holds —
    /// <c>CTempEnts::MAX_TEMP_ENTITIES</c>.
    ///
    /// <para>
    /// ANSWERED RATHER THAN RESTATED. A C# constant beside a C++ enum is the
    /// stand-in that drifts silently, and the drift here would be a pool that
    /// overflows the native slot table or one that refuses at 500 while native
    /// still has room. Zero means the slot is not wired, which the pool treats
    /// as "no pool" rather than as a capacity.
    /// </para>
    /// </summary>
    public unsafe int TempEntPoolCapacity()
    {
        if (!s_Initialized || !ClientAttached) return 0;

        return ClientShims.TempEntPoolCapacity();
    }

    /// <summary>
    /// Give one back: <c>RemoveFromLeafSystem</c>, <c>OnRemoveTempEntity</c>
    /// and the memory pool free, in stock <c>TempEntFree</c>'s order.
    /// </summary>
    public unsafe void TempEntPoolDestroy(int slot)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.TempEntPoolDestroy(slot);
    }

    /// <summary>
    /// Fill <paramref name="life"/> from the live object. False for an empty
    /// slot, and <paramref name="life"/> is then default.
    /// </summary>
    public unsafe bool TempEntPoolReadLife(int slot, out TempEntLife life)
    {
        life = default;

        if (!s_Initialized || !ClientAttached) return false;

        fixed (TempEntLife* pLife = &life)
        {
            return ClientShims.TempEntPoolReadLife(slot, pLife) != 0;
        }
    }

    /// <summary>
    /// The transform C# owns while it simulates this tempent. Read ONCE, when
    /// the managed pool takes it over — everything after that is integrated on
    /// this side, so this is how C# learns what Prepare and the spawn helper
    /// left behind rather than a per-frame query.
    /// </summary>
    public unsafe bool TempEntPoolReadTransform(int slot, out TempEntTransform transform)
    {
        transform = default;

        if (!s_Initialized || !ClientAttached) return false;

        fixed (TempEntTransform* p = &transform)
        {
            return ClientShims.TempEntPoolReadTransform(slot, p) != 0;
        }
    }

    /// <summary>
    /// Write the transform back, which is how a C#-simulated tempent gets
    /// drawn. ONCE per tempent per frame; see <see cref="TempEntTransform"/>
    /// for why this is not a set of per-field accessors.
    /// </summary>
    public unsafe bool TempEntPoolWriteTransform(int slot, in TempEntTransform transform)
    {
        if (!s_Initialized || !ClientAttached) return false;

        fixed (TempEntTransform* p = &transform)
        {
            return ClientShims.TempEntPoolWriteTransform(slot, p) != 0;
        }
    }

    /// <summary>
    /// The write-once state <c>Frame</c> reads and never writes — bounce
    /// factor, bounce sound, sprite animation bounds, the player-attachment
    /// offset, the owner to trace past and the flicker frame. Read ONCE when
    /// the managed pool takes a tempent over.
    ///
    /// <para>
    /// A THIRD STRUCT rather than a bigger transform, and asked of the OBJECT
    /// rather than remembered from the managed spawn record. See
    /// <see cref="TempEntStatics"/>: this build still has three NATIVE spawn
    /// routes, and a managed cache of what C# itself wrote would simulate them
    /// with <c>Prepare</c>'s defaults.
    /// </para>
    /// </summary>
    public unsafe bool TempEntPoolReadStatics(int slot, out TempEntStatics statics)
    {
        statics = default;

        if (!s_Initialized || !ClientAttached) return false;

        fixed (TempEntStatics* p = &statics)
        {
            return ClientShims.TempEntPoolReadStatics(slot, p) != 0;
        }
    }

    /// <summary>
    /// <c>GetWindspeedAtTime</c> — the client's current wind velocity, which is
    /// what <c>FTENT_WINDBLOWN</c> accelerates towards. The zero vector with no
    /// engine and on a map with no <c>env_wind</c>, which are the same answer
    /// the engine's own implementation gives.
    /// </summary>
    public unsafe Vector ClientWindSpeed(float time)
    {
        if (!s_Initialized || !ClientAttached) return Vector.Zero;

        float* xyz = stackalloc float[3] { 0f, 0f, 0f };

        ClientShims.ClientWindSpeed(time, xyz);

        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    /// <summary>
    /// A CLIENT entity's origin by INDEX. False when the index names no entity
    /// — the ordinary case for a player who has left, where stock simply does
    /// not move the tempent.
    /// </summary>
    public unsafe bool ClientEntityOrigin(int entityIndex, out Vector origin)
    {
        origin = Vector.Zero;

        if (!s_Initialized || !ClientAttached) return false;

        float* xyz = stackalloc float[3];

        if (ClientShims.ClientEntityOrigin(entityIndex, xyz) == 0) return false;

        origin = new Vector(xyz[0], xyz[1], xyz[2]);
        return true;
    }

    /// <summary>
    /// Write <paramref name="life"/> back into the live object. False for an
    /// empty slot.
    ///
    /// <para>
    /// EVERY FIELD IS WRITTEN, so a caller reads first and edits what it read.
    /// A partial write is not offered: the alternative is eight setters and a
    /// caller that forgets one, which is a tempent that fades at the wrong rate
    /// rather than a compile error.
    /// </para>
    /// </summary>
    public unsafe bool TempEntPoolWriteLife(int slot, in TempEntLife life)
    {
        if (!s_Initialized || !ClientAttached) return false;

        fixed (TempEntLife* pLife = &life)
        {
            return ClientShims.TempEntPoolWriteLife(slot, pLife) != 0;
        }
    }

    /// <summary>
    /// One step of the physics — <c>C_LocalTempEntity::Frame</c>. False when
    /// the tempent asked to die, which is an animating sprite that ran off its
    /// last frame.
    ///
    /// <para>
    /// STILL NATIVE, and not because the pool is: the collision arm needs
    /// <c>UTIL_TraceLine</c> and a whole <c>trace_t</c>, and this build's client
    /// has no trace at all — the ABI's is wired <c>#ifndef CLIENT_DLL</c>. That
    /// is the same wall <c>docs/te-ledger.md</c> records for
    /// <c>CTEProjectedDecal</c>, and it is what holds ledger rows C4 and C5
    /// open.
    /// </para>
    /// </summary>
    public unsafe bool TempEntPoolSimulate(int slot, float frameTime, int frameNumber)
    {
        if (!s_Initialized || !ClientAttached) return false;

        return ClientShims.TempEntPoolSimulate(slot, frameTime, frameNumber) != 0;
    }

    /// <summary>
    /// <c>CTempEnts::AddVisibleTempEntity</c> — put it back in the leaf system
    /// for this frame. False when it cannot be drawn at all, which for stock
    /// means it has no model.
    /// </summary>
    public unsafe bool TempEntPoolAddVisible(int slot)
    {
        if (!s_Initialized || !ClientAttached) return false;

        return ClientShims.TempEntPoolAddVisible(slot) != 0;
    }

    /// <summary>
    /// Construct a tempent into a slot the managed pool has already reserved:
    /// the memory-pool allocation, <c>Prepare( model, curtime )</c>, the
    /// priority, the origin and the leaf system — stock
    /// <c>CTempEnts::TempEntAlloc</c> from the list add down.
    ///
    /// <para>
    /// HOW EVERY MANAGED SPAWN BUILDS ITS OBJECT. The managed helper reserves
    /// from the pool it IS and calls this; there used to be a twin half where
    /// native entered <c>CTempEnts::TempEntAlloc</c> and called OUT to reserve
    /// through <c>OnTempEntPoolAlloc</c>, but that whole direction was deleted
    /// with the tempent pool sweep — the only code that entered it was the
    /// stock <c>c_te_legacytempents.cpp</c>, which compiles into no target. The
    /// list, the free list and the eviction rule live only on this side.
    /// </para>
    ///
    /// <para>
    /// False means nothing was constructed and the slot is still EMPTY — an
    /// out-of-range or occupied slot, a model index that resolves to nothing, or
    /// the memory pool refusing. The caller must give the reservation back;
    /// <see cref="SourceSharp.Client.TempEnts.ClientTempEntPool"/> does that in
    /// one place so no helper has to remember to.
    /// </para>
    /// </summary>
    public unsafe bool TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority)
    {
        if (!s_Initialized || !ClientAttached) return false;

        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };

        return ClientShims.TempEntPoolCreate(slot, xyz, modelIndex, priority) != 0;
    }

    /// <summary>
    /// Write a whole <see cref="TempEntSpawn"/> into the live object. False for
    /// an empty slot.
    ///
    /// <para>
    /// EVERY FIELD IS WRITTEN, for <c>TempEntPoolWriteLife</c>'s reason and one
    /// more of its own: the record is built from
    /// <see cref="TempEntSpawn.Prepared"/>, so "the caller did not set this" and
    /// "the caller set this to Prepare's value" are the same thing and a mask
    /// would distinguish two states that are one.
    /// </para>
    /// </summary>
    public unsafe bool TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn)
    {
        if (!s_Initialized || !ClientAttached) return false;

        fixed (TempEntSpawn* pSpawn = &spawn)
        {
            return ClientShims.TempEntPoolWriteSpawn(slot, pSpawn) != 0;
        }
    }

    /// <summary>
    /// An effect INDEX, as the name the wire's number stands for. The mirror of
    /// <see cref="EffectDispatchIndex"/>.
    ///
    /// <para>
    /// CLIENT ONLY, and the reverse of that slot's reason: the client is the
    /// side that has to pick a callback, so it is the side that reads the
    /// "EffectDispatch" string table backwards. A server that asked gets the
    /// empty string.
    /// </para>
    ///
    /// <para>
    /// AN EMPTY ANSWER IS "the table does not hold that index", which is how
    /// stock's NULL return crosses a boundary that cannot carry one.
    /// <c>c_te_effect_dispatch.cpp</c> does nothing at all in that case, and
    /// neither does the managed player — a dispatch with no name is not a
    /// dispatch of the empty name.
    /// </para>
    ///
    /// <para>
    /// The buffer is 128 bytes because <c>MAX_EFFECT_DISPATCH_STRING_BITS</c>
    /// bounds how MANY names there are (1024) and nothing bounds how long one
    /// is; 128 is the same order as every other name buffer on this ABI and is
    /// far past the longest of the fifty-two registrations
    /// (<c>"ParticleEffectStop"</c>, eighteen).
    /// </para>
    /// </summary>
    public unsafe string EffectDispatchName(int index)
    {
        if (!s_Initialized || !ClientAttached || index < 0)
            return string.Empty;

        const int bufSize = 128;
        byte* buf = stackalloc byte[bufSize];
        ClientShims.EffectDispatchName(index, buf, bufSize);
        return FromUtf8(buf);
    }

    /// <summary>
    /// Local player health, or -1 when there is no local player — the case in
    /// the menu and between levels. Distinct from 0 so the HUD can tell "no
    /// player" from "dead".
    /// </summary>
    public int LocalPlayerHealth
        => (s_Initialized && ClientAttached) ? ClientShims.GetLocalPlayerHealth() : -1;

    /// <summary>
    /// One row of the client's native ammo table (its CAmmoDef). Client only
    /// since plan_server_native_v2 T2: server.so keeps no ammo table.
    /// </summary>
    public void RegisterAmmoType(string name, DamageType damageType, int maxCarry,
                                        int tracerType = 0, float damageForce = 0f,
                                        float damage = 0f)
    {
        if (!s_Initialized || !ClientAttached) return;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            ClientShims.RegisterAmmoType(p, (int)damageType, maxCarry, tracerType, damageForce, damage);
    }

    //-------------------------------------------------------------------------
    // Movement. What a trigger does to whoever is standing in it.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Hands a broken surface the cracked edges it should draw.
    ///
    /// Pushed when the state changes, not asked for per frame: what stays native
    /// is the mesh building, which cannot come across at four interop calls a
    /// quad, and what stays here is deciding which edges there are.
    /// </summary>
    public unsafe void SurfaceSetEdges(EntityHandle entity, byte[] packed, int count)
    {
        if (!s_Initialized || !ClientAttached) return;

        fixed (byte* p = packed)
        {
            ClientShims.SurfaceSetEdges(entity.Value, p, count);
        }
    }

    /// <summary>
    /// The entity index the engine gave THIS client, 0 when there is no local
    /// player — the menu, or between levels. Client-side. The client half of
    /// "is this entity mine": the viewmodel and the HUD both decide locality
    /// by comparing an OwnerIndex against this.
    /// </summary>
    public int GetLocalPlayerIndex()
    {
        if (!s_Initialized || !ClientAttached) return 0;

        return ClientShims.GetLocalPlayerIndex();
    }

    /// <summary>
    /// Puts a standalone entity into (or takes it out of) the renderer's
    /// VIEWMODEL pass, where it is drawn at the local player's eyes with the
    /// viewmodel FOV and the depth-range hack — exactly where a stock
    /// C_BaseViewModel draws. Client-side. Managed code decides WHICH entity
    /// (the one whose OwnerIndex is the local player); this is the mechanism.
    /// </summary>
    public void StandaloneSetViewModel(EntityHandle entity, bool enable)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.StandaloneSetViewModel(entity.Value, enable ? 1 : 0);
    }

    /// <summary>
    /// The same trace, choosing which entities it may hit. See
    /// <see cref="TraceFilter"/> for why this is an enumeration and not an
    /// ignore-entity parameter: a simple filter is not hit-all with one entity
    /// removed, because it also applies collision groups and the game rules.
    /// </summary>
    public unsafe ClientTraceResult ClientTraceLine(Vector start, Vector end, TraceMask mask,
                                                           TraceFilter filter,
                                                           int passEntityIndex = -1)
    {
        if (!s_Initialized || !ClientAttached)
            return new ClientTraceResult(false, 1f, end, Vector.Zero, 0, -1, -1, 0);

        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* endPos = stackalloc float[3];
        float* normal = stackalloc float[3];
        float fraction;
        int hitbox;
        int entIndex;
        int surfaceProps;
        int surfaceFlags;

        int stopped = ClientShims.ClientTraceLine(s, e, (int)mask, (int)filter, passEntityIndex,
                                          &fraction, endPos, normal, &hitbox, &entIndex,
                                          &surfaceProps, &surfaceFlags);

        return new ClientTraceResult(
            stopped != 0,
            fraction,
            new Vector(endPos[0], endPos[1], endPos[2]),
            new Vector(normal[0], normal[1], normal[2]),
            hitbox,
            entIndex,
            surfaceProps,
            surfaceFlags);
    }

    /// <summary>
    /// <c>IVEngineClient::Con_NPrintf</c>, which is varargs and so cannot be
    /// issued through a fixed indirect signature at all.
    ///
    /// <para>
    /// The native side passes <paramref name="text"/> as an ARGUMENT —
    /// <c>Con_NPrintf( pos, "%s", text )</c> — and never as the format string.
    /// Caller-controlled text in the format position reads the stack on any
    /// <c>%s</c> that happens to be in a model name or a chat line.
    /// </para>
    /// </summary>
    public unsafe void ConNPrintf(void* engineClient, int position, string text)
    {
        if (!s_Initialized || !ClientAttached || engineClient == null)
            return;

        byte[] bytes = Vtable.Utf8(text);

        fixed (byte* p = bytes)
            ClientShims.ConNPrintf(engineClient, position, p);
    }

    /// <summary>
    /// <c>IVEngineClient::Con_NXPrintf</c>, with <c>con_nprint_s</c> flattened
    /// into its scalars.
    ///
    /// <para>
    /// The struct's layout stays native's business. <c>con_nprint.h</c> says of
    /// it that the "structure cannot be changed by mods", and a C# copy would be
    /// a second declaration of it with nothing holding the two in step.
    /// </para>
    /// </summary>
    public unsafe void ConNXPrintf(
        void* engineClient, int index, float timeToLive,
        float red, float green, float blue, bool fixedWidthFont, string text)
    {
        if (!s_Initialized || !ClientAttached || engineClient == null)
            return;

        byte[] bytes = Vtable.Utf8(text);
        float* colour = stackalloc float[3] { red, green, blue };

        fixed (byte* p = bytes)
            ClientShims.ConNXPrintf(engineClient, index, timeToLive, colour,
                            Vtable.Bit(fixedWidthFont), p);
    }

    /// <summary>
    /// <c>IVEngineClient::GetLightForPoint</c>, which returns <c>Vector</c> BY
    /// VALUE — three floats in <c>xmm0</c> and <c>xmm1</c> under the SysV
    /// classification, which a <c>delegate* unmanaged&lt;...&gt;</c> has no
    /// spelling for. The floats come back through a pointer instead.
    /// </summary>
    /// <returns>False when nothing answered, in which case nothing is written.</returns>
    public unsafe bool TryGetLightForPoint(
        void* engineClient, in Vector position, bool clamp, out Vector light)
        => TryLightForPoint(fast: false, engineClient, position, clamp, out light);

    /// <summary>
    /// <c>IVEngineClient::GetLightForPointFast</c>, the other half of the pair
    /// above and refused for the same reason.
    /// </summary>
    public unsafe bool TryGetLightForPointFast(
        void* engineClient, in Vector position, bool clamp, out Vector light)
        => TryLightForPoint(fast: true, engineClient, position, clamp, out light);

    // plan_client_vtables CC7: the two slots are routed (Slot_/Has_), so the
    // choice between them is a flag rather than a struct field passed along.
    private unsafe bool TryLightForPoint(
        bool fast, void* engineClient, in Vector position, bool clamp, out Vector light)
    {
        light = default;

        if (!s_Initialized || engineClient == null || !(fast ? ClientAttached : ClientAttached))
            return false;

        float* input = stackalloc float[3] { position.X, position.Y, position.Z };
        float* answer = stackalloc float[3];

        // NOT WRITTEN ON A MISS, deliberately: native leaves the buffer alone
        // when it refuses, so "the light here is black" and "there was no
        // engine" stay different answers.
        answer[0] = float.NaN;

        if (fast)
            ClientShims.GetLightForPointFast(engineClient, input, Vtable.Bit(clamp), answer);
        else
            ClientShims.GetLightForPoint(engineClient, input, Vtable.Bit(clamp), answer);

        if (float.IsNaN(answer[0]))
            return false;

        light = new Vector(answer[0], answer[1], answer[2]);
        return true;
    }

    /// <summary>
    /// Which skin of a model is drawn. Silently does nothing for an entity with
    /// no animating model, which is what stock does too. Client only: the
    /// server's body was empty and went in plan_server_native_v2 T2, so a
    /// skin chosen on the server reaches no client (docs/todo.md).
    /// </summary>
    public void EntitySetSkin(EntityHandle entity, int skin)
    {
        if (s_Initialized && ClientAttached)
            ClientShims.EntitySetSkin(entity.Value, skin);
    }

    /// <summary>
    /// Makes, or updates, the sprite trail's renderable for a managed entity:
    /// the render mode (refused outside RenderMode_t, and then nothing is
    /// made) and whether it is switched on.
    ///
    /// <para>
    /// The ring itself, and every number it is drawn with, is managed
    /// (ClientSpriteTrails, lane c2-fx): the renderable ticks and draws it by
    /// handle through IManagedFxClient.
    /// </para>
    ///
    /// <para>
    /// The position is not passed. Native sweeps it in PreRender, because a
    /// trail is by definition something that FOLLOWS and a managed entity that
    /// only moved delivers no data change at all.
    /// </para>
    /// </summary>
    public void SpriteTrailUpdate(EntityHandle owner, int renderMode, bool visible)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.SpriteTrailUpdate(owner.Value, renderMode, visible ? 1 : 0);
    }

    /// <summary>The trail goes when its managed entity does.</summary>
    public void SpriteTrailDestroy(EntityHandle owner)
    {
        if (s_Initialized && ClientAttached)
            ClientShims.SpriteTrailDestroy(owner.Value);
    }

    /// <summary>
    /// Declares that managed code draws this entity itself, and the box it
    /// occupies while doing it. Client-side.
    ///
    /// <para>
    /// Pushed rather than asked for: the bounds change only when the thing being
    /// drawn changes, while the engine asks for them every frame.
    /// </para>
    /// </summary>
    public void StandaloneSetCustomDraw(EntityHandle entity, Vector mins, Vector maxs)
    {
        if (!s_Initialized || !ClientAttached) return;

        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };

        ClientShims.StandaloneSetCustomDraw(entity.Value, lo, hi);
    }

    /// <summary>
    /// How an entity that draws ITSELF is sorted, blended and depth-tested.
    ///
    /// <para>
    /// <see cref="StandaloneSetCustomDraw"/> says WHERE such an entity draws;
    /// this says HOW. Three answers a stock <c>C_BaseEntity</c> derives from its
    /// render mode and the parallel hierarchy has no member to derive:
    /// which of the renderer's two lists it belongs in, whether it is drawn
    /// depth-tested, and its constant alpha.
    /// </para>
    ///
    /// <para>
    /// <paramref name="translucent"/> is not cosmetic. A renderable in the
    /// opaque list is drawn by <c>DrawOpaqueRenderable</c>, which never sets a
    /// blend — an additive sprite left there is drawn at full strength and never
    /// reaches the depth split <paramref name="ignoreZ"/> selects.
    /// </para>
    /// </summary>
    public void StandaloneSetBlending(EntityHandle entity, bool translucent,
                                             bool ignoreZ, int alpha)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.StandaloneSetBlending(entity.Value, translucent ? 1 : 0, ignoreZ ? 1 : 0, alpha);
    }

    /// <summary>
    /// Overrules the model about whether this entity casts a shadow.
    /// Client-side.
    ///
    /// <para>
    /// <see cref="ShadowCastChoice.Auto"/> is the stock rule and is right for
    /// nearly everything: a studio model casts, a brush model and a sprite do
    /// not. The two overrides are for the cases stock also has one for —
    /// EF_NOSHADOW and prop_dynamic's <c>disableshadows</c> are both
    /// <see cref="ShadowCastChoice.Never"/>, and
    /// <see cref="ShadowCastChoice.Simple"/> is the only way an entity that
    /// draws ITSELF can cast anything, since it has no studio header to be
    /// judged by.
    /// </para>
    ///
    /// <para>
    /// Neither override can make an entity cast that is not being drawn, nor
    /// make the viewmodel cast. Both are refused in
    /// <c>shadow_cast_policy.h</c> before the choice is read, and for the same
    /// reason: a shadow under something the viewer cannot see has no object to
    /// explain it.
    /// </para>
    /// </summary>
    public void StandaloneSetShadowCast(EntityHandle entity, ShadowCastChoice choice)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.StandaloneSetShadowCast(entity.Value, (int)choice);
    }

    /// <summary>
    /// What this entity is casting right now, what it was asked to cast, and
    /// whether the client shadow manager is holding a handle for it at all.
    /// Client-side.
    ///
    /// <para>
    /// The third fact is the one Phase 5's gate needed and could not get. The
    /// shell's <c>ShadowCastType()</c> answered SHADOWS_SIMPLE for every drawn
    /// entity while <c>GetShadowHandle()</c> was hard-coded to invalid, so the
    /// manager held nothing and projected nothing — the entity reported that it
    /// cast a shadow and cast none, and the only thing in the game that could
    /// tell the difference was a photograph of the floor.
    /// </para>
    /// </summary>
    public unsafe ShadowCast StandaloneShadowCast(EntityHandle entity,
                                                         out ShadowCastChoice asked,
                                                         out bool hasShadow)
    {
        asked = ShadowCastChoice.Auto;
        hasShadow = false;

        if (!s_Initialized || !ClientAttached) return ShadowCast.Unknown;

        int choice = 0;
        int held = 0;
        int cast = ClientShims.StandaloneShadowCast(entity.Value, &choice, &held);

        asked = (ShadowCastChoice)choice;
        hasShadow = held != 0;

        return (ShadowCast)cast;
    }

    /// <summary>
    /// One sprite, this frame only. Answers 1 if anything was drawn.
    ///
    /// <para>
    /// The whole of what a sprite needs from native code: bind the material,
    /// work out three axes from the .vmt's own orientation, build one quad. Every
    /// value it takes is a networked field the managed half already holds
    /// decoded, which is why there is no client-side sprite OBJECT behind this
    /// the way there was a steam jet behind the old SteamJetUpdate slot.
    /// </para>
    ///
    /// <para>
    /// WHERE it is and WHICH WAY IT FACES are not arguments. Native reads both
    /// off the entity, and has to: what it holds is the INTERPOLATED place for
    /// the frame being drawn, and this side could not supply one anyway —
    /// <see cref="EntityGetOrigin"/> is a server-side slot and answers a silent
    /// zero vector on the client.
    /// </para>
    /// </summary>
    public int DrawSprite(EntityHandle owner, int modelIndex,
                                 float frame, int renderMode, int renderFx,
                                 int r, int g, int b, int a,
                                 float scale, bool worldSpaceScale,
                                 float hdrColorScale, float glowProxySize)
    {
        if (!s_Initialized || !ClientAttached) return 0;

        return ClientShims.DrawSprite(owner.Value, modelIndex, frame, renderMode, renderFx,
                              r, g, b, a, scale, worldSpaceScale ? 1 : 0,
                              hdrColorScale, glowProxySize);
    }

    /// <summary>
    /// One sprite, this frame only, a little away from its owner's origin.
    ///
    /// <para>
    /// <see cref="DrawSprite"/> with one argument added, and the argument is
    /// the whole reason it exists: a plasma flame is SEVEN sprites belonging to
    /// one entity — six in a ring whose radius follows the flame's scale, and a
    /// glow beneath them that rises as it grows. Stock gave each of those seven
    /// its own <c>C_Sprite</c> held as a member; a managed entity can hold none,
    /// and would not want seven client entities per fire to draw seven quads
    /// that depend on nothing but this frame's scale.
    /// </para>
    ///
    /// <para>
    /// The offset is in WORLD axes rather than the owner's, which is stock's
    /// choice too: the ring is laid out around the vertical and every flame
    /// points up, whatever the entity's angles say.
    /// </para>
    /// </summary>
    public int DrawSpriteOffset(EntityHandle owner, Vector offset, int modelIndex,
                                       float frame, int renderMode, int renderFx,
                                       int r, int g, int b, int a,
                                       float scale, bool worldSpaceScale,
                                       float hdrColorScale, float glowProxySize)
    {
        if (!s_Initialized || !ClientAttached) return 0;

        float* o = stackalloc float[3] { offset.X, offset.Y, offset.Z };

        return ClientShims.DrawSpriteOffset(owner.Value, o, modelIndex, frame, renderMode, renderFx,
                                    r, g, b, a, scale, worldSpaceScale ? 1 : 0,
                                    hdrColorScale, glowProxySize);
    }

    /// <summary>
    /// Starts a named particle system on a managed entity, and keeps it at that
    /// entity's position for as long as it runs.
    ///
    /// <para>
    /// The counterpart of <see cref="SpriteTrailUpdate"/> rather than of
    /// <see cref="DrawSprite"/>: a particle system is a simulation that runs
    /// between frames, so something native has to own it. What differs from
    /// stock is that a managed entity cannot host one —
    /// <c>CParticleProperty</c> hangs off <c>CBaseEntity</c> and the parallel
    /// hierarchy has no such thing — so the effect is created ownerless and
    /// driven from the entity's origin by a per-frame sweep.
    /// </para>
    /// </summary>
    /// <returns>true if a particle system by that name was found and started.</returns>
    public bool ParticleSystemStart(EntityHandle owner, string name)
    {
        if (!s_Initialized || !ClientAttached || name.Length == 0) return false;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { return ClientShims.ParticleSystemStart(owner.Value, p) != 0; }
    }

    /// <summary>
    /// Stops the particle system belonging to a managed entity.
    ///
    /// The particles already in the air finish rather than blinking away, which
    /// is a second or two of fire burning out and is what stock's
    /// <c>~C_FireSmoke</c> asks for.
    /// </summary>
    public void ParticleSystemStop(EntityHandle owner)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.ParticleSystemStop(owner.Value);
    }

    /// <summary>
    /// Which of a running particle system's control points are kept at the
    /// entity that owns it, as a bitmask — bit 0 is control point 0.
    ///
    /// <para>
    /// <see cref="ParticleSystemStart"/> feeds control point 0 and nothing
    /// else, and for every system this build had started before the flame that
    /// was the whole truth: all eight <c>env_fire_*</c> systems reference
    /// control point 0 alone. <c>burning_character</c> reads control point 1 as
    /// well, through a "Position on Model Random" that falls back to the
    /// control point's POSITION when it has no model — and an unfed control
    /// point is <c>(0, 0, 0)</c>, so the whole flame emits at the centre of the
    /// map rather than at what is burning.
    /// </para>
    /// </summary>
    /// <returns>true if the entity had a running system to configure.</returns>
    public bool ParticleSystemFollowPoints(EntityHandle owner, int pointMask)
    {
        if (!s_Initialized || !ClientAttached) return false;

        return ClientShims.ParticleSystemFollowPoints(owner.Value, pointMask) != 0;
    }

    /// <summary>
    /// Sends <paramref name="payload"/> to the server this client is connected
    /// to, as a SourceSharp net message under <paramref name="key"/>
    /// (ss_client_message.h, reliable). Client-side. False when there is no
    /// channel, or the key (1-63 bytes) or payload (up to 4 KiB) does not fit.
    /// The server hands it to <c>IClientLifecycle.OnClientMessage</c>.
    /// </summary>
    public bool SendServerMessage(string key, ReadOnlySpan<byte> payload)
    {
        if (!s_Initialized || !ClientAttached || key.Length == 0) return false;

        var keyBytes = Encoding.UTF8.GetBytes(key + "\0");

        fixed (byte* pKey = keyBytes)
        fixed (byte* pData = payload)
        {
            return ClientShims.SendServerMessage(pKey, pData, payload.Length) != 0;
        }
    }

    /// <summary>
    /// Pushes one block of the client's world render state into the subsystem
    /// that reads it.
    ///
    /// <para>
    /// The three callers are the client halves of <c>shadow_control</c>,
    /// <c>water_lod_control</c> and <c>env_tonemap_controller</c>, and each
    /// calls this and nothing else: everything about what the numbers ARE is
    /// decided on the server, networked, and decoded before this is reached.
    /// See ClientSetWorldRenderState in sourcesharp.h for the block each
    /// selector expects.
    /// </para>
    ///
    /// <para>
    /// Client only. On the server the slot is null and this does nothing, which
    /// is the same degrade every other one-sided call here takes.
    /// </para>
    /// </summary>
    public void ClientSetWorldRenderState(WorldRenderState which, ReadOnlySpan<float> values)
    {
        if (!s_Initialized || !ClientAttached) return;

        fixed (float* p = values)
        {
            ClientShims.ClientSetWorldRenderState((int)which, p, values.Length);
        }
    }

    /// <summary>
    /// Where the local player is standing, or null when there is not one yet —
    /// which is every frame between connecting and spawning, and is why this
    /// answers nullable rather than a zero vector that reads as the map origin.
    /// </summary>
    public Vector? LocalPlayerOrigin
    {
        get
        {
            if (!s_Initialized || !ClientAttached)
                return null;

            float* xyz = stackalloc float[3];

            return ClientShims.GetLocalPlayerOrigin(xyz) == 0
                ? null
                : new Vector(xyz[0], xyz[1], xyz[2]);
        }
    }

    /// <summary>
    /// Loads a colour-correction lookup and answers a handle, or 0 if the file
    /// would not load.
    /// </summary>
    public int ColorCorrectionAdd(string lookupFile)
    {
        if (!s_Initialized || !ClientAttached) return 0;

        var bytes = Encoding.UTF8.GetBytes(lookupFile + "\0");
        fixed (byte* p = bytes)
        {
            return ClientShims.ClientColorCorrection((int)ColorCorrectionOp.Add, 0, p, 0f);
        }
    }

    /// <summary>
    /// How much of a loaded lookup to apply, 0..1. Pushed every frame while the
    /// entity lives, because the weight is a function of where the player is
    /// standing and that changes on its own.
    /// </summary>
    public void ColorCorrectionSetWeight(int handle, float weight)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.ClientColorCorrection((int)ColorCorrectionOp.SetWeight, handle, null, weight);
    }

    /// <summary>
    /// Releases a lookup. Not optional: the manager holds it until told, and a
    /// colour correction whose entity is gone would tint the map forever.
    /// </summary>
    public void ColorCorrectionRemove(int handle)
    {
        if (!s_Initialized || !ClientAttached) return;

        ClientShims.ClientColorCorrection((int)ColorCorrectionOp.Remove, handle, null, 0f);
    }

    /// <summary>
    /// States what fog the viewer is standing in — plan_entity.md step
    /// 8e-render. CLIENT ONLY; the slot is null on the server.
    ///
    /// <para>
    /// A push for the same reason <see cref="SetSkyboxParams"/> is one, from the
    /// other end: the values change when a mapper's entity says so, and
    /// <c>viewrender.cpp</c> asks for them several times per frame across four
    /// call sites. The native side keeps the last thing pushed and hands back a
    /// pointer to it.
    /// </para>
    /// </summary>
    public void SetViewerFogParams(in FogParams parameters)
    {
        if (!s_Initialized || !ClientAttached) return;

        fixed (FogParams* p = &parameters) { ClientShims.SetViewerFogParams(p); }
    }

    /// <summary>
    /// Says this viewer has no fog controller, which is NOT the same as saying
    /// its fog is off.
    ///
    /// <para>
    /// <c>SourceSharp_ViewerFogParams</c> then answers NULL — the early-out
    /// stock takes for a player with no fog controller — rather than a zeroed
    /// <c>fogparams_t</c>, which every caller would read as a real "no fog".
    /// Step 7c's whole objection to a zeroed struct was that a screenshot cannot
    /// tell "this map has no fog" from "nobody asked"; this keeps the two apart.
    /// </para>
    /// </summary>
    public void ClearViewerFogParams()
    {
        if (!s_Initialized || !ClientAttached) return;
        ClientShims.SetViewerFogParams(null);
    }

    /// <summary>
    /// States what 3D skybox the viewer is shown — the <c>defect-skybox</c>
    /// lane. CLIENT ONLY; the slot is null on the server.
    ///
    /// <para>
    /// The other end of <see cref="SetSkyboxParams"/> and the same
    /// <see cref="SkyboxParams"/>: one map's sky camera making one round trip,
    /// pushed on the server and landing here. A push rather than an answer for
    /// the reason that one is: the values change when a mapper's entity says
    /// so, and <c>viewrender.cpp</c> asks for them several times a frame across
    /// eight call sites.
    /// </para>
    /// </summary>
    public void SetViewerSkyboxParams(in SkyboxParams parameters)
    {
        if (!s_Initialized || !ClientAttached) return;

        fixed (SkyboxParams* p = &parameters) { ClientShims.SetViewerSkyboxParams(p); }
    }

    /// <summary>
    /// Says this viewer has no sky camera, which is NOT the same as saying its
    /// sky camera is in area 255.
    ///
    /// <para>
    /// <c>SourceSharp_ViewerSkyboxParams</c> then answers NULL — the early-out
    /// stock's <c>CSkyboxView</c> takes for a player with no sky camera —
    /// rather than a zeroed <c>sky3dparams_t</c>, whose area of 0 is a REAL
    /// visleaf area and would send the renderer at a camera that is not there.
    /// Area 255 is the different fact that a camera IS there and sits in the
    /// engine's "nowhere"; <c>PreRender3dSkyboxWorld</c> refuses that on its own
    /// line, one line after the NULL.
    /// </para>
    /// </summary>
    public void ClearViewerSkyboxParams()
    {
        if (!s_Initialized || !ClientAttached) return;
        ClientShims.SetViewerSkyboxParams(null);
    }

    /// <summary>
    /// Places or moves one dynamic light. Client-side.
    ///
    /// <para>
    /// The engine keeps a small fixed pool keyed by <see cref="DynamicLight.Key"/>,
    /// and asking again with the same key returns the same light — which is how
    /// a light follows something instead of leaving one behind every frame.
    /// </para>
    /// </summary>
    public void DynamicLightUpdate(in DynamicLight light)
    {
        if (!s_Initialized || !ClientAttached) return;

        fixed (DynamicLight* p = &light) { ClientShims.DynamicLightUpdate(p); }
    }

    /// <summary>
    /// Puts a light out now.
    ///
    /// <para>
    /// Not the same as ceasing to refresh it: a light burns for whatever its
    /// last <see cref="DynamicLight.LiveFor"/> said, and an entity that has been
    /// removed wants its light gone in the same frame.
    /// </para>
    /// </summary>
    public void DynamicLightRelease(int key)
    {
        if (!s_Initialized || !ClientAttached) return;
        ClientShims.DynamicLightRelease(key);
}

    /// <summary>Turns one of the map's compiled occluders on or off. Client-side.</summary>
    public void ActivateOccluder(int occluderIndex, bool active)
    {
        if (s_Initialized && ClientAttached)
            ClientShims.ActivateOccluder(occluderIndex, active ? 1 : 0);
    }

    /// <summary>
    /// Which of stock's two distance-fade rules this entity follows, and over
    /// what band. Client-side.
    ///
    /// <para>
    /// Two rules rather than one because stock has two and they disagree about
    /// the curve AND about what the distance is measured to — see
    /// <see cref="FadeStyle"/>.
    /// </para>
    /// </summary>
    public void StandaloneSetFade(EntityHandle entity, FadeStyle style,
                                         float startDistance, float endDistance,
                                         float minimumAlpha = 0f)
    {
        if (s_Initialized && ClientAttached)
            ClientShims.StandaloneSetFade(entity.Value, (int)style, startDistance, endDistance, minimumAlpha);
    }

    /// <summary>
    /// A second brush model drawn at this entity's transform, after its own and
    /// at full opacity. Client-side; a negative index clears it.
    /// </summary>
    public void StandaloneSetOverlayModel(EntityHandle entity, int modelIndex)
    {
        if (s_Initialized && ClientAttached)
            ClientShims.StandaloneSetOverlayModel(entity.Value, modelIndex);
    }

    /// <summary>
    /// True when a client entity is DORMANT — out of the local player's PVS,
    /// so every value it holds is the last one that arrived rather than the
    /// current one. Client-side.
    ///
    /// <para>
    /// Ask this before believing any client-side reading, and print it in any
    /// report. A dormant entity answers a query without complaint, and that is
    /// not hypothetical: a viewmodel whose bounds sat below the floor (v_
    /// models are authored in CAMERA space) was culled, went dormant, and
    /// carried on answering plausible stale numbers to everything that asked.
    /// </para>
    /// </summary>
    public bool StandaloneIsDormant(EntityHandle entity)
    {
        if (!s_Initialized || !ClientAttached) return false;

        return ClientShims.StandaloneIsDormant(entity.Value) != 0;
    }

    public void ClientEmitSound(EntityHandle entity, string soundName, Vector? origin = null)
    {
        if (!s_Initialized || !ClientAttached || soundName.Length == 0) return;

        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");

        if (origin is Vector at)
        {
            float* xyz = stackalloc float[3];
            xyz[0] = at.X;
            xyz[1] = at.Y;
            xyz[2] = at.Z;

            fixed (byte* p = bytes) ClientShims.ClientEmitSound(entity.Value, p, xyz);
        }
        else
        {
            fixed (byte* p = bytes) ClientShims.ClientEmitSound(entity.Value, p, null);
        }
    }

    // SoundEntQuery AND SoundEntInsert WERE HERE.
    //
    // plan_gamedll_after.md Phase A3, second pair, removed both slots and these
    // two wrappers with them. CSoundEnt's pool is SourceSharp.Server's SoundEnt,
    // so the two callers -- env_microphone and ai_sound -- resolve
    // IServerSoundEnt and call it. Nothing crosses the boundary for a list that
    // lives on this side of it.

    /// <summary>
    /// One titles.txt entry by name, or null when the level has no such entry.
    ///
    /// Client-side: the table is the client's, and it holds the placement and
    /// the timing as well as the words — a titles.txt message is a whole
    /// game_text, not a string.
    /// </summary>
    public TextMessage? HudTextMessageGet(string name)
    {
        if (!s_Initialized || !ClientAttached || name.Length == 0)
            return null;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        TextMessage message = default;

        fixed (byte* p = bytes)
        {
            if (ClientShims.HudTextMessageGet(p, &message) == 0)
                return null;
        }

        return message;
    }

    //-------------------------------------------------------------------------
    // What the stock user-message hooks did, carried out over values C# parsed
    // (the user-message payload model): the client's view effects, rumble,
    // viewport and voice status are native state. Client-side only.
    //-------------------------------------------------------------------------

    /// <summary>The two facts MsgFunc_VGUIMenu read off native state, for a decision made here.</summary>
    public ViewportFacts ClientViewportQuery(string panel)
    {
        if (!s_Initialized || !ClientAttached || panel.Length == 0)
            return ViewportFacts.None;

        var bytes = Encoding.UTF8.GetBytes(panel + "\0");
        fixed (byte* p = bytes)
            return (ViewportFacts)ClientShims.ClientViewportQuery(p);
    }

    /// <summary>
    /// MsgFunc_VGUIMenu's effects on the viewport, each one asked for in
    /// <paramref name="actions"/>: the data travels as name, NUL, value, NUL.
    /// </summary>
    public void ClientViewportShow(string panel, bool show, IReadOnlyList<KeyValuePair<string, string>> data,
                                   ViewportActions actions)
    {
        if (!s_Initialized || !ClientAttached || panel.Length == 0)
            return;

        var pairs = new List<byte>();
        foreach (var (name, value) in data)
        {
            pairs.AddRange(Encoding.UTF8.GetBytes(name));
            pairs.Add(0);
            pairs.AddRange(Encoding.UTF8.GetBytes(value));
            pairs.Add(0);
        }

        var nameBytes = Encoding.UTF8.GetBytes(panel + "\0");
        var pairBytes = pairs.ToArray();
        fixed (byte* n = nameBytes)
        fixed (byte* d = pairBytes)
            ClientShims.ClientViewportShow(n, show ? 1 : 0, d, pairBytes.Length, (int)actions);
    }

    /// <summary>
    /// The key this player has bound to a command, or an empty string when
    /// nothing is. Client-side — the server has no idea what anyone's keyboard
    /// looks like.
    /// </summary>
    public string HudLookupBinding(string binding)
    {
        if (!s_Initialized || !ClientAttached || binding.Length == 0)
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(binding + "\0");
        byte* buffer = stackalloc byte[64];

        fixed (byte* p = bytes)
            ClientShims.HudLookupBinding(p, buffer, 64);

        return FromUtf8(buffer);
    }

    /// <summary>
    /// A "#Token" through the localisation tables. Answers with the token
    /// unchanged when there is no entry, so a caller can always draw the result.
    /// </summary>
    public string HudLocalise(string token)
    {
        if (!s_Initialized || !ClientAttached || token.Length == 0)
            return token;

        var bytes = Encoding.UTF8.GetBytes(token + "\0");
        byte* buffer = stackalloc byte[512];

        fixed (byte* p = bytes)
            ClientShims.HudLocalise(p, buffer, 512);

        return FromUtf8(buffer);
    }

    /// <summary>
    /// Every client shell re-evaluates whether the leaf system should be
    /// showing it. Answers how many did.
    ///
    /// <para>
    /// <b>CLIENT ONLY</b>, and zero on the server, where the slot is null.
    /// Visibility there is the PVS's question and a different one.
    /// </para>
    ///
    /// <para>
    /// This is what lets a rendering toggle whose whole effect is its change
    /// callback live in managed code — <c>vcollide_wireframe</c>, which turns
    /// nothing on by itself and instead forces every renderable to re-evaluate.
    /// It answers a count for <see cref="EntitiesInvalidateMdlCacheAll"/>'s
    /// reason: the effect is on the NEXT frame, so nothing about the call
    /// itself is observable and a body that swept nothing would be
    /// indistinguishable from one that swept everything.
    /// </para>
    /// </summary>
    /// <returns>how many shells re-evaluated.</returns>
    public int EntitiesUpdateVisibilityAll()
    {
        if (!s_Initialized || !ClientAttached) return 0;
        return ClientShims.EntitiesUpdateVisibilityAll();
    }

    /// <summary>
    /// Traces a line through the CLIENT's world.
    ///
    /// <para>
    /// A SLOT OF ITS OWN rather than a client arm of <see cref="TraceLine"/>,
    /// and the reasons are in that order of weight. The server's trace answers
    /// no <see cref="ClientTraceResult.Hitbox"/>, and hitbox is the only field
    /// the first caller reads — a projected decal branches on it and nothing
    /// else. This side answers from the client's own copy of the BSP, which is
    /// the copy that decides what the client draws. And what it hit comes back
    /// as an entity INDEX, never an <see cref="EntityHandle"/>: reading a
    /// handle back through <c>CBaseHandle::Get()</c> on this side is behaviour
    /// ledger row C8, which segfaulted a live client.
    /// </para>
    ///
    /// <para>
    /// NO IGNORE ENTITY. The native side traces with
    /// <c>CTraceFilterHitAll</c>, which is stock's filter at the only place on
    /// this side that traces at all. Gaining an ignore would mean
    /// <c>CTraceFilterSimple</c>, which also brings collision groups and
    /// gamerules with it — so a caller passing "nothing to ignore" would get a
    /// different trace than one that passed nothing at all.
    /// </para>
    /// </summary>
    public ClientTraceResult ClientTraceLine(Vector start, Vector end, TraceMask mask)
        => ClientTraceLine(start, end, mask, TraceFilter.HitAll);
}

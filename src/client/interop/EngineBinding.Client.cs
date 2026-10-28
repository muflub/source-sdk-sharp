using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

// The client's half of the engine bindings (plan_shim_areas_split phase 7): the
// members of the shared seams that only the client's engine serves, forwarded
// as in EngineBinding.cs, and the server's members, answered as a client
// engine answered them, from the server's attach guard, which is never
// attached here. Each class's other part, with its interface, is in
// src/shared/interop/EngineBinding.cs.

internal sealed unsafe partial class EngineBrushMovement
{
    private readonly IStandaloneClientArea _standaloneClientArea;

    public EngineBrushMovement(EngineSide side, IStandaloneClientArea standaloneClientArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _standaloneClientArea = standaloneClientArea ?? throw new ArgumentNullException(nameof(standaloneClientArea));
    }

    //-------------------------------------------------------------------------
    // Movement. What a trigger does to whoever is standing in it.
    //-------------------------------------------------------------------------

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void Spin(EntityHandle entity, Vector degreesPerSecond) { }
}

internal sealed unsafe partial class EngineConsole
{
    private readonly ILog _log;

    public EngineConsole(EngineSide side, ILog log, IConsoleArea consoleArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _consoleArea = consoleArea ?? throw new ArgumentNullException(nameof(consoleArea));
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void SendClientCommand(EntityHandle player, string command) { }
    // The server's members, as a client engine answers them: the client has
    // no IVEngineServer, and nothing on the client asks these.
    public void ExecuteServerCommand(string command) { }
}

internal sealed unsafe partial class EngineDamage
{
    public EngineDamage()
    {
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType) { }
    public void ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType, Vector force, Vector position) { }
    public void RegisterRules(DamageRuleMasks masks) { }
    public void TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction = null, Vector forceOrigin = default, bool physicsOnly = false) { }
}

internal sealed unsafe partial class EngineDiagnostics
{
    public EngineDiagnostics()
    {
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public int PlayerShadowCounts(Span<int> into) => 0;
    public int ExitWatchArm(string recordPath) => 0;
    public int ExitWatchRead(Span<int> into) => 0;
    public int ExitWatchDisarm() => 0;
}

internal sealed unsafe partial class EngineEffects
{
    private readonly IEffectsRenderClientArea _effectsRenderClientArea;
    private readonly ITempEntBackendsClientArea _tempEntBackendsClientArea;
    private readonly ITempEntPoolClientArea _tempEntPoolClientArea;

    public EngineEffects(EngineSide side,
                         IEffectsRenderClientArea effectsRenderClientArea,
                         IMovementArea movementArea,
                         ITempEntBackendsClientArea tempEntBackendsClientArea,
                         ITempEntPoolClientArea tempEntPoolClientArea)
    {
        _effectsRenderClientArea = effectsRenderClientArea ?? throw new ArgumentNullException(nameof(effectsRenderClientArea));
        _movementArea = movementArea ?? throw new ArgumentNullException(nameof(movementArea));
        _tempEntBackendsClientArea = tempEntBackendsClientArea ?? throw new ArgumentNullException(nameof(tempEntBackendsClientArea));
        _tempEntPoolClientArea = tempEntPoolClientArea ?? throw new ArgumentNullException(nameof(tempEntPoolClientArea));
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
    public void ClientEnergySplash(Vector position, Vector direction, bool explosive)
    {
        float* pPos = stackalloc float[3];
        pPos[0] = position.X;
        pPos[1] = position.Y;
        pPos[2] = position.Z;

        float* pDir = stackalloc float[3];
        pDir[0] = direction.X;
        pDir[1] = direction.Y;
        pDir[2] = direction.Z;

        _tempEntBackendsClientArea.ClientEnergySplash(pPos, pDir, explosive ? 1 : 0);
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
    public void ClientSparks(Vector position, int magnitude, int trailLength, Vector? direction = null)
    {
        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        if (direction is Vector dir)
        {
            float* pDir = stackalloc float[3];
            pDir[0] = dir.X; pDir[1] = dir.Y; pDir[2] = dir.Z;

            _tempEntBackendsClientArea.ClientSparks(pPos, magnitude, trailLength, pDir);
            return;
        }

        _tempEntBackendsClientArea.ClientSparks(pPos, magnitude, trailLength, null);
    }

    /// <summary>
    /// A ricochet: metal sparks AND the sound. <c>g_pEffects-&gt;Ricochet</c>.
    /// </summary>
    public void ClientRicochet(Vector position, Vector direction)
    {
        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        float* pDir = stackalloc float[3];
        pDir[0] = direction.X; pDir[1] = direction.Y; pDir[2] = direction.Z;

        _tempEntBackendsClientArea.ClientRicochet(pPos, pDir);
    }

    /// <summary>
    /// The same sparks with NO SOUND, which is the whole difference from
    /// <see cref="ClientRicochet"/> and is stock's own comment on the
    /// interface. <c>g_pEffects-&gt;MetalSparks</c>, which is
    /// <c>FX_MetalSpark</c>.
    /// </summary>
    public void ClientMetalSparks(Vector position, Vector direction)
    {
        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        float* pDir = stackalloc float[3];
        pDir[0] = direction.X; pDir[1] = direction.Y; pDir[2] = direction.Z;

        _tempEntBackendsClientArea.ClientMetalSparks(pPos, pDir);
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
    public void ClientSmoke(Vector origin, int modelIndex, float scale, float frameRate)
    {
        float* pOrigin = stackalloc float[3];
        pOrigin[0] = origin.X; pOrigin[1] = origin.Y; pOrigin[2] = origin.Z;

        _tempEntBackendsClientArea.ClientSmoke(pOrigin, modelIndex, scale, frameRate);
    }

    /// <summary>
    /// A muzzle flash. <c>g_pEffects-&gt;MuzzleFlash</c>, which switches on the
    /// type over <c>FX_MuzzleEffect</c>, <c>FX_GunshipMuzzleEffect</c> and
    /// <c>FX_StriderMuzzleEffect</c> and prints a line for anything else —
    /// stock's own behaviour, left alone rather than turned into a silent
    /// default.
    /// </summary>
    public void ClientMuzzleFlash(Vector origin, Vector angles, float scale, int type)
    {
        float* pOrigin = stackalloc float[3];
        pOrigin[0] = origin.X; pOrigin[1] = origin.Y; pOrigin[2] = origin.Z;

        float* pAngles = stackalloc float[3];
        pAngles[0] = angles.X; pAngles[1] = angles.Y; pAngles[2] = angles.Z;

        _tempEntBackendsClientArea.ClientMuzzleFlash(pOrigin, pAngles, scale, type);
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
    public void ClientDecalShoot(int decalIndex, int entIndex, Vector position, Vector? sAxis = null, int flags = 0)
    {
        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        if (sAxis is Vector axis)
        {
            float* pAxis = stackalloc float[3];
            pAxis[0] = axis.X; pAxis[1] = axis.Y; pAxis[2] = axis.Z;

            _tempEntBackendsClientArea.ClientDecalShoot(decalIndex, entIndex, pPos, pAxis, flags);
            return;
        }

        _tempEntBackendsClientArea.ClientDecalShoot(decalIndex, entIndex, pPos, null, flags);
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
    public void ClientPlayerDecalShoot(Vector position, int player, int entIndex)
    {
        float* pPos = stackalloc float[3];
        pPos[0] = position.X; pPos[1] = position.Y; pPos[2] = position.Z;

        _tempEntBackendsClientArea.ClientPlayerDecalShoot(pPos, player, entIndex);
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
    public int ClientDecalIndexFromName(string name)
    {
        if (name.Length == 0) return -1;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { return _tempEntBackendsClientArea.ClientDecalIndexFromName(p); }
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
    public void ClientDecalStaticProp(Vector start, Vector end, int staticPropIndex, int decalIndex)
    {
        float* pStart = stackalloc float[3];
        pStart[0] = start.X; pStart[1] = start.Y; pStart[2] = start.Z;

        float* pEnd = stackalloc float[3];
        pEnd[0] = end.X; pEnd[1] = end.Y; pEnd[2] = end.Z;

        _tempEntBackendsClientArea.ClientDecalStaticProp(pStart, pEnd, staticPropIndex, decalIndex);
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
    public bool ClientIsLowViolence()
    {
        return _tempEntBackendsClientArea.ClientIsLowViolence() != 0;
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
    public string ClientDecalNameFromIndex(int decalIndex)
    {
        // 260, which is what every other string slot on this ABI gives itself.
        // A decal name is a material path.
        const int bufSize = 260;
        byte* buf = stackalloc byte[bufSize];

        return _tempEntBackendsClientArea.ClientDecalNameFromIndex(decalIndex, buf, bufSize) > 0 ? EngineSide.FromUtf8(buf) : string.Empty;
    }

    /// <summary>
    /// A decal on a STUDIO model — <c>C_BaseEntity::AddStudioDecal</c>, which
    /// is <c>modelrender-&gt;AddDecal</c> on the entity's own model instance.
    /// BEHAVIOUR LEDGER ROWS C2 AND C3's studio arm.
    ///
    /// <para>
    /// A START AND A DELTA, NOT TWO POINTS. Stock builds the <c>Ray_t</c> inside
    /// <c>AddDecal</c> and then widens its DELTA by a tenth
    /// (<c>ray.m_Delta *= 1.1f</c>); managed does that multiply
    /// (ClientEntityDecal.AddStudioDecal, lane M1-C) and hands the delta over,
    /// because handing the lengthened ENDPOINT and letting <c>Ray_t::Init</c>
    /// subtract would give the engine <c>(start + d * 1.1f) - start</c>, which
    /// is not <c>d * 1.1f</c>.
    /// </para>
    ///
    /// <para>
    /// No hitbox: <c>AddStudioDecal</c> takes one and never reads it. No LOD
    /// limit: both of stock's call sites take <c>ADDDECAL_TO_ALL_LODS</c> by
    /// default. <c>r_drawmodeldecals</c> is managed's to test before this
    /// (DecalSettings).
    /// </para>
    /// </summary>
    public void ClientDecalStudioModel(int entIndex, Vector rayStart, Vector rayDelta, int decalIndex)
    {
        float* pStart = stackalloc float[3];
        pStart[0] = rayStart.X; pStart[1] = rayStart.Y; pStart[2] = rayStart.Z;

        float* pDelta = stackalloc float[3];
        pDelta[0] = rayDelta.X; pDelta[1] = rayDelta.Y; pDelta[2] = rayDelta.Z;

        _tempEntBackendsClientArea.ClientDecalStudioModel(entIndex, pStart, pDelta, decalIndex);
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
    public void ClientDecalBrushModel(int entIndex, Vector decalCenter, int decalIndex)
    {
        float* pCenter = stackalloc float[3];
        pCenter[0] = decalCenter.X; pCenter[1] = decalCenter.Y; pCenter[2] = decalCenter.Z;

        _tempEntBackendsClientArea.ClientDecalBrushModel(entIndex, pCenter, decalIndex);
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
    public bool TempEntPoolReadStatics(int slot, out TempEntStatics statics)
    {
        statics = default;

        fixed (TempEntStatics* p = &statics)
        {
            return _tempEntPoolClientArea.TempEntPoolReadStatics(slot, p) != 0;
        }
    }

    /// <summary>
    /// <c>GetWindspeedAtTime</c> — the client's current wind velocity, which is
    /// what <c>FTENT_WINDBLOWN</c> accelerates towards. The zero vector with no
    /// engine and on a map with no <c>env_wind</c>, which are the same answer
    /// the engine's own implementation gives.
    /// </summary>
    public Vector ClientWindSpeed(float time)
    {
        float* xyz = stackalloc float[3] { 0f, 0f, 0f };

        _tempEntPoolClientArea.ClientWindSpeed(time, xyz);

        return new Vector(xyz[0], xyz[1], xyz[2]);
    }

    //-------------------------------------------------------------------------
    // THE PARTICLE-SYSTEM TEMP ENTITIES (C-Phase 3, lane CP).
    //
    // Eight named primitives, not one ClientParticleTeSpawn(kind, params): the
    // seven TEs on DT_TEParticleSystem share a base TABLE and nothing else.
    // Sparks needs none of them — ClientSparks above already is its primitive,
    // which is why there are eight for seven.
    //-------------------------------------------------------------------------


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
    public int TempEntPoolCapacity()
    {
        return _tempEntPoolClientArea.TempEntPoolCapacity();
    }

    /// <summary>
    /// Give one back: <c>RemoveFromLeafSystem</c>, <c>OnRemoveTempEntity</c>
    /// and the memory pool free, in stock <c>TempEntFree</c>'s order.
    /// </summary>
    public void TempEntPoolDestroy(int slot)
    {
        _tempEntPoolClientArea.TempEntPoolDestroy(slot);
    }

    /// <summary>
    /// Fill <paramref name="life"/> from the live object. False for an empty
    /// slot, and <paramref name="life"/> is then default.
    /// </summary>
    public bool TempEntPoolReadLife(int slot, out TempEntLife life)
    {
        life = default;

        fixed (TempEntLife* pLife = &life)
        {
            return _tempEntPoolClientArea.TempEntPoolReadLife(slot, pLife) != 0;
        }
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
    public bool TempEntPoolWriteLife(int slot, in TempEntLife life)
    {
        fixed (TempEntLife* pLife = &life)
        {
            return _tempEntPoolClientArea.TempEntPoolWriteLife(slot, pLife) != 0;
        }
    }

    /// <summary>
    /// The transform C# owns while it simulates this tempent. Read ONCE, when
    /// the managed pool takes it over — everything after that is integrated on
    /// this side, so this is how C# learns what Prepare and the spawn helper
    /// left behind rather than a per-frame query.
    /// </summary>
    public bool TempEntPoolReadTransform(int slot, out TempEntTransform transform)
    {
        transform = default;

        fixed (TempEntTransform* p = &transform)
        {
            return _tempEntPoolClientArea.TempEntPoolReadTransform(slot, p) != 0;
        }
    }

    /// <summary>
    /// Write the transform back, which is how a C#-simulated tempent gets
    /// drawn. ONCE per tempent per frame; see <see cref="TempEntTransform"/>
    /// for why this is not a set of per-field accessors.
    /// </summary>
    public bool TempEntPoolWriteTransform(int slot, in TempEntTransform transform)
    {
        fixed (TempEntTransform* p = &transform)
        {
            return _tempEntPoolClientArea.TempEntPoolWriteTransform(slot, p) != 0;
        }
    }

    /// <summary>
    /// A CLIENT entity's origin by INDEX. False when the index names no entity
    /// — the ordinary case for a player who has left, where stock simply does
    /// not move the tempent.
    /// </summary>
    public bool ClientEntityOrigin(int entityIndex, out Vector origin)
    {
        origin = Vector.Zero;

        float* xyz = stackalloc float[3];

        if (_tempEntPoolClientArea.ClientEntityOrigin(entityIndex, xyz) == 0) return false;

        origin = new Vector(xyz[0], xyz[1], xyz[2]);
        return true;
    }

    /// <summary>
    /// <c>CTempEnts::AddVisibleTempEntity</c> — put it back in the leaf system
    /// for this frame. False when it cannot be drawn at all, which for stock
    /// means it has no model.
    /// </summary>
    public bool TempEntPoolAddVisible(int slot)
    {
        return _tempEntPoolClientArea.TempEntPoolAddVisible(slot) != 0;
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
    public bool TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority)
    {
        float* xyz = stackalloc float[3] { origin.X, origin.Y, origin.Z };

        return _tempEntPoolClientArea.TempEntPoolCreate(slot, xyz, modelIndex, priority) != 0;
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
    public bool TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn)
    {
        fixed (TempEntSpawn* pSpawn = &spawn)
        {
            return _tempEntPoolClientArea.TempEntPoolWriteSpawn(slot, pSpawn) != 0;
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
    public string EffectDispatchName(int index)
    {
        if (index < 0)
            return string.Empty;

        const int bufSize = 128;
        byte* buf = stackalloc byte[bufSize];
        _tempEntBackendsClientArea.EffectDispatchName(index, buf, bufSize);
        return EngineSide.FromUtf8(buf);
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
    public int DrawSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize)
    {
        return _effectsRenderClientArea.DrawSprite(owner.Value, modelIndex, frame, renderMode, renderFx,
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
    public int DrawSpriteOffset(EntityHandle owner, Vector offset, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize)
    {
        float* o = stackalloc float[3] { offset.X, offset.Y, offset.Z };

        return _effectsRenderClientArea.DrawSpriteOffset(owner.Value, o, modelIndex, frame, renderMode, renderFx,
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
        if (name.Length == 0) return false;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { return _effectsRenderClientArea.ParticleSystemStart(owner.Value, p) != 0; }
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
        _effectsRenderClientArea.ParticleSystemStop(owner.Value);
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
        return _effectsRenderClientArea.ParticleSystemFollowPoints(owner.Value, pointMask) != 0;
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
        _effectsRenderClientArea.SpriteTrailUpdate(owner.Value, renderMode, visible ? 1 : 0);
    }

    /// <summary>The trail goes when its managed entity does.</summary>
    public void SpriteTrailDestroy(EntityHandle owner)
    {
        _effectsRenderClientArea.SpriteTrailDestroy(owner.Value);
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void BloodEffect(Vector origin, Vector direction, BloodColor color, float amount, bool stream = false, BloodSpray sprayFlags = BloodSpray.None) { }
    public void BubbleEffect(EntityHandle entity, int count, float speed) { }
    public void BubbleTrailEffect(Vector from, Vector to, int count) { }
    public void TempEntPlayback(int handle, ReadOnlySpan<byte> blob, TeFilter filter, float delay) { }
    public int EffectDispatchIndex(string name) => 0;
    public void DustEffect(Vector origin, Vector direction, float size, float speed) { }
    public void ImpactEffect(Vector start, Vector end, int surfaceProp, DamageType damageType, EntityHandle entity) { }
    public void FunnelEffect(Vector origin, int spriteIndex, bool reversed) { }
    public void MuzzleFlashEffect(Vector origin, Vector angles, float scale, int type = 1) { }
    public bool PaintDecal(int texture, Vector origin, Vector? angles, float projectDistance, bool permanent, bool lowPriority) => false;
    public void SparkEffect(Vector origin, int magnitude, int trailLength, bool makeSound = true, Vector? direction = null) { }
    public void ExplosionEffect(Vector origin, int modelIndex, float scale, int frameRate, int flags, int radius, int magnitude) { }
    public void SplashEffect(Vector origin, float scale, bool inSlime = false) { }
    public void TracerEffect(Vector start, Vector end) { }
}

internal sealed unsafe partial class EngineEntities
{
    private readonly EntityIoRoutes _entityIo;
    private readonly EntityRemovalRoutes _removal;

    private readonly IViewClientArea _viewClientArea;

    /// <summary>
    /// A side with no client fetches: the server, and every container that
    /// registers none. Each answers nothing, which is exactly what the real
    /// ones answered there (their slots are client-only, so every Has_ was
    /// false).
    /// </summary>
    public EngineEntities(EngineSide side, EntityIoRoutes entityIo, EntityRemovalRoutes removal,
                          IEntitiesArea entitiesArea,
                          IModelsArea modelsArea,
                          IViewClientArea viewClientArea)
        : this(side, entityIo, removal, NoInterpolationBlocks.Instance, NoRenderPolicyBlocks.Instance, NoClientReplayEngine.Instance,
               entitiesArea, modelsArea, viewClientArea)
    {
    }

    /// <summary>
    /// The server's: no client fetches, and its attachment frames answered by
    /// its managed skeletons once the composition fills the route (plan
    /// physics_bones_native_zero step 1.2).
    /// </summary>
    public EngineEntities(EngineSide side, EntityIoRoutes entityIo, EntityRemovalRoutes removal,
                          StandaloneSkeletonRoute skeletons,
                          IEntitiesArea entitiesArea,
                          IModelsArea modelsArea,
                          IViewClientArea viewClientArea)
        : this(side, entityIo, removal, NoInterpolationBlocks.Instance, NoRenderPolicyBlocks.Instance, NoClientReplayEngine.Instance,
               entitiesArea, modelsArea, viewClientArea)
    {
        _skeletons = skeletons ?? throw new ArgumentNullException(nameof(skeletons));
    }

    /// <summary>
    /// The client's: the three held objects read the client's own slots, and
    /// the client composition builds them and hands them in (P1 run 3: they
    /// are client code, so the shared binding names only their interfaces).
    /// EngineServices.Build picks this constructor when the container holds
    /// all three.
    /// </summary>
    public EngineEntities(EngineSide side, EntityIoRoutes entityIo, EntityRemovalRoutes removal,
                          IInterpolationBlocks interpolationBlocks,
                          IRenderPolicyBlocks renderPolicyBlocks,
                          IClientReplayEngine clientReplay,
                          IEntitiesArea entitiesArea,
                          IModelsArea modelsArea,
                          IViewClientArea viewClientArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _entityIo = entityIo ?? throw new ArgumentNullException(nameof(entityIo));
        _removal = removal ?? throw new ArgumentNullException(nameof(removal));
        _interpolationBlocks = interpolationBlocks ?? throw new ArgumentNullException(nameof(interpolationBlocks));
        _renderPolicyBlocks = renderPolicyBlocks ?? throw new ArgumentNullException(nameof(renderPolicyBlocks));
        _clientReplay = clientReplay ?? throw new ArgumentNullException(nameof(clientReplay));
        _entitiesArea = entitiesArea ?? throw new ArgumentNullException(nameof(entitiesArea));
        _modelsArea = modelsArea ?? throw new ArgumentNullException(nameof(modelsArea));
        _viewClientArea = viewClientArea ?? throw new ArgumentNullException(nameof(viewClientArea));
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
    public int UpdateVisibilityAll()
    {
        return _viewClientArea.EntitiesUpdateVisibilityAll();
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void AddBaseVelocity(EntityHandle entity, Vector velocity) { }
    public void ApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround) { }
    public EntityHandle Create(string className) => EntityHandle.None;
    public EntityHandle CreateFromMap(string className, byte[] packedKeys) => EntityHandle.None;
    public Vector GetAngularVelocity(EntityHandle entity) => default;
    public (Vector Mins, Vector Maxs) GetBounds(EntityHandle entity) => (Vector.Zero, Vector.Zero);
    public Vector GetBoundsSize(EntityHandle entity) => default;
    public string GetClassName(EntityHandle h) => string.Empty;
    public Vector GetEyeAngles(EntityHandle h) => default;
    public Vector GetEyePosition(EntityHandle h) => default;
    public int GetHealth(EntityHandle h) => 0;
    public string GetName(EntityHandle entity) => string.Empty;
    public Vector GetSpawnAngles(EntityHandle entity) => default;
    public int GetSpawnFlags(EntityHandle entity) => 0;
    public Vector GetSpawnOrigin(EntityHandle entity) => default;
    public Vector GetVelocity(EntityHandle h) => default;
    public Vector GetWorldSpaceCenter(EntityHandle h) => default;
    public bool IsPlayer(EntityHandle h) => false;
    public void SetAngles(EntityHandle entity, Vector angles) { }
    public void SetAngularVelocity(EntityHandle entity, Vector degreesPerSecond) { }
    public void SetHealth(EntityHandle h, int health) { }
    public bool SetKeyValue(EntityHandle entity, string key, string value) => false;
    public void SetOrigin(EntityHandle h, Vector origin) { }
    public void SetOwner(EntityHandle h, EntityHandle owner) { }
    public void SetVelocity(EntityHandle h, Vector velocity) { }
    public void Spawn(EntityHandle entity) { }
    public void Teleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity) { }
    public bool SetParent(EntityHandle child, EntityHandle parent, int parentAttachment = 0) => false;
    public EntityHandle GetParent(EntityHandle child) => EntityHandle.None;
    public int GetParentAttachment(EntityHandle child) => 0;
    public void SetLocalTransform(EntityHandle child, Vector? origin, Vector? angles) { }
    public void GetLocalTransform(EntityHandle child, out Vector origin, out Vector angles)
    {
        origin = default;
        angles = default;
    }
    public ParentProbe ParentProbe(EntityHandle entity) => SourceSharp.Abi.ParentProbe.None;
    public Span<byte> GetEngineBlock(EntityHandle entity) => Span<byte>.Empty;
    public void DestroyPhysics(EntityHandle entity) { }
    public float GetGravity(EntityHandle entity) => 0f;
    public void SetGravity(EntityHandle entity, float gravity) { }
    public SolidFlags GetSolidFlags(EntityHandle entity) => SolidFlags.None;
    public SolidType GetSolidType(EntityHandle entity) => SolidType.None;
    public void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags) { }
    public void SetBounds(EntityHandle entity, Vector mins, Vector maxs) { }
    public void SetSpawnFlags(EntityHandle entity, int spawnFlags) { }
    public void PositionChanged(EntityHandle entity, Vector previousOrigin) { }
    public IEnumerable<EntityHandle> FindAllByClassname(string className) => Array.Empty<EntityHandle>();
    public void Remove(EntityHandle entity) => _removal.Remove(entity);
    private partial bool EngineAttachmentFrame(EntityHandle entity, int attachment, out Vector origin, out QAngle angles)
    {
        origin = default;
        angles = default;
        return false;
    }
    // The server's members, as a client engine answers them: the client has
    // no IVEngineServer, and nothing on the client asks these.
    public int Count() => 0;
}

internal sealed unsafe partial class EngineEntityPhysics
{
    public EngineEntityPhysics(EngineSide side)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void ApplyForceCenter(EntityHandle entity, Vector force) { }
    public void ApplyForceOffset(EntityHandle entity, Vector force, Vector worldPoint) { }
    public void ApplyTorque(EntityHandle entity, Vector torque) { }
    public void BecomePhysicsTrigger(EntityHandle entity, bool isTrigger) { }
    public void EnableMotion(EntityHandle entity, bool enable) { }
    public void Wake(EntityHandle entity) { }
    public void Sleep(EntityHandle entity) { }
    public void AddGameFlags(EntityHandle entity, uint flags) { }
    public float GetMass(EntityHandle entity) => 0f;
    public bool InitPhysics(EntityHandle entity, bool asleep = false, bool debris = false) => false;
    public void SetBounce(EntityHandle h, int bounces) { }
    public void SetBrushSolidity(EntityHandle entity, BrushSolidity solidity) { }
    public CollisionGroup GetCollisionGroup(EntityHandle entity) => CollisionGroup.None;
    public SolidFlags GetSolidFlags(EntityHandle entity) => SolidFlags.None;
    public void SetCollisionGroup(EntityHandle entity, CollisionGroup group) { }
    public void SetGravity(EntityHandle h, float gravity) { }
    public void SetMass(EntityHandle entity, float mass) { }
    public void SetNotSolid(EntityHandle entity, bool notSolid) { }
    public nint PhysicsObject(EntityHandle entity) => 0;
    public nint EnvironmentPointer() => 0;
    public Vector CollideExtent(nint physicsObject, Vector direction) => Vector.Zero;
    public Vector Inertia(nint physicsObject) => Vector.Zero;
    public nint WorldObject() => 0;
    public void SetPhysicsStatic(EntityHandle entity, bool isStatic) { }
}

internal sealed unsafe partial class EngineEntityRender
{
    private readonly IStandaloneClientArea _standaloneClientArea;

    /// <summary>
    /// The binding the container builds, on the mirror the pushes feed.
    ///
    /// <para>
    /// <see cref="EngineServices"/> constructs one of these per container —
    /// the defaults, the client's, the server's — so the mirror cannot be state
    /// on this object: the dormancy push would feed one instance and the render
    /// loop would read another, and nothing would say so. It is the side's
    /// composition's (plan_abi_hotreload D7): the module registers its mirror
    /// before the engine areas, <see cref="EngineServices"/> resolves this
    /// parameter from that container, and the module hands the same instance to
    /// the push handlers.
    /// </para>
    /// </summary>
    public EngineEntityRender(EngineSide side, IFwdModelInfo modelInfo, ModelDescriptors models,
                              StandaloneMirror mirror,
                              IModelsArea modelsArea,
                              IStandaloneClientArea standaloneClientArea)
        : this(side, modelInfo, models, mirror, new StandaloneSkeletonRoute(), modelsArea, standaloneClientArea)
    {
    }

    /// <summary>
    /// The server's: its skeletons are managed (plan physics_bones_native_zero
    /// step 1.2), so the bone members ask the route's
    /// <see cref="IStandaloneSkeletons"/> once the composition has filled it,
    /// and native only before that. EngineServices.Build picks this
    /// constructor when the container holds a route.
    /// </summary>
    public EngineEntityRender(EngineSide side, IFwdModelInfo modelInfo, ModelDescriptors models,
                              StandaloneMirror mirror,
                              StandaloneSkeletonRoute skeletons,
                              IModelsArea modelsArea,
                              IStandaloneClientArea standaloneClientArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _modelInfo = modelInfo ?? throw new ArgumentNullException(nameof(modelInfo));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _mirror = mirror;
        _skeletons = skeletons ?? throw new ArgumentNullException(nameof(skeletons));
        _modelsArea = modelsArea ?? throw new ArgumentNullException(nameof(modelsArea));
        _standaloneClientArea = standaloneClientArea ?? throw new ArgumentNullException(nameof(standaloneClientArea));
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
    public void SetCustomDraw(EntityHandle entity, Vector mins, Vector maxs)
    {
        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };

        _standaloneClientArea.StandaloneSetCustomDraw(entity.Value, lo, hi);
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
    public void SetBlending(EntityHandle entity, bool translucent, bool ignoreZ, int alpha)
    {
        _standaloneClientArea.StandaloneSetBlending(entity.Value, translucent ? 1 : 0, ignoreZ ? 1 : 0, alpha);
    }

    /// <summary>
    /// What this entity is casting right now and what it was asked to cast.
    /// Client-side.
    ///
    /// <para>
    /// Whether the client shadow manager holds a handle for it, the third fact
    /// Phase 5's gate needed, is the managed manager's since C7 cluster 12 S2
    /// (<c>IEntityShadowState.HasShadow</c>); the slot's third out is passed
    /// null.
    /// </para>
    /// </summary>
    public ShadowCast ShadowCast(EntityHandle entity, out ShadowCastChoice asked)
    {
        asked = ShadowCastChoice.Auto;

        int choice = 0;
        int cast = _standaloneClientArea.StandaloneShadowCast(entity.Value, &choice);

        asked = (ShadowCastChoice)choice;

        return (ShadowCast)cast;
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
    public void SetFade(EntityHandle entity, FadeStyle style, float startDistance, float endDistance, float minimumAlpha = 0f)
    {
        _standaloneClientArea.StandaloneSetFade(entity.Value, (int)style, startDistance, endDistance, minimumAlpha);
    }

    /// <summary>
    /// A second brush model drawn at this entity's transform, after its own and
    /// at full opacity. Client-side; a negative index clears it.
    /// </summary>
    public void SetOverlayModel(EntityHandle entity, int modelIndex)
    {
        _standaloneClientArea.StandaloneSetOverlayModel(entity.Value, modelIndex);
    }

    /// <summary>
    /// Puts a standalone entity into (or takes it out of) the renderer's
    /// VIEWMODEL pass, where it is drawn at the local player's eyes with the
    /// viewmodel FOV and the depth-range hack — exactly where a stock
    /// C_BaseViewModel draws. Client-side. Managed code decides WHICH entity
    /// (the one whose OwnerIndex is the local player); this is the mechanism.
    /// </summary>
    public void SetViewModel(EntityHandle entity, bool enable)
    {
        _standaloneClientArea.StandaloneSetViewModel(entity.Value, enable ? 1 : 0);
    }

    /// <summary>
    /// Which skin of a model is drawn. Silently does nothing for an entity with
    /// no animating model, which is what stock does too. Client only: the
    /// server's body was empty and went in plan_server_native_v2 T2; a skin
    /// the server picks reaches the client through the networked state
    /// (346d1022f).
    /// </summary>
    public void SetSkin(EntityHandle entity, int skin)
    {
        _standaloneClientArea.EntitySetSkin(entity.Value, skin);
    }

    private partial bool EngineIsDormant(EntityHandle entity) => E.StandaloneIsDormant(_standaloneClientArea, entity);

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void SetModel(EntityHandle entity, string model) { }
    public void SetTransmitAlways(EntityHandle entity, bool always) { }
    public void SetVisible(EntityHandle entity, bool visible) { }
}

internal sealed unsafe partial class EngineGameEvents
{
    public EngineGameEvents(IEventsArea eventsArea)
    {
        _eventsArea = eventsArea ?? throw new ArgumentNullException(nameof(eventsArea));
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void EventSetInt(IntPtr handle, string key, int value) { }
    public void EventSetBool(IntPtr handle, string key, bool value) { }
    public void EventSetString(IntPtr handle, string key, string value) { }
}

internal sealed unsafe partial class EngineGameRulesPolicy
{
    public EngineGameRulesPolicy()
    {
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void RegisterPolicy(GameRulesPolicyBlob policy) { }
}

internal sealed unsafe partial class EngineHost
{
    public EngineHost(EngineSide side, IUniformRandom random) : this(side, random, new NoSteamGameServer()) { }

    public EngineHost(EngineSide side, IUniformRandom random, ISteamGameServer steam)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _steam = steam ?? throw new ArgumentNullException(nameof(steam));
    }
    // The server's members, as a client engine answers them: the client has
    // no IVEngineServer, and nothing on the client asks these.
    public bool IsDedicatedServer => false;
}

internal sealed unsafe partial class EngineHud
{
    private readonly IHudClientArea _hudClientArea;
    private readonly IUserMessagesClientArea _userMessagesClientArea;
    private readonly IViewClientArea _viewClientArea;

    public EngineHud(EngineSide side,
                     IHudClientArea hudClientArea,
                     IUserMessagesClientArea userMessagesClientArea,
                     IViewClientArea viewClientArea)
    {
        _hudClientArea = hudClientArea ?? throw new ArgumentNullException(nameof(hudClientArea));
        _userMessagesClientArea = userMessagesClientArea ?? throw new ArgumentNullException(nameof(userMessagesClientArea));
        _viewClientArea = viewClientArea ?? throw new ArgumentNullException(nameof(viewClientArea));
    }

    public int LocalPlayerIndex => GetLocalPlayerIndex();
    /// <summary>
    /// A "#Token" through the localisation tables. Answers with the token
    /// unchanged when there is no entry, so a caller can always draw the result.
    /// </summary>
    public string Localise(string token)
    {
        if (token.Length == 0)
            return token;

        var bytes = Encoding.UTF8.GetBytes(token + "\0");
        byte* buffer = stackalloc byte[512];

        fixed (byte* p = bytes)
            _hudClientArea.HudLocalise(p, buffer, 512);

        return EngineSide.FromUtf8(buffer);
    }

    /// <summary>
    /// The key this player has bound to a command, or an empty string when
    /// nothing is. Client-side — the server has no idea what anyone's keyboard
    /// looks like.
    /// </summary>
    public string LookupBinding(string binding)
    {
        if (binding.Length == 0)
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(binding + "\0");
        byte* buffer = stackalloc byte[64];

        fixed (byte* p = bytes)
            _hudClientArea.HudLookupBinding(p, buffer, 64);

        return EngineSide.FromUtf8(buffer);
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
    public TextMessage? TextMessageGet(string name)
    {
        if (name.Length == 0)
            return null;

        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        TextMessage message = default;

        fixed (byte* p = bytes)
        {
            if (_hudClientArea.HudTextMessageGet(p, &message) == 0)
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
    public ViewportFacts ViewportQuery(string panel)
    {
        if (panel.Length == 0)
            return ViewportFacts.None;

        var bytes = Encoding.UTF8.GetBytes(panel + "\0");
        fixed (byte* p = bytes)
            return (ViewportFacts)_userMessagesClientArea.ClientViewportQuery(p);
    }

    /// <summary>
    /// MsgFunc_VGUIMenu's effects on the viewport, each one asked for in
    /// <paramref name="actions"/>: the data travels as name, NUL, value, NUL.
    /// </summary>
    public void ViewportShow(string panel, bool show, IReadOnlyList<KeyValuePair<string, string>> data, ViewportActions actions)
    {
        if (panel.Length == 0)
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
            _userMessagesClientArea.ClientViewportShow(n, show ? 1 : 0, d, pairBytes.Length, (int)actions);
    }

    /// <summary>
    /// Local player health, or -1 when there is no local player — the case in
    /// the menu and between levels. Distinct from 0 so the HUD can tell "no
    /// player" from "dead".
    /// </summary>
    public int LocalPlayerHealth
        => _hudClientArea.GetLocalPlayerHealth();

    /// <summary>
    /// The entity index the engine gave THIS client, 0 when there is no local
    /// player — the menu, or between levels. Client-side. The client half of
    /// "is this entity mine": the viewmodel and the HUD both decide locality
    /// by comparing an OwnerIndex against this.
    /// </summary>
    private int GetLocalPlayerIndex()
    {
        return _hudClientArea.GetLocalPlayerIndex();
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
            float* xyz = stackalloc float[3];

            return _viewClientArea.GetLocalPlayerOrigin(xyz) == 0
                ? null
                : new Vector(xyz[0], xyz[1], xyz[2]);
        }
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void DrawWorldText(Vector origin, int line, string text, float duration, int r = 255, int g = 255, int b = 255, int a = 255) { }
    public bool DrawEntityOverlay(EntityHandle entity, int line, string? text, float duration, int r = 255, int g = 255, int b = 255, int a = 255) => false;
    public (uint Drawn, uint Clipped, uint NoViewer, uint NoOverlay, bool ViewerSet) OverlayReadout() => (0, 0, 0, 0, false);
    public void AddLineOverlay(Vector start, Vector end, int r, int g, int b, bool noDepthTest = true, float duration = 0f) { }
    public void ViewPunch(Vector origin, Vector punch, float radius, bool inAir = false) { }

}

internal sealed unsafe partial class EngineNetworking
{
    private readonly INetClientArea _netClientArea;

    public EngineNetworking(EngineSide side, INetClientArea netClientArea, INetworkArea networkArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _netClientArea = netClientArea ?? throw new ArgumentNullException(nameof(netClientArea));
        _networkArea = networkArea ?? throw new ArgumentNullException(nameof(networkArea));
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
        if (key.Length == 0) return false;

        var keyBytes = Encoding.UTF8.GetBytes(key + "\0");

        fixed (byte* pKey = keyBytes)
        fixed (byte* pData = payload)
        {
            return _netClientArea.SendServerMessage(pKey, pData, payload.Length) != 0;
        }
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public PayloadAgreement CheckPayloadAgreement(EntityHandle h) => PayloadAgreements.NotSupplied;
    public EntityState EntityGetState(EntityHandle entity, out MoveType moveType)
    {
        moveType = MoveType.None;
        return EntityState.None;
    }
    public Span<byte> GetEntityBlob(EntityHandle h) => Span<byte>.Empty;
    public void MarkEntityBlockChanged(EntityHandle h, int blockOffset) { }
}

internal sealed unsafe partial class EngineOverlaps
{
    public EngineOverlaps()
    {
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public int OverlapReport(Span<OverlapPair> into) => 0;
}

internal sealed unsafe partial class EnginePlayerMovement
{
    public EnginePlayerMovement(EngineSide side, IMovementArea movementArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _movementArea = movementArea ?? throw new ArgumentNullException(nameof(movementArea));
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void SetSlowMovement(EntityHandle player, bool slow) { }
    public MoveType SetMoveType(EntityHandle player, MoveType moveType) => MoveType.None;
    public void SnapEyeAngles(EntityHandle player, Vector pitchYawRoll) { }
}

internal sealed unsafe partial class EnginePlayers
{
    public EnginePlayers(EngineSide side)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void PlaceAtSpawn(EntityHandle player, Vector origin, Vector angles) { }
    public EntityHandle GetHandle(int playerIndex) => EntityHandle.None;
    public EntityHandle GetCommandClient() => EntityHandle.None;
    public ObserverMode GetObserverMode(EntityHandle player) => ObserverMode.None;
    public Vector GetShootOrigin(EntityHandle player) => default;
    public int GetTeam(EntityHandle player) => -1;
    public bool IsAlive(EntityHandle player) => false;
    public void MakePickup(EntityHandle entity, float sizeX, float sizeY, float sizeZ) { }
    public void Respawn(EntityHandle player) { }
    public void SetObserverMode(EntityHandle player, ObserverMode mode) { }
    public void SetTeam(EntityHandle player, int team) { }
    public void SetPickupEnabled(EntityHandle entity, bool enabled) { }
    public bool TryStandalonePlayerEarPosition(int edictIndex, out Vector ears)
    {
        ears = default;
        return false;
    }
    public EntityHandle FindByUserId(int userId) => EntityHandle.None;
    // The server's members, as a client engine answers them: the client has
    // no IVEngineServer, and nothing on the client asks these.
    public int GetUserId(EntityHandle player) => 0;

    public bool BoxInPvs(EntityHandle player, Vector mins, Vector maxs) => false;

    public void SetViewEntity(EntityHandle player, EntityHandle view) { }
}

internal sealed unsafe partial class EnginePrecache
{
    public EnginePrecache(EngineSide side, IFwdModelInfo modelInfo, BreakModels breaks)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _modelInfo = modelInfo ?? throw new ArgumentNullException(nameof(modelInfo));
        _breaks = breaks ?? throw new ArgumentNullException(nameof(breaks));
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public int PrecacheDecal(string name) => -1;
    public void PrecacheMaterial(string material) { }
    public void PrecacheParticleSystem(string name) { }
    public int PrecacheModel(string model) => -1;
    public void PrecacheSound(string soundName) { }
}

internal sealed unsafe partial class EngineScripted
{
    public EngineScripted(EngineSide side)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void RegisterEntityClass(string className, string[] outputNames, ScriptedEntityKind kind) { }
    public void RegisterEntityClassOps(string className, uint opMask) { }
    public List<EntityHandle> TemplateSpawnKeyed(IReadOnlyList<KeyedSpawn> spawns, int limit = 64) => new List<EntityHandle>();
}

internal sealed unsafe partial class EngineSound
{

    private readonly ISoundscapeClientArea _soundscapeClientArea;

    public EngineSound(EngineSide side, IFwdPhysicsSurfaceProps surfaceProps, IFwdSoundEmitter soundEmitter, ISoundscapeClientArea soundscapeClientArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _surfaceProps = surfaceProps ?? throw new ArgumentNullException(nameof(surfaceProps));
        _soundEmitter = soundEmitter ?? throw new ArgumentNullException(nameof(soundEmitter));
        _soundscapeClientArea = soundscapeClientArea ?? throw new ArgumentNullException(nameof(soundscapeClientArea));
    }

    // The client's patch arms (plan_server_native_v2 T4a): the five slots are
    // IShimSoundscapeClient's alone since the server's patches became managed.
    private partial void SidePatchEmit(int patch, float volume01, float pitch, int flags, float startTime, bool emitCaption)
        => _soundscapeClientArea.SoundPatchEmit(patch, volume01, pitch, flags, startTime, emitCaption ? 1 : 0);

    private partial void SidePatchStop(int patch) => _soundscapeClientArea.SoundPatchStop(patch);

    private partial void SidePatchState(int patch, out int entIndex, out bool filterActive)
    {
        int index = -1;
        int active = 0;

        _soundscapeClientArea.SoundPatchState(patch, &index, &active);

        entIndex = index;
        filterActive = active != 0;
    }

    private partial void SidePatchFree(int patch) => _soundscapeClientArea.SoundPatchFree(patch);

    private partial int SidePatchCreate(EntityHandle entity, int channel, string soundName, int soundLevel)
    {
        var bytes = Encoding.UTF8.GetBytes(soundName + "\0");
        fixed (byte* p = bytes)
            return _soundscapeClientArea.SoundPatchCreate(entity.Value, channel, p, soundLevel);
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public void EmitAmbientSound(int entIndex, Vector origin, string soundName, float volume, int soundLevel, int flags, int pitch) { }
    public bool EmitWorldSoundScript(string soundName, Vector origin, int channel, float volume) => false;
    public void EmitSoundAtPosition(string soundName, Vector origin) { }
    public void EntityStopSound(EntityHandle entity, string soundName) { }
    public void EntityEmitSound(EntityHandle entity, string soundName) { }
}

internal sealed unsafe partial class EngineVisibility
{
    private readonly IStandaloneClientArea _standaloneClientArea;
    private readonly IViewClientArea _viewClientArea;

    public EngineVisibility(EngineSide side, IStandaloneClientArea standaloneClientArea, IViewClientArea viewClientArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _standaloneClientArea = standaloneClientArea ?? throw new ArgumentNullException(nameof(standaloneClientArea));
        _viewClientArea = viewClientArea ?? throw new ArgumentNullException(nameof(viewClientArea));
    }

    /// <summary>Turns one of the map's compiled occluders on or off. Client-side.</summary>
    public void ActivateOccluder(int occluderIndex, bool active)
    {
        _standaloneClientArea.ActivateOccluder(occluderIndex, active ? 1 : 0);
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
        fixed (float* p = values)
        {
            _viewClientArea.ClientSetWorldRenderState((int)which, p, values.Length);
        }
    }

    // The lookups' handles (lane M1-A: the table shell_worldrender.cpp kept).
    private readonly SourceSharp.Client.Interop.ColorLookupTable _colorLookups = new();

    /// <summary>
    /// Loads a colour-correction lookup and answers a handle, or 0 if the file
    /// would not load.
    /// </summary>
    public int ColorCorrectionAdd(string lookupFile)
    {
        var bytes = Encoding.UTF8.GetBytes(lookupFile + "\0");
        fixed (byte* p = bytes)
        {
            return _colorLookups.Register((nint)_viewClientArea.ColorLookupAdd(p));
        }
    }

    /// <summary>
    /// Releases a lookup. Not optional: the manager holds it until told, and a
    /// colour correction whose entity is gone would tint the map forever.
    /// </summary>
    public void ColorCorrectionRemove(int handle)
    {
        if (_colorLookups.Forget(handle, out nint lookup))
            _viewClientArea.ColorLookupRemove((void*)lookup);
    }

    /// <summary>
    /// How much of a loaded lookup to apply, 0..1. Pushed every frame while the
    /// entity lives, because the weight is a function of where the player is
    /// standing and that changes on its own.
    /// </summary>
    public void ColorCorrectionSetWeight(int handle, float weight)
    {
        if (_colorLookups.TryGet(handle, out nint lookup))
            _viewClientArea.ColorLookupSetWeight((void*)lookup, weight);
    }

    /// <summary>Every lookup's weight back to zero, before the frame re-weighs them.</summary>
    public void ColorCorrectionResetWeights() => _viewClientArea.ColorLookupResetWeights();

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
        _standaloneClientArea.DynamicLightRelease(key);
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
        fixed (DynamicLight* p = &light) { _standaloneClientArea.DynamicLightUpdate(p); }
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
        fixed (FogParams* p = &parameters) { _viewClientArea.SetViewerFogParams(p); }
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
        _viewClientArea.SetViewerFogParams(null);
    }

    /// <inheritdoc/>
    public void SetViewerSkyboxParams(in SkyboxParams parameters)
    {
        fixed (SkyboxParams* p = &parameters) { _viewClientArea.SetViewerSkyboxParams(p); }
    }

    /// <inheritdoc/>
    public void ClearViewerSkyboxParams()
    {
        _viewClientArea.SetViewerSkyboxParams(null);
    }

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public uint AreaPortalCreate(int portalNumber, int portalVersion) => 0;
    public void AreaPortalDestroy(uint portal) { }
    public void AreaPortalSetOpen(uint portal, bool open) { }
    public void AreaPortalSetWindow(uint portal, EntityHandle entity, float fadeDistance) { }
    public void ClearSkyboxParams() { }
    public void SetSkyboxParams(in SkyboxParams parameters) { }
    public bool TryStandalonePlayerSkyboxArea(int edictIndex, out int area)
    {
        area = 0;
        return false;
    }

    // The server's members, as a client engine answers them: the client has
    // no IVEngineServer, and nothing on the client asks these.
    public void SetLightStyle(int style, string pattern) { }
}

internal sealed unsafe partial class EngineWeapons
{
    private readonly IStandaloneClientArea _standaloneClientArea;

    public EngineWeapons(IStandaloneClientArea standaloneClientArea)
    {
        _standaloneClientArea = standaloneClientArea ?? throw new ArgumentNullException(nameof(standaloneClientArea));
    }

    /// <summary>
    /// One row of the client's native ammo table (its CAmmoDef). Client only
    /// since plan_server_native_v2 T2: server.so keeps no ammo table.
    /// </summary>
    public void RegisterAmmoType(string name, DamageType damageType, int maxCarry, int tracerType = 0, float damageForce = 0f, float damage = 0f)
    {
        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            _standaloneClientArea.RegisterAmmoType(p, (int)damageType, maxCarry, tracerType, damageForce, damage);
    }
}

internal sealed unsafe partial class EngineWorldQuery
{
    private readonly IViewClientArea _viewClientArea;

    private readonly ITempEntBackendsClientArea _tempEntBackendsClientArea;
    private readonly ITempEntPoolClientArea _tempEntPoolClientArea;

    public EngineWorldQuery(EngineSide side, IFwdPhysicsSurfaceProps surfaceProps, IFwdEngineTrace trace,
                            ITempEntBackendsClientArea tempEntBackendsClientArea,
                            ITempEntPoolClientArea tempEntPoolClientArea,
                            IViewClientArea viewClientArea)
    {
        _side = side ?? throw new ArgumentNullException(nameof(side));
        _surfaceProps = surfaceProps ?? throw new ArgumentNullException(nameof(surfaceProps));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _tempEntBackendsClientArea = tempEntBackendsClientArea ?? throw new ArgumentNullException(nameof(tempEntBackendsClientArea));
        _tempEntPoolClientArea = tempEntPoolClientArea ?? throw new ArgumentNullException(nameof(tempEntPoolClientArea));
        _viewClientArea = viewClientArea ?? throw new ArgumentNullException(nameof(viewClientArea));
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
    public ModelType? ClientEntityModelType(int entIndex)
    {
        int answer = _tempEntBackendsClientArea.ClientEntityModelType(entIndex);

        return answer < 0 ? null : (ModelType)answer;
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
    public bool ClientEntityModelBounds(int entityIndex, out Vector mins, out Vector maxs,
                                        out Vector angles)
    {
        mins = Vector.Zero;
        maxs = Vector.Zero;
        angles = Vector.Zero;

        float* lo = stackalloc float[3];
        float* hi = stackalloc float[3];
        float* pyr = stackalloc float[3];

        if (_tempEntPoolClientArea.ClientEntityModelBounds(entityIndex, lo, hi, pyr) == 0) return false;

        mins = new Vector(lo[0], lo[1], lo[2]);
        maxs = new Vector(hi[0], hi[1], hi[2]);
        angles = new Vector(pyr[0], pyr[1], pyr[2]);
        return true;
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
        => ClientTraceLineCore(start, end, mask, TraceFilter.HitAll);

    public ClientTraceResult ClientTraceLine(Vector start, Vector end, TraceMask mask,
                                             TraceFilter filter, int passEntityIndex = -1)
        => ClientTraceLineCore(start, end, mask, filter, passEntityIndex);

    /// <inheritdoc/>
    public unsafe ClientTraceResult ClientClipRayToEntities(Vector start, Vector end, TraceMask mask,
                                                            ReadOnlySpan<int> entityIndices)
    {
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* endPos = stackalloc float[3];
        float* normal = stackalloc float[3];
        float fraction;
        int hitbox;
        int entIndex;
        int surfaceProps;
        int surfaceFlags;
        int stopped;

        // ONE CROSSING FOR THE WHOLE LIST: the indices go over as one buffer.
        fixed (int* indices = entityIndices)
        {
            stopped = _viewClientArea.ClientClipRayToEntities(s, e, (int)mask, indices, entityIndices.Length,
                                                              &fraction, endPos, normal, &hitbox, &entIndex,
                                                              &surfaceProps, &surfaceFlags);
        }

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

    /// <inheritdoc/>
    public unsafe ClientBoxTrace ClientTraceHullEntities(Vector start, Vector end, Vector mins, Vector maxs,
                                                          TraceMask mask, int passEntityIndex,
                                                          ReadOnlySpan<int> entityIndices)
    {
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };
        float* endPos = stackalloc float[3];
        float* origin = stackalloc float[3];
        float fraction = 1f;
        int entIndex = -1;
        int stopped;

        // One crossing for the whole list, as the ray's clip.
        fixed (int* indices = entityIndices)
        {
            stopped = _viewClientArea.ClientTraceHullEntities(s, e, lo, hi, (int)mask, passEntityIndex,
                                                              indices, entityIndices.Length,
                                                              &fraction, endPos, &entIndex, origin);
        }

        return new ClientBoxTrace(stopped != 0, fraction, new Vector(endPos[0], endPos[1], endPos[2]),
                                   entIndex, new Vector(origin[0], origin[1], origin[2]));
    }

    /// <summary>
    /// The same trace, choosing which entities it may hit. See
    /// <see cref="TraceFilter"/> for why this is an enumeration and not an
    /// ignore-entity parameter: a simple filter is not hit-all with one entity
    /// removed, because it also applies collision groups and the game rules.
    /// </summary>
    private unsafe ClientTraceResult ClientTraceLineCore(Vector start, Vector end, TraceMask mask,
                                                           TraceFilter filter,
                                                           int passEntityIndex = -1)
    {
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* endPos = stackalloc float[3];
        float* normal = stackalloc float[3];
        float fraction;
        int hitbox;
        int entIndex;
        int surfaceProps;
        int surfaceFlags;

        int stopped = _viewClientArea.ClientTraceLine(s, e, (int)mask, (int)filter, passEntityIndex,
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

    // The server's members, as a client engine answers them: what each one's
    // attach guard answered here, where the server's areas are never attached.
    public (Vector Right, Vector Up) AngleVectorsFromForward(Vector forward) => (default, default);
    public int EntitiesInSphere(Vector center, float radius, Span<EntityHandle> into) => 0;
    public HullResult TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask, EntityHandle ignore = default) => new HullResult(false, false, EntityHandle.None);
    public SurfaceTraceResult TraceBox(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask, EntityHandle ignore = default) => new SurfaceTraceResult(false, 1f, end, Vector.Zero, EntityHandle.None, -1, 0);
    public EntityHullTrace EntityTraceHull(EntityHandle entity, Vector start, Vector end, Vector mins, Vector maxs, uint mask, int collisionGroup) => new EntityHullTrace(start, true, true);
    public TraceResult TraceLine(Vector start, Vector end, TraceMask mask, EntityHandle ignore = default) => new TraceResult(false, 1f, end, Vector.Zero, EntityHandle.None);
    public SurfaceTraceResult TraceLineSurface(Vector start, Vector end, TraceMask mask, EntityHandle ignore = default) => new SurfaceTraceResult(false, 1f, end, Vector.Zero, EntityHandle.None, -1, 0);
    public EntityHandle TracePlayerAim(EntityHandle player, float range, out Vector hitPoint)
    {
        hitPoint = default;
        return EntityHandle.None;
    }
    public WaterBelow WaterSurfaceBelow(Vector start) => new WaterBelow(false, false, false, start);
    public string WorldChapterTitle() => string.Empty;
    public int EntityModelSurfaceProp(EntityHandle entity) => -1;
    public int SurfacePropIndex(string name) => -1;

    // The server's members, as a client engine answers them: the client has
    // no IVEngineServer, and nothing on the client asks these.
    public int GetArea(Vector origin) => 0;
}

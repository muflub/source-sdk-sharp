namespace SourceSharp.Abi;

/// <summary>
/// Client-only hooks. Implemented alongside <see cref="ISideModule"/> by the
/// client module.
/// </summary>
public interface IClientModule
{
    /// <summary>
    /// Called once per user command, after the base input code has built it.
    /// The command may be read or modified in place; changes are written back
    /// into the engine's CUserCmd.
    ///
    /// This runs every tick, so avoid allocating here.
    /// </summary>

    /// <summary>
    /// Routes an arriving temp entity to whichever managed class plays it.
    /// Client-only: the server writes these and never receives one.
    ///
    /// <para>
    /// The handle is the one THIS SIDE's <c>TempEntRegister</c> answered, and
    /// the blob is the native shell's own storage — valid only for the duration
    /// of the call, and deliberately not cleared between events, because the
    /// engine deltas consecutive events of one class against each other. An
    /// implementation that retained either would be reading the next effect.
    /// </para>
    /// </summary>
    void DispatchTempEntity(int handle, ReadOnlySpan<byte> blob);

    /// <summary>
    /// A native forward raised a temp entity LOCALLY. The id and the numbers
    /// arrive rather than a blob: the effect is local, so it never has to become
    /// bytes, and the module turns them into the declaration and plays it.
    /// Answers whether a player took it.
    /// </summary>
    bool ForwardTempEntity(int id, ReadOnlySpan<float> floats, ReadOnlySpan<int> ints);

    /// <summary>
    /// One frame of the pool: expiry, the physics step, the visibility cull and
    /// the frees. <c>CTempEnts::Update</c>'s body, from the same place
    /// <c>cdll_client_int.cpp</c> called it.
    /// </summary>
    void TempEntPoolUpdate(float frameTime);

    /// <summary>
    /// Free every live tempent. <c>CTempEnts::Clear</c>'s body, minus the
    /// break-model lighting list — that stays native and its owner clears it
    /// immediately after this returns.
    /// </summary>
    void TempEntPoolClear();

    // The three OnNetEntity* handlers went with the deleted shell, and their
    // ABI slots were REAPED at 240 — see host/sourcesharp.h.

    /// <summary>
    /// How much of a jump a player gets: 1 is the ordinary jump, 0 refuses it.
    ///
    /// Declared on BOTH module interfaces because movement is predicted and the
    /// question is asked on both sides. A rule that answers differently on the
    /// two makes the player rubber-band.
    /// </summary>
    float GetJumpScale(in JumpRequest request);

    /// <summary>
    /// How slippery the ground under a player is. Runs on BOTH sides, every
    /// movement frame the player is grounded, so it must decide from its
    /// arguments alone.
    /// </summary>
    float GetFrictionScale(int playerIndex, float friction, float speed,
                           bool ducking, int groundMaterial);

    /// <summary>
    /// How fast a player may move right now. Runs on both sides; asked after
    /// the engine's own speed factors.
    /// </summary>
    float GetMaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial);

    //-------------------------------------------------------------------------
    // The standalone entity hierarchy, client side. Same shape as the server's,
    // asked at a different moment — see ClientStandaloneRegistry.
    //-------------------------------------------------------------------------
    bool StandaloneCreate(EntityHandle entity, string className);
    void StandaloneSpawn(EntityHandle entity);

    /// <summary>
    /// Draw this entity, once per frame.
    ///
    /// <para>
    /// Only reaches entities that asked for it through
    /// <see cref="EngineSide.StandaloneSetCustomDraw"/>. Returns 1 if anything was
    /// drawn, which is what the engine's DrawModel answers.
    /// </para>
    /// </summary>
    int StandaloneDraw(EntityHandle entity);

    /// <summary>
    /// A view's custom-draw entities, prepared in one crossing (C7 item 7): for
    /// each, what its DrawModel will answer once its prepared draw is carried
    /// out, or -1 for one that must still draw in place.
    /// </summary>
    void StandaloneDrawBatch(ReadOnlySpan<uint> handles, Span<int> results);
    void StandaloneRemoved(EntityHandle entity);

    /// <summary>
    /// A hierarchy entity's own replicated state, freshly unpacked.
    ///
    /// The blob is borrowed native memory, valid only for this call — which is
    /// why it arrives as a span rather than an array, and why a class that
    /// wants to keep a value must copy it out.
    /// </summary>
    void StandaloneDataChanged(EntityHandle entity, ReadOnlySpan<byte> blob, int updateType);

    /// <summary>
    /// The engine has reached a stage of the client frame —
    /// <c>IBaseClientDLL::FrameStageNotify</c>, one crossing per stage.
    ///
    /// <para>
    /// <b>This is the client's <c>RunFrame</c>.</b> <c>plan_entity.md</c> §3.4:
    /// "the server frame is <c>GameFrame → managed RunFrame(curtime)</c>; the
    /// client frame is <c>FrameStageNotify(stage) → managed OnStage(stage)</c>."
    /// Per-frame client work the port moves into managed code hangs off a stage
    /// of this one call rather than taking an ABI slot of its own, so the
    /// client's per-frame crossing count stays flat as more of the client
    /// moves.
    /// </para>
    ///
    /// <para>
    /// <b>EVERY stage arrives, including the ones nothing is hung off yet.</b>
    /// The native shell does not filter: a stage that is silently never
    /// delivered is indistinguishable, from here, from a stage the engine never
    /// raised, and what a stage means is a managed decision. An implementation
    /// with nothing to do in a stage returns.
    /// </para>
    ///
    /// <para>
    /// Runs on the engine's main thread inside the frame. It must not allocate
    /// and must not throw — an exception here unwinds into the engine's C++
    /// frames.
    /// </para>
    /// </summary>
    void OnStage(ClientFrameStage stage);

    /// <summary>
    /// The engine has flushed its model cache. Every studio header this DLL
    /// still holds is now built over studio data that is gone — lane
    /// diag-visibility.
    ///
    /// <para>
    /// <b>THE MIRROR OF <c>IServerHosting.InvalidateMdlCache</c></b>, and the
    /// client had none. The client shell's own body walked
    /// <c>ClientEntityList().FirstBaseEntity()</c>, which answers
    /// <c>C_BaseEntity</c>; <c>C_StandaloneEntity</c> is not one, so the flush
    /// reached ZERO of this mod's entities and nothing anywhere said so. That is
    /// the shape of defect this method exists to make impossible: the decision
    /// and the report are here, where they can be counted and printed, and the
    /// walk is the one native step C# calls back
    /// (<c>Engine.EntitiesInvalidateMdlCacheAll</c>).
    /// </para>
    ///
    /// <para>
    /// RARE BY CONSTRUCTION: reachable from <c>flush</c> and
    /// <c>flush_locked</c>, both <c>FCVAR_CHEAT</c>, and from the engine's own
    /// model reload. So a line of output per call is a measurement rather than
    /// noise.
    /// </para>
    /// </summary>
    void InvalidateMdlCache();

    /// <summary>
    /// WHICH EFFECT FAMILY AN IMPACT IS, WHICH DECAL IT LEAVES, AND WHETHER
    /// FLECKS ARE DRAWN (plan_shim.md §6, lane 4b-effects).
    ///
    /// <para>
    /// ImpactEffectPolicy builds what one family is made OF.
    /// This answers which family it IS — the <c>iMaterial == CHAR_TEX_*</c>
    /// chain, the twenty-six-row surface-to-particle-system table, the fleck
    /// suppression both of those recomputed, and the decal name. All four were
    /// literals in <c>shell_impact_effects.cpp</c>.
    /// </para>
    ///
    /// <para>
    /// <b>ONE CALL PER IMPACT.</b> <paramref name="input"/> and
    /// <paramref name="output"/> are laid out by
    /// <c>game/client/impact_effects.h</c>, which both sides read; the two name
    /// spans receive NUL-terminated ASCII, and an empty span means the caller
    /// did not ask for that one.
    /// </para>
    ///
    /// <para>
    /// Answers the number of ints written to <paramref name="output"/>, or −1
    /// — on which native does what stock's fall-through does, which is nothing.
    /// </para>
    /// </summary>
    int ImpactChoice(ReadOnlySpan<int> input, Span<int> output,
                     Span<byte> particleName, Span<byte> decalName);
}

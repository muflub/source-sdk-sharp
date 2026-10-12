namespace SourceSharp.Abi;

/// <summary>
/// Implemented once per side by the game assemblies. The bootstrap discovers
/// this by reflection so it never needs a compile-time reference into a
/// collectible ALC.
/// </summary>
public interface ISideModule
{
    void Initialize(Side side);
    void Frame(float frameTime);

    /// <summary>
    /// The frame's two ends. <see cref="Frame"/> is its middle — the point
    /// FrameUpdatePostEntityThink reaches, which is where the stock rules class
    /// ran its own Think.
    ///
    /// <para>
    /// Defaulted, and server-only in practice: the two native slots that reach
    /// these are filled on the server and left NULL on the client, which has no
    /// entity-think phase of its own to sit either side of.
    /// </para>
    /// </summary>
    /// <summary>
    /// plan_engineapi_vtables D4: the managed root native acquires this side's
    /// managed-built interfaces from. Zero for a side that serves none. Native
    /// accepts it through its own Validate (plan_managed_vtable_reload.md L1).
    /// Every block lives in <paramref name="vtables"/>, the side's registry,
    /// which the Bootstrap keeps across reloads as an opaque handle (L3).
    /// </summary>
    nint BuildManagedRoot(ManagedVtables vtables) => 0;

    void FramePreThink(float frameTime) { }

    /// <inheritdoc cref="FramePreThink"/>
    void FramePostThink(float frameTime) { }

    void LevelInit(string mapName);
    void LevelShutdown();
    void ConCommandDispatch(int commandId, string args);
    void Shutdown();

    /// <summary>
    /// Routes a fired game event to its declared handlers. Both sides receive
    /// events — the server the authoritative ones, the client the networked
    /// copies — so this lives here rather than on one side's interface.
    /// </summary>
    void DispatchGameEvent(string eventName, IntPtr handle);

    /// <summary>
    /// One standalone player command, phase 0 before the movement and phase 1
    /// after it (Phase 9c4). BOTH sides run it — the server once per command,
    /// the client once per REPLAYED command — which is the bucket-2 test in
    /// person: whatever runs here must compute the same answer from the same
    /// inputs on both, and <paramref name="firstTime"/> is the only licence
    /// to do anything a replay must not repeat.
    /// </summary>
    void OnStandalonePlayerCommand(EntityHandle player, in UserCmd cmd, int phase, bool firstTime) { }

    /// <summary>
    /// One client prediction frame (step 6f): <c>IPrediction::Update</c>,
    /// forwarded whole.
    ///
    /// <para>
    /// The CLIENT'S, and defaulted to nothing on the server for the reason the
    /// server has no <c>IPrediction</c>: there is nothing to replay when your
    /// own answer is the authoritative one.
    /// </para>
    /// </summary>
    /// <param name="incomingAcknowledged">The last command the server has
    /// acknowledged, ABSOLUTE — the number the ring slots are stamped with.</param>
    /// <param name="worldUpdateArrived">A packet landed since the last call, so
    /// the decoded state is the server's word and the acknowledged command has
    /// something to be judged against.</param>
    void OnClientPredictionUpdate(int startFrame, bool validFrame, int incomingAcknowledged,
                                  int outgoingCommand, bool worldUpdateArrived) { }

    /// <summary>
    /// Puts every live entity's state somewhere that outlives this assembly,
    /// and answers how many were saved. Called on the way OUT of a hot reload,
    /// before anything is unloaded.
    /// </summary>
    /// <remarks>
    /// DEFAULTED TO ZERO rather than made abstract, and the default is honest
    /// rather than convenient: a side that keeps no entities of its own has
    /// nothing to carry, and forcing it to write a method returning nothing
    /// would be a line of ceremony that a reader has to check is really empty.
    /// A side that DOES keep entities and does not override this is caught by
    /// the reload's own report, which prints what it saved and what it restored
    /// beside the number of entities that exist.
    /// </remarks>
    int ReloadSave() => 0;

    /// <summary>
    /// Recreates the managed half of every entity that had one and puts its
    /// state back, answering how many. Called on the way IN, after the native
    /// resync — which zeroes a reshaped class's blob, so a restore that ran
    /// before it would have its writes wiped.
    /// </summary>
    int ReloadRestore() => 0;

    /// <summary>
    /// Every live entity's state as BYTES, for a <c>.sav</c>. Answers how many
    /// bytes the whole block needs and writes nothing unless every one of them
    /// fits, so the shell sizes with an empty span and fills on the second
    /// call; 0 is nothing to save and -1 is a block that could not be encoded
    /// at all. See <c>SaveCollectEntities</c> in sourcesharp.h.
    /// </summary>
    /// <remarks>
    /// DEFAULTED like <see cref="ReloadSave"/> and for the same reason: a side
    /// that keeps no entities of its own has nothing to put in a save, and 0
    /// is the honest answer rather than a stub someone has to read to confirm
    /// is empty.
    /// </remarks>
    int SaveCollect(Span<byte> destination) => 0;

    /// <summary>
    /// The other direction: how many entities were restored, or -1 if the block
    /// was refused — in which case NOTHING was restored. A partial restore is
    /// the failure the format is arranged against, so a refusal must leave the
    /// world exactly as it found it.
    /// </summary>
    int SaveRestore(ReadOnlySpan<byte> source) => 0;
}

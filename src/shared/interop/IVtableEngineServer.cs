namespace SourceSharp.Abi;

//=============================================================================
// IVEngineServer, AND THE HAZARDS THAT ARE ONLY THIS INTERFACE'S.
//
// eiface.h's class body holds 124 `virtual` declarations and ZERO preprocessor
// directives, so unlike cdll_int.h nothing about its numbering depends on how
// the engine was compiled. The generated struct is 124 fields.
//
//-----------------------------------------------------------------------------
// TWO THINGS TO KNOW BEFORE ADDING A METHOD HERE.
//
// IT IS REACHED FROM INSIDE A FRAME. Game code calls this while the engine is
// running the simulation, so a wrong call here can corrupt entity state rather
// than return a bad number. Everything exposed below is read-only.
//
// `edict_t *` DOES NOT MEAN HERE WHAT THE HEADER IMPLIES. This port's entity
// model has changed underneath it -- CWorld is its own hierarchy and NextEnt
// walks no SDK entities at all. Twelve methods take one, including
// PEntityOfEntIndex at 16 compiled call sites, the heaviest method on the
// interface, and not one of them is exposed.
//
//-----------------------------------------------------------------------------
// THE VERSION STRING IS DOING REAL WORK HERE. eiface.h says of the older names
// "These only differ in new items added to the end" and then makes both of them
// TYPEDEFS of the current class -- so from inside this tree IVEngineServer021
// and IVEngineServer023 are one type with one table. The shipped engine
// disagrees: it answers the older names with a different object of a different
// class. Asking for the wrong string would index a shim.
//=============================================================================

/// <summary>
/// What the server engine knows about the mod, the map and what has been
/// precached, as game code should see it — no pointer arithmetic, no slot
/// number. Shared, so the shared bindings can ask it; its implementation,
/// over the engine's object and libss_fwd_server's tables, is Server.Interop's
/// <c>EngineServer</c> (plan_engine_forwarders.md, one library per side).
/// </summary>
public interface IVtableEngineServer
{
    /// <summary>
    /// <c>INTERFACEVERSION_VENGINESERVER</c>. The CURRENT name — the older ones
    /// are typedefs in this tree and different objects in the shipped engine,
    /// so asking for one of those would index a shim.
    /// </summary>
    const string InterfaceVersion = "VEngineServer023";

    /// <summary>The mod directory the engine is running out of, or null.</summary>
    string? GameDir();

    /// <summary>
    /// The map's pristine entity lump, as the .bsp holds it, or null.
    /// </summary>
    string? MapEntitiesString();

    /// <summary>Whether a model path is in the server's precache table.</summary>
    bool IsModelPrecached(string path);

    //-------------------------------------------------------------------------
    // THREE MEMBERS THAT ARRIVED WITH plan_native_api.md PHASE 3, and each
    // replaces a flat ABI entry that was doing exactly this call.
    //
    // THE REASONS THEY WERE ABSENT ARE READ AND ANSWERED RATHER THAN OVERRULED.
    // tools/vtable-policy.json refused all four, and three of its four reasons
    // belong to the deleted verification apparatus: "a wrong slot returning
    // zero AGREES with it", "the only other source would be a managed
    // reimplementation of the engine's own walk". Those are arguments about
    // whether a slot could be SECOND-SOURCED, and the owner's ruling retired
    // the question -- the version string is the contract, and a slot is the
    // method the header says it is. What is left is the semantic caution on
    // GetEntityCount, which is real and is carried onto the member.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Whether this is a dedicated server.
    /// </summary>
    bool IsDedicatedServer();

    /// <summary>
    /// How many EDICTS the engine is holding — <c>num_edicts</c> minus
    /// <c>free_edicts</c>.
    ///
    /// <para>
    /// <b>Not a count of managed entities, and the difference is not academic
    /// in this port:</b> a save captures 0 of dm_lockdown's 473. Both
    /// hierarchies occupy edict slots and the managed hierarchy is not all of
    /// them, so this answers the engine's limit question and nothing else.
    /// </para>
    /// </summary>
    int EntityCount();

    /// <summary>
    /// Which visibility area a point is in, or 255 for none.
    ///
    /// <para>
    /// 255 is the engine's own value and is passed through: further down the
    /// pipe it means "no sky camera", and softening it here would leave a
    /// caller unable to say so.
    /// </para>
    /// </summary>
    int GetArea(Vector origin);

    /// <summary>
    /// The SteamID64 the engine authenticated for the client at
    /// <paramref name="entIndex"/>, or 0 when there is none yet (not
    /// authenticated, a bot, an empty slot). <c>GetClientSteamIDByPlayerIndex</c>.
    /// </summary>
    ulong ClientSteamId(int entIndex);

    /// <summary>
    /// The client's address exactly as the engine's network channel has it
    /// (<c>GetPlayerNetInfo(entIndex)->GetAddress()</c>), or null for an empty
    /// slot or a bot.
    /// </summary>
    string? ClientAddress(int entIndex);

    /// <summary>
    /// The client's average outgoing latency in seconds
    /// (<c>GetPlayerNetInfo(entIndex)->GetAvgLatency(FLOW_OUTGOING)</c>), what
    /// stock's UTIL_GetPlayerConnectionInfo reads for the scoreboard's ping;
    /// null for an empty slot or a client with no channel.
    /// </summary>
    float? ClientAvgLatency(int entIndex);

    /// <summary>A client's convar value as the server holds it (<c>GetClientConVarValue</c>), or null.</summary>
    string? ClientConVarValue(int entIndex, string name);

    // NO Con_NPrintf AND NO ClientCommand, AND NEITHER GETS A C++ PROXY.
    //
    // Both are varargs, so managed cannot issue them through a fixed indirect
    // signature — and unlike the client's pair, a proxy would be dead code
    // anyway, because they are refused for a second reason as well:
    // ClientCommand runs a command on a client. A proxy for a call nothing may
    // make is a slot in the ABI with no caller.
    //
    // AND LightStyle IS STILL ABSENT, WHICH IS A CONVERSION THIS PHASE DID NOT
    // MAKE. plan_native_api.md Appendix B rules EngineLightStyle "C -- converts
    // now": it is one engine->LightStyle call and IVEngineServer is bound. It
    // is not converted, because it collides with a decision this tree already
    // took and recorded --
    // VtableBindingTests.TheEngineServerOffersNothingThatChangesTheWorld names
    // LightStyle among six methods this impl must never offer, and the rule
    // behind it is that this class is a READ-ONLY window on IVEngineServer.
    //
    // The capability is not in question: the flat slot EngineLightStyle already
    // makes exactly this call, and IEngineVisibility.SetLightStyle is public
    // game surface today. What is in question is whether the impl class stops
    // being read-only, and lifting a standing policy line to satisfy a plan row
    // is the owner's call rather than a lane's. The row stays on the flat ABI
    // and is NOT in this phase's orphan list.
    //
    // Also absent, and each for its own reason rather than for want of anyone
    // looking: PrecacheModel, CreateEdict, ServerCommand and EmitAmbientSound
    // all CHANGE something, and ServerCommand runs a console line; Time and
    // GetServerTime are answered by the engine's own CGlobalVars through the
    // bound data pointer, which is a load rather than a call. The struct types
    // all 124 slots regardless — it describes the engine's ABI, not this
    // interface's surface.
}


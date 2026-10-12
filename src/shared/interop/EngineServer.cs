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
/// number.
/// </summary>
public interface IVtableEngineServer
{
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

/// <summary>
/// <see cref="IVtableEngineServer"/> over the engine's own object, called
/// through its vtable.
/// </summary>
public sealed unsafe class EngineServer : IVtableEngineServer
{
    /// <summary>
    /// <c>INTERFACEVERSION_VENGINESERVER</c>. The CURRENT name — the older ones
    /// are typedefs in this tree and different objects in the shipped engine,
    /// so asking for one of those would index a shim.
    /// </summary>
    public const string InterfaceVersion = "VEngineServer023";

    private readonly void* _self;
    private readonly EngineServerVtbl* _vtbl;

    private EngineServer(void* self, void** table)
    {
        _self = self;
        _vtbl = (EngineServerVtbl*)table;
    }

    /// <summary>
    /// The engine server, or null with the reason. Null on the client, which is
    /// not an error: this interface exists only on the server.
    ///
    /// <para>
    /// Through <paramref name="engine"/>'s own side's interface factory
    /// (plan_abi_hotreload D8), never the process-wide one that followed the
    /// thread's active side.
    /// </para>
    /// </summary>
    public static IVtableEngineServer? Acquire(ModuleEngine engine, out string why)
        => Acquire((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, out why);

    /// <summary>
    /// The same, through one side's own interface factory (plan_abi_hotreload
    /// D8): what <see cref="EngineBound"/> holds per side.
    /// </summary>
    internal static IVtableEngineServer? Acquire(NativeInterfaceFactory factory, out string why)
        => Vtable.TryAcquire<EngineServerVtbl>(
               factory, SourceSharpFactoryType.AppSystem,
               InterfaceVersion, out void* self, out void** table, out why)
            ? new EngineServer(self, table)
            : null;

    /// <inheritdoc/>
    public string? GameDir() => Fill(_vtbl->GetGameDir, _self, Vtable.MaxPath);

    /// <inheritdoc/>
    public string? MapEntitiesString()
        => Vtable.TextOf(_vtbl->GetMapEntitiesString(_self), Vtable.MaxEntityString);

    /// <inheritdoc/>
    public bool IsModelPrecached(string path) => Ask(_vtbl->IsModelPrecached, _self, path);

    /// <inheritdoc/>
    public bool IsDedicatedServer() => _vtbl->IsDedicatedServer(_self) != 0;

    /// <inheritdoc/>
    public int EntityCount() => _vtbl->GetEntityCount(_self);

    /// <inheritdoc/>
    public ulong ClientSteamId(int entIndex)
    {
        // A CSteamID is one 64-bit word (its bitfields are a union with
        // m_unAll64Bits), so the pointer reads as the SteamID64 itself.
        void* steamId = _vtbl->GetClientSteamIDByPlayerIndex(_self, entIndex);
        return steamId is null ? 0 : *(ulong*)steamId;
    }

    public string? ClientAddress(int entIndex)
    {
        void* channel = _vtbl->GetPlayerNetInfo(_self, entIndex);
        if (channel is null)
            return null;

        var info = *(NetChannelInfoVtbl**)channel;
        return Vtable.TextOf(info->GetAddress(channel), MaxAddress);
    }

    public float? ClientAvgLatency(int entIndex)
    {
        void* channel = _vtbl->GetPlayerNetInfo(_self, entIndex);
        if (channel is null)
            return null;

        var info = *(NetChannelInfoVtbl**)channel;
        return info->GetAvgLatency(channel, 0);   // FLOW_OUTGOING
    }

    public string? ClientConVarValue(int entIndex, string name)
    {
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* p = bytes)
            return Vtable.TextOf(_vtbl->GetClientConVarValue(_self, entIndex, p), MaxConVarValue);
    }

    private const int MaxConVarValue = 256;

    // "[ipv6]:port" is 47 bytes; the engine's own netadr text is shorter.
    private const int MaxAddress = 64;

    public int GetArea(Vector origin)
    {
        // `const Vector &` IS A POINTER, which is the whole of what the ABI
        // says about a reference -- the generated field spells it void* for
        // that reason. Three floats on the stack, and the engine reads them
        // during the call and does not keep them.
        Vector local = origin;

        return _vtbl->GetArea(_self, &local);
    }

    //-------------------------------------------------------------------------
    // TWO SHAPES THAT ARE NOT ONE-LINERS AND SO ARE HELPERS, not members with a
    // body. `GetGameDir` writes into a caller's buffer and `IsModelPrecached`
    // takes a string, and neither is expressible as a single expression -- so
    // the shape lives here once and the member above stays a delegation.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A <c>void ( char *, int )</c> slot that fills a caller's buffer.
    ///
    /// <para>
    /// THE BUFFER IS ZEROED BY THE RUNTIME AND THAT IS LOAD-BEARING: a callee
    /// that writes nothing leaves an empty string rather than whatever was
    /// there. And it is scanned rather than read with
    /// <see cref="Vtable.TextOf"/>, which would answer null for a full buffer
    /// with no terminator where this answers its contents.
    /// </para>
    /// </summary>
    private static string Fill(
        delegate* unmanaged<void*, byte*, int, void> slot, void* self, int size)
    {
        var buffer = new byte[size];

        fixed (byte* p = buffer)
        {
            slot(self, p, size);

            int length = 0;

            while (length < size && p[length] != 0)
                length++;

            return System.Text.Encoding.UTF8.GetString(p, length);
        }
    }

    /// <summary>A <c>bool ( const char * )</c> slot.</summary>
    private static bool Ask(
        delegate* unmanaged<void*, byte*, byte> slot, void* self, string text)
    {
        byte[] bytes = Vtable.Utf8(text);

        fixed (byte* p = bytes)
            return slot(self, p) != 0;
    }
}

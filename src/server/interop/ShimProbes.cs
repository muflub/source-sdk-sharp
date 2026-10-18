namespace SourceSharp.Abi;

//=============================================================================
// ONE SENTENCE PER SHIM INTERFACE, FOR cs_vtable / cs_vtable_client.
//
// plan_native_api.md Part 5a step 5: an area's probe line, with an answer
// something OUTSIDE the engine knows. The probes for the five engine
// interfaces call public impl classes directly from SourceSharp.Server; the
// shim areas cannot be probed that way and this file is why.
//
// WHY THE PROBE IS ON THIS SIDE OF THE ASSEMBLY BOUNDARY.
//
// docs/vtable-binding.md §1a is emphatic that TYPING IS NOT BINDING and that
// the generated layout structs are `internal`. A probe that reached into
// ShimFrameVtbl from SourceSharp.Server would need two things this port has
// deliberately withheld: that assembly compiled `/unsafe`, and
// InternalsVisibleTo pointed at it -- which would hand game code the god-class
// and every layout along with the one struct it wanted. The area facades are
// internal for the same reason.
//
// So the pointer never leaves SourceSharp.Abi and the command asks for a
// STRING. It grows one method per shim area, exactly as cs_vtable grows one
// line per served name.
//
//-----------------------------------------------------------------------------
// A PROBE THAT CALLS THE FACADE PROVES NOTHING, and this is the trap worth
// naming once here rather than rediscovering per area.
//
// IEngineFrame.TickCount reads the engine's own CGlobalVars when that pointer
// is bound and only falls back to the vtable when it is not -- so on a live
// server, calling the facade never touches the binding at all and a probe
// built on it would print a correct number with the slot completely unbound.
// The probes below call the SLOT, and print an independent answer beside it.
//=============================================================================

/// <summary>
/// The live probe lines for the shim-layer interfaces.
///
/// <para>
/// Public because <c>cs_vtable</c> lives in <c>SourceSharp.Server</c>;
/// everything it touches stays internal.
/// </para>
///
/// <para>
/// Each probe takes the asking module's <see cref="ModuleEngine"/>
/// (plan_abi_hotreload D8) and acquires through that side's own interface
/// factory and reads that side's globals, rather than the process-wide
/// factory and the static <c>Engine</c>, which followed the thread's side.
/// </para>
/// </summary>
public static unsafe class ShimProbes
{
    /// <summary>
    /// <c>IVEngineClient</c> asked of THIS side's factory, which is expected to
    /// refuse it: the interface exists only on the client. Reported rather than
    /// skipped, by the standing rule <see cref="Frame"/> states.
    ///
    /// <para>
    /// The acquire is here and not on <c>EngineClient</c> because that class
    /// lives in Client.Interop (lane side-audit-2, 2026-10-02: it wraps an
    /// interface only the client serves, and its proxies are client-only
    /// slots), which the server does not reference.
    /// <see cref="EngineClientInterfaceVersion"/> is its version string, held
    /// equal to <c>EngineClient.InterfaceVersion</c> by a fact.
    /// </para>
    /// </summary>
    public static string EngineClient(ModuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        // The interface alone, not its forwarder table: the table is the
        // client's (Client.Interop, libss_fwd_client), and the question is
        // whether the server's engine answers the version string at all.
        return engine.Api.Interfaces.TryCreate(
                   SourceSharpFactoryType.AppSystem, EngineClientInterfaceVersion,
                   out _, out _, out string? why)
            ? "IVEngineClient: SERVED on the server, which serves no such interface"
            : $"IVEngineClient: REFUSED — {why}";
    }

    /// <summary><c>VENGINE_CLIENT_INTERFACE_VERSION</c>, as <c>EngineClient</c> passes it.</summary>
    public const string EngineClientInterfaceVersion = "VEngineClient014";

    /// <summary>
    /// <c>IShimFrame</c>: the tick through the shim's own vtable, the engine's
    /// <c>CGlobalVars</c> tick beside it, and the frame time.
    ///
    /// <para>
    /// <b>The two ticks are two routes to one number.</b> One comes back
    /// through slot 1 of a table this tree generated from a header it owns; the
    /// other is a load from the engine's own struct through the data pointer
    /// plan_crossings.md Phase X1 bound. Agreeing is what says the slot is the
    /// method the struct claims — an arity-safe misbinding is exactly the
    /// failure a span check cannot see.
    /// </para>
    ///
    /// <para>
    /// A refusal is REPORTED rather than skipped, which is the standing rule
    /// for every line of this command: a refusal nobody printed is
    /// indistinguishable from a call nobody made.
    /// </para>
    /// </summary>
    public static string Frame(ModuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (!Vtable.TryAcquireShim<ShimFrameVtbl>(
                engine.Api.Interfaces, EngineFrame.InterfaceVersion,
                out void* self, out void** table, out string why))
            return $"IShimFrame: REFUSED — {why}";

        var vtbl = (ShimFrameVtbl*)table;

        int throughTheShim = vtbl->TickCount(self);
        float frameTime = vtbl->GetFrameTime(self);

        nint globals = engine.Api.GlobalVarsPointer;

        string second = globals != 0
            ? new Globals(globals).TickCount.ToString()
            : "(gpGlobals not bound)";

        return $"IShimFrame: bound, TickCount() = {throughTheShim}, "
             + $"gpGlobals->tickcount = {second}, GetFrameTime() = {frameTime}";
    }

    /// <summary>
    /// <c>IShimLevel</c>: bound or refused, and the size of the table it
    /// answered for.
    ///
    /// <para>
    /// <b>THIS ONE DOES NOT CALL A METHOD, and that is deliberate.</b> Every
    /// member of IShimLevel is a STEP of the level lifecycle -- it parses the
    /// entity lump, clears the entity list, tears down the sound entity. There
    /// is no read on it to second-source, and calling one from a console
    /// command in the middle of a live map would not be a probe, it would be a
    /// level teardown.
    /// </para>
    ///
    /// <para>
    /// So what this line reports is the acquire itself and the SPAN native
    /// answered for -- and the twenty-two steps are exercised, in order and
    /// four times over, by the thing that is actually equipped to do it:
    /// <c>make live-gate</c>'s two map loads. A level area's live gate is a
    /// level load.
    /// </para>
    /// </summary>
    public static string Level(ModuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (!engine.Api.Interfaces.TryCreate(
                SourceSharpFactoryType.Internal, EngineLevel.InterfaceVersion,
                out nint _, out int entries, out string? error))
            return $"IShimLevel: REFUSED — {error}";

        int declared = Vtable.SlotsIn<ShimLevelVtbl>();

        return $"IShimLevel: bound, native says {entries} slot(s) and "
             + $"ShimLevelVtbl declares {declared}"
             + (declared <= entries ? "" : " — WHICH IS MORE THAN NATIVE HAS");
    }

}

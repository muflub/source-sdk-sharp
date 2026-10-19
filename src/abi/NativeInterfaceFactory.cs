using System.Collections.Concurrent;
using System.Text;

namespace SourceSharp.Abi;

//=============================================================================
// ACQUISITION, AS ONE METHOD WITH THE FACTORY KIND IN FRONT.
//
// plan_native_api.md Part 3b. Three directions compose here and the last
// simplifies the first two: "an internal factory pattern like the existing
// engine pattern", "it should not be opening any modules at all to do this",
// and "all the factory methods can be converted into a single factory method
// with factory type being the first arg that is switched on in native code."
//
// WHAT THAT DELETES, and it is the larger half of the change. The tree used to
// reach an interface by opening a module and reading its exports --
// NativeModules, ProcMaps over /proc/self/maps, Rtti, and two ad-hoc factories
// (EngineFactory, ModuleFactory) that were the same code with a filename
// fixed. None of it survives. The engine already handed this game DLL its
// CreateInterfaceFn pointers; native holds them where they arrive and switches
// on the kind, and managed never holds an engine function pointer at all.
//
// WHY THE PRIMITIVE RETURNS bool RATHER THAN THE POINTER, against the shape
// this tree used to have. `nint TryCreate(..., out string why)` lets a caller
// take the 0 and walk on, and that is exactly what happened: EngineFactory's
// own Create became `=> TryCreate(interfaceVersion, out _, modules)` -- the
// reason discarded, 0 returned, and the diagnostic surfacing later as an
// unrelated null dereference. A bool with out parameters makes ignoring the
// failure a visible act.
//
// AREAS CALL Create AND Create THROWS. An area that cannot acquire its
// interface is not a degraded area, it is a bug, and the native precedent
// already argues it: SourceSharp_AcquireEngineInterfaces returns false the
// moment one interface is missing, because a server missing `modelinfo` does
// not half-work -- it crashes on the first model, several seconds later,
// somewhere else. TryCreate is for the genuinely optional, for pre-init
// diagnostics, and for the probe commands, which want to REPORT a refusal
// rather than die on it.
//=============================================================================

/// <summary>
/// Which factory answers. Mirrors <c>SourceSharpFactoryType</c> in
/// <c>sourcesharp.h</c>, and is <c>: int</c> because the C enum crosses by
/// value in a 32-bit register.
/// </summary>
public enum SourceSharpFactoryType : int
{
    /// <summary>The engine's <c>appSystemFactory</c>.</summary>
    AppSystem = 0,

    /// <summary>The physics module's factory.</summary>
    Physics = 1,

    /// <summary>
    /// The filesystem, wherever this side gets it — the real third factory on
    /// the server, <c>appSystemFactory</c> on the client, which has no third
    /// factory at all. A caller names the role; where it lives is native's
    /// business.
    /// </summary>
    FileSystem = 2,

    /// <summary>
    /// SourceSharp's own — the interfaces the native shim layer implements.
    /// Answerable from DLL load, since its objects are ours.
    /// </summary>
    Internal = 3,
}

/// <summary>
/// An interface could not be acquired.
///
/// <para>
/// Its OWN type, so acquisition failure is catchable distinctly from any
/// <see cref="InvalidOperationException"/> a bound call might raise later. The
/// message carries the factory kind, the version string, and the native
/// reason — native contributed only the last of the three.
/// </para>
/// </summary>
public sealed class InterfaceAcquisitionException : Exception
{
    /// <summary>Which factory was asked.</summary>
    public SourceSharpFactoryType FactoryType { get; }

    /// <summary>The version string that was asked for.</summary>
    public string InterfaceVersion { get; }

    /// <summary>
    /// What the acquire failed on.
    ///
    /// <para>
    /// For a FACTORY refusal this is native's own literal out of the closed
    /// set, verbatim. For a SPAN-CHECK refusal — the layout and the class
    /// disagreeing about how long the table is — it is managed's comparison,
    /// which names both numbers. The two are genuinely different failures and
    /// the property does not pretend otherwise.
    /// </para>
    /// </summary>
    public string Reason { get; }

    internal InterfaceAcquisitionException(
        SourceSharpFactoryType factoryType, string interfaceVersion, string reason)
        : base(NativeInterfaceFactory.Compose(factoryType, interfaceVersion, reason))
    {
        FactoryType = factoryType;
        InterfaceVersion = interfaceVersion;
        Reason = reason;
    }

    private InterfaceAcquisitionException(
        SourceSharpFactoryType factoryType, string interfaceVersion,
        string reason, string message)
        : base(message)
    {
        FactoryType = factoryType;
        InterfaceVersion = interfaceVersion;
        Reason = reason;
    }

    /// <summary>
    /// The same failure, from a report that is ALREADY a whole sentence.
    ///
    /// <para>
    /// <b>This exists because the obvious spelling composed the message
    /// twice</b>, and it took a live run to notice:
    /// the shim acquire of the day answered with a composed report — native's
    /// reason with the factory kind and the version string already in front of
    /// it — and handing that to the constructor above as a bare <c>reason</c>
    /// produced <i>"could not acquire X from the Internal factory: could not
    /// acquire X from the Internal factory: factory does not know that
    /// interface"</i>, once per frame, 1,795 times in one map load.
    /// </para>
    ///
    /// <para>
    /// The report is also not always a factory reason: it is the span check's
    /// comparison when the layout and the class disagree, and composing a
    /// factory sentence in front of that would say the wrong thing about a
    /// failure that has nothing to do with the factory.
    /// </para>
    /// </summary>
    internal static InterfaceAcquisitionException FromReport(
        SourceSharpFactoryType factoryType, string interfaceVersion, string report)
        => new(factoryType, interfaceVersion, report, report);
}

/// <summary>
/// The managed front for the one native acquisition slot.
///
/// <para>
/// <b>Internal, and the pointers do not leave the assembly.</b> Typing is not
/// binding: the layout structs are <c>internal</c> and so is this, so a raw
/// <c>nint</c> never reaches game code. The public surface stays the
/// <c>IEngine*</c> areas and the bound interfaces, and this is consumed by area
/// constructors and impl <c>Acquire</c> methods and nowhere else.
/// </para>
/// </summary>
internal sealed unsafe class NativeInterfaceFactory
{
    /// <summary>
    /// The native slot's shape, as a managed delegate, so a fact can supply one.
    ///
    /// <para>
    /// The seam is here rather than deeper because acquisition happens once per
    /// interface: an indirection that runs once buys a hermetic test of the
    /// error contract, which is the half of this file that cannot be exercised
    /// in a real process without breaking the process.
    /// </para>
    /// </summary>
    internal delegate void* AcquireFn(
        SourceSharpFactoryType type, byte* interfaceVersion,
        int* entries, byte* error, int errorBytes);

    private readonly AcquireFn _acquire;

    // Keyed on (type, version). Caching is safe because CreateInterface
    // hands out REGISTERED SINGLETONS -- EXPOSE_SINGLE_INTERFACE_GLOBALVAR
    // registers one file-scope static per version string -- and the shim
    // layer's own factory keeps the property by construction.
    //
    // NO SIDE IN THE KEY, BECAUSE THE CACHE IS ONE SIDE'S. Each side's native
    // switch answers out of its OWN DLL (SOURCESHARP_FACTORY_INTERNAL hands back
    // server.so's singleton on the server and client.so's on the client for
    // the same version string), so two sides must never share an answer. They
    // cannot: this factory is one side's engine's (EngineSide.Interfaces), over
    // that side's acquire slot, in that side's copy of the Abi
    // (plan_side_isolation P5). The side was in the key while one cache keyed
    // on a thread's side could be asked by either.
    private readonly ConcurrentDictionary<(SourceSharpFactoryType, string), (nint Self, int Entries)>
        _bound = new();

    internal NativeInterfaceFactory(AcquireFn acquire) => _acquire = acquire;

    // NO PROCESS-WIDE INSTANCE (plan_abi_hotreload D8). There was one, over
    // the active side's engine, for the acquirers that held no side; each now
    // acquires through its own side's (EngineSide.Interfaces), so the cache
    // below belongs to one side's factory and two sides share none.

    /// <summary>
    /// Acquire, reporting rather than throwing.
    /// </summary>
    /// <param name="type">Which factory answers.</param>
    /// <param name="interfaceVersion">The factory name, which is the contract.</param>
    /// <param name="self">The interface pointer, on success; zero otherwise.</param>
    /// <param name="entries">Native's precompiled vtable length; zero on failure.</param>
    /// <param name="error">
    /// The composed reason on failure — native's closed-set literal with the
    /// factory kind and the version string managed itself adds — or null on
    /// success.
    /// </param>
    internal bool TryCreate(
        SourceSharpFactoryType type, string interfaceVersion,
        out nint self, out int entries, out string? error)
    {
        bool ok = Ask(type, interfaceVersion, out self, out entries, out string? reason);

        error = ok ? null : Compose(type, interfaceVersion ?? "(null)", reason!);
        return ok;
    }

    /// <summary>
    /// Acquire, or throw <see cref="InterfaceAcquisitionException"/>.
    /// </summary>
    internal nint Create(SourceSharpFactoryType type, string interfaceVersion, out int entries)
    {
        if (Ask(type, interfaceVersion, out nint self, out entries, out string? reason))
            return self;

        throw new InterfaceAcquisitionException(
            type, interfaceVersion ?? "(null)", reason!);
    }

    /// <summary>
    /// The acquire itself, answering with NATIVE'S OWN reason rather than a
    /// composed message.
    ///
    /// <para>
    /// Split out so that <see cref="Create"/> can hand the raw reason to the
    /// exception, which exposes it as a property. Re-deriving it by cutting a
    /// composed string apart would be a parser over prose, and prose is the one
    /// thing here nobody promised the shape of.
    /// </para>
    /// </summary>
    private bool Ask(
        SourceSharpFactoryType type, string interfaceVersion,
        out nint self, out int entries, out string? reason)
    {
        self = 0;
        entries = 0;
        reason = null;

        if (interfaceVersion is null)
        {
            reason = "no interface version was named";
            return false;
        }

        var key = (type, interfaceVersion);

        if (_bound.TryGetValue(key, out var already))
        {
            self = already.Self;
            entries = already.Entries;
            return true;
        }

        // THE SUCCESS PATH ALLOCATES NOTHING ON THE HEAP FOR THE REASON. The
        // buffer is a stackalloc and native writes it only on failure, so a
        // bound interface costs one call and a dictionary insert.
        byte* buffer = stackalloc byte[Vtable.MaxAcquireError];
        byte[] name = Vtable.Utf8(interfaceVersion);

        int count;
        void* answer;

        fixed (byte* p = name)
            answer = _acquire(type, p, &count, buffer, Vtable.MaxAcquireError);

        if (answer == null)
        {
            // DECODED ONLY WHEN THE RETURN IS NULL, which is what makes a stale
            // reason unreportable rather than merely unlikely: on success
            // nothing here ever looks at the buffer.
            reason = Text(buffer, Vtable.MaxAcquireError);
            return false;
        }

        self = (nint)answer;
        entries = count;

        _bound[key] = (self, entries);
        return true;
    }

    /// <summary>
    /// The message, composed the same way for the exception and for
    /// <see cref="TryCreate"/>'s <c>error</c>.
    ///
    /// <para>
    /// MANAGED ADDS THE CONTEXT, NATIVE ADDS THE REASON, and the split is why
    /// the buffer can be promised ASCII: the one string that could carry a byte
    /// over 0x7F is the caller's own version string, and native never echoes
    /// it. Managed already has it and puts it here.
    /// </para>
    /// </summary>
    internal static string Compose(
        SourceSharpFactoryType type, string interfaceVersion, string reason)
        => $"could not acquire \"{interfaceVersion}\" from the {type} factory: {reason}";

    /// <summary>
    /// The reason out of the buffer: bytes up to the terminator, or the whole
    /// buffer if native truncated exactly to the end without room for one.
    /// </summary>
    private static string Text(byte* buffer, int size)
    {
        int length = 0;

        while (length < size && buffer[length] != 0)
            length++;

        return length == 0
            ? "no reason was reported"
            : Encoding.ASCII.GetString(buffer, length);
    }
}

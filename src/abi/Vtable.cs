using System.Runtime.CompilerServices;
using System.Text;

namespace SourceSharp.Abi;

//=============================================================================
// CALLING AN ENGINE INTERFACE FROM C# WITH NO C++ IN BETWEEN.
//
// Three facts about the Itanium C++ ABI and nothing else:
//
//   * an object's first word is its vtable pointer;
//   * a vtable entry is a function pointer;
//   * `this` is the first SysV argument -- which is exactly what
//     `delegate* unmanaged<void*, ...>` puts there.
//
// So a bound call is `_vtbl->GetLevelName(_self)`. The field's POSITION in the
// generated struct is the slot number, no index is written at any call site,
// and nothing at all happens per call.
//
//-----------------------------------------------------------------------------
// WHAT USED TO BE HERE, AND WHY IT IS NOT.
//
// This file was 851 lines and it verified: a symbol route against the shipped
// binary, an RTTI route reading the typeinfo record in front of every vtable, a
// `this`-adjustment confirmation over three ABI invariants, a per-slot layout
// check requiring each bound slot to hold a function neither neighbour holds,
// and a ledger row type carrying a mangled name per method. Beside it ran a
// live differential that called every bound method and held the answer against
// something outside the engine.
//
// All of it is deleted, and the reason is one sentence from the owner:
//
//     THE ENGINE FACTORY INTERFACES ARE STABLE AND DO NOT CHANGE UNLESS THE
//     INTERFACE VERSION STRING CHANGES.
//
// That is the contract Valve's own convention already makes. `CreateInterface`
// either answers for "VEngineClient014" or it does not, and if it does, the
// layout is the one that string names. An apparatus that re-proved it on every
// acquire was answering a question nobody had asked, and its cost was not only
// its own size: it made every unproved slot a `NotBound_` placeholder, which is
// how 259 of 350 fields came to be untyped.
//
//-----------------------------------------------------------------------------
// WHAT REPLACES IT IS ONE COMPARISON, AND IT CHECKS US RATHER THAN THE ENGINE.
//
// SourceSharp.Abi/VtableLayouts.g.cs is GENERATED from the interface headers by
// tools/vtable-layout. A generated file can go stale -- somebody edits a header,
// or lands the generator's input without re-running it -- and then field 71 of
// the struct is a different method from slot 71 of the engine's table. No call
// faults. The wrong function returns whatever it left in the return register,
// writes through whatever it thought its arguments were, and the symptom
// arrives somewhere else, later.
//
// So native holds the count, taken from the COMPILER rather than counted:
// `VTableHook::MemberOf( &Interface::LastMethod ).slot + 1`, in
// sourcesharp/game/shared/shell_vtable_interfaces.cpp. SourceSharp_Acquire-
// Interface hands over the pointer AND that number, and TryAcquire below holds
// it against `sizeof(TVtbl) / sizeof(void*)`.
//
// Equal, construct the impl. Not equal, refuse and name both numbers.
//
// THE ASSUMPTION UNDERNEATH IS MADE TRUE RATHER THAN TRUSTED. Both counts come
// from OUR headers, so the check is only as good as those headers matching the
// engine's -- and `cdll_int.h` declares two virtuals inside `#if` blocks, each
// of which shifts every slot below it. shell_vtable_interfaces.cpp `#error`s if
// it is compiled with USE_SDL off or NO_VOICE on, which are the shipped
// engine's settings, and vtable_index_agreement_test.cpp pins the resulting
// length with a static_assert.
//=============================================================================

/// <summary>
/// The shared half of the vtable binding: acquire, and the two marshalling
/// helpers a one-line delegation needs.
///
/// <para>
/// <b>Nothing here is per call.</b> <see cref="TryAcquire{TVtbl}"/> runs once
/// when an interface is bound; after that the impl classes index their struct
/// directly and this file is not in the path.
/// </para>
/// </summary>
public static unsafe class Vtable
{
    //-------------------------------------------------------------------------
    // HOW FAR A `const char *` ANSWER IS FOLLOWED.
    //
    // Shared, because they are facts about Source's own buffers rather than
    // about any one interface -- and because a per-interface class kept alive to
    // hold one integer is exactly the row table this refactor removed.
    //
    // A CAP IS NOT A GUESS ABOUT THE ANSWER'S LENGTH. It is where the scan for
    // the terminating NUL stops, so a pointer into something that is not a
    // string reads a bounded amount and returns nonsense instead of walking off
    // the end of the mapping.
    //-------------------------------------------------------------------------

    /// <summary>MAX_PATH's own bound, for a level name or a directory.</summary>
    public const int MaxPath = 512;

    /// <summary>Source's own model path limit.</summary>
    public const int MaxModelPath = 260;

    /// <summary>
    /// How many bytes an acquisition failure's reason may take, mirroring
    /// <c>SOURCESHARP_ACQUIRE_ERROR_BYTES</c>.
    ///
    /// <para>
    /// NOT A CAP OF THE SAME KIND as the two above. Those bound a scan for a
    /// terminator in memory somebody else owns; this is the size of a buffer
    /// managed itself passes down, and native truncates into it rather than
    /// refusing. The closed set of reasons is far shorter than this — the room
    /// is spare on purpose, because the day a reason grows is not a day to
    /// discover a bound.
    /// </para>
    /// </summary>
    public const int MaxAcquireError = 256;

    /// <summary>
    /// A map's entity lump, which is the one answer here that is genuinely
    /// large — <c>dm_lockdown</c>'s is a few hundred kilobytes of text.
    /// </summary>
    public const int MaxEntityString = 1 << 21;

    /// <summary>
    /// How many slots a layout struct spans: its size in pointers.
    ///
    /// <para>
    /// This is the struct's CLAIM about the interface, and it is the managed
    /// half of the one check. <c>sizeof</c> of a generated
    /// <c>[StructLayout(LayoutKind.Sequential)]</c> struct of pointer-sized
    /// fields is exactly the field count times eight, so nothing counts
    /// anything.
    /// </para>
    /// </summary>
    public static int SlotsIn<TVtbl>() where TVtbl : unmanaged
        => sizeof(TVtbl) / sizeof(void*);

    /// <summary>
    /// Ask native for an interface by version string and hold its table length
    /// against <typeparamref name="TVtbl"/>.
    ///
    /// <para>
    /// THE ORDER MATTERS AND IT IS SHORT. Native answers for the version string
    /// or it does not; if it does, its precompiled count must equal this
    /// struct's field count; and only then is there anything to construct. A
    /// failure at either step binds nothing at all — there is no partially
    /// bound state for a caller to reach a method through.
    /// </para>
    ///
    /// <para>
    /// <b>Always through a named factory</b> (plan_abi_hotreload D8): the
    /// caller's side's own (<c>EngineSide.Interfaces</c>) or a test's seam. The
    /// overloads that took none reached the process-wide factory, which
    /// followed the thread's active side; their last callers moved to their
    /// side's and they went.
    /// </para>
    /// </summary>
    /// <param name="factory">The asking side's interface factory.</param>
    /// <param name="factoryType">Which of native's factories answers.</param>
    /// <param name="interfaceVersion">
    /// The factory name, which is the contract — <c>"VEngineClient014"</c> and
    /// the rest. Native serves the ones its own side was handed and answers
    /// null for the others, which is not an error: <c>IVEngineClient</c> exists
    /// only on the client and <c>IVEngineServer</c> only on the server.
    /// </param>
    /// <param name="self">The interface pointer, on success.</param>
    /// <param name="table">Its vtable, on success.</param>
    /// <param name="why">One line saying what failed. Empty on success.</param>
    internal static bool TryAcquire<TVtbl>(
        NativeInterfaceFactory factory, SourceSharpFactoryType factoryType,
        string interfaceVersion, out void* self, out void** table, out string why)
        where TVtbl : unmanaged
    {
        self = null;
        table = null;

        if (!factory.TryCreate(
                factoryType, interfaceVersion, out nint answer, out int entries, out string? error))
        {
            why = error!;
            return false;
        }

        // THE CHECK. Both numbers are named, because "the layout is wrong" is
        // not something anybody can act on and "the header says 146, the struct
        // says 145" points straight at a stale VtableLayouts.g.cs.
        int declared = SlotsIn<TVtbl>();

        if (entries != declared)
        {
            why = $"native says \"{interfaceVersion}\" has {entries} vtable entries and "
                  + $"{typeof(TVtbl).Name} declares {declared} — "
                  + "VtableLayouts.g.cs is stale against the headers it is generated "
                  + "from. Run `tools/vtable-layout --write` and read the diff.";
            return false;
        }

        self = (void*)answer;

        // The object's first word IS its vtable pointer. That is the ABI, not a
        // guess about this engine.
        table = *(void***)self;

        if (table == null)
        {
            why = $"\"{interfaceVersion}\" answered with an object whose vtable pointer is null";
            self = null;
            return false;
        }

        why = string.Empty;
        return true;
    }

    /// <summary>
    /// The same, for a SHIM interface — one this tree declares, implements and
    /// compiles itself, served by <see cref="SourceSharpFactoryType.Internal"/>.
    ///
    /// <para>
    /// <b>The span check is <c>managed &lt;= native</c> here, and that is the
    /// only difference.</b> Equality is available for the engine's five because
    /// both numbers come from our headers and the shipped module's layout is
    /// pinned by its version string. A shim class is ours to extend: appending
    /// a method to it puts the new slot at the END and moves nothing, so every
    /// slot managed already knows keeps its index, and a rebuilt <c>server.so</c>
    /// that gained shim methods must not refuse an assembly that has not been
    /// rebuilt with it. The reverse — a managed layout expecting MORE than
    /// native has — is exactly what refuses, by name.
    /// </para>
    ///
    /// <para>
    /// <b>What it does not protect against, said plainly.</b> An insert or a
    /// reorder in the middle passes this check and silently misbinds. That is
    /// why shim interface declarations are append-only BY RULE and why the
    /// generated layout is committed: an insert shows up as a diff in the
    /// middle of a file whose rule is end-only growth, and
    /// <c>tools/vtable-layout</c> fails the run.
    /// </para>
    ///
    /// <para>
    /// <b>Always against a named factory</b> (plan_abi_hotreload D8): the
    /// caller's side's own (<c>EngineSide.Interfaces</c>), or a test's seam.
    /// The overload that took no factory reached the process-wide one, which
    /// followed the thread's active side; its last callers moved to their
    /// side's, and it went. The parameter is also what lets the span check be
    /// TESTED rather than restated: a fact about "shorter binds, longer
    /// refuses" hands in a factory whose native half reports a chosen length.
    /// </para>
    /// </summary>
    internal static bool TryAcquireShim<TVtbl>(
        NativeInterfaceFactory factory, string interfaceVersion,
        out void* self, out void** table, out string why)
        where TVtbl : unmanaged
    {
        self = null;
        table = null;

        if (!factory.TryCreate(
                SourceSharpFactoryType.Internal, interfaceVersion,
                out nint answer, out int entries, out string? error))
        {
            why = error!;
            return false;
        }

        int declared = SlotsIn<TVtbl>();

        if (declared > entries)
        {
            why = $"native says \"{interfaceVersion}\" has {entries} vtable entries and "
                  + $"{typeof(TVtbl).Name} declares {declared} — a shim layout may be "
                  + "SHORTER than the class it describes (the class is append-only) but "
                  + "never longer. Either VtableLayouts.g.cs is stale against the shim "
                  + "header, or this assembly is newer than the game DLL beside it. Run "
                  + "`tools/vtable-layout --write` and read the diff.";
            return false;
        }

        self = (void*)answer;
        table = *(void***)self;

        if (table == null)
        {
            why = $"\"{interfaceVersion}\" answered with an object whose vtable pointer is null";
            self = null;
            return false;
        }

        why = string.Empty;
        return true;
    }

    /// <summary>
    /// A <c>const char *</c> answer as a managed string, or null.
    ///
    /// <para>
    /// A UTIL RATHER THAN A MEMBER, so a method returning <c>const char *</c>
    /// stays a one-liner:
    /// <c>=&gt; Vtable.TextOf(_vtbl-&gt;GetLevelName(_self), MaxPath);</c>
    /// </para>
    /// </summary>
    public static string? TextOf(byte* answer, int maxLength)
    {
        if (answer == null)
            return null;

        int length = 0;

        while (length < maxLength && answer[length] != 0)
            length++;

        return Encoding.UTF8.GetString(answer, length);
    }

    /// <summary>
    /// A managed string as NUL-terminated UTF-8, for a <c>const char *</c>
    /// parameter.
    /// </summary>
    public static byte[] Utf8(string text)
    {
        int count = Encoding.UTF8.GetByteCount(text);
        var bytes = new byte[count + 1];

        Encoding.UTF8.GetBytes(text, 0, text.Length, bytes, 0);

        return bytes;
    }

    /// <summary>
    /// A <c>bool</c> parameter, as the single byte C++ passes.
    ///
    /// <para>
    /// C# <c>bool</c> is not blittable in an unmanaged function pointer, so the
    /// generated structs spell it <c>byte</c> and the conversion happens at the
    /// one-line call site rather than being hidden in a marshaller.
    /// </para>
    /// </summary>
    public static byte Bit(bool value) => value ? (byte)1 : (byte)0;
}

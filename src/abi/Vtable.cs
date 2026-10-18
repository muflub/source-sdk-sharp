using System.Runtime.CompilerServices;
using System.Text;

namespace SourceSharp.Abi;

//=============================================================================
// OUR OWN NATIVE INTERFACES, AND THE HELPERS EVERY BINDING SHARES.
//
// The engine's interfaces are no longer called through their vtables: every
// call managed makes goes through a generated native forwarder
// (plan_engine_forwarders.md, Forwarders.cs), so nothing here knows how either
// C++ ABI lays out an engine class. What is left is the acquire of the shim
// interfaces this tree declares and compiles itself (TryAcquireShim), whose
// layouts are ours and ordered alike on both ABIs -- no virtual destructor, no
// overload -- and the marshalling helpers a one-line delegation needs.
//
// So a bound shim call is `_vtbl->Frame(_self)`. The field's POSITION in the
// generated struct is the slot number, no index is written at any call site,
// and nothing at all happens per call.
//=============================================================================

/// <summary>
/// The shim acquire, and the marshalling helpers a one-line delegation needs.
///
/// <para>
/// <b>Nothing here is per call.</b> <see cref="TryAcquireShim{TVtbl}"/> runs
/// once when an interface is bound; after that the impl classes index their
/// struct directly and this file is not in the path.
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
    /// Ask native for a SHIM interface — one this tree declares, implements and
    /// compiles itself, served by <see cref="SourceSharpFactoryType.Internal"/>
    /// — by name, and hold its table length against <typeparamref name="TVtbl"/>.
    ///
    /// <para>
    /// <b>The span check is <c>managed &lt;= native</c>.</b> A shim class is
    /// ours to extend: appending
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

        // Our layouts have no virtual destructor and no overload, so the span
        // is the same under both ABIs and the struct's size is the count.
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

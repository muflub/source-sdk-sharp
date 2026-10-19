using System.Runtime.CompilerServices;
using System.Text;

namespace SourceSharp.Abi;

//=============================================================================
// THE MARSHALLING HELPERS EVERY BINDING SHARES.
//
// No interface is called through its vtable from managed: every call goes
// through a generated native forwarder (plan_engine_forwarders.md,
// plan_forwarders_everywhere.md, Forwarders.cs), so nothing here knows how
// either C++ ABI lays out a class. What is left are the bounds and the
// marshalling helpers a one-line delegation needs.
//=============================================================================

/// <summary>
/// The marshalling helpers a one-line delegation needs, and the bounds a
/// <c>const char *</c> answer is read within.
///
/// <para>
/// <b>Nothing here is per call</b> beyond the helper itself: a binding calls
/// its forwarder struct directly, as <c>_fwd-&gt;Frame(_self)</c>.
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

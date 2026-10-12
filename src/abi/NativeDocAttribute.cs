using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// The C++ comment that belongs above a struct or a field in the GENERATED
/// native declaration of the engine block.
///
/// <para>
/// WHY THE PROSE IS AN ATTRIBUTE AND NOT JUST AN XML DOC. The native
/// declaration used to be hand-written, and the reasoning that went with it —
/// why the head's last word is declared rather than left implicit, why the
/// reservations are <c>uint64</c> and not
/// bytes, why the client tail's pose is not the head's placement — lived beside
/// the fields it explained. Generating the struct without the prose would have
/// moved the fields into build output and left the reasoning behind in a file
/// with nothing under it. So the prose comes along: it is declared here, once,
/// and emitted into the header beside the field it is about.
/// </para>
///
/// <para>
/// The XML <c>&lt;summary&gt;</c> on the same member is for managed callers and
/// says the same thing in C# terms; this is the exact text a C++ reader sees,
/// wrapped as it will be emitted. <c>{size}</c> in a struct's prose is replaced
/// with what <see cref="Marshal.SizeOf(Type)"/> reports, so a sentence that
/// quotes the struct's width cannot go stale.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Field, AllowMultiple = false)]
public sealed class NativeDocAttribute : Attribute
{
    /// <summary>The comment's lines, already wrapped, without the leading <c>//</c>.</summary>
    public NativeDocAttribute(params string[] lines) => Lines = lines;

    /// <summary>The comment's lines, already wrapped, without the leading <c>//</c>.</summary>
    public string[] Lines { get; }

    /// <summary>
    /// A section rule to emit above this field, as in
    /// <c>// ---- identity ----...</c>. Null for a field that continues the
    /// section before it.
    /// </summary>
    public string? Section { get; set; }
}

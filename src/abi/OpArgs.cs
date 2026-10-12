using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// THE ARGUMENTS OF ONE ENTITY OP. Mirrors <c>SourceSharpOpArgs</c> in
/// <c>sourcesharp.h</c> (plan_entity.md §3.4, step 1f).
///
/// <para>
/// §3.4 replaces thirteen per-event ABI slots with one. Thirteen events do not
/// have one argument list, so this is the union of them, flattened: each op
/// reads the fields it has and ignores the rest. The per-op table lives on the
/// <c>Dispatch</c> slot's own comment in <c>sourcesharp.h</c>, in one place, so
/// there is one thing to keep true rather than two.
/// </para>
///
/// <para>
/// A STRUCT AND NOT A BYTE BUFFER: a fixed layout is checked by both compilers,
/// where a buffer would put a hand-written decoder on each side of a layout
/// neither checks. The field order leaves NO interior padding —
/// <see cref="BlobBytes"/> and <see cref="Other"/> fill the eight bytes an
/// 8-aligned pointer would otherwise leave after <see cref="Blob"/>, and the
/// two trailing 32-bit words fill the tail — so both sides can assert one
/// number, 64, rather than trusting the compiler and the marshaller to have
/// agreed. <c>EntityOpTests.OpArgsIsSixtyFourBytes</c> and
/// <c>entity_op_test.cpp</c> are the two assertions.
/// </para>
///
/// <para>
/// Crosses BY POINTER. The caller owns it for the duration of the call and
/// every pointer in it — the blob, the strings, the vectors — is the engine's
/// or the shell's and does not outlive the call. Nothing outside the
/// bootstrap's dispatch thunk should hold one; what a callback gets is the
/// frame-scoped <see cref="EntityRef"/> built from it.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct OpArgs
{
    /// <summary>The entity's payload blob, engine block first. Null if it has none.</summary>
    public void* Blob;

    /// <summary>How long <see cref="Blob"/> is. Zero when it is null.</summary>
    public int BlobBytes;

    /// <summary>
    /// The other entity in this op — a toucher, an attacker, a blocker, an
    /// activator, a filter's subject. The INVALID handle when the op has none,
    /// never null: "no entity" has one spelling in this API and it is a handle
    /// that does not resolve.
    /// </summary>
    public uint Other;

    /// <summary>The classname, whenever the op carries one. UTF-8, null-terminated.</summary>
    public byte* Text0;

    /// <summary>The op's first string: a keyvalue's key, an input's name.</summary>
    public byte* Text1;

    /// <summary>The op's second string: a keyvalue's value, an input's value as text.</summary>
    public byte* Text2;

    /// <summary>Damage: where the blow landed. PositionChanged: the previous origin. May be null.</summary>
    public float* Vec0;

    /// <summary>Damage: which way it was going. May be null.</summary>
    public float* Vec1;

    /// <summary>Damage: how much. Input: the value converted to a number.</summary>
    public float Float0;

    /// <summary>Damage: the DMG_ word. FilterPasses: the damage type.</summary>
    public int Int0;
}

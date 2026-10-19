using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// THE ENGINE BLOCK: the fixed prefix at offset 0 of the blob every entity
/// carries (plan.md §4, plan_entity.md §3.2).
///
/// <para>
/// WHY IT EXISTS. Most of what the engine asks a shell for is a getter it calls
/// synchronously and often — <c>GetCollisionOrigin</c>, <c>OBBMins</c>,
/// <c>GetSolid</c>, <c>GetRenderOrigin</c>, <c>GetModel</c>,
/// <c>IsTransparent</c>, <c>ShadowCastType</c>, <c>GetSkin</c>. The spatial
/// partition asks per trace and the renderer per draw, so those calls cannot
/// cross into managed code; and they are not native policy either. They are
/// answered from memory MANAGED CODE WRITES. This is that memory.
/// </para>
///
/// <para>
/// A STRUCT AND NOT A TABLE OF OFFSETS. A shell getter has to be one load at a
/// constant offset. An offset table would put an indirection in the partition's
/// inner loop and buy nothing; a struct can also be asserted, which a table
/// looked up at runtime cannot.
/// </para>
///
/// <para>
/// BOTH TAILS ARE PRESENT ON BOTH SIDES, and that is not an oversight. The
/// fingerprint below goes on the join wire: the server sends its value and the
/// client compares it with its own (see <see cref="EngineBlockFingerprint"/> and
/// <c>LayoutCheckMessage</c>). If the server built a block with only the server
/// tail and the client one with only the client tail, the two values could never
/// agree and the comparison would refuse every join it ever saw — a check that
/// cannot pass is no more use than one that cannot fail. So the block is ONE
/// layout, identical on both sides, and the tails say who WRITES what rather
/// than who has it.
/// </para>
///
/// <para>
/// §7's "ENGINE BLOCK DRIFT" USED TO BE ABOUT TWO DECLARATIONS — this one and a
/// hand-written <c>EngineBlock_t</c> in
/// <c>sourcesharp/game/shared/engine_block.h</c>. There is one now:
/// <see cref="NativeHeaderText"/> emits the native structs as well as the
/// assertions about them, and that header is what the game includes. Three
/// checks still stand behind it, and they check different things:
/// </para>
/// <list type="number">
///   <item>an xUnit fact per field against <see cref="Marshal.OffsetOf(Type, string)"/>;</item>
///   <item>a gtest per field against C++'s <c>offsetof</c>;</item>
///   <item>a fingerprint computed FROM THIS DECLARATION, exchanged at join.</item>
/// </list>
/// <para>
/// The first two are per-side and cannot catch a client and a server built from
/// different sources, because each build is self-consistent. The third can. The
/// gtest is now the one check whose two sides come from different authors — its
/// offsets are hand-written literals and the struct they measure is generated —
/// which makes it more valuable than it was when the struct beside it was typed
/// by a person, not less.
/// </para>
///
/// <para>
/// THE FIELD NAMES ARE THE SAME ON BOTH SIDES, deliberately, rather than
/// Hungarian on the C++ half. The generated header names each field in an
/// <c>offsetof</c>, so identical spelling is what makes the two declarations
/// mechanically comparable instead of comparable through a mapping table that
/// would itself be a third thing to keep honest.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[NativeDoc(
    "The block itself, at offset 0 of every entity's blob: {size} bytes of head",
    "and two tails, in the order the fingerprint hashes them.")]
public struct EngineBlock
{
    /// <summary>What both sides read: identity and placement.</summary>
    [NativeDoc("What both sides read: identity and placement.")]
    public EngineBlockHead Head;

    /// <summary>What the server writes and the engine reads back through it.</summary>
    [NativeDoc("What the server writes and the engine reads back through it.")]
    public EngineBlockServerTail Server;

    /// <summary>The interpolated pose and the render state the client draws from.</summary>
    [NativeDoc("The interpolated pose and the render state the client draws from.")]
    public EngineBlockClientTail Client;

    /// <summary>
    /// The block's size in bytes — the number every generated entity layout
    /// starts after, because the block is the FIRST thing in the blob.
    ///
    /// <para>
    /// A <c>const</c>, AND THAT IS WHAT MAKES IT USABLE BY THE GENERATOR.
    /// <c>NetStateGenerator</c> has to know where an entity's own fields begin
    /// while it is compiling, and the only thing it can read at that point is
    /// the declaration's syntax and symbols — it cannot run
    /// <see cref="Marshal.SizeOf{T}()"/>, and reimplementing .NET's sequential
    /// layout rules to work the answer out would be a second opinion about this
    /// struct that could be subtly wrong and still believed.
    /// </para>
    ///
    /// <para>
    /// So the declaration states its own size and <c>EngineBlockTests</c>
    /// asserts this number against what the runtime actually lays out. Change a
    /// field without changing this and that fact goes red before anything is
    /// misplaced.
    /// </para>
    /// </summary>
    public const int SizeBytes = 488;

    /// <summary>
    /// What the runtime says, for the fact that compares the two. Kept beside
    /// <see cref="SizeBytes"/> so the pair reads as the pair it is.
    /// </summary>
    public static int MarshalledBytes => Marshal.SizeOf<EngineBlock>();

    //-------------------------------------------------------------------------
    // The generated native header.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The exact text of <c>sourcesharp/game/shared/engine_block_generated.h</c>,
    /// built from this declaration by reflection: the <c>struct</c>s, the prose
    /// above them, the fingerprint, and one <c>static_assert</c> per field.
    ///
    /// <para>
    /// THE NATIVE SIDE IS BUILD OUTPUT, and that is the whole point of emitting
    /// the structs and not only the assertions. It used to be that
    /// <c>engine_block.h</c> declared <c>EngineBlock_t</c> by hand and the
    /// generated asserts caught it drifting; two declarations of one layout,
    /// with a good guard. Now there is one declaration — this one — and
    /// <c>engine_block.h</c> is prose and an <c>#include</c>.
    /// </para>
    ///
    /// <para>
    /// The file is still not hand-editable: <c>EngineBlockTests</c> compares
    /// what is on disk with this and goes red on any difference, in either
    /// direction. What the surviving <c>static_assert</c>s catch is no longer a
    /// hand edit — there is none to make — but the two things this generator
    /// does not control: the widths and alignments of Valve's <c>Vector</c>,
    /// <c>QAngle</c>, <c>matrix3x4_t</c> and <c>uint64</c>, and
    /// <see cref="NativeTypeNames"/>, which is hand-written data. The generated
    /// header says so, at length, above the assertions.
    /// </para>
    /// </summary>
    public static string NativeHeaderText => BuildNativeHeader();

    /// <summary>The path of the generated header, relative to the repository root.</summary>
    public const string NativeHeaderPath =
        "src/sourcesharp/game/shared/engine_block_generated.h";

    private static string BuildNativeHeader()
    {
        var sb = new StringBuilder();

        // "\n" and not Environment.NewLine: the file is compared byte for byte
        // against this and lives in git, so the line ending is part of the
        // contract rather than a property of whoever last ran the suite.
        void Line(string text) => sb.Append(text).Append('\n');

        void Rule() => Line("//" + new string('-', 77));

        void Banner(params string[] lines)
        {
            Rule();
            foreach (string l in lines)
                Line(l.Length == 0 ? "//" : "// " + l);
            Rule();
        }

        // The attribution header (tools/valve-header.txt): the header this
        // writes is native code built against SDK headers, which the move
        // ledger marks valve_header=yes, and tools/valve-header check wants
        // it byte for byte at the top.
        Line("//========= Copyright Valve Corporation, All rights reserved. ============//");
        Line("//");
        Line("// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:");
        Line("// https://github.com/ValveSoftware/source-sdk-2013");
        Line("//");
        Line("//=============================================================================//");
        Line("");
        Line("// <auto-generated/> from SourceSharp.Abi.EngineBlock.");
        Line("//");
        Line("// DO NOT EDIT, AND DO NOT DECLARE THE BLOCK ANYWHERE ELSE. The structs below");
        Line("// and the static_asserts under them are BOTH built from the C# declaration of");
        Line("// the engine block (plan_entity.md §3.2), and EngineBlockTests compares this");
        Line("// file byte for byte with what that declaration produces. There is no longer a");
        Line("// second, hand-written EngineBlock_t to keep in step with it: engine_block.h");
        Line("// includes this and adds nothing to the layout.");
        Line("//");
        // NO TRAILING BACKSLASHES in these three lines, however natural a shell
        // continuation would look here: a `//` comment line ending in one splices
        // the next line into it, and gcc says so as -Wcomment on every TU that
        // includes this file. The command is written on one line instead.
        Line("// Regenerate with this, as ONE command -- the wrapping below is this");
        Line("// comment's and not the shell's:");
        Line("//     SOURCESHARP_WRITE_ENGINE_BLOCK_HEADER=1 dotnet test");
        Line("//         src/sourcesharp/managed/SourceSharp.Tests");
        Line("//         --filter FullyQualifiedName~EngineBlockTests");
        Line("//");
        Line("// The prose about WHY the block exists is in engine_block.h. The prose about");
        Line("// what each struct and each field IS travels with the declaration, out of the");
        Line("// [NativeDoc] attributes on the C# side, and is emitted below.");
        Line("");
        Line("#ifndef SOURCESHARP_ENGINE_BLOCK_GENERATED_H");
        Line("#define SOURCESHARP_ENGINE_BLOCK_GENERATED_H");
        Line("");
        Line("#include <stddef.h>");
        Line("#include <stdint.h>");
        Line("");
        Line("#include \"mathlib/mathlib.h\"");
        Line("#include \"mathlib/vector.h\"");
        Line("");
        Banner("The fingerprint of the C# declaration, as the join handshake sends it.",
               "",
               "Nothing a C++ author can type moves this number -- that is the whole of the",
               "defence. It is here so the native half carries a record of WHICH C# block it",
               "was built against, and so a stale header is a value that disagrees with",
               "EngineBlockFingerprint.Current rather than a silence.");
        Line($"#define SOURCESHARP_ENGINE_BLOCK_FINGERPRINT 0x{EngineBlockFingerprint.Current:x8}u");

        //---------------------------------------------------------------------
        // The declarations themselves.
        //---------------------------------------------------------------------

        foreach ((string cppName, Type type) in NativeStructs)
        {
            Line("");
            Banner(ProseOf(type, Marshal.SizeOf(type)));
            Line($"struct {cppName}");
            Line("{");

            FieldInfo[] fields = FieldsOf(type);

            // One column for the whole struct, wide enough for its widest type
            // name. Per struct rather than per file so a nested struct's long
            // name does not push every primitive in the head out to column 25.
            int column = fields.Max(f => NativeDeclarationOf(f).CppType.Length) + 1;

            for (int i = 0; i < fields.Length; i++)
            {
                NativeDocAttribute doc = DocOf(fields[i]);

                if (i > 0)
                    Line("");

                if (doc.Section is not null)
                {
                    Line("\t" + $"// ---- {doc.Section} ".PadRight(75, '-'));
                    Line("");
                }

                foreach (string l in doc.Lines)
                    Line(l.Length == 0 ? "\t//" : "\t// " + l);

                (string cppType, string declarator) = NativeDeclarationOf(fields[i]);
                Line("\t" + cppType.PadRight(column) + declarator + ";");
            }

            Line("};");
        }

        //---------------------------------------------------------------------
        // The assertions.
        //---------------------------------------------------------------------

        Line("");
        Banner("Sizes, and then every field's offset EXCEPT THE FIRST OF EACH STRUCT.",
               "",
               "WHAT THESE STILL CATCH, NOW THAT THE STRUCTS ABOVE ARE GENERATED TOO. Both",
               "halves of each comparison come out of the same C# declaration, so they no",
               "longer catch a hand edit to EngineBlock_t -- there is nothing left to hand",
               "edit. Two things can still drive the two apart, and both are real:",
               "",
               "  * THE ENGINE'S OWN TYPES. Vector, QAngle, matrix3x4_t and uint64 are",
               "    Valve's, not ours. The offsets below are what .NET lays the C# struct",
               "    out at, using ITS opinion of those widths; the offsets they are compared",
               "    against are what the C++ compiler lays this struct out at, using Valve's",
               "    headers. sizeof(Vector) becoming 16 for an SSE build moves every offset",
               "    after Origin, and stops the build here rather than in a trace.",
               "  * THE C#-TO-C++ TYPE MAP. EngineBlock.NativeTypeNames is hand-written data,",
               "    and it is what turns `public int Skin` into `int32_t Skin`. A field",
               "    mapped to the wrong width emits a struct that lays out differently from",
               "    the C# one these numbers came from, and the first assert past the",
               "    mistake fires.",
               "",
               "WHAT WAS DELETED, AND WHY. One assert per struct: offsetof( X, <first field>",
               ") == 0. A C++ struct with no base class puts its first member at offset zero,",
               "always. That assert could not fail when EngineBlock_t was hand-written and it",
               "cannot fail now, and a check that cannot fail is not a check.",
               "",
               "THE INDEPENDENT SECOND OPINION IS IN engine_block_layout_test.cpp and stays",
               "there: it asserts every offset again against HAND-WRITTEN literals, and it",
               "asserts that Valve's real PVSInfo_t and CPlayerState still fit the room the",
               "server tail reserves for them -- a comparison against a struct we do not own,",
               "which no amount of generating either side can make tautological.");

        foreach ((string cppName, Type type) in NativeStructs)
        {
            Line("");
            Line($"static_assert( sizeof( {cppName} ) == {Marshal.SizeOf(type)},");
            Line($"               \"{cppName} is not the size SourceSharp.Abi.{type.Name} is\" );");

            foreach (FieldInfo field in FieldsOf(type).Skip(1))
            {
                int offset = (int)Marshal.OffsetOf(type, field.Name);
                Line($"static_assert( offsetof( {cppName}, {field.Name} ) == {offset},");
                Line($"               \"{cppName}::{field.Name} is not where {type.Name}.{field.Name} is\" );");
            }
        }

        Line("");
        Line("#endif // SOURCESHARP_ENGINE_BLOCK_GENERATED_H");

        return sb.ToString();
    }

    /// <summary>
    /// The C++ comment above a struct, with <c>{size}</c> replaced by the width
    /// the runtime actually reports — so a sentence that quotes the struct's
    /// size cannot outlive the size.
    /// </summary>
    private static string[] ProseOf(Type type, int size)
        => (type.GetCustomAttribute<NativeDocAttribute>()
            ?? throw new InvalidOperationException(
                $"{type.Name} carries no [NativeDoc]. The native declaration is generated "
                + "from this one, so a struct with no prose would emit a struct with no "
                + "explanation and the reasoning would be lost rather than moved."))
           .Lines.Select(l => l.Replace("{size}", size.ToString())).ToArray();

    /// <summary>
    /// The C++ comment above a field. Mandatory for the reason
    /// <see cref="ProseOf"/> gives, and a field added without one is a failed
    /// build of the header rather than a silently bare line in it.
    /// </summary>
    private static NativeDocAttribute DocOf(FieldInfo field)
        => field.GetCustomAttribute<NativeDocAttribute>()
           ?? throw new InvalidOperationException(
               $"{field.DeclaringType?.Name}.{field.Name} carries no [NativeDoc]. Every "
               + "field of the block is emitted into the native header with the prose that "
               + "explains it; a field without prose would arrive there bare.");

    /// <summary>
    /// How one C# field is spelled in C++: the type name, and the declarator
    /// (the field's name, plus an array bound if it is a reservation).
    /// </summary>
    public static (string CppType, string Declarator) NativeDeclarationOf(FieldInfo field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var buffer = field.GetCustomAttribute<FixedBufferAttribute>();

        return buffer is not null
            ? (NativeTypeOf(buffer.ElementType), $"{field.Name}[{buffer.Length}]")
            : (NativeTypeOf(field.FieldType), field.Name);
    }

    /// <summary>
    /// The C++ spelling of one managed type, or a throw.
    ///
    /// <para>
    /// A THROW AND NOT A FALLBACK, because a fallback is the one way this
    /// generator could do real damage. Emitting <c>int</c> for a type it does
    /// not know would produce a native struct that lays out differently from the
    /// C# one WITHOUT moving the fingerprint — the fingerprint is computed from
    /// the C# declaration alone, and the C# declaration would be unchanged. That
    /// is precisely the failure the block exists to make impossible, so an
    /// unknown type stops the generator instead.
    /// </para>
    /// </summary>
    public static string NativeTypeOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        foreach ((string cppName, Type managed) in NativeStructs)
            if (managed == type)
                return cppName;

        return NativeTypeNames.TryGetValue(type, out string? name)
            ? name
            : throw new NotSupportedException(
                $"{type.FullName} has no C++ spelling, so the engine block's native "
                + "declaration cannot be generated. Add one to EngineBlock.NativeTypeNames. "
                + "Guessing a width here would emit a native struct that disagrees with the "
                + "C# one and move nothing the fingerprint can see.");
    }

    /// <summary>
    /// How the block's LEAF types are spelled in C++.
    ///
    /// <para>
    /// FIXED WIDTHS, NEVER <c>int</c> OR <c>unsigned</c>. The fingerprint is
    /// computed from the C# declaration, so a C++ width that differed from the
    /// C# one is exactly the drift it cannot see; <c>int32_t</c> and friends say
    /// the width in the name.
    /// </para>
    ///
    /// <para>
    /// <c>Vector</c>, <c>QAngle</c> and <c>matrix3x4_t</c> are spelled as the
    /// ENGINE TYPES and not as float arrays, because the shell's getters return
    /// references into the block and the engine expects those exact types back.
    /// Their widths are Valve's to decide, which is why the offsets asserted at
    /// the bottom of the generated header can still fail.
    /// </para>
    ///
    /// <para>
    /// The block's own structs are not here: they come from
    /// <see cref="NativeStructs"/>, so the pairing is stated once.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<Type, string> NativeTypeNames { get; } =
        new Dictionary<Type, string>
        {
            [typeof(int)]       = "int32_t",
            [typeof(uint)]      = "uint32_t",
            [typeof(byte)]      = "uint8_t",
            [typeof(ulong)]     = "uint64",
            [typeof(float)]     = "float",
            [typeof(Vector)]    = "Vector",
            [typeof(QAngle)]    = "QAngle",
            [typeof(Transform)] = "matrix3x4_t",
        };

    /// <summary>
    /// The four declarations the header asserts about, C++ name first, in the
    /// order they are emitted. A pair rather than a naming convention, so that
    /// renaming one side is a compile error here instead of a silently empty
    /// set of assertions.
    /// </summary>
    public static IReadOnlyList<(string CppName, Type Managed)> NativeStructs { get; } =
        new (string, Type)[]
        {
            ("EngineBlockHead_t",       typeof(EngineBlockHead)),
            ("EngineBlockServerTail_t", typeof(EngineBlockServerTail)),
            ("EngineBlockClientTail_t", typeof(EngineBlockClientTail)),
            ("EngineBlock_t",           typeof(EngineBlock)),
        };

    /// <summary>
    /// One struct's instance fields in declaration order — which, under
    /// <see cref="LayoutKind.Sequential"/>, is memory order.
    /// </summary>
    public static FieldInfo[] FieldsOf(Type type)
        => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
}

/// <summary>
/// The shared head: who the entity is and where it is. Read by both sides, and
/// by every <c>ICollideable</c> getter the spatial partition calls.
///
/// <para>
/// 160 BYTES, AND THE LAST TWO FIELDS ARE WHY IT IS STILL A MULTIPLE OF EIGHT.
/// The server tail behind this one holds storage the engine writes engine
/// structs into, and those want eight-byte alignment; a head that ended at an
/// odd word would be followed by four bytes of padding that BOTH declarations
/// would have to invent identically. Padding nobody wrote is padding nobody
/// checks, so the words at the end are declared instead — and none of them is
/// padding at all: <see cref="ChildCount"/>,
/// <see cref="InterpolationFrame"/> and <see cref="OldInterpolationFrame"/> are
/// three fields with jobs, which explains the alignment at least as well. The
/// teleport pair was added in that order for exactly this reason: one of them
/// alone would have left a hole behind it.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[NativeDoc(
    "The shared head: who the entity is and where it is.",
    "",
    "{size} BYTES, AND THE LAST FIELDS ARE WHY IT IS A MULTIPLE OF EIGHT. The",
    "server tail behind this one holds storage the engine writes engine structs",
    "into, and those want eight-byte alignment. A head that ended at an odd word",
    "would be followed by four bytes of padding that both declarations would have",
    "to invent identically, and padding nobody wrote is padding nobody checks. So",
    "those words are declared and hashed like any other field -- and they are",
    "ChildCount, InterpolationFrame and OldInterpolationFrame, three fields with",
    "jobs, rather than named holes.")]
public struct EngineBlockHead
{
    // ---- identity ----------------------------------------------------------

    /// <summary>This entity's <c>CBaseHandle</c>, packed index and serial.</summary>
    [NativeDoc("This entity's CBaseHandle, packed index and serial.", Section = "identity")]
    public uint Handle;

    /// <summary>The classname's index in the shared string table.</summary>
    [NativeDoc("The classname's index in the shared string table.")]
    public int ClassnameIndex;

    /// <summary>What <c>GetModelIndex</c> returns.</summary>
    [NativeDoc("What GetModelIndex returns.")]
    public int ModelIndex;

    /// <summary>The collision model, which is not always the render model.</summary>
    [NativeDoc("The collision model, which is not always the render model.")]
    public int CollisionModelIndex;

    // ---- placement ---------------------------------------------------------

    /// <summary>
    /// What <c>GetCollisionOrigin</c> returns, and — through
    /// <see cref="InterpolatedAttribute"/> — the value the client keeps a
    /// history of and draws a point out of. See <c>SourceSharp.Client</c>'s
    /// <c>Interpolation</c>.
    /// </summary>
    [NativeDoc("What GetCollisionOrigin returns.", Section = "placement")]
    [Interpolated(InterpolationKind.Vector, nameof(EngineBlockClientTail.RenderOrigin))]
    public Vector Origin;

    /// <summary>What <c>GetCollisionAngles</c> returns. Interpolated, as a
    /// facing rather than as three numbers — see <see cref="InterpolationKind.Angle"/>.</summary>
    [NativeDoc("What GetCollisionAngles returns.")]
    [Interpolated(InterpolationKind.Angle, nameof(EngineBlockClientTail.RenderAngles))]
    public QAngle Angles;

    /// <summary>OBB mins, in collision space.</summary>
    [NativeDoc("OBB mins, in collision space.")]
    public Vector Mins;

    /// <summary>OBB maxs, in collision space.</summary>
    [NativeDoc("OBB maxs, in collision space.")]
    public Vector Maxs;

    /// <summary>
    /// The cached world transform — Source's <c>matrix3x4_t</c>, twelve floats,
    /// row-major. <c>CollisionToWorldTransform</c> returns a reference to it, so
    /// it is storage and not something computed per call.
    /// </summary>
    [NativeDoc(
        "The cached world transform. CollisionToWorldTransform returns a reference",
        "to it, so it is storage rather than something computed per call.")]
    public Transform WorldTransform;

    /// <summary>A <c>SolidType_t</c>.</summary>
    [NativeDoc("A SolidType_t.")]
    public int SolidType;

    /// <summary>The <c>FSOLID_*</c> bits.</summary>
    [NativeDoc("The FSOLID_* bits.")]
    public int SolidFlags;

    /// <summary>A <c>Collision_Group_t</c>.</summary>
    [NativeDoc("A Collision_Group_t.")]
    public int CollisionGroup;

    /// <summary>
    /// The move parent's handle, or an invalid handle.
    ///
    /// <para>
    /// <b>Written by managed code, and the second of the two words parenting is
    /// guarded on</b> — see <see cref="ChildCount"/> for the first.
    /// <c>ChildCount</c> answers "does anything hang off me"; this answers "does
    /// anything carry me", and between them every hot entry point into parenting
    /// decides whether to cross without crossing to find out. It is also the
    /// whole of the answer to "is this entity parented": a question that had a
    /// registry walk behind it is now one load and a validity test, so it costs
    /// no crossing at all.
    /// </para>
    /// </summary>
    [NativeDoc(
        "The move parent, as a handle. Never a pointer: the blob is engine-owned",
        "memory and a pointer into it outlives nothing safely.",
        "",
        "Written by managed code. With ChildCount it is what lets a position",
        "write, a removal and the two frame conversions decide whether parenting",
        "is involved at all without crossing to ask, and it is the whole answer",
        "to \"is this entity parented\".")]
    public uint ParentHandle;

    /// <summary>The owner's handle, or an invalid handle.</summary>
    [NativeDoc("The owner, as a handle, and for the same reason.")]
    public uint OwnerHandle;

    /// <summary>A <c>MoveType_t</c>.</summary>
    [NativeDoc("A MoveType_t.")]
    public int MoveType;

    /// <summary>The <c>FL_*</c> flags.</summary>
    [NativeDoc("The FL_* flags.")]
    public int Flags;

    /// <summary>The <c>EF_*</c> effects bits.</summary>
    [NativeDoc("The EF_* effects bits.")]
    public int Effects;

    /// <summary>0 to 3, as <c>GetWaterLevel</c> reports it.</summary>
    [NativeDoc("0 to 3, as GetWaterLevel reports it.")]
    public int WaterLevel;

    /// <summary>
    /// HOW MANY ENTITIES THIS ONE CARRIES — the parenting registry's child
    /// count, and the word that used to be <c>Padding0</c>.
    ///
    /// <para>
    /// <b>It is a guard, and the guard is the whole reason parenting can be
    /// managed policy.</b> The registry lives in <see cref="Hierarchy"/> now, so
    /// native code cannot see whether the entity it has just moved carries
    /// anything — and a position write is the hottest thing on this hierarchy.
    /// Without this word every move would have to cross into managed code to ask
    /// "do I have children", which is a crossing spent to discover that no
    /// crossing was needed. With it the tail of a position write is one load at a
    /// constant offset and a branch, which is exactly what §3.2 says the block is
    /// for: <i>they are answered from memory that managed code writes</i>.
    /// </para>
    ///
    /// <para>
    /// <b>Written by managed code only</b>, by
    /// <c>SourceSharp.Server.HierarchyDriver</c>, on every link made, every link
    /// dropped and every level shutdown. Native never writes it and never
    /// decides anything from it beyond "is it zero".
    /// </para>
    ///
    /// <para>
    /// It replaces <c>Padding0</c> rather than being appended after it, so the
    /// head is still 152 bytes and still a multiple of eight — the padding's own
    /// reason survives it, because a real field explains an eight-aligned tail at
    /// least as well as a named hole does. There is no implicit padding here
    /// either way, which was the point of naming the hole in the first place.
    /// </para>
    /// </summary>
    [NativeDoc(
        "How many entities this one carries. Written by managed code only, and",
        "read by native code only as \"is it zero\": it is what lets the tail of a",
        "position write skip the crossing that would ask managed whether this",
        "entity has children. It occupies the word that used to be Padding0, so",
        "the head is still a multiple of eight and the tails after it still start",
        "where both declarations say.")]
    public int ChildCount;

    /// <summary>
    /// THE DISCONTINUITY COUNTER — stock's <c>m_ubInterpolationFrame</c>, and the
    /// half of a teleport that has to reach the client.
    ///
    /// <para>
    /// <b>A COUNTER AND NOT A FLAG, and the wire is why.</b> A "do not
    /// interpolate this frame" BIT has to be cleared by whoever set it, so the
    /// one snapshot carrying it is the only chance the client has of seeing it —
    /// and a client that was not sent that tick, or whose delta was computed
    /// against a later one, never does. The entity then slides anyway and
    /// nothing says so. A counter has no such window: its VALUE is in every
    /// delta after the jump, so the client compares it against
    /// <see cref="OldInterpolationFrame"/> and notices whenever it next hears
    /// from the entity. That is the reason stock's own <c>EF_NOINTERP</c> was
    /// replaced by this counter — <c>c_baseentity.h:1402</c> still says so in
    /// its comment.
    /// </para>
    ///
    /// <para>
    /// <b>Written by managed code only</b>, by <c>TeleportDiscontinuity</c> on
    /// the server, which bumps it modulo
    /// <see cref="TeleportDiscontinuity.ParityMax"/> and marks the word — the
    /// same four bytes <c>m_ubInterpolationFrame</c>'s SendProp addresses
    /// (<c>standalone_nettable.h</c>). Native code neither writes it nor reads
    /// it: the decision "this move is a jump, not a motion" is policy and stays
    /// in C#.
    /// </para>
    /// </summary>
    [NativeDoc(
        "Stock's m_ubInterpolationFrame: the discontinuity counter a teleport",
        "bumps, networked as m_ubInterpolationFrame. A COUNTER and not a flag,",
        "because a flag lives for one snapshot and a client that missed that",
        "snapshot never learns the entity jumped. Written by managed code only,",
        "and read by the client's interpolation pass against OldInterpolationFrame.")]
    public int InterpolationFrame;

    /// <summary>
    /// WHAT THE CLIENT LAST ACTED ON — stock's <c>m_ubOldInterpolationFrame</c>,
    /// and the other half of <c>IsNoInterpolationFrame()</c>, which is exactly
    /// <c>m_ubOldInterpolationFrame != m_ubInterpolationFrame</c>
    /// (<c>c_baseentity.h:2416</c>).
    ///
    /// <para>
    /// <b>Written by the CLIENT only</b>, by <c>Interpolation.SamplePass</c>,
    /// immediately after it has thrown the entity's history away. It is never
    /// networked and never read on the server, where it is four bytes that do
    /// nothing — the price of one layout for both sides, which is the rule this
    /// block already lives by (see the class prose on why both tails exist
    /// everywhere).
    /// </para>
    ///
    /// <para>
    /// It is also what keeps the head a multiple of eight now that
    /// <see cref="InterpolationFrame"/> has been added: two words, both with a
    /// job, rather than one word and a hole. See the prose on this struct.
    /// </para>
    /// </summary>
    [NativeDoc(
        "Stock's m_ubOldInterpolationFrame: the value the client last reset on.",
        "IsNoInterpolationFrame() is exactly the two words differing. Written by",
        "the client's interpolation pass and by nothing else; never networked,",
        "and dead on the server -- the block is one layout for both sides.")]
    public int OldInterpolationFrame;
}

/// <summary>
/// The server tail: storage the ENGINE writes into, through pointers the shell
/// hands it.
///
/// <para>
/// RESERVATIONS, NOT DECLARATIONS, and on purpose. <c>PVSInfo_t</c> and
/// <c>CPlayerState</c> are the engine's layouts, not ours; restating either in
/// C# would make this file a second opinion about a struct we do not own, and
/// the first time Valve changed one the two would part company silently. So the
/// C# side fixes the SIZE and the WHERE — which is what the block is for — and
/// the native side's gtest asserts the engine's real struct still fits
/// (<c>engine_block_layout_test.cpp</c>). Both numbers were measured, not
/// guessed: <c>sizeof(PVSInfo_t)</c> is 40 and <c>sizeof(CPlayerState)</c> is 64
/// on linux64 with GAME_DLL.
/// </para>
///
/// <para>
/// THE PLAYER STATE HAS TO BE REAL STORAGE. The engine reaches it through
/// <c>GetPlayerState</c>, and its HLTV and Replay paths write through the
/// result with NO null test. Returning null from <c>GetPlayerState</c>
/// would therefore not be a missing feature, it would be a crash in the engine
/// the moment HLTV or Replay looked. Step 3c put the real thing in this room:
/// the player shell constructs a <c>CPlayerState</c> in place here, and
/// <c>GetPlayerState</c> hands the engine a pointer to it.
/// </para>
///
/// <para>
/// <c>ulong</c> and not <c>byte</c> for the reservations: an array of bytes has
/// an alignment of one, and the engine structs that go in them contain
/// pointers. Eight-byte words make the alignment a property of the declaration
/// instead of a property of where the block happened to land.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[NativeDoc(
    "The server tail: storage the ENGINE writes into, through pointers the shell",
    "hands it. {size} bytes.",
    "",
    "RESERVATIONS, NOT DECLARATIONS. PVSInfo_t and CPlayerState are the engine's",
    "layouts, not ours. Restating either in C# would make EngineBlock.cs a second",
    "opinion about a struct we do not own, and the first time Valve changed one",
    "the two would part company silently. So the C# side fixes the SIZE and the",
    "WHERE, and engine_block_layout_test.cpp asserts the engine's real struct",
    "still fits -- which is a check that fails loudly on the day it stops being",
    "true, rather than a copy that goes on looking right.",
    "",
    "THE PLAYER STATE HAS TO BE REAL STORAGE. The engine reaches it through",
    "GetPlayerState, and its HLTV and Replay paths write through the result",
    "with NO null test.",
    "Returning NULL there would not be a missing feature, it would be a crash in",
    "the engine the moment HLTV or Replay looked. Step 3c put the real thing in",
    "this room: the player shell constructs a CPlayerState in place here, and",
    "GetPlayerState hands the engine a pointer to it.",
    "",
    "uint64 and not a byte array: an array of bytes has an alignment of one, and",
    "the engine structs that go in these contain pointers.")]
public unsafe struct EngineBlockServerTail
{
    /// <summary>
    /// Room for the engine's <c>PVSInfo_t</c> (40 bytes): head node, cluster
    /// count, the cluster pointer, two area numbers, the centre, and the inline
    /// cluster array. The engine fills it in <c>BuildEntityClusterList</c>.
    /// </summary>
    [NativeDoc(
        "Room for PVSInfo_t (40 bytes), which the engine fills in",
        "BuildEntityClusterList.")]
    public fixed ulong PvsInfo[5];

    /// <summary>
    /// Room for <c>CPlayerState</c> (64 bytes) on a player slot. Zero on
    /// everything else, and read by nothing until a player owns the block.
    /// </summary>
    [NativeDoc(
        "Room for CPlayerState (64 bytes) on a player slot, and the storage",
        "GetPlayerState hands the engine. Constructed in place by the player",
        "shell; zero on everything else.")]
    public fixed ulong PlayerState[8];

    /// <summary>
    /// WHERE THIS PLAYER'S EYES ARE, as managed code last answered it — the one
    /// position <c>ClientSetupVisibility</c> adds to the PVS and
    /// <c>ClientEarPosition</c> puts the sound listener at (step 3c,
    /// plan_entity.md §3.10).
    ///
    /// <para>
    /// It is a CACHE of a managed answer and not a second copy of anything
    /// native: the shell no longer computes an eye position at all. See
    /// <see cref="ViewOriginFrame"/> for what makes it at most one crossing per
    /// client per frame, which is what §3.10 asks for.
    /// </para>
    /// </summary>
    [NativeDoc(
        "Where this player's eyes are, as managed code last answered it: the one",
        "position ClientSetupVisibility adds to the PVS and ClientEarPosition puts",
        "the sound listener at. A cache of a managed answer, stamped by",
        "ViewOriginFrame.")]
    public Vector ViewOrigin;

    /// <summary>
    /// The frame <see cref="ViewOrigin"/> was filled on, or a frame number that
    /// can never be current for a block nobody has asked about yet.
    ///
    /// <para>
    /// <b>WHY THE STAMP EXISTS.</b> <c>ClientEarPosition</c> is not called once
    /// a frame — the engine calls it once per PVS- or
    /// PAS-filtered message, so a busy frame asks it many times per client.
    /// Answering each one with a crossing is what the tree did before this step.
    /// The stamp turns "ask managed" into "ask managed once", and every ask
    /// after the first in a frame is a load out of this struct.
    /// </para>
    ///
    /// <para>
    /// <b>-1 AND NOT 0 FOR "NEVER FILLED".</b> Frame 0 is a real frame, and a
    /// zeroed blob would otherwise read as "already answered this frame" for the
    /// whole of the first one.
    /// </para>
    /// </summary>
    [NativeDoc(
        "The frame ViewOrigin was filled on. -1 for a block nobody has asked",
        "about: frame 0 is a real frame and a zeroed blob would otherwise read as",
        "\"already answered\" for the whole of it. What makes the view origin one",
        "crossing per client per frame rather than one per multicast message.")]
    public int ViewOriginFrame;
}

/// <summary>
/// The client tail: the interpolated pose, the render state, and the animation
/// inputs the bone code reads.
///
/// <para>
/// The pose here is NOT the placement in the head. The head carries where the
/// entity is according to the last update; this carries where it is being drawn
/// this frame, which interpolation computes and which the renderer asks for per
/// draw. Keeping them apart is what lets <c>GetRenderOrigin</c> and
/// <c>GetCollisionOrigin</c> both be one load without either one lying.
/// </para>
///
/// <para>
/// WHAT IS NOT HERE YET. §3.7's pose parameters, bone controllers and overlay
/// layers are animation state this tail will eventually carry; they are Phase
/// 6's, and adding them moves the fingerprint, which is the mechanism working
/// rather than a problem with it.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[NativeDoc(
    "The client tail: the interpolated pose, the render state, and the animation",
    "inputs the bone code reads. {size} bytes.",
    "",
    "THE POSE HERE IS NOT THE PLACEMENT IN THE HEAD. The head carries where the",
    "entity is according to the last update; this carries where it is being drawn",
    "this frame, which interpolation computes and the renderer asks for per draw.",
    "Keeping them apart is what lets GetRenderOrigin and GetCollisionOrigin both",
    "be one load without either one lying.",
    "",
    "§3.7's pose parameters, bone controllers and overlay layers are not here yet.",
    "They are Phase 6's, and adding them moves the fingerprint -- which is the",
    "mechanism working rather than a problem with it.")]
public struct EngineBlockClientTail
{
    /// <summary>What <c>GetRenderOrigin</c> returns: the interpolated position.</summary>
    [NativeDoc("What GetRenderOrigin returns.")]
    public Vector RenderOrigin;

    /// <summary>What <c>GetRenderAngles</c> returns.</summary>
    [NativeDoc("What GetRenderAngles returns.")]
    public QAngle RenderAngles;

    /// <summary>Render-bounds mins, which are not the collision bounds.</summary>
    [NativeDoc("Render-bounds mins, which are not the collision bounds.")]
    public Vector RenderMins;

    /// <summary>Render-bounds maxs.</summary>
    [NativeDoc("Render-bounds maxs.")]
    public Vector RenderMaxs;

    /// <summary>Render colour, red. Four bytes rather than a <c>Color32</c>:
    /// the managed <c>Color32</c> is four ints, and the engine's is four
    /// bytes.</summary>
    [NativeDoc(
        "Render colour, one byte a channel -- the engine's color32, not the",
        "managed Color32, which is four ints.")]
    public byte RenderColorR;

    /// <summary>Render colour, green.</summary>
    [NativeDoc("Render colour, green.")]
    public byte RenderColorG;

    /// <summary>Render colour, blue.</summary>
    [NativeDoc("Render colour, blue.")]
    public byte RenderColorB;

    /// <summary>Render colour, alpha.</summary>
    [NativeDoc("Render colour, alpha.")]
    public byte RenderColorA;

    /// <summary>The fx blend the renderer asks for, 0 to 255.</summary>
    [NativeDoc("The fx blend the renderer asks for, 0 to 255.")]
    public int FxBlend;

    /// <summary>What <c>GetSkin</c> returns.</summary>
    [NativeDoc("What GetSkin returns.")]
    public int Skin;

    /// <summary>The studio body group selection.</summary>
    [NativeDoc("The studio body group selection.")]
    public int Body;

    /// <summary>
    /// What a managed class ASKED for: a <c>ShadowCastChoice</c> (auto, never,
    /// simple), which is not a <c>ShadowType_t</c> and never was.
    ///
    /// <para>
    /// IT WAS CALLED <c>ShadowCastType</c> AND HELD THIS, which is a name that
    /// described a different value from the one stored in it — the allocator
    /// seeded it with <c>STANDALONE_SHADOW_AUTO</c> and the shell read it as
    /// <c>facts.nChoice</c>. Step 6b split the two: the ask is here and the
    /// answer is <see cref="ShadowType"/>, so the pair reads the way the
    /// diagnostic already reported it ("auto, casting nothing" is a model with
    /// nothing to cast with; "simple, casting nothing" is an entity that is not
    /// being drawn).
    /// </para>
    /// </summary>
    [NativeDoc(
        "What a managed class ASKED for: a ShadowCastChoice (auto, never, simple),",
        "which is not a ShadowType_t. The ANSWER is ShadowType below.")]
    public int ShadowChoice;

    /// <summary>A <c>RenderGroup_t</c>, decided by managed code.</summary>
    [NativeDoc("A RenderGroup_t, decided by managed code.")]
    public int RenderGroup;

    /// <summary>The studio sequence the bone setup runs.</summary>
    [NativeDoc("The studio sequence the bone setup runs.")]
    public int Sequence;

    /// <summary>Where in that sequence, 0 to 1.</summary>
    [NativeDoc("Where in that sequence, 0 to 1.")]
    public float Cycle;

    /// <summary>How fast the cycle advances.</summary>
    [NativeDoc("How fast the cycle advances.")]
    public float PlaybackRate;

    /// <summary>The server time the animation was last set from.</summary>
    [NativeDoc("The server time the animation was last set from.")]
    public float AnimTime;

    /// <summary>The server time the entity last simulated at.</summary>
    [NativeDoc("The server time the entity last simulated at.")]
    public float SimulationTime;

    // ---- render policy: what managed code DECIDED (plan_entity.md step 6b) --
    //
    // Every field from here to PolicyOutputs is one half of the same rule:
    // §3.2's getters cannot cross and cannot be native policy, so managed code
    // decides and the shell loads. What managed decides is in this group; what
    // it decided FROM is in the two groups after it.

    /// <summary>
    /// The transform the RENDERER reads, which is not the one the partition
    /// reads.
    ///
    /// <para>
    /// <c>Head.WorldTransform</c> is built from <c>Head.Origin</c> and
    /// <c>Head.Angles</c> and is what <c>CollisionToWorldTransform</c> returns;
    /// this one is built from <see cref="RenderOrigin"/> and
    /// <see cref="RenderAngles"/> and is what <c>RenderableToWorldTransform</c>
    /// returns. Both returned the head's until step 6b, which is why
    /// <c>MoveToEye</c> had to move the head as well as the tail — pinning the
    /// viewmodel to the eye also moved the entity's COLLISION origin there,
    /// because that is where the one shared transform came from.
    /// </para>
    /// </summary>
    [NativeDoc(
        "The transform the RENDERER reads: built from RenderOrigin and RenderAngles,",
        "and returned by RenderableToWorldTransform. Head.WorldTransform is the",
        "PARTITION's, built from the head and returned by CollisionToWorldTransform.",
        "Both were the head's until step 6b, which is why pinning the viewmodel to the",
        "eye used to move the entity's collision origin there too.",
        Section = "render policy: what managed code DECIDED (plan_entity.md step 6b)")]
    public Transform RenderTransform;

    /// <summary>
    /// What <c>ShouldDraw</c> returns, minus the one term that cannot be
    /// precomputed — see <see cref="HideWhenOwnerSeen"/>. Non-zero draws.
    /// </summary>
    [NativeDoc(
        "What ShouldDraw returns, minus the one term that cannot be precomputed --",
        "see HideWhenOwnerSeen below. Non-zero draws.")]
    public int Drawn;

    /// <summary>What <c>IsTransparent</c> returns. Non-zero is translucent.</summary>
    [NativeDoc("What IsTransparent returns. Non-zero is translucent.")]
    public int Transparent;

    /// <summary>A <c>ShadowType_t</c>: the ANSWER, from <see cref="ShadowChoice"/>
    /// and the model facts below.</summary>
    [NativeDoc("A ShadowType_t: the ANSWER, from ShadowChoice and the model facts below.")]
    public int ShadowType;

    /// <summary>The <c>SHADOW_FLAGS_*</c> bits <c>CreateShadow</c> is given.</summary>
    [NativeDoc("The SHADOW_FLAGS_* bits CreateShadow is given.")]
    public int ShadowFlags;

    // ---- render policy: what managed code decided FROM, that it also owns ---

    /// <summary>
    /// Out of the client's PVS. The engine owns this one — <c>NotifyShouldTransmit</c>
    /// is an <c>IClientNetworkable</c> method only the engine calls — so the
    /// shell writes it and managed code reads it, which is the opposite
    /// direction from everything else in this group.
    ///
    /// <para>
    /// FIVE SEPARATE WORDS RATHER THAN A BITFIELD, and that is the block's own
    /// rule rather than a preference. Every byte of the block belongs to a field
    /// with a NAME, checked by an xUnit fact against <c>Marshal.OffsetOf</c> and
    /// a gtest against <c>offsetof</c>; a bitfield would put five facts inside
    /// one word where no per-field check could reach them, and buy sixteen bytes
    /// on an entity that already carries four hundred and eighty.
    /// </para>
    /// </summary>
    [NativeDoc(
        "Out of the client's PVS. The engine owns this one -- NotifyShouldTransmit is an",
        "IClientNetworkable method only the engine calls -- so the shell writes it and",
        "managed code reads it, the opposite direction from the rest of this group.",
        Section = "render policy: what managed code decided FROM, that it also owns")]
    public int Dormant;

    /// <summary>
    /// Drawn in the viewmodel pass. The leaf system keeps these in a list of
    /// their own — the one <c>CollateViewModelRenderables</c> reads — and never
    /// in the leaves.
    /// </summary>
    [NativeDoc("Drawn in the viewmodel pass, which is a list of its own and not the leaves.")]
    public int ViewModel;

    /// <summary>
    /// Draws ITSELF, through <c>OnDraw</c>. A beam has no model by design — it
    /// IS a drawing — so "has a model" is the wrong question to ask about it.
    /// </summary>
    [NativeDoc("Draws ITSELF through OnDraw. A beam has no model by design -- it IS a drawing.")]
    public int CustomDraw;

    /// <summary>Blended, as a managed class that draws itself said through <c>SetBlending</c>.</summary>
    [NativeDoc("Blended, as a managed class that draws itself said through SetBlending.")]
    public int Translucent;

    /// <summary>
    /// Hidden while the third-person camera can see this entity's owner: the
    /// viewmodel's rule, and the ONE render-policy term that cannot be decided
    /// ahead of the draw.
    ///
    /// <para>
    /// The camera is solved during view setup, after every stage a managed pass
    /// can run on, and <c>CViewRender</c> asks <c>ShouldDraw</c> for the
    /// viewmodel list after that. So the shell combines this bit with the live
    /// engine fact — <c>Drawn &amp;&amp; !(HideWhenOwnerSeen &amp;&amp; SeesOwner)</c>
    /// — and the decision stays managed because WHICH entities carry the bit is
    /// what this field says. A precomputed <see cref="Drawn"/> alone would be one
    /// frame stale, which is a pair of arms flashing on screen for a frame on
    /// every <c>thirdperson</c>.
    /// </para>
    /// </summary>
    [NativeDoc(
        "Hidden while the third-person camera can see this entity's owner: the",
        "viewmodel's rule, and the ONE render-policy term that cannot be decided ahead",
        "of the draw. The camera is solved during view setup, after every stage a",
        "managed pass can run on, so the shell ANDs this bit with the live engine fact.")]
    public int HideWhenOwnerSeen;

    /// <summary>A <c>FadeStyle</c>: 0 none, 1 LOD, 2 portal window.</summary>
    [NativeDoc("A FadeStyle: 0 none, 1 LOD, 2 portal window.")]
    public int FadeStyle;

    /// <summary>Where the fade starts, in world units.</summary>
    [NativeDoc("Where the fade starts, in world units.")]
    public float FadeStartDistance;

    /// <summary>Where the fade ends.</summary>
    [NativeDoc("Where the fade ends.")]
    public float FadeEndDistance;

    /// <summary>The alpha a portal-window fade bottoms out at, 0 to 1.</summary>
    [NativeDoc("The alpha a portal-window fade bottoms out at, 0 to 1.")]
    public float FadeMinAlpha;

    // ---- render policy: the model facts, which are the ENGINE's -------------
    //
    // Read off the studio header by the shell when the model index changes, and
    // published here because the shadow rule is a function of them. The read is
    // engine data access and stays native; the RULE over them is C#'s.

    /// <summary>
    /// The model's <c>modtype_t</c> — 0 bad, 1 brush, 2 sprite, 3 studio — or
    /// <b>−1</b> when the entity has no model at all, which is what
    /// <c>GetModel() == NULL</c> means and is not the same as
    /// <c>mod_bad</c>.
    /// </summary>
    [NativeDoc(
        "The model's modtype_t -- 0 bad, 1 brush, 2 sprite, 3 studio -- or -1 when the",
        "entity has no model at all, which is what GetModel() == NULL means and is not",
        "the same as mod_bad.",
        Section = "render policy: the model facts, which are the ENGINE's")]
    public int ModelType;

    // ---- render policy: the two serials that keep the loop from running -----

    /// <summary>
    /// Bumped by whoever changes any policy INPUT above — the shell when the
    /// model index, the dormancy or a managed setter's value moves. Managed
    /// code recomputes an entity's policy only when this differs from the value
    /// it last computed for, so the pass costs one load and one compare per
    /// entity per frame and no arithmetic at all in the common case.
    /// </summary>
    [NativeDoc(
        "Bumped by whoever changes any policy INPUT above. Managed code recomputes an",
        "entity's policy only when this differs from what it last computed for, so the",
        "pass costs one load and one compare per entity per frame in the common case.",
        Section = "render policy: the two serials that keep the loop from running")]
    public uint PolicyInputs;

    /// <summary>
    /// Bumped by managed code when it writes new OUTPUTS. The shell applies an
    /// entity's policy to the leaf system and the shadow manager only when this
    /// differs from the value it last applied — which is what makes "the shell's
    /// leaf and shadow calls become block-driven" a sweep with a compare in it
    /// rather than a call per entity per frame.
    /// </summary>
    [NativeDoc(
        "Bumped by managed code when it writes new OUTPUTS. The shell applies an",
        "entity's policy to the leaf system and the shadow manager only when this",
        "differs from what it last applied.")]
    public uint PolicyOutputs;

    /// <summary>
    /// WHICH HITBOX SET this model is wearing — the number
    /// <c>CStandaloneEntity::TestHitboxes</c> hard-coded to 0 and the number
    /// the client's <c>ModelRenderInfo_t.hitboxset</c> hard-coded to 0 beside
    /// it. Step 4d recorded the gap and named step 6d as its owner.
    ///
    /// <para>
    /// <b>It is a decision, which is why it is here.</b> Stock keeps
    /// <c>m_nHitboxSet</c> on <c>CBaseAnimating</c> and a mod moves it to swap
    /// a character's boxes mid-animation — a head that becomes shootable only
    /// while the helmet is off, a vehicle whose crew are hittable only through
    /// the open hatch. Every model in this mod declares exactly one set today,
    /// so 0 is every set there is; what changes is that the number now comes
    /// from managed code rather than from a literal in two files, and the day a
    /// model carries two, nothing native has to be edited.
    /// </para>
    ///
    /// <para>
    /// <b>It occupies the word that was <c>Padding1</c>,</b> deliberately: the
    /// tail padding existed so that every byte of the block belonged to a named
    /// field, and a real field serves that purpose better than a placeholder
    /// does. It was a rename rather than a layout change when it landed; it
    /// moved to 204 when the four unwritten Studio*Count words before it went
    /// (2026-10-18).
    /// </para>
    ///
    /// <para>
    /// <b>In the CLIENT tail and read by the server, which is a stated
    /// exception.</b> 4d asked for it "in the engine block beside the sequence",
    /// and <see cref="Sequence"/> is here. Both tails are allocated on both
    /// sides — the block is one struct — so the server reading this word costs
    /// nothing and keeps the animation state in one group. The alternative,
    /// a sixth word in the head, moves <c>Server</c> and <c>Client</c> and
    /// every absolute offset pinned against them.
    /// </para>
    /// </summary>
    [NativeDoc(
        "WHICH HITBOX SET this model is wearing. TestHitboxes and the client's",
        "ModelRenderInfo_t both hard-coded 0 before step 6d; stock keeps this on",
        "CBaseAnimating and a mod moves it to swap a character's boxes",
        "mid-animation, which is a decision and so belongs to managed code.",
        "It took the word that used to be explicit tail padding.")]
    public int HitboxSet;
}

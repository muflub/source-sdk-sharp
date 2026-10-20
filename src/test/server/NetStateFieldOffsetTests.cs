using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The blob offset of a declared field, by name — what native asks instead of
/// being shipped a copy of the field list.
///
/// <para>
/// WHY THIS EXISTS AT ALL, which is the whole of Step 7's argument. Native used
/// to be told every field over <c>NetFieldBegin</c>/<c>Add</c>/<c>Commit</c> and
/// to keep the list, and the client's surface renderer looked its offsets up in
/// that copy. Two copies of one layout drift; this one would have drifted
/// silently, because a wrong offset reads a plausible number out of the field
/// beside it and nothing anywhere reports it. The copy is gone and the question
/// is asked instead, so there is one layout.
/// </para>
///
/// <para>
/// WHAT THAT LEAVES UNGUARDED, and what this file guards. A rename now breaks
/// the renderer instead of desyncing it — which is better, but the symptom is
/// still only a warning at first draw ("the glass will not crack") on a client
/// nobody is reading the console of. The names native asks for are part of the
/// contract and were never written down anywhere a test could see them. They are
/// written down here.
/// </para>
/// </summary>
public class NetStateFieldOffsetTests
{
    // This class's own catalog of the shared declarations (statics to zero).
    private readonly NetStateCatalog _catalog = NetStateCatalog.Of(TestServices.ServerGame, new RecordingLog());

    /// <summary>
    /// The names shell_surface_render.cpp resolves at its first update, in the
    /// order it resolves them. A copy, unavoidably — the other end is C++ — but a
    /// copy that FAILS rather than one that drifts: this list and that one are
    /// the same sixteen strings, and if they stop being so this goes red while
    /// the renderer only stops drawing.
    /// </summary>
    private static readonly string[] SurfaceRendererFields =
    {
        "Wide", "High", "PanelWidth", "PanelHeight",
        "Normal", "Corner", "Broken", "SurfaceType",
        "PaneBits.0", "PaneBits.1", "PaneBits.2", "PaneBits.3",
        "PaneBits.4", "PaneBits.5", "PaneBits.6", "PaneBits.7",
    };

    /// <summary>
    /// The map is filled by Discover, which is what both modules call at startup
    /// before publishing. Called here for the same reason: a lookup that
    /// answered before anything had been discovered would be answering from
    /// somewhere else.
    /// </summary>
    private static IReadOnlyList<StandaloneNetState> Discovered()
        => StandaloneNetStates.Discover(typeof(BreakableSurfaceNetState).Assembly, new RecordingLog());

    [Fact]
    public void TheSharedAssemblyDeclaresNetworkedStateToLookUp()
        => Assert.True(Discovered().Count > 0, "the shared assembly declares networked state to look up");

    /// <summary>
    /// Every field of every declared state resolves to the offset the generator
    /// computed — which is the only thing this call is allowed to be.
    /// </summary>
    [Fact]
    public void EveryDeclaredFieldResolvesToItsLayoutOffset()
    {
        var states = Discovered();
        int checkedFields = 0;
        string? wrong = null;

        foreach (var state in states)
        {
            foreach (var field in state.Fields)
            {
                checkedFields++;

                int answered = _catalog.FieldOffset(state.ClassName, field.Name);

                if (answered != field.Offset && wrong is null)
                {
                    wrong = $"{state.ClassName}.{field.Name} answered {answered}, " +
                            $"layout says {field.Offset}";
                }
            }
        }

        Assert.True(wrong is null, $"every declared field resolves to its layout offset ({checkedFields} field(s))");

        if (wrong is not null)
            Assert.Fail($"first disagreement: {wrong}");
    }

    [Fact]
    public void TheFirstDeclaredFieldOfSomeClassSitsRightBehindTheEngineBlock()
    {
        // Blob-RELATIVE, never entity-relative. The two shells put their blobs at
        // different offsets inside unrelated C++ classes, so an answer that had
        // a base folded into it would be right on one side and wrong on the
        // other — and wrong by a constant, which is the shape that reads as
        // plausible data.
        //
        // THE ZERO MOVED, AND ON PURPOSE. This used to say offset 0. The blob now
        // opens with the ENGINE BLOCK (plan_entity.md §3.2), the fixed struct
        // every shell getter reads at a constant offset, so the first byte a
        // class can declare is the first byte after it. Still blob-relative:
        // EngineBlock.SizeBytes is the same number on both sides, which is the
        // whole reason the block is one declaration and not two halves.
        Assert.True(
            Discovered().Any(s => s.Fields.Count > 0 && s.Fields[0].Offset == EngineBlock.SizeBytes),
            "the first declared field of some class sits immediately behind the engine block");
    }

    [Fact]
    public void TheSurfaceRendererCanResolveEveryFieldItAsksFor()
    {
        // Discovery, done HERE rather than inherited from another suite.
        StandaloneNetStates.Discover(TestServices.ServerGame, new RecordingLog());

        string missing = string.Join(", ", SurfaceRendererFields.Where(
            name => _catalog.FieldOffset(EntityNames.FuncBreakableSurf, name) < 0));

        Assert.True(missing.Length == 0, missing.Length == 0
                ? "the surface renderer's sixteen field names all resolve"
                : $"the surface renderer asks for fields that no longer exist: {missing}");

        // The eight pane words are contiguous and four bytes apart, which is what
        // the renderer's loop assumes when it reads them one at a time.
        int first = _catalog.FieldOffset(EntityNames.FuncBreakableSurf, "PaneBits.0");
        bool stepped = true;

        for (int i = 0; i < 8; i++)
        {
            if (_catalog.FieldOffset(EntityNames.FuncBreakableSurf, $"PaneBits.{i}")
                != first + i * 4)
            {
                stepped = false;
            }
        }

        Assert.True(stepped, "the pane words are eight consecutive dwords");
    }

    [Fact]
    public void FailsClosed()
    {
        Assert.True(_catalog.FieldOffset("ss_no_such_class_at_all", "Wide") == -1, "a class nobody declared answers -1");

        Assert.True(_catalog.FieldOffset(EntityNames.FuncBreakableSurf, "NotAField") == -1, "a field the class does not declare answers -1");

        // Null and empty arrive from C, where a missing string is a null pointer
        // and marshals to one of these. -1 rather than a throw: this is called
        // from native, and an exception unwinding into Source's frames corrupts
        // the process.
        Assert.True(_catalog.FieldOffset("", "Wide") == -1, "an empty classname answers -1");
        Assert.True(_catalog.FieldOffset(EntityNames.FuncBreakableSurf, "") == -1, "an empty field name answers -1");
    }

    /// <summary>
    /// Case-insensitively on the CLASSNAME, case-sensitively on the field.
    ///
    /// The classname is an entity classname, and the engine treats those — and
    /// the datatable names derived from them — case-insensitively; a managed
    /// DT_func_lod once paired with stock's DT_Func_LOD and killed the join. The
    /// field name is a C# member and there is exactly one spelling of it.
    /// </summary>
    [Fact]
    public void ClassNamesMatchTheWayTheEngineMatchesThem()
    {
        int wanted = _catalog.FieldOffset(EntityNames.FuncBreakableSurf, "Wide");

        Assert.True(_catalog.FieldOffset(
                EntityNames.FuncBreakableSurf.ToUpperInvariant(), "Wide") == wanted, "a classname in the wrong case still resolves");

        Assert.True(_catalog.FieldOffset(EntityNames.FuncBreakableSurf, "wide") == -1, "a field name in the wrong case does not");
    }

    /// <summary>
    /// The catalog is an instance (statics to zero: three static maps every
    /// side's Discover wrote, last side winning): one built over no
    /// declarations answers -1 for a class another catalog knows.
    /// </summary>
    [Fact]
    public void EachCatalogAnswersFromItsOwnDeclarations()
    {
        var empty = NetStateCatalog.Of(Array.Empty<Type>());

        Assert.True(_catalog.FieldOffset(EntityNames.FuncBreakableSurf, "Wide") >= 0, "the full catalog knows the class");
        Assert.Equal(-1, empty.FieldOffset(EntityNames.FuncBreakableSurf, "Wide"));
        Assert.Equal("", empty.ClassNameOf(NetSchema.TypeIdOf(EntityNames.FuncBreakableSurf)));
    }
}


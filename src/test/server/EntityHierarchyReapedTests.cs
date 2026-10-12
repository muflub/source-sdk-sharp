using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>EntityHierarchy</c> was reaped from the engine ABI at
/// <c>SOURCESHARP_ABI_VERSION</c> 241, and stays reaped.
///
/// <para>
/// It answered ONE question — "which of the two entity models did this handle
/// resolve to" — for the Part 6 Phase 6 gate's probe and deliberately for
/// nothing else; <c>sourcesharp.h</c> said in its own comment that no game
/// logic may branch on it. After the Phase 4 and Phase 7 exits there is one
/// entity model, step 8a deleted the probe and all seven managed readers, and
/// the question has no answer left to give.
/// </para>
///
/// <para>
/// WHY THIS GUARD EXISTS AT ALL. The removal itself was proved by the compiler:
/// an engine API slot is called by MANAGED code, so "is it dead" is a
/// compile-time question, and with the field deleted
/// <c>src/buildsourcesharp debug</c> is exit 0 — a green build after a C# field
/// is deleted IS the proof that nothing named it. What the compiler cannot do
/// is stop it coming BACK. A re-added slot compiles fine on whichever side it
/// is added to, and <c>AbiParityTests.Covers</c> only catches it once the two
/// declarations disagree; a slot re-added to BOTH sides, with no caller and no
/// version bump, is exactly what this file is here to fail on.
/// </para>
///
/// <para>
/// WHERE IT WOULD COME BACK. Since plan_client_vtables CC-last deleted the
/// engine API struct, a slot is declared as a shim-area method (ishim_areas.h,
/// ishim_areas_client.h) and mirrored by its generated layout
/// (VtableLayouts.g.cs), and a managed slot as a managed-built area method
/// (no struct way back in since plan_client_vtables CE-last; AbiParityTests
/// holds both structs' absence). Every read goes through <see cref="AbiParityTests.CodeOnly"/>, so
/// prose that names the reaped slot is not read as a declaration. (The two
/// struct tombstones that spelled it, and the fact that proved the stripper
/// hid them, went with the struct.)
/// </para>
///
/// <para>
/// EVERY FACT IS ANCHORED. Each one first requires its file to be found AND to
/// contain a live name of its own: <c>EntityGetCollisionGroup</c>, the slot
/// that sat immediately BEFORE the reaped one, for the server's
/// implementation; <c>EntityGetModelName</c>, a shared slot, for the shim
/// areas, their layouts and the client's forwards; <c>ConNPrintf</c> for the
/// client-only areas; and <c>SourceSharpManagedApi</c> / <c>ManagedApi</c>
/// for the struct files. Without that anchor a renamed file, a broken walk-up
/// or a stripper that returned the empty string would satisfy every absence
/// check by finding nothing, which is the one way an absence test passes while
/// asking nothing.
/// </para>
/// </summary>
public class EntityHierarchyReapedTests
{
    /// <summary>The reaped slot's name, as it was spelled on both sides.</summary>
    private const string Reaped = "EntityHierarchy";

    /// <summary>
    /// The live slot that sat immediately before it — <c>EngineApi[304]</c>
    /// before the reap and after it, since the hole is behind it. A server
    /// shim-area method since plan_engineapi_vtables C6c; it anchors the native
    /// implementation file.
    /// </summary>
    private const string LiveNeighbour = "EntityGetCollisionGroup";

    /// <summary>A shared slot: in the shim areas, their layouts and the client's forwards.</summary>
    private const string SharedNeighbour = "EntityGetModelName";

    private const string AreasRel = "game/shared/ishim_areas.h";
    private const string ClientAreasRel = "game/shared/ishim_areas_client.h";
    private const string LayoutsRel = "managed/SourceSharp.Abi/VtableLayouts.g.cs";
    // plan_client_vtables CC-last: the client fills no struct; its wiring is
    // the generated forwards from its shim areas to the bodies.
    private const string WiringRel = "game/shared/shell_shim_areas_client.inc";
    private const string NativeRel = "game/server/shell_entityapi.cpp";

    // ---------------------------------------------------------------- reading

    /// <summary>
    /// A shim-relative file, read and comment-stripped, with the anchor already
    /// checked. Returns the CODE of the file and nothing else.
    /// </summary>
    private static string AnchoredCode(string relative, string anchor)
    {
        // NOT a skip. These files are always in the tree; "not found" means the
        // farm is broken, and a broken farm would make every absence check
        // below pass for having read nothing at all.
        string path = RepoLayout.Sdk("src", "sourcesharp", relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{relative} does not exist at {path}");

        string code = AbiParityTests.CodeOnly(File.ReadAllText(path));

        Assert.Contains(anchor, code, StringComparison.Ordinal);

        return code;
    }

    // ------------------------------------------------------- the declarations

    [Fact]
    public void TheWiringIsGone()
    {
        // sourcesharp_gamesystem.cpp carried BOTH halves of the wiring: the
        // extern declaration and the `engineApi.EntityHierarchy = ...`
        // assignment. A slot declared and never assigned was a null pointer
        // that every guarded call in Engine.cs skipped in silence. Since
        // plan_client_vtables CC-last the client's wiring is its generated
        // forwards, so that is where a re-added slot's wiring would appear.
        Assert.DoesNotContain(Reaped, AnchoredCode(WiringRel, SharedNeighbour), StringComparison.Ordinal);
    }

    [Fact]
    public void TheNativeImplementationIsGone()
    {
        // SourceSharp_EntityHierarchy read the one resolve and reported which
        // arm it filled. The `.so` link would NOT have caught it being left
        // behind: it is unreferenced once the slot goes, and an unreferenced
        // function is dead-stripped, so the build stays green either way.
        Assert.DoesNotContain(Reaped, AnchoredCode(NativeRel, LiveNeighbour), StringComparison.Ordinal);
    }

    // TheTombstonesAreCommentsAndTheStripperRemovesThem went with the struct
    // (plan_client_vtables CC-last 2c): the two tombstones it read were
    // comments on the struct's declarations, and no file read above names the
    // reaped slot in prose any more. The anchors keep a stripper that returned
    // the empty string from passing the absence facts.
}

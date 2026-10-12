using SourceSharp.MapGen;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Finding the map, as opposed to judging it.
///
/// <para>
/// <see cref="SandboxArtefactTests"/> can only ever score whatever map this
/// machine happens to have on disk, and in most trees that is none — so the
/// rule it depends on, "read THIS tree's map and no other", would be exercised
/// by nothing. These build the layouts instead: a checkout nested inside
/// another checkout is the shape agent worktrees actually have, and it is the
/// shape the old walk got wrong.
/// </para>
/// </summary>
public class SandboxArtefactLookupTests
{
    /// <summary>A throwaway directory tree, removed when the test ends.</summary>
    private sealed class Scratch : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "ss-artefact-lookup-" + Guid.NewGuid().ToString("n"));

        public Scratch() => Directory.CreateDirectory(Root);

        /// <summary>Makes <paramref name="relative"/> look like a checkout root.</summary>
        public string Checkout(string relative)
        {
            string path = Path.Combine(Root, relative);

            Directory.CreateDirectory(Path.Combine(path, "src", "sourcesharp"));
            File.WriteAllText(Path.Combine(path, "Makefile"), "# marker\n");

            return path;
        }

        /// <summary>Puts a compiled map inside a checkout.</summary>
        public string Map(string checkout)
        {
            string path = Path.Combine(checkout, Path.Combine(SandboxArtefact.RelativeParts));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "VBSP not really\n");

            return path;
        }

        /// <summary>Somewhere a test binary might run from inside a checkout.</summary>
        public string BinDir(string checkout)
        {
            string path = Path.Combine(
                checkout, "src", "sourcesharp", "managed", "SourceSharp.Tests", "bin");

            Directory.CreateDirectory(path);

            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test over.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// THE REGRESSION. The exact shape of an agent worktree: a checkout with no
    /// map, nested inside a checkout that has one. The lookup must report that
    /// the inner tree has no map — not hand back the outer tree's.
    /// </summary>
    [Fact]
    public void ANestedCheckoutWithNoMapDoesNotBorrowTheEnclosingCheckoutsMap()
    {
        using var scratch = new Scratch();

        string outer = scratch.Checkout("main-checkout");
        string outerMap = scratch.Map(outer);
        string inner = scratch.Checkout(Path.Combine("main-checkout", ".claude", "worktrees", "agent-1"));

        SandboxMapLookup lookup = SandboxArtefact.Locate(scratch.BinDir(inner));

        Assert.Equal(SandboxMapState.NoMapInTree, lookup.State);
        Assert.Null(lookup.Path);
        Assert.Equal(inner, lookup.Root);

        // Stated separately from the state check: the failure being guarded is
        // specifically "returned the neighbour's file", so name the file.
        Assert.True(lookup.Path != outerMap,
            $"the lookup handed back the ENCLOSING checkout's map {outerMap} for a tree rooted at {inner}");
    }

    /// <summary>
    /// The same nesting, but the inner tree has compiled its own map. It must
    /// score that one — the near file, not the far one.
    /// </summary>
    [Fact]
    public void ANestedCheckoutWithItsOwnMapReadsItsOwnMap()
    {
        using var scratch = new Scratch();

        string outer = scratch.Checkout("main-checkout");
        string outerMap = scratch.Map(outer);
        string inner = scratch.Checkout(Path.Combine("main-checkout", ".claude", "worktrees", "agent-1"));
        string innerMap = scratch.Map(inner);

        SandboxMapLookup lookup = SandboxArtefact.Locate(scratch.BinDir(inner));

        Assert.Equal(SandboxMapState.Present, lookup.State);
        Assert.Equal(inner, lookup.Root);
        Assert.Equal(innerMap, lookup.Path);
        Assert.NotEqual(outerMap, lookup.Path);
    }

    /// <summary>
    /// The ordinary case, so the nesting tests are not the only evidence the
    /// lookup works at all.
    /// </summary>
    [Fact]
    public void AStandaloneCheckoutWithAMapFindsIt()
    {
        using var scratch = new Scratch();

        string checkout = scratch.Checkout("solo");
        string map = scratch.Map(checkout);

        SandboxMapLookup lookup = SandboxArtefact.Locate(scratch.BinDir(checkout));

        Assert.Equal(SandboxMapState.Present, lookup.State);
        Assert.Equal(map, lookup.Path);
    }

    /// <summary>
    /// No checkout anywhere above. This is not "no map" — it is "the layout
    /// moved" — and it has to be a distinct answer, because the caller fails on
    /// it where it would skip on an absent map.
    /// </summary>
    [Fact]
    public void NoEnclosingCheckoutIsItsOwnAnswerRatherThanAnAbsentMap()
    {
        using var scratch = new Scratch();

        string loose = Path.Combine(scratch.Root, "not-a-checkout", "bin");
        Directory.CreateDirectory(loose);

        SandboxMapLookup lookup = SandboxArtefact.Locate(loose);

        Assert.Equal(SandboxMapState.NoTreeFound, lookup.State);
        Assert.Null(lookup.Root);
        Assert.Null(lookup.Path);
    }

    /// <summary>
    /// Both markers, not either. One marker alone would let any stray Makefile
    /// under src/ anchor the walk short of the real root, which would report
    /// every artefact missing in a tree that has them all.
    /// </summary>
    [Fact]
    public void ACheckoutRootNeedsBothMarkers()
    {
        using var scratch = new Scratch();

        string neither = Path.Combine(scratch.Root, "neither");
        string makefileOnly = Path.Combine(scratch.Root, "makefile-only");
        string sourcesharpOnly = Path.Combine(scratch.Root, "sourcesharp-only");

        Directory.CreateDirectory(neither);
        Directory.CreateDirectory(makefileOnly);
        File.WriteAllText(Path.Combine(makefileOnly, "Makefile"), "# marker\n");
        Directory.CreateDirectory(Path.Combine(sourcesharpOnly, "src", "sourcesharp"));

        Assert.False(RepoTree.IsRoot(neither));
        Assert.False(RepoTree.IsRoot(makefileOnly));
        Assert.False(RepoTree.IsRoot(sourcesharpOnly));
        Assert.True(RepoTree.IsRoot(scratch.Checkout("both")));
    }

    /// <summary>
    /// A Makefile sitting under src/ must not anchor the walk. This is the
    /// concrete reason the second marker is there, so it is checked rather than
    /// only argued for in a comment.
    /// </summary>
    [Fact]
    public void AMakefileBelowTheRootDoesNotAnchorTheWalk()
    {
        using var scratch = new Scratch();

        string checkout = scratch.Checkout("solo");
        string map = scratch.Map(checkout);

        File.WriteAllText(Path.Combine(checkout, "src", "Makefile"), "# a build file, not a root\n");

        SandboxMapLookup lookup = SandboxArtefact.Locate(scratch.BinDir(checkout));

        Assert.Equal(checkout, lookup.Root);
        Assert.Equal(map, lookup.Path);
    }

    /// <summary>
    /// The three explanations have to be tellable apart by someone reading a
    /// test log, which is the whole reason they are strings and not just an
    /// enum.
    /// </summary>
    [Fact]
    public void EachStateExplainsItselfDistinguishably()
    {
        using var scratch = new Scratch();

        string checkout = scratch.Checkout("solo");
        string absent = SandboxArtefact.Locate(scratch.BinDir(checkout)).Explanation;

        scratch.Map(checkout);
        string present = SandboxArtefact.Locate(scratch.BinDir(checkout)).Explanation;

        string noTree = SandboxArtefact.Locate(scratch.Root).Explanation;

        Assert.Contains("NO COMPILED MAP IN THIS TREE", absent);
        Assert.Contains("make map MAP=ss_sandbox", absent);
        Assert.DoesNotContain("NO COMPILED MAP IN THIS TREE", present);
        Assert.Contains("no checkout root", noTree);
        Assert.DoesNotContain("NO COMPILED MAP IN THIS TREE", noTree);
    }

    /// <summary>
    /// The attribute is three lines of adapter, but it is the piece that decides
    /// whether a lane sees a skip or a failure — so both of its branches are
    /// driven here rather than being left to whatever this machine has on disk.
    /// </summary>
    [Fact]
    public void TheFactAttributeSkipsOnlyForAnAbsentMap()
    {
        using var scratch = new Scratch();

        string checkout = scratch.Checkout("solo");
        string bin = scratch.BinDir(checkout);

        var absent = new SandboxMapFactAttribute(bin);

        Assert.NotNull(absent.Skip);
        Assert.Contains("NO COMPILED MAP IN THIS TREE", absent.Skip);

        scratch.Map(checkout);

        Assert.Null(new SandboxMapFactAttribute(bin).Skip);

        // A tree that cannot be found must NOT skip: the test body asserts on
        // that state, and a skip here would swallow it.
        Assert.Null(new SandboxMapFactAttribute(scratch.Root).Skip);
    }
}

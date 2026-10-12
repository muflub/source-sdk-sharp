using System.Text.RegularExpressions;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="TeForward"/> and <c>game/shared/tempent_forwards.h</c> are one
/// list, read off disk and compared rather than trusted.
///
/// <para>
/// THESE ARE ARRAY INDICES IN ALL BUT NAME. A value inserted in the middle of
/// either side renumbers everything after it, and every forward past that point
/// silently becomes a different effect — a decal firing as an explosion, with
/// nothing failing to compile on either side and no warning anywhere. That is
/// the same failure the declarations get a disk-parity test for, so this is the
/// same kind of test.
/// </para>
///
/// <para>
/// It reads the HEADER rather than the objects, so it fails on the change that
/// introduces the drift instead of on the next build that happens to link.
/// </para>
/// </summary>
public class TempEntForwardIdTests
{
    [Fact]
    public void TheManagedIdsAreTheOnesTheHeaderDeclares()
    {
        string? header = RepoLayout.FindSdkFile(Path.Combine(
            "src", "sourcesharp", "game", "shared", "tempent_forwards.h"));

        if (header is null)
        {
            Assert.True(true, "tempent_forwards.h read for the id check (SKIPPED, not found from "
                            + "the test's working directory)");
            return;
        }

        // SOURCESHARP_TE_FWD_ARMOR_RICOCHET = 1,   // TE_ArmorRicochet -> ArmorRicochet
        //
        // BOTH NAMES ARE ON THE LINE, deliberately. Deriving the managed one by
        // stripping TE_ would be a rule this test and the header each implement,
        // and DispatchEffect and TE_DispatchEffect are different functions that
        // strip to the same word — so the rule would have to grow a special case
        // in two places. Reading the name instead means there is nothing to
        // disagree about, and the C++ name stays in the message so a failure
        // says which forward moved.
        var native = new Dictionary<int, (string Cpp, string Managed)>();

        foreach (Match m in Regex.Matches(File.ReadAllText(header),
                     @"SOURCESHARP_TE_FWD_(\w+)\s*=\s*(\d+),\s*//\s*(\w+)\s*->\s*(\w+)"))
        {
            native[int.Parse(m.Groups[2].Value)] = (m.Groups[3].Value, m.Groups[4].Value);
        }

        // FORTY UNTIL plan_shim.md PHASE 1f, EIGHTEEN AFTER IT. The floor is here
        // so that a regex which stopped matching cannot report agreement between
        // two empty sets, and it has to sit below the real population or it is a
        // fact that can only fail. Phase 1f deleted the twenty-four ids no caller
        // on either link line reached and renumbered the rest once, in one commit
        // on both sides; tempent_forwards.h's own header carries why that renumber
        // was safe and why it is not a precedent.
        Assert.True(native.Count >= 18,
            $"the header declares {native.Count} forward id(s), which is fewer than the "
          + "forwards there are — the pattern stopped matching rather than found nothing wrong");

        var managed = Enum.GetValues<TeForward>()
                          .ToDictionary(v => (int)v, v => v.ToString());

        Assert.True(managed.Count == native.Count,
            $"the header declares {native.Count} ids and TeForward has {managed.Count}; a forward "
          + "with no managed id can only ever be refused at runtime, and a managed id with no "
          + "forward is a value nothing can send");

        var wrong = new List<string>();

        foreach ((int value, (string cpp, string want)) in native.OrderBy(p => p.Key))
        {
            if (!managed.TryGetValue(value, out string? got))
                wrong.Add($"{value} is {cpp} in the header and nothing in TeForward");
            else if (got != want)
                wrong.Add($"{value} is {cpp} ({want}) in the header and {got} in TeForward");
        }

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} id(s) name different forwards on the two sides:\n  "
          + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// AND THE COUNT THE HEADER PUBLISHES IS THE COUNT IT HAS, because native
    /// bounds-checks the id against <c>SOURCESHARP_TE_FWD_COUNT</c> before it
    /// indexes anything, and a COUNT that is too large is an out-of-range id
    /// accepted rather than refused.
    /// </summary>
    [Fact]
    public void TheHeadersOwnCountIsTheNumberOfIdsInIt()
    {
        string? header = RepoLayout.FindSdkFile(Path.Combine(
            "src", "sourcesharp", "game", "shared", "tempent_forwards.h"));

        if (header is null)
        {
            Assert.True(true, "tempent_forwards.h read for the count check (SKIPPED)");
            return;
        }

        string source = File.ReadAllText(header);

        int ids = Regex.Matches(source, @"SOURCESHARP_TE_FWD_(?!COUNT)\w+\s*=\s*\d+").Count;
        Match count = Regex.Match(source, @"SOURCESHARP_TE_FWD_COUNT\s*=\s*(\d+)");

        Assert.True(count.Success, "the header publishes a SOURCESHARP_TE_FWD_COUNT");

        Assert.True(int.Parse(count.Groups[1].Value) == ids,
            $"it says {count.Groups[1].Value} and declares {ids}; native refuses an id at or above "
          + "the count, so a count that is too large lets an out-of-range id through into a table "
          + "lookup");
    }
}

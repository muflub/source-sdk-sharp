using System.Reflection;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Who still reaches into <c>EngineSide</c>, read from the IL of a side's
/// product assemblies: every (caller, member) pair outside the class itself,
/// each caller named by its outermost type (docs/plans/plan_engineside_removal.md,
/// Phase 0).
///
/// <para>
/// <b>A ceiling that only comes down.</b> The pairs are held against a
/// baseline file per side (<c>src/test/managed/Fixtures/engineside-reach-*.tsv</c>).
/// A pair the baseline does not list fails: something reached back into the
/// side. A baseline pair that is gone fails too, so each phase deletes the
/// rows it retired and the next one cannot quietly put them back. The plan's
/// end state is an empty file, then no class.
/// </para>
/// </summary>
internal static class EngineSideReachCensus
{
    private const string Side = "SourceSharp.Abi.EngineSide";

    /// <summary>Every (caller, member) pair of <paramref name="assemblies"/> that reaches into EngineSide, as "caller\tmember".</summary>
    public static string[] Reach(params Assembly[] assemblies)
        => IlCallCensus.Calls(callee => callee.DeclaringType?.FullName == Side, assemblies)
               .Where(c => c.Caller != Side)
               .Select(c => $"{c.Caller}\t{c.Callee}")
               .Order(StringComparer.Ordinal)
               .ToArray();

    /// <summary>The baseline file's rows; a missing or empty file fails rather than passing everything.</summary>
    public static string[] Baseline(string name)
    {
        string path = RepoLayout.Repo("src", "test", "managed", "Fixtures", name);
        Assert.True(File.Exists(path), "the EngineSide reach baseline is missing: " + path);
        return File.ReadLines(path).Where(l => l.Length > 0 && l[0] != '#').ToArray();
    }

    /// <summary>Fails naming every pair added since the baseline and every baseline pair now gone.</summary>
    public static void AssertAtBaseline(string[] reach, string[] baseline, string name)
    {
        string[] added = reach.Except(baseline).ToArray();
        string[] gone = baseline.Except(reach).ToArray();
        Assert.True(added.Length == 0,
            $"{added.Length} new reach into EngineSide (inject the service instead):\n" + string.Join("\n", added));
        Assert.True(gone.Length == 0,
            $"{gone.Length} reach retired: delete these rows from {name} so they cannot come back:\n" + string.Join("\n", gone));
    }
}

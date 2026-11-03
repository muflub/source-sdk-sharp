using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The user's ruling N1 (2026-10-25, docs/plans/plan_tf2_port.md §6): TF is
/// always uppercase in a managed name — <c>TFSpawnSelector</c>, <c>TFClass</c>,
/// <c>ITFPlayerClasses</c> — never <c>Tf…</c>. The scan reads every C# source
/// file of this repo, so a name written the other way fails here, wherever it is.
/// </summary>
public sealed partial class ManagedNamesSpellTFTests
{
    // Tf followed by an uppercase letter or a digit, at the start of an
    // identifier (optionally after the interface I) or inside one after a
    // lowercase letter (NonTfBranch). camelCase locals (tfClass) start lower.
    [GeneratedRegex(@"(?:(?<![A-Za-z0-9_])I?|(?<=[a-z]))Tf(?=[A-Z0-9])")]
    private static partial Regex MixedCaseTF();

    private static IEnumerable<string> SourceFiles()
    {
        string src = RepoLayout.Repo("src");
        Assert.True(Directory.Exists(src), "no src directory at " + src);
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     // This file spells the wrong form on purpose, as the pattern's samples.
                     && Path.GetFileName(f) != nameof(ManagedNamesSpellTFTests) + ".cs");
    }

    [Theory]
    [InlineData("TfClass")]
    [InlineData("ITfPlayerClasses")]
    [InlineData("Tf2LifecycleOrderTests")]
    [InlineData("WorldDecalMatchesTheNonTfBranch")]
    public void The_pattern_finds_a_Tf_name(string name) => Assert.Matches(MixedCaseTF(), name);

    [Theory]
    [InlineData("TFClass")]
    [InlineData("ITFPlayerClasses")]
    [InlineData("tfClass")]
    [InlineData("CTFPlayer")]
    [InlineData("TF_COND_AIMING")]
    public void The_pattern_passes_a_TF_name(string name) => Assert.DoesNotMatch(MixedCaseTF(), name);

    [Fact]
    public void No_managed_name_spells_Tf()
    {
        var files = SourceFiles().ToList();
        Assert.True(files.Count > 1000, "only " + files.Count + " C# files found: the scan is not reading the repo");
        Assert.Contains(files, f => f.Contains("tf2", StringComparison.Ordinal));

        var hits = files
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(h => MixedCaseTF().IsMatch(h.line))
            .Select(h => $"{Path.GetRelativePath(RepoLayout.RepoRoot, h.f)}:{h.i + 1}: {h.line.Trim()}")
            .ToList();

        Assert.True(hits.Count == 0, "TF is uppercase in managed names (ruling N1):\n" + string.Join("\n", hits.Take(20)));
    }
}

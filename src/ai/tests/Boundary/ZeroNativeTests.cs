using System.Diagnostics;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Boundary;

/// <summary>
/// The AI is 100 % managed (plan_hl2mp_ai §3.3, ruling 2026-11-01): no commit
/// of the AI lane changes hand-written native code, and the native symbol
/// census gains only the generated forwarder rows of §3.1.
///
/// <para>An AI commit is one in <see cref="PlanBase"/>..HEAD that touches
/// <c>src/ai/</c>; uncommitted work counts as one more when it touches
/// <c>src/ai/</c> too. Commits of other lanes after the plan base are not the
/// AI's, so a native change landed on main by another lane does not turn this
/// red (the plan's "base..HEAD is empty" read assumed a lane in isolation;
/// §12.4). tools/ai-mutations adds a native line and a symbol row beside an AI
/// edit and sees both facts go red.</para>
/// </summary>
public class ZeroNativeTests
{
    /// <summary>The commit that added the approved plan; the AI lane starts here.</summary>
    private const string PlanBase = "1ca008249";

    private static readonly string[] NativeDirs = ["src/server/native/", "src/client/native/", "src/shared/native/", "src/fwd/native/"];

    private const string SymbolCensus = "docs/native-symbols.tsv";

    /// <summary>
    /// The forwarder slots §3.1 rules in, by the engine function each wraps.
    /// A census row added by an AI commit must name one of them.
    /// </summary>
    private static readonly string[] AllowedSymbols = ["CreateFakeClientEx", "IndexOfEdict", "SetFakeClientConVarValue"];

    private static string Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = RepoLayout.RepoRoot };
        foreach (string a in args)
            psi.ArgumentList.Add(a);
        using Process p = Process.Start(psi) ?? throw new InvalidOperationException("git did not start");
        string output = p.StandardOutput.ReadToEnd();
        string error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({p.ExitCode}): {error}");
        return output;
    }

    /// <summary>Each AI change set: a commit's sha (or "working tree") and the paths it touches.</summary>
    private static List<(string Id, string[] Paths)> AiChanges()
    {
        var sets = new List<(string, string[])>();
        string log = Git("log", "--no-renames", "--format=%x01%H", "--name-only", PlanBase + "..HEAD");
        foreach (string chunk in log.Split('\u0001', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] lines = chunk.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            sets.Add((lines[0], lines[1..]));
        }

        string[] dirty = Git("status", "--porcelain", "--untracked-files=all", "--no-renames")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l[3..].Trim())
            .ToArray();
        sets.Add(("working tree", dirty));
        return sets.Where(s => s.Item2.Any(p => p.StartsWith("src/ai/", StringComparison.Ordinal))).ToList();
    }

    [Fact]
    public void TheLaneHasAiCommits()
        // The positive arm: the read finds the AI's own commits, so an empty
        // finding below is about them and not about an empty set.
        => Assert.Contains(AiChanges(), c => c.Id != "working tree");

    [Fact]
    public void NoAiChangeTouchesNativeCode()
    {
        string[] findings = AiChanges()
            .SelectMany(c => c.Paths.Where(p => NativeDirs.Any(d => p.StartsWith(d, StringComparison.Ordinal))).Select(p => $"{c.Id}: {p}"))
            .ToArray();
        Assert.Empty(findings);
    }

    [Fact]
    public void NoAiChangeAddsANativeSymbolBeyondTheRuledRows()
    {
        var findings = new List<string>();
        foreach ((string id, string[] paths) in AiChanges())
        {
            if (!paths.Contains(SymbolCensus))
                continue;
            string diff = id == "working tree"
                ? Git("diff", "-U0", "HEAD", "--", SymbolCensus)
                : Git("show", "-U0", "--format=", id, "--", SymbolCensus);
            foreach (string line in diff.Split('\n'))
            {
                bool added = line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal);
                bool removed = line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal);
                if (removed || (added && !AllowedSymbols.Any(s => line.Contains(s, StringComparison.Ordinal))))
                    findings.Add($"{id}: {line}");
            }
        }

        Assert.Empty(findings);
    }
}

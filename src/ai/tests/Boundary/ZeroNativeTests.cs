using System.Diagnostics;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Boundary;

/// <summary>
/// The AI is 100 % managed (plan_hl2mp_ai §3.3, ruling 2026-11-01): an AI
/// change touches no hand-written native code, and the server's forwarder
/// table carries the three generated rows of §3.1.
///
/// <para>An AI change is a commit after <see cref="Base"/> that touches
/// <c>src/ai/</c>; uncommitted work counts as one more when it touches
/// <c>src/ai/</c> too. Commits of other lanes are not the AI's, so a native
/// change landed on main by another lane does not turn this red (§12.4).
/// tools/ai-mutations adds a native line beside an AI edit and sees the fact
/// go red.</para>
/// </summary>
public class ZeroNativeTests
{
    private static readonly string[] NativeDirs = ["src/server/native/", "src/client/native/", "src/shared/native/", "src/fwd/native/"];

    /// <summary>The forwarder slots §3.1 rules in, by the engine function each wraps.</summary>
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

    /// <summary>
    /// The last commit that changed this file. A publish squashes history, so
    /// no sha stays valid; a change to this fact is where its count starts.
    /// </summary>
    private static string Base()
        => Git("log", "-1", "--format=%H", "--", "src/ai/tests/Boundary/ZeroNativeTests.cs").Trim();

    /// <summary>Each AI change set: a commit's sha (or "working tree") and the paths it touches.</summary>
    private static List<(string Id, string[] Paths)> AiChanges()
    {
        var sets = new List<(string, string[])>();
        string log = Git("log", "--no-renames", "--format=%x01%H", "--name-only", Base() + "..HEAD");
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
    public void TheServerTableForwardsTheThreeRuledRows()
    {
        string table = File.ReadAllText(RepoLayout.Repo("src/fwd/native/fwd_server.g.inc"));
        Assert.All(AllowedSymbols, s => Assert.Contains($"SSFwd_EngineServer_{s}(", table, StringComparison.Ordinal));
    }

    [Fact]
    public void NoAiChangeTouchesNativeCode()
    {
        string[] findings = AiChanges()
            .SelectMany(c => c.Paths.Where(p => NativeDirs.Any(d => p.StartsWith(d, StringComparison.Ordinal))).Select(p => $"{c.Id}: {p}"))
            .ToArray();
        Assert.Empty(findings);
    }
}

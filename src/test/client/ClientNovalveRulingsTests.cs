using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_client_novalve §2.1 and C1: the fork TUs the C0 census found with no
/// caller are each ruled in <c>docs/client-novalve-rulings.tsv</c>. A
/// <c>delete</c> row's TU has left client.so; a <c>port</c> or <c>live</c>
/// row's TU is still compiled, so a ported behaviour cannot leave with its
/// file unnoticed. The build is read through the CMake graph.
/// </summary>
public class ClientNovalveRulingsTests
{
    // §2.1's list, as the plan names it (farm-relative, extension added below).
    // Stems, not whole file names: half of these are deleted, and
    // SourceFileNamesExistTests rightly refuses a whole-name literal for a file
    // that is gone. These are not lookups that could go vacuous -- an absence
    // here is asserted against a graph proven to have been read.
    private static readonly string[] Phases = ["C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10"];

    private sealed record Row(string Tu, string Verdict, string Phase, string Reason);

    private static List<Row> Rows()
    {
        string path = RepoLayout.Repo("docs", "client-novalve-rulings.tsv");
        Assert.True(File.Exists(path), "docs/client-novalve-rulings.tsv was not found; a scan that read nothing would pass");
        var rows = new List<Row>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            string[] f = line.Split('\t');
            Assert.True(f.Length == 7, $"a rulings row has {f.Length} columns, not 7: {line}");
            rows.Add(new Row(f[0], f[1], f[2], f[6]));
        }
        Assert.True(rows.Count > 0, "the rulings file has no rows");
        return rows;
    }

    [Fact]
    public void EveryRowHasAVerdictAPhaseAndAReason()
    {
        foreach (Row r in Rows())
        {
            Assert.True(r.Verdict is "delete" or "port" or "live" or "ported", $"{r.Tu}: verdict '{r.Verdict}' is not delete, port, live or ported");
            Assert.True(Phases.Contains(r.Phase), $"{r.Tu}: phase '{r.Phase}' is not one of C1..C10");
            Assert.True(r.Verdict != "delete" || r.Phase == "C1", $"{r.Tu}: a delete row is carried out in C1, not {r.Phase}");
            Assert.True(r.Verdict == "delete" || r.Phase != "C1", $"{r.Tu}: a {r.Verdict} row names the later phase that ports it");
            Assert.True(r.Verdict != "port" || r.Reason.Contains("PROOF:"), $"{r.Tu}: a port row names the fact that would prove the managed tree covers it");
            Assert.True(r.Verdict != "ported" || r.Reason.Contains("PORTED:"), $"{r.Tu}: a ported row names the managed code and the fact that replaced it");
            Assert.True(r.Reason.Length >= 40, $"{r.Tu}: the reason is too short to be evidence");
        }
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void NoDeleteRowsTuIsCompiledIntoClientSo()
    {
        // Proof of read: the graph lists the client's own DLL entry point.
        Assert.True(CMakeGraph.Compiles(CMakeGraph.Client, "sourcesharp/game/client/shell_clientdll.cpp"),
            "the client's compile graph does not list shell_clientdll.cpp: the absences below would pass over a graph never read");

        List<Row> deletes = Rows().Where(r => r.Verdict == "delete").ToList();
        Assert.NotEmpty(deletes);
        foreach (Row r in deletes)
            Assert.False(CMakeGraph.Compiles(CMakeGraph.Client, r.Tu), $"{r.Tu} is ruled delete and is still compiled into client.so");
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void EveryPortOrLiveRowsTuIsStillCompiled()
    {
        // §2.1 has no port or live row left since 2026-10-18, when the last
        // (flashlighteffect.cpp) became ported (C7 cluster 12): every row is
        // delete or ported, and those two facts hold them. This one guards a
        // row reopened as port or live; the graph is proved read first.
        Assert.True(CMakeGraph.Compiles(CMakeGraph.Client, "sourcesharp/game/client/shell_clientdll.cpp"),
            "the client's compile graph does not list shell_clientdll.cpp: the checks below would pass over a graph never read");
        List<Row> kept = Rows().Where(r => r.Verdict is "port" or "live").ToList();
        foreach (Row r in kept)
            Assert.True(CMakeGraph.Compiles(CMakeGraph.Client, r.Tu),
                $"{r.Tu} is ruled {r.Verdict} ({r.Phase}) but has left client.so: a port goes only after the managed fact it names exists");
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void EveryPortedRowsTuHasLeft()
    {
        // A row moves from port/live to ported in the commit that evicts the TU
        // (its managed successor and the fact that proves it named in the reason).
        foreach (Row r in Rows().Where(r => r.Verdict == "ported"))
            Assert.False(CMakeGraph.Compiles(CMakeGraph.Client, r.Tu), $"{r.Tu} is ruled ported and is still compiled into client.so");
    }
}

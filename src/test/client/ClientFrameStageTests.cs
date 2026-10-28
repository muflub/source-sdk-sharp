using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>plan_entity.md</c> 5b: <c>FrameStageNotify(stage) → managed
/// OnStage(stage)</c>, one crossing per stage.
///
/// <para>
/// <b>What is actually at risk here, and why these are source audits.</b>
/// <see cref="ClientFrameStage"/> is a mirror of the ENGINE's
/// <c>ClientFrameStage_t</c> and the shell passes the engine's own number
/// through unchanged. Nothing in this process can catch a renumbering: managed
/// code would keep compiling, the shell would keep crossing, and every stage
/// would simply mean the wrong thing — interpolation running at
/// <c>RENDER_END</c> and the render tail at <c>NET_UPDATE_START</c>, both of
/// which look like a physics bug rather than an enum bug. So the values are
/// pinned against <c>public/cdll_int.h</c> itself, and the wiring is pinned
/// against the two files that carry it.
/// </para>
///
/// <para>
/// <b>These are not "the check that cannot fail".</b> Each one is stated so it
/// goes red when the thing it is about changes: the value facts parse the
/// engine header and would fail on a reorder there; the wiring facts assert on
/// the CLIENT branch of <c>LoadSide</c> specifically, so a slot moved to the
/// server branch fails rather than passing on a substring.
/// </para>
/// </summary>
public class ClientFrameStageTests
{
    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(RepoLayout.SdkTree, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// <c>ClientFrameStage_t</c> as the engine header declares it: the names in
    /// declaration order, which — the enum having no explicit initialisers past
    /// <c>FRAME_UNDEFINED=-1</c> — are the values 0, 1, 2, …
    /// </summary>
    private static List<string> EngineStageNames()
    {
        string header = Read("src/public/cdll_int.h");

        var block = Regex.Match(header, @"enum\s+ClientFrameStage_t\s*\{(.*?)\}\s*;", RegexOptions.Singleline);
        Assert.True(block.Success, "public/cdll_int.h no longer declares enum ClientFrameStage_t");

        // Strip comments, then take the identifier at the head of each entry.
        string body = Regex.Replace(block.Groups[1].Value, @"//[^\n]*", string.Empty);

        var names = new List<string>();
        foreach (string entry in body.Split(','))
        {
            var m = Regex.Match(entry.Trim(), @"^([A-Z_][A-Z_0-9]*)");
            if (m.Success)
                names.Add(m.Groups[1].Value);
        }

        return names;
    }

    // ------------------------------------------------------------------
    // The ABI slot.
    // ------------------------------------------------------------------

    private const string Binding = "src/sourcesharp/managed/SourceSharp.Client.Interop/ClientRuntimeBinding.cs";

    /// <summary>The binding's ClientStage method, from its signature to its closing brace.</summary>
    private static string BindingClientStage()
    {
        string binding = Read(Binding);
        int at = binding.IndexOf("public void ClientStage(int stage)", StringComparison.Ordinal);
        Assert.True(at > 0, "ClientRuntimeBinding has no ClientStage slot");
        int end = binding.IndexOf("\n    }", at, StringComparison.Ordinal);
        Assert.True(end > at, "ClientRuntimeBinding.ClientStage has no closing brace");
        return binding[at..end];
    }

    /// <summary>
    /// The stage reaches managed code through the managed-built runtime's
    /// ClientStage slot (plan_client_vtables CE1), and no longer through a
    /// ManagedApi table slot: nothing in the side's load (SideLoad.cs) fills one, and the runtime's
    /// row in docs/managed-interfaces.tsv declares it.
    /// </summary>
    [Fact]
    public void ClientStageIsTheRuntimeAreasSlotNotTheTables()
    {
        string entry = Read("src/sourcesharp/managed/SourceSharp.Shared.Interop/SideLoad.cs");
        Assert.Contains("public static int Load(Side side, CrossingOp op, void* pShimRoot,", entry);
        Assert.DoesNotContain("pOut->ClientStage", entry);

        string rows = File.ReadAllText(RepoLayout.Repo("docs", "managed-interfaces.tsv"));
        Assert.Contains("IManagedRuntimeClient\tClientStage\tvoid\tint32_t nStage\n", rows);
    }

    [Fact]
    public void TheBindingCountsItselfAsClientStage()
        => Assert.Contains("crossings.Count(CrossingOp.ClientStage);", BindingClientStage());

    /// <summary>
    /// The slot routes to <c>IClientModule.OnStage</c> and to nothing else: the
    /// binding calls the host's OnStage, and ClientModule's host OnStage is its
    /// own OnStage with the stage cast. A slot that counted itself and then
    /// called the wrong thing would pass every crossing fact in the suite.
    /// </summary>
    [Fact]
    public void TheBindingCallsOnStage()
    {
        Assert.Contains("host.OnStage(stage);", BindingClientStage());

        string module = Read("src/sourcesharp/managed/SourceSharp.Client/ClientModule.cs");
        Assert.Contains("void IClientRuntimeHost.OnStage(int stage) => OnStage((ClientFrameStage)stage);", module);
    }

    [Fact]
    public void ClientStageIsACountedCrossingOp()
    {
        Assert.True(Enum.IsDefined(typeof(CrossingOp), CrossingOp.ClientStage));
        Assert.Equal("ClientStage", CrossingOp.ClientStage.ToString());
    }

    // ------------------------------------------------------------------
    // The native shell's half.
    // ------------------------------------------------------------------

}

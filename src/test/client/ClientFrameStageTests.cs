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

    [Fact]
    public void TheEngineHeaderStillDeclaresEightStages()
    {
        // Seven notifications plus FRAME_UNDEFINED. If this number moves, every
        // fact below is about a different enum and the mirror needs a new
        // member — which is exactly the change this fact exists to stop being
        // silent.
        Assert.Equal(8, EngineStageNames().Count);
    }

    [Fact]
    public void UndefinedIsMinusOne()
    {
        Assert.Equal("FRAME_UNDEFINED", EngineStageNames()[0]);
        Assert.Equal(-1, (int)ClientFrameStage.Undefined);
    }

    [Fact]
    public void StartIsZero()
    {
        Assert.Equal("FRAME_START", EngineStageNames()[1]);
        Assert.Equal(0, (int)ClientFrameStage.Start);
    }

    [Fact]
    public void NetUpdateStartIsOne()
    {
        Assert.Equal("FRAME_NET_UPDATE_START", EngineStageNames()[2]);
        Assert.Equal(1, (int)ClientFrameStage.NetUpdateStart);
    }

    [Fact]
    public void NetUpdatePostDataUpdateStartIsTwo()
    {
        Assert.Equal("FRAME_NET_UPDATE_POSTDATAUPDATE_START", EngineStageNames()[3]);
        Assert.Equal(2, (int)ClientFrameStage.NetUpdatePostDataUpdateStart);
    }

    [Fact]
    public void NetUpdatePostDataUpdateEndIsThree()
    {
        Assert.Equal("FRAME_NET_UPDATE_POSTDATAUPDATE_END", EngineStageNames()[4]);
        Assert.Equal(3, (int)ClientFrameStage.NetUpdatePostDataUpdateEnd);
    }

    [Fact]
    public void NetUpdateEndIsFour()
    {
        Assert.Equal("FRAME_NET_UPDATE_END", EngineStageNames()[5]);
        Assert.Equal(4, (int)ClientFrameStage.NetUpdateEnd);
    }

    [Fact]
    public void RenderStartIsFive()
    {
        Assert.Equal("FRAME_RENDER_START", EngineStageNames()[6]);
        Assert.Equal(5, (int)ClientFrameStage.RenderStart);
    }

    [Fact]
    public void RenderEndIsSix()
    {
        Assert.Equal("FRAME_RENDER_END", EngineStageNames()[7]);
        Assert.Equal(6, (int)ClientFrameStage.RenderEnd);
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

    /// <summary>
    /// <c>FrameStageNotify</c> crosses EXACTLY ONCE — the measurable half of
    /// 5b's gate, stated over the source so a second crossing added later fails
    /// here rather than in a live run somebody has to notice.
    /// </summary>
    [Fact]
    public void TheShellCrossesOncePerFrameStageNotify()
    {
        string shell = Read("src/sourcesharp/game/client/shell_clientdll.cpp");

        int method = shell.IndexOf("void CShellClientDLL::FrameStageNotify( ClientFrameStage_t curStage )",
                                   StringComparison.Ordinal);
        Assert.True(method > 0, "shell_clientdll.cpp no longer defines FrameStageNotify");

        int end = shell.IndexOf("\n}", method, StringComparison.Ordinal);
        Assert.True(end > method);

        string body = shell[method..end];
        Assert.Single(Regex.Matches(body, @"pRuntime->ClientStage\("));
    }

    /// <summary>
    /// And no OTHER method of the shell crosses on that slot. The rule is one
    /// crossing per stage; a second caller anywhere in the file would make it
    /// one crossing per stage plus whatever else happened.
    /// </summary>
    [Fact]
    public void NothingElseInTheShellCallsClientStage()
    {
        string shell = Read("src/sourcesharp/game/client/shell_clientdll.cpp");

        Assert.Single(Regex.Matches(shell, @"pRuntime->ClientStage\("));
    }

    /// <summary>
    /// No stage is filtered out native-side. The crossing sits above the switch
    /// with no test on <c>curStage</c> in front of it, which is what makes
    /// "every stage arrives" true rather than hoped for.
    /// </summary>
    [Fact]
    public void TheCrossingIsNotConditionalOnTheStage()
    {
        string shell = Read("src/sourcesharp/game/client/shell_clientdll.cpp");

        int method = shell.IndexOf("void CShellClientDLL::FrameStageNotify( ClientFrameStage_t curStage )",
                                   StringComparison.Ordinal);
        int cross = shell.IndexOf("pRuntime->ClientStage(", method, StringComparison.Ordinal);
        int firstSwitch = shell.IndexOf("switch( curStage )", method, StringComparison.Ordinal);

        Assert.True(cross > method && firstSwitch > cross,
            "the ClientStage crossing is no longer above the stage switch");

        // Between the method's opening and the crossing there is no test of
        // curStage at all — only the probe line, the g_CurFrameStage store and
        // the null test on the managed-built runtime.
        string preamble = shell[method..cross];
        Assert.DoesNotContain("curStage ==", preamble);
        Assert.DoesNotContain("curStage !=", preamble);
    }
}

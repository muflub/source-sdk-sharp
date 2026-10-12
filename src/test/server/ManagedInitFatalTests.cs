using System.Text.RegularExpressions;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// That a failed managed init stops the process, on both sides.
///
/// <para>
/// The bug: a user hit <c>Assert(0)</c> at <c>usermessages.cpp:132</c> seven
/// times and then a SIGSEGV inside <c>engine.so</c> with no game-DLL frame. The
/// assert was correct and the file was not wrong. What had actually happened was
/// that <c>CSourceSharpSystem::Init()</c> failed to bring the .NET runtime up,
/// printed one <c>Warning</c> and <c>return true</c>d — so the client went on to
/// hook eight user messages against a list C# never got to register.
/// </para>
///
/// <para>
/// There is nothing to degrade into. This mod's entities, its player class, its
/// game rules and its user-message list are all C#. So all three failure paths
/// are now fatal, and the two facts that make that true are structural rather
/// than behavioural — which is why they are checked by reading the function
/// rather than by running it. A unit test cannot call something that does not
/// return, and the only other witness is a launch.
/// </para>
///
/// <para>
/// Scoped to the body of <c>CSourceSharpSystem::Init()</c> and to the body of
/// the fatal helper, never to the file. A file-wide scan would be satisfied by
/// any <c>Error(</c> anywhere in 2,500 lines — including one in a function
/// nothing calls — and would then pass with the subject deleted.
/// </para>
/// </summary>
public class ManagedInitFatalTests
{
    private const string GameSystem =
        "src/sourcesharp/game/shared/sourcesharp_gamesystem.cpp";

    /// <summary>
    /// The client DLL the engine actually loads.
    ///
    /// <para>
    /// This used to be <c>src/game/client/cdll_client_int.cpp</c>, and pointing
    /// it there was wrong in a way nothing could see: that file is not in
    /// <c>client_csharp.vpc</c>, so it is not compiled into this mod's
    /// <c>client.so</c> at all, and the block this test read was behind
    /// <c>#ifdef SOURCESHARP</c> — a macro the vpcs that DO compile the file
    /// (<c>client_hl2</c>, <c>client_episodic</c>, …) do not define. The check
    /// passed on a source file no configuration in the tree ever built. The
    /// class the engine finds behind <c>CLIENT_DLL_INTERFACE_VERSION</c> is
    /// <c>CShellClientDLL</c>, and this is the file it lives in.
    /// </para>
    /// </summary>
    private const string ClientInt = "src/sourcesharp/game/client/shell_clientdll.cpp";

    private const string FatalHelper = "SourceSharp_FatalInitFailure";

    /// <summary>
    /// The signature of the function these checks read.
    ///
    /// <para>
    /// It used to be <c>virtual bool Init()</c>, and the body did not change —
    /// it moved. plan_gamedll.md Phase 2 factored the acquire-fill-LoadSide
    /// sequence out of <c>CSourceSharpSystem::Init</c> into this free function
    /// so the SERVER can reach it from <c>LoadManagedRuntime</c> while the
    /// CLIENT keeps reaching it from <c>CSourceSharpSystem::Init</c>, which is
    /// now a one-line forward. Both sides therefore run these same failure
    /// paths, so every assertion below still holds of exactly one function —
    /// and pointing them at the forwarder instead would leave them reading
    /// three lines and passing for the wrong reason.
    /// </para>
    /// </summary>
    private const string LoadSequence = "bool SourceSharp_LoadManagedSide( void )";

    // ------------------------------------------------------------------
    // Scanners. Public so ManagedInitFatalScanTests can drive them with
    // literal snippets, where the right answer sits beside the input.
    // ------------------------------------------------------------------

    /// <summary>
    /// The brace-matched body of a member or free function whose signature line
    /// contains <paramref name="signature"/>.
    ///
    /// <para>
    /// Its own matcher rather than <c>UserMessageHookParityTests.FunctionBody</c>
    /// because the subject is declared <c>virtual bool Init() OVERRIDE</c>, and
    /// that one requires the brace to follow the closing paren directly.
    /// </para>
    /// </summary>
    internal static string? BodyAfter(string code, string signature)
    {
        string src = UserMessageHookParityTests.StripComments(code);

        int at = src.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0)
            return null;

        int open = src.IndexOf('{', at);
        if (open < 0)
            return null;

        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0)
                return src.Substring(open + 1, i - open - 1);
        }

        return null;
    }

    /// <summary>How many times a name is called in this text.</summary>
    internal static int CallCount(string body, string name) =>
        Regex.Matches(UserMessageHookParityTests.StripComments(body),
            @"\b" + Regex.Escape(name) + @"\s*\(").Count;

    /// <summary>
    /// The stretch of <c>Init()</c> that must contain no way out but the fatal
    /// helper: from the idempotence gate at the top to the line that closes it
    /// at the bottom. Null when either landmark is missing, which is itself a
    /// failure the caller reports.
    /// </summary>
    internal static string? BetweenGateAndClose(string initBody)
    {
        int gate = initBody.IndexOf("ShouldRun", StringComparison.Ordinal);
        int close = initBody.IndexOf("MarkSucceeded", StringComparison.Ordinal);

        if (gate < 0 || close < 0)
            return null;

        // Past the gate's own `return true;`, which is the idempotent no-op and
        // not a degrade path.
        //
        // This is also the ONE test that enforces the ordering. A separate
        // `close <= gate` guard above would read as belt-and-braces and is
        // exactly that: the semicolon after the gate's return necessarily comes
        // after the gate, so a MarkSucceeded that precedes the gate — the
        // original bug's shape — already fails here. Two guards for one fact
        // means one of them can be removed with nothing going red.
        int semi = initBody.IndexOf(';', initBody.IndexOf("return", gate));
        if (semi < 0 || semi >= close)
            return null;

        return initBody.Substring(semi + 1, close - semi - 1);
    }

    /// <summary>Every <c>return &lt;something&gt;;</c> in this text.</summary>
    internal static int ReturnCount(string body) =>
        Regex.Matches(UserMessageHookParityTests.StripComments(body),
            @"\breturn\b[^;]*;").Count;

    // ------------------------------------------------------------------
    // The checks.
    // ------------------------------------------------------------------

    [Fact]
    public void TheFatalHelperActuallyCallsError()
    {
        string? path = RepoLayout.FindSdkFile(GameSystem);
        if (path is null)
            return;

        string? body = BodyAfter(File.ReadAllText(path),
            $"static void {FatalHelper}(");

        Assert.True(body is not null,
            $"{GameSystem} must define {FatalHelper}; it is what makes a failed "
            + "managed init stop the process");

        Assert.True(CallCount(body!, "Error") == 1,
            $"{FatalHelper} must call Error() exactly once — Error is NORETURN "
            + "and is what this codebase already uses for a managed side that "
            + "did not deliver (shell_server.cpp, shell_usermessages.cpp)");

        Assert.True(CallCount(body!, "Warning") == 0,
            $"{FatalHelper} must not warn instead of erroring");

        Assert.True(CallCount(body!, "SourceSharpInit_Format") == 1,
            $"{FatalHelper} must build its message with SourceSharpInit_Format, "
            + "so what the user reads is what managed_init_failure_test.cpp checks");
    }

    private static readonly string[] FallibleCalls = { "GetModBinDir", "Acquire", "LoadMySide" };

    [Fact]
    public void AllThreeFailurePathsInInitAreFatal()
    {
        string? path = RepoLayout.FindSdkFile(GameSystem);
        if (path is null)
            return;

        string? init = BodyAfter(File.ReadAllText(path), LoadSequence);

        Assert.True(init is not null,
            $"{GameSystem} must define SourceSharp_LoadManagedSide(); without it "
            + "this check would be reading nothing and would pass for it");

        // GetModBinDir, Acquire, LoadMySide — the three calls that can fail.
        // LoadMySide is the load both Init and the hot reload go through
        // (LoadSideV2 on the server, LoadSide on the client; plan_engineapi_vtables A3).
        foreach (string stage in FallibleCalls)
            Assert.True(CallCount(init!, "SourceSharp_" + stage) >= 1,
                $"Init() must still call SourceSharp_{stage}; if it does not, "
                + "the count below is counting the wrong function's failures");

        Assert.True(CallCount(init!, FatalHelper) == 3,
            $"Init() must reach {FatalHelper} on each of its three failure "
            + $"paths, found {CallCount(init!, FatalHelper)}");

        // Each stage constant named exactly once, so the message cannot send
        // the reader to the wrong call.
        foreach (string stage in new[] { "ModBinDir", "Acquire", "LoadSide" })
        {
            string token = "kSourceSharpInitStage_" + stage;
            int n = Regex.Matches(init!, Regex.Escape(token)).Count;
            Assert.True(n == 1,
                $"Init() must name {token} exactly once, found {n}");
        }
    }

    [Fact]
    public void InitHasNoWayOutBetweenTheGateAndTheEndButTheFatalHelper()
    {
        string? path = RepoLayout.FindSdkFile(GameSystem);
        if (path is null)
            return;

        string? init = BodyAfter(File.ReadAllText(path), LoadSequence);
        Assert.True(init is not null, $"{GameSystem} must define SourceSharp_LoadManagedSide()");

        string? middle = BetweenGateAndClose(init!);

        Assert.True(middle is not null,
            "SourceSharp_LoadManagedSide() must gate on ShouldRun() at the top and close with "
            + "MarkSucceeded() at the bottom, in that order");

        // The region has to be the real one. A zero-return count over an empty
        // or truncated slice would pass for the wrong reason, so the three
        // calls that can fail must all be inside it.
        foreach (string stage in FallibleCalls)
            Assert.True(middle!.Contains("SourceSharp_" + stage, StringComparison.Ordinal),
                $"the stretch scanned must contain the SourceSharp_{stage} call; "
                + "it does not, so this check is reading the wrong slice of Init()");

        Assert.True(ReturnCount(middle!) == 0,
            "Init() must not return between the idempotence gate and the line "
            + "that closes it: every one of those returns was a managed init "
            + $"that had failed and let the game carry on. Found "
            + $"{ReturnCount(middle!)}");
    }

    [Fact]
    public void TheGateClosesOnlyAfterEveryFailurePoint()
    {
        string? path = RepoLayout.FindSdkFile(GameSystem);
        if (path is null)
            return;

        string? init = BodyAfter(File.ReadAllText(path), LoadSequence);
        Assert.True(init is not null, $"{GameSystem} must define SourceSharp_LoadManagedSide()");

        Assert.True(CallCount(init!, "MarkSucceeded") == 1,
            "SourceSharp_LoadManagedSide() must close its gate exactly once");

        int close = init!.IndexOf("MarkSucceeded", StringComparison.Ordinal);
        int lastFatal = init.LastIndexOf(FatalHelper, StringComparison.Ordinal);

        Assert.True(lastFatal >= 0 && close > lastFatal,
            "MarkSucceeded() must come after every failure point in Init(). The "
            + "flag it replaced was set on ENTRY, which marked a FAILED attempt "
            + "as initialised and made the client's second call — through "
            + "IGameSystem::InitAllSystems(), the only one whose false could "
            + "have stopped startup — silently do nothing");

        // And nothing else may close it. Shutdown reopens it; that is Reset.
        string all = UserMessageHookParityTests.StripComments(File.ReadAllText(path));
        Assert.True(CallCount(all, "MarkSucceeded") == 1,
            $"{GameSystem} must call MarkSucceeded() from Init() and nowhere else");
    }

    /// <summary>
    /// The ordering the whole diagnosis rests on: the client registers C#'s
    /// user-message list BEFORE the HUD hooks against it. Fatal init does not
    /// replace this — a managed side that came up late would register the names
    /// after the hooks had already failed, and nothing would be fatal about it.
    /// </summary>
    [Fact]
    public void TheClientRegistersMessagesBeforeTheHudHooksThem()
    {
        string? path = RepoLayout.FindSdkFile(ClientInt);
        if (path is null)
            return;

        string src = UserMessageHookParityTests.StripComments(File.ReadAllText(path));

        // WHAT THE CLIENT CALLS NOW. `SourceSharp_ClientEarlyInit()` stood here
        // and is gone: it was a void free function that reached a file-static
        // game system's Init(), so its answer was thrown away — which is the
        // reason a failed init had to be made fatal rather than returned. The
        // shell says `m_pManagedRuntime = LoadManagedRuntime( this )` instead,
        // which is CShellGameDll::DLLInit's own line, and it checks the answer.
        int early = src.IndexOf("LoadManagedRuntime( this )", StringComparison.Ordinal);
        int hud = src.IndexOf("gHUD.Init()", StringComparison.Ordinal);

        Assert.True(early >= 0,
            $"{ClientInt} must bring the managed runtime up with LoadManagedRuntime( this )");
        Assert.True(hud >= 0, $"{ClientInt} must call gHUD.Init()");
        Assert.True(early < hud,
            "LoadManagedRuntime( this ) must run before gHUD.Init(): the HUD "
            + "hooks eight user messages there and every one of them is declared "
            + "in C#, so hooking first is one assert per hook");

        // And the route it replaced is gone rather than merely bypassed. Two
        // routes to one load is how a listen server acquires twice from one DLL.
        Assert.Equal(-1, src.IndexOf("SourceSharp_ClientEarlyInit", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------
}

using SourceSharp.Abi;
using Debug.Api;

using Xunit;

using static SourceSharp.Tests.DebugApiGolden;

namespace SourceSharp.Tests;

/// <summary>
/// Golden JSON for /convars, /commands, /convar and /command
/// (plan_devapi_interfaces P2, group G1), written before the console moved
/// behind IDebugConsole.
/// </summary>
public class DebugApiGoldenConsoleTests
{
    private sealed class GoldenConVar : IntConVar
    {
        public GoldenConVar()
        {
            Bind(-1, "cs_golden_value", "3", "a \"golden\" convar", ConVarFlags.Archive);
            NotifyChanged("7", "3");
        }
    }

    private sealed class GoldenBoundedConVar : FloatConVar
    {
        public GoldenBoundedConVar()
            => Bind(-1, "cs_golden_bounded", "0.5", "bounded", ConVarFlags.None, min: 0f, max: 2.5f);
    }

    internal static IReadOnlyList<ConVarBase> ConVars() => [new GoldenConVar(), new GoldenBoundedConVar()];

    internal static readonly ConCommandInfo[] Commands =
    [
        new("cs_golden_do", "does \"the\" thing", "GoldenCommand", true, ConVarFlags.Cheat),
        new("cs_golden_refused", "refused", "GoldenCommand", false, ConVarFlags.None),
    ];

    internal static readonly NativeConsoleEntry[] Native =
    [
        new("sv_cheats", "1", "0", "Allow cheats on server", (int)(ConVarFlags.Notify | ConVarFlags.Replicated), false, 0f, 1f),
        new("cs_golden_value", "99", "0", "the engine's copy of ours", 0, false),
        new("changelevel", "", "", "Change server to the specified map", (int)(ConVarFlags.GameDll | ConVarFlags.Cheat), true),
        new("cs_golden_do", "", "", "does the thing", (int)(ConVarFlags.GameDll | ConVarFlags.Cheat), true),
    ];

    internal static string? ManagedValue(string name) => name == "cs_golden_value" ? "7" : null;

    internal static string? NativeValue(string name) => name == "sv_gravity" ? "600" : null;

    /// <summary>What the routes ran on the server console, in order.</summary>
    private List<string> Executed { get; } = [];

    private DebugApiServer Server()
    {
        // G1: the same sources through the server's IDebugConsole.
        return DebugApiServer.ForTest(TestDebugHooks.Server(
            console: new SourceSharp.Server.DebugConsoleHook(ConVars, () => Commands, ManagedValue, () => Native, NativeValue, Executed.Add)));
    }

    internal static readonly IReadOnlyDictionary<string, string> Expected = new Dictionary<string, string>
    {
        ["/convars"] =
            """
            {"convars":[{"name":"cs_golden_value","value":"7","default":"3","help":"a \"golden\" convar","type":"int","flags":"Archive","min":null,"max":null,"registered":false,"declaredBy":"GoldenConVar","source":"managed"},{"name":"cs_golden_bounded","value":"0.5","default":"0.5","help":"bounded","type":"float","flags":"None","min":0,"max":2.5,"registered":false,"declaredBy":"GoldenBoundedConVar","source":"managed"}],"count":2}
            """,
        ["/convars?source=native"] =
            """
            {"convars":[{"name":"sv_cheats","value":"1","default":"0","help":"Allow cheats on server","type":"unknown","flags":"Notify, Replicated","min":0,"max":1,"registered":true,"declaredBy":"engine","source":"native"}],"count":1}
            """,
        ["/convars?source=all"] =
            """
            {"convars":[{"name":"cs_golden_value","value":"7","default":"3","help":"a \"golden\" convar","type":"int","flags":"Archive","min":null,"max":null,"registered":false,"declaredBy":"GoldenConVar","source":"managed"},{"name":"cs_golden_bounded","value":"0.5","default":"0.5","help":"bounded","type":"float","flags":"None","min":0,"max":2.5,"registered":false,"declaredBy":"GoldenBoundedConVar","source":"managed"},{"name":"sv_cheats","value":"1","default":"0","help":"Allow cheats on server","type":"unknown","flags":"Notify, Replicated","min":0,"max":1,"registered":true,"declaredBy":"engine","source":"native"}],"count":3}
            """,
        ["/convars?source=nativ"] =
            """
            {"error":"no such source","source":"nativ","expected":["managed","native","all"]}
            """,
        ["/commands"] =
            """
            {"commands":[{"name":"cs_golden_do","help":"does \"the\" thing","declaredBy":"GoldenCommand","flags":"Cheat","registered":true,"source":"managed"},{"name":"cs_golden_refused","help":"refused","declaredBy":"GoldenCommand","flags":"None","registered":false,"source":"managed"}],"count":2}
            """,
        ["/commands?source=native"] =
            """
            {"commands":[{"name":"changelevel","help":"Change server to the specified map","declaredBy":"server.dll","flags":"GameDll, Cheat","registered":true,"source":"native"}],"count":1}
            """,
        ["/commands?source=all"] =
            """
            {"commands":[{"name":"cs_golden_do","help":"does \"the\" thing","declaredBy":"GoldenCommand","flags":"GameDll, Cheat","registered":true,"source":"managed"},{"name":"cs_golden_refused","help":"refused","declaredBy":"GoldenCommand","flags":"None","registered":false,"source":"managed"},{"name":"changelevel","help":"Change server to the specified map","declaredBy":"server.dll","flags":"GameDll, Cheat","registered":true,"source":"native"}],"count":3}
            """,
        ["/convar?name=sv_gravity"] =
            """
            {"name":"sv_gravity","value":"600"}
            """,
        ["/convar?name=cs_golden_value"] =
            """
            {"name":"cs_golden_value","value":"7"}
            """,
        ["/convar?name=cs_nonsense"] =
            """
            {"name":"cs_nonsense","value":"(no convar of that name; a command has no value to read)"}
            """,
        ["/convar?name=cs_golden_bounded&value=a%20b"] =
            """
            {"name":"cs_golden_bounded","value":"(no convar of that name; a command has no value to read)"}
            """,
    };

    public static TheoryData<string> Paths => new(Expected.Keys);

    [Theory]
    [MemberData(nameof(Paths))]
    public void Each_console_route_renders_exactly(string path)
    {
        using var server = Server();
        Assert.Equal(Expected[path], Pumped(server, path).Body);
    }

    [Fact]
    public void A_convar_write_runs_the_quoted_assignment_before_reading_back()
    {
        using var server = Server();
        _ = Pumped(server, "/convar?name=cs_golden_bounded&value=a%20b");
        Assert.Equal(["cs_golden_bounded \"a b\""], Executed);
    }

    [Fact]
    public void A_command_answers_ok_with_where_its_log_starts()
    {
        using var server = Server();
        Assert.Matches("^\\{\"ok\":true,\"logFrom\":\\d+,\"eventsFrom\":\\d+,\"consoleFrom\":\\d+\\}$", Pumped(server, "/command?c=echo%20golden").Body);
        Assert.Equal(["echo golden"], Executed);
        Assert.Equal("{\"error\":\"no command given\"}", Pumped(server, "/command").Body);
    }
}

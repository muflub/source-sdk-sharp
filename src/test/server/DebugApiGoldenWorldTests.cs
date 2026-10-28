using SourceSharp.Abi;
using Debug.Api;

using Xunit;

using static SourceSharp.Tests.DebugApiGolden;

namespace SourceSharp.Tests;

/// <summary>
/// Golden JSON, and the engine calls behind it, for the routes that drive the
/// world: /entities, /entity/input, /entity/damage, /player/aim,
/// /entity/connect and /entities/spawn (plan_devapi_interfaces P2, group G2),
/// written before they moved behind IDebugWorld. The engine is a
/// RecordingEngine, so every call each route makes is part of the golden.
/// </summary>
public partial class DebugApiGoldenWorldTests
{

    internal const string SpawnBody =
        "{\"placement\":\"origin\",\"player\":1,\"name\":\"golden\",\"entities\":[" +
        "{\"classname\":\"prop_golden\",\"targetname\":\"g1\",\"origin\":[1,2,3],\"angles\":[0,90,0]," +
        "\"keyvalues\":{\"health\":\"10\"},\"connections\":[{\"output\":\"OnBreak\",\"target\":\"g2\",\"input\":\"Kill\",\"param\":\"\",\"delay\":0.5,\"times\":1}]}]}";

    public static TheoryData<string, string, string> Requests => new()
    {
        { "entities", "GET", "/entities?class=prop_golden" },
        { "input", "GET", "/entity/input?target=door&input=Open&param=x%20y" },
        { "input-activator", "GET", "/entity/input?target=door&input=Open&activator=1" },
        { "damage", "GET", "/entity/damage?class=prop_golden&amount=10" },
        { "damage-at", "GET", "/entity/damage?target=door&amount=5&type=64&at=4,5,6" },
        { "damage-from", "GET", "/entity/damage?target=door&amount=7&from=door" },
        { "damage-index", "GET", "/entity/damage?target=%2333&amount=3" },
        { "aim", "GET", "/player/aim?player=1" },
        { "connect", "GET", "/entity/connect?source=a&output=OnTrigger&target=b&input=Kill&param=p&delay=1.5&times=2" },
        { "spawn", "POST", "/entities/spawn" },
    };

    internal static readonly IReadOnlyDictionary<string, string> ExpectedBody = new Dictionary<string, string>
    {
        ["entities"] =
            """
            {"class":"prop_golden","entities":[{"handle":262177,"index":33,"classname":"prop_golden","name":"golden","health":75,"solid":6,"solidflags":4,"origin":[1.50,-2.00,3.25],"angles":[0.00,90.00,0.00]},{"handle":327714,"index":33,"classname":"prop_golden","name":"golden","health":75,"solid":6,"solidflags":4,"origin":[1.50,-2.00,3.25],"angles":[0.00,90.00,0.00]}],"count":2}
            """,
        ["input"] =
            """
            {"ok":true,"via":"AcceptInput","matchedBy":"name","targets":1,"accepted":1,"logFrom":0,"eventsFrom":0,"consoleFrom":0}
            """,
        ["input-activator"] =
            """
            {"ok":true,"via":"AcceptInput","matchedBy":"name","targets":1,"accepted":1,"logFrom":0,"eventsFrom":0,"consoleFrom":0}
            """,
        ["damage"] =
            """
            {"ok":true,"amount":10,"type":2,"hit":[{"classname":"prop_golden","healthBefore":75,"healthAfter":75,"gone":false},{"classname":"prop_golden","healthBefore":75,"healthAfter":75,"gone":false}],"count":2,"logFrom":0,"eventsFrom":0,"consoleFrom":0}
            """,
        ["damage-at"] =
            """
            {"ok":true,"amount":5,"type":64,"hit":[{"classname":"prop_golden","healthBefore":75,"healthAfter":75,"gone":false}],"count":1,"logFrom":0,"eventsFrom":0,"consoleFrom":0}
            """,
        ["damage-from"] =
            """
            {"ok":true,"amount":7,"type":2,"hit":[{"classname":"prop_golden","healthBefore":75,"healthAfter":75,"gone":false}],"count":1,"logFrom":0,"eventsFrom":0,"consoleFrom":0}
            """,
        ["damage-index"] =
            """
            {"ok":true,"amount":3,"type":2,"hit":[{"classname":"prop_golden","healthBefore":75,"healthAfter":75,"gone":false}],"count":1,"logFrom":0,"eventsFrom":0,"consoleFrom":0}
            """,
        ["aim"] =
            """
            {"player":1,"eye":[1.50,-2.00,3.25],"aim":[10.00,20.00,30.00],"hit":true,"hitClass":"prop_golden"}
            """,
        ["connect"] =
            """
            {"ok":true,"command":"ent_fire a AddOutput \"OnTrigger b:Kill:p:1.5:2\"","logFrom":0,"eventsFrom":0,"consoleFrom":0}
            """,
        ["spawn"] =
            """
            {"ok":true,"created":1,"requested":1,"entities":[{"classname":"prop_golden","name":"g1","index":33,"handle":262177,"created":true,"error":"","origin":[1.50,-2.00,3.25],"angles":[0.00,90.00,0.00]}],"logFrom":0,"eventsFrom":0,"consoleFrom":0}
            """,
    };

    /// <summary>The engine calls each route makes, the clock and the mod directory left out (sampling reads them on its own schedule).</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> ExpectedCalls = new Dictionary<string, string[]>
    {
        ["entities"] = ["Entities.FindAllByClassname(prop_golden)", "Entities.GetOrigin(#33)", "Entities.GetAngles(#33)", "Entities.GetIndex(#33)", "Entities.GetClassName(#33)", "Entities.GetName(#33)", "Entities.GetHealth(#33)", "Entities.GetSolidType(#33)", "Entities.GetSolidFlags(#33)", "Entities.GetOrigin(#34)", "Entities.GetAngles(#34)", "Entities.GetIndex(#34)", "Entities.GetClassName(#34)", "Entities.GetName(#34)", "Entities.GetHealth(#34)", "Entities.GetSolidType(#34)", "Entities.GetSolidFlags(#34)"],
        ["input"] = ["Entities.FindAllByName(door, 512)", "Entities.FireInput(#33, Open, x y, (invalid handle))"],
        ["input-activator"] = ["Entities.FindAllByName(door, 512)", "Players.GetHandle(1)", "Entities.IsValid(#1)", "Entities.FireInput(#33, Open, null, #1)"],
        ["damage"] = ["Entities.FindAllByClassname(prop_golden)", "Entities.GetHealth(#33)", "Damage.Apply(#33, (invalid handle), 10, Bullet)", "Entities.IsValid(#33)", "Entities.GetHealth(#33)", "Entities.GetClassName(#33)", "Entities.GetHealth(#34)", "Damage.Apply(#34, (invalid handle), 10, Bullet)", "Entities.IsValid(#34)", "Entities.GetHealth(#34)", "Entities.GetClassName(#34)"],
        ["damage-at"] = ["Entities.FindByName(door)", "Entities.IsValid(#33)", "Entities.GetHealth(#33)", "Entities.GetOrigin(#33)", "Damage.TakeDamage(#33, (invalid handle), 5, Blast, (0.3, 0.9, 0.3), (4.0, 5.0, 6.0), False)", "Entities.IsValid(#33)", "Entities.GetHealth(#33)", "Entities.GetClassName(#33)"],
        // The attacker by name, and the hit credited to it (from=door: the door hurts itself).
        ["damage-from"] = ["Entities.FindByName(door)", "Entities.IsValid(#33)", "Entities.FindByName(door)", "Entities.IsValid(#33)", "Entities.GetHealth(#33)", "Damage.Apply(#33, #33, 7, Bullet)", "Entities.IsValid(#33)", "Entities.GetHealth(#33)", "Entities.GetClassName(#33)"],
        // A target by #index (the players have no names): the first entity the walk finds at that index.
        ["damage-index"] = ["Entities.FindAllByClassname(*)", "Entities.GetIndex(#33)", "Entities.IsValid(#33)", "Entities.GetHealth(#33)", "Damage.Apply(#33, (invalid handle), 3, Bullet)", "Entities.IsValid(#33)", "Entities.GetHealth(#33)", "Entities.GetClassName(#33)"],
        ["aim"] = ["Players.GetHandle(1)", "Entities.IsValid(#1)", "WorldQuery.TracePlayerAim(#1, 8192)", "Entities.GetOrigin(#1)", "Entities.IsValid(#34)", "Entities.IsValid(#34)", "Entities.GetClassName(#34)"],
        ["connect"] = ["Console.ExecuteServerCommand(ent_fire a AddOutput \"OnTrigger b:Kill:p:1.5:2\")"],
        ["spawn"] = ["Console.ExecuteServerCommand(ent_create prop_golden targetname __ss_spawn_1 health 10)", "Entities.FindByName(__ss_spawn_1)", "Entities.IsValid(#33)", "Entities.Teleport(#33, (1.0, 2.0, 3.0), (0.0, 90.0, 0.0), True)", "Console.ExecuteServerCommand(ent_fire __ss_spawn_1 AddOutput \"targetname g1\")", "Entities.GetIndex(#33)", "Console.ExecuteServerCommand(ent_fire g1 AddOutput \"OnBreak g2:Kill::0.5:1\")", "Entities.IsValid(#33)", "Entities.GetOrigin(#33)", "Entities.GetAngles(#33)"],
    };

    internal static (string Body, string[] Calls) Run(string method, string path)
    {
        var engine = Engine();
        using var server = DebugApiServer.ForTest(TestDebugHooks.Server(engine: Container(engine)));
        Assert.True(HttpSocketServer.TryParse(
            $"{method} {path} HTTP/1.1\r\nContent-Length: {(method == "POST" ? SpawnBody.Length : 0)}\r\n\r\n{(method == "POST" ? SpawnBody : "")}",
            out var request));
        string body = PumpedRoute(server, request);
        return (body, engine.Calls.Select(c => c.ToString())
                                  .Where(c => c is not ("Clock.Now()" or "Host.ManagedDirectory()")).ToArray());
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public void Each_route_renders_exactly(string name, string method, string path)
        => Assert.Equal(ExpectedBody[name], Run(method, path).Body);

    [Theory]
    [MemberData(nameof(Requests))]
    public void Each_route_makes_exactly_its_engine_calls(string name, string method, string path)
        => Assert.Equal(ExpectedCalls[name], Run(method, path).Calls);

    [Fact]
    public void A_damage_event_carries_the_game_clock()
    {
        var engine = Engine();
        using var server = DebugApiServer.ForTest(TestDebugHooks.Server(engine: Container(engine)));
        Assert.True(HttpSocketServer.TryParse("GET /entity/damage?target=door HTTP/1.1\r\n\r\n", out var request));
        _ = PumpedRoute(server, request);
        Assert.Contains("\"time\":12.5,\"kind\":\"input\",\"classname\":\"\",\"name\":\"door\",\"detail\":\"25 damage <- debug-api\"",
                        Get(server, "/events").Body, StringComparison.Ordinal);
    }

    private static string PumpedRoute(DebugApiServer server, HttpRequest request)
    {
        using var stop = new CancellationTokenSource();
        var pump = new Thread(() => { while (!stop.IsCancellationRequested) { server.Pump(); Thread.Sleep(1); } }) { IsBackground = true };
        pump.Start();
        try { return server.Route(request).Body; }
        finally { stop.Cancel(); pump.Join(); }
    }
}

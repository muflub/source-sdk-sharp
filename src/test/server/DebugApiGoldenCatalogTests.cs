using SourceSharp.Abi;
using Debug.Api;

using Xunit;

using static SourceSharp.Tests.DebugApiGolden;

namespace SourceSharp.Tests;

/// <summary>
/// Golden JSON for /entityclasses and /instances (plan_devapi_interfaces P2,
/// group G3), written before they moved behind IDebugEntityCatalog.
/// </summary>
public class DebugApiGoldenCatalogTests
{
    internal static readonly ScriptedClassInfo[] Classes =
    [
        new("prop_golden", "Logical", "Mod.Golden\"Prop",
            ["Open", "Close"], ["OnOpen"],
            [new("speed", "float"), new("label", "string")], 2),
        new("prop_bare", "Shelled", "Mod.Bare", [], [], [], 0),
    ];

    internal static readonly ScriptedInstanceInfo[] Instances =
    [
        new("prop_golden", 0x0004_0021u, 33, true, new Vector(1.5f, -2f, 3.25f), 100, 4,
            [new("speed", "float", "1.5"), new("label", "string", "a \"b\"")],
            [new("OnOpen", 2, "x"), new("OnClose", 0, null)]),
        new("prop_bare", 0xFFFF_FFFFu, -1, false, Vector.Zero, 0, 0, [], []),
    ];

    internal const string ClassesJson =
        "{\"classes\":[" +
        "{\"className\":\"prop_golden\",\"kind\":\"Logical\",\"declaredBy\":\"Mod.Golden\\\"Prop\",\"liveCount\":2," +
        "\"inputs\":[\"Open\",\"Close\"],\"outputs\":[\"OnOpen\"]," +
        "\"vars\":[{\"name\":\"speed\",\"type\":\"float\"},{\"name\":\"label\",\"type\":\"string\"}]}," +
        "{\"className\":\"prop_bare\",\"kind\":\"Shelled\",\"declaredBy\":\"Mod.Bare\",\"liveCount\":0," +
        "\"inputs\":[],\"outputs\":[],\"vars\":[]}" +
        "],\"count\":2}";

    internal const string InstancesJson =
        "{\"instances\":[" +
        "{\"className\":\"prop_golden\",\"handle\":262177,\"index\":33,\"valid\":true,\"health\":100,\"spawnFlags\":4," +
        "\"origin\":[1.50,-2.00,3.25]," +
        "\"vars\":[{\"name\":\"speed\",\"type\":\"float\",\"value\":\"1.5\"},{\"name\":\"label\",\"type\":\"string\",\"value\":\"a \\\"b\\\"\"}]," +
        "\"outputs\":[{\"name\":\"OnOpen\",\"fireCount\":2,\"lastValue\":\"x\"},{\"name\":\"OnClose\",\"fireCount\":0,\"lastValue\":null}]}," +
        "{\"className\":\"prop_bare\",\"handle\":4294967295,\"index\":-1,\"valid\":false,\"health\":0,\"spawnFlags\":0," +
        "\"origin\":[0.00,0.00,0.00],\"vars\":[],\"outputs\":[]}" +
        "],\"count\":2}";

    private static DebugApiServer Server()
    {
        // G3: through the server's IDebugEntityCatalog, over the same infos.
        return DebugApiServer.ForTest(TestDebugHooks.Server(classes: () => Classes, instances: () => Instances));
    }

    [Fact]
    public void Entity_classes_render_exactly()
    {
        using var server = Server();
        Assert.Equal(ClassesJson, Get(server, "/entityclasses").Body);
    }

    [Fact]
    public void Instances_render_exactly()
    {
        using var server = Server();
        Assert.Equal(InstancesJson, Pumped(server, "/instances").Body);
    }
}

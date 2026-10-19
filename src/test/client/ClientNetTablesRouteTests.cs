using System.Text.Json;

using SourceSharp.Abi;
using SourceSharp.Client;
using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Interop;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's <c>nettables</c> route (plan_client_novalve §4.4): the recv
/// tables the engine holds, the types this side declares, and whether the two
/// sides agree.
/// </summary>
public class ClientNetTablesRouteTests
{
    private sealed class Tiers(params NetTierInfo[] tiers) : INetTiers
    {
        public IReadOnlyList<NetTierInfo> For(NetShellKind shell) => shell == NetShellKind.Standalone ? tiers : [];
        public IReadOnlyList<int> Sizes => tiers.Select(t => t.PayloadBytes).ToArray();
        public int Ceiling(int layoutBytes) => tiers.Select(t => t.PayloadBytes).Where(b => b >= layoutBytes).DefaultIfEmpty(-1).Min();
        public double Fill(int layoutBytes) => 0;
        public bool NearCeiling(int layoutBytes) => false;
    }

    private sealed class Recv(ClientRecvPropRow[] props, int refusedUpdates, ClientSchemaRefusalRow[] refusals)
        : IClientRecvTables
    {
        public IReadOnlyList<ClientRecvPropRow> Props() => props;
        public (int RefusedUpdates, IReadOnlyList<ClientSchemaRefusalRow> Types) Refusals() => (refusedUpdates, refusals);
    }

    private static ClientRecvPropRow[] TierTable(string table, int props)
        => Enumerable.Range(0, props)
                     .Select(i => new ClientRecvPropRow(i == 0 ? "CStandalone_" + table : "", table, $"w{i:D2}", 0, 0, 4 * i, 1))
                     .ToArray();

    private static ClientDebugNetTables Route(int engineProps, params ClientSchemaRefusalRow[] refusals)
    {
        var tiers = new Tiers(new NetTierInfo(0, "DT_SS_Tier0", 64, 16, 22, true));
        var recv = new Recv([.. TierTable("DT_SS_Tier0", engineProps),
                             new ClientRecvPropRow("CWorld", "DT_World", "m_flWaveHeight", 1, 0, 8, 1)],
                            refusals.Sum(r => r.Count), refusals);
        var types = new ClientNetSchemaTypes(tiers, () => StandaloneNetStates.Discover(
            typeof(ClientModule).Assembly, new RecordingLog()));
        return new ClientDebugNetTables(recv, tiers, types);
    }

    private static JsonDocument Read(ClientDebugNetTables route)
    {
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(netTables: route));
        return JsonDocument.Parse(client.Answer(DebugChannelQuery.NetTables));
    }

    [Fact]
    public void The_route_reads_the_engine_tables_and_the_declared_types()
    {
        using var json = Read(Route(engineProps: 22));
        var root = json.RootElement;

        // The positive arm: both tables, every prop, and this side's own
        // declarations, which the client assembly has by the dozen.
        Assert.Equal(2, root.GetProperty("tableCount").GetInt32());
        var tier = root.GetProperty("tables").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "DT_SS_Tier0");
        Assert.Equal(22, tier.GetProperty("props").GetArrayLength());
        Assert.Equal("CStandalone_DT_SS_Tier0", tier.GetProperty("class").GetString());
        Assert.Equal("float", root.GetProperty("tables")[1].GetProperty("props")[0].GetProperty("type").GetString());
        Assert.True(root.GetProperty("types").GetArrayLength() > 0);
        Assert.Contains(root.GetProperty("types").EnumerateArray(),
                        t => t.GetProperty("fields").EnumerateArray().Any(f => f.GetProperty("bits").GetInt32() > 0));

        Assert.True(root.GetProperty("agreement").GetProperty("agrees").GetBoolean());
    }

    [Fact]
    public void A_tier_the_engine_holds_differently_disagrees()
    {
        using var json = Read(Route(engineProps: 21));
        var agreement = json.RootElement.GetProperty("agreement");

        Assert.False(agreement.GetProperty("agrees").GetBoolean());
        var tier = Assert.Single(agreement.GetProperty("tiers").EnumerateArray());
        Assert.Equal((22, 21), (tier.GetProperty("tierProps").GetInt32(), tier.GetProperty("engineProps").GetInt32()));
    }

    [Fact]
    public void A_refused_schema_disagrees()
    {
        using var json = Read(Route(22, new ClientSchemaRefusalRow(0xABCD, 1, 2, 7, "ss_crate")));
        var agreement = json.RootElement.GetProperty("agreement");

        Assert.False(agreement.GetProperty("agrees").GetBoolean());
        Assert.Equal(7, agreement.GetProperty("refusedUpdates").GetInt32());
        Assert.Equal("ss_crate", Assert.Single(agreement.GetProperty("refusals").EnumerateArray()).GetProperty("class").GetString());
    }

    private sealed class FixedNetTables(DebugClientNetTables tables) : IDebugClientNetTables
    {
        public DebugClientNetTables Read() => tables;
    }

    [Fact]
    public void The_nettables_answer_renders_exactly()
    {
        var tables = new DebugClientNetTables(
            Tables: [new DebugClientRecvTable("DT_SS_Tier0", "CStandalone0", [new DebugClientRecvProp("w00", "int", 1, 32, 1)])],
            Types: [new DebugClientNetType("ss_crate", 0x1234u, 0xBEEFu, "DT_SS_Tier0", 12, 3,
                [new DebugClientNetField("Health", "int", 0, 4, 10, 0)])],
            Tiers: [new DebugClientTierAgreement("DT_SS_Tier0", 1, 1, true)],
            RefusedUpdates: 0, Refusals: [], Agrees: true);

        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(netTables: new FixedNetTables(tables)));
        Assert.Equal(DebugApiClientGoldenTests.Golden("nettables"), client.Answer(DebugChannelQuery.NetTables));
    }
}

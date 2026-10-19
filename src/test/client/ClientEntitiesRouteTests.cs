using System.Runtime.InteropServices;
using System.Text.Json;

using SourceSharp.Abi;
using SourceSharp.Client;
using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using Debug.Api;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The client's <c>entities</c> route (plan_client_novalve §4.4): the client's
/// own entity list, from the engine blocks the render-policy pass reads.
/// </summary>
public class ClientEntitiesRouteTests
{
    private sealed class Entity(IEngineEntityRender render, string className) : ClientStandaloneEntity(render)
    {
        public override string ClassName { get; } = className;
    }

    private sealed class Standalones(Dictionary<int, ClientStandaloneEntity> byIndex) : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => byIndex.Values.ToList();

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => byIndex.GetValueOrDefault(entityIndex);
    }

    private sealed class Blocks(params byte[][] blocks) : IRenderPolicyBlocks
    {
        public int Begins;

        public int Begin()
        {
            Begins++;
            return blocks.Length;
        }

        public Span<byte> Block(int index) => blocks[index];
    }

    private static byte[] BlockFor(uint handle, Vector origin, bool dormant, uint outputs)
    {
        var block = new EngineBlock();
        block.Head.Handle = handle;
        block.Head.ModelIndex = 12;
        block.Head.Origin = origin;
        block.Client.RenderOrigin = origin;
        block.Client.Dormant = dormant ? 1 : 0;
        block.Client.ShadowType = 1;
        block.Client.RenderGroup = 2;
        block.Client.Drawn = 1;
        block.Client.PolicyInputs = 5;
        block.Client.PolicyOutputs = outputs;

        var bytes = new byte[EngineBlock.SizeBytes];
        MemoryMarshal.Write(bytes, in block);
        return bytes;
    }

    private static (ClientDebugEntities Route, Blocks Blocks) TwoEntities()
    {
        var engine = Fake();
        engine.Returns("EntityRender.GetModelName", "models/props/crate.mdl");
        engine.Returns("EntityRender.ShadowCast.hasShadow", true);
        engine.Returns("EntityRender.ShadowCast.asked", default(ShadowCastChoice));
        engine.Returns("EntityRender.ShadowCast", default(ShadowCast));

        var interpolation = new Interpolation();
        interpolation.RingFor(7, 0).AddToHead(1.0f, 1, 2, 3, flushNewer: false);
        interpolation.RingFor(7, 0).AddToHead(1.1f, 1, 2, 4, flushNewer: false);

        var blocks = new Blocks(
            BlockFor(0x0003_0009u, new Vector(9, 0, 0), dormant: true, outputs: 0),
            BlockFor(0x0002_0007u, new Vector(1, 2, 3), dormant: false, outputs: 4));
        // A real declared state, bound to local storage and written, on the crate.
        var occluder = new OccluderNetState();
        occluder.BindLocal();
        occluder.Index = 37;
        occluder.Active = true;
        var crate = new Entity(engine, "ss_crate");
        crate.NetStates = [occluder];
        var standalones = new Standalones(new() { [7] = crate, [9] = new Entity(engine, "ss_lamp") });

        var tiers = new Tiers(new NetTierInfo(0, "DT_SS_Tier0", 64, 16, 22, true));
        var types = new ClientNetSchemaTypes(tiers, () => StandaloneNetStates.Discover(
            typeof(ClientModule).Assembly, new RecordingLog()));
        return (new ClientDebugEntities(blocks, engine, standalones, interpolation, types), blocks);
    }

    [Fact]
    public void The_route_lists_every_block_with_its_class_and_state()
    {
        var (route, blocks) = TwoEntities();
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(entities: route));
        using var json = JsonDocument.Parse(client.Answer(DebugChannelQuery.Entities));
        var list = json.RootElement.GetProperty("entities");

        // The positive arm: both blocks, by index, read from the bytes.
        Assert.Equal(1, blocks.Begins);
        Assert.Equal(2, json.RootElement.GetProperty("count").GetInt32());
        Assert.Equal([7, 9], list.EnumerateArray().Select(e => e.GetProperty("index").GetInt32()));
        var crate = list[0];
        Assert.Equal("ss_crate", crate.GetProperty("class").GetString());
        Assert.Equal("0x00020007", crate.GetProperty("handle").GetString());
        Assert.Equal("models/props/crate.mdl", crate.GetProperty("model").GetString());
        Assert.Equal(3f, crate.GetProperty("origin")[2].GetSingle());
        Assert.Equal(2, crate.GetProperty("interpolationDepth").GetInt32());
        Assert.False(crate.GetProperty("dormant").GetBoolean());
        Assert.Equal(4u, crate.GetProperty("policy").GetProperty("outputs").GetUInt32());
        Assert.True(list[1].GetProperty("dormant").GetBoolean());
        Assert.Equal(0, list[1].GetProperty("interpolationDepth").GetInt32());
    }

    [Fact]
    public void The_class_argument_filters_the_list()
    {
        var (route, _) = TwoEntities();
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(entities: route));
        using var json = JsonDocument.Parse(client.Answer(DebugChannelQuery.Entities, "SS_LAMP"));

        Assert.Equal("SS_LAMP", json.RootElement.GetProperty("class").GetString());
        Assert.Equal(9, Assert.Single(json.RootElement.GetProperty("entities").EnumerateArray()).GetProperty("index").GetInt32());
    }

    [Fact]
    public void Reading_the_depth_makes_no_ring()
    {
        var interpolation = new Interpolation();
        int before = interpolation.LiveRings;
        Assert.Equal(0, interpolation.RingDepth(42));
        Assert.Equal(before, interpolation.LiveRings);
    }

    private sealed class FixedEntities(IReadOnlyList<DebugClientEntity> list, DebugClientEntityState? state = null)
        : IDebugClientEntities
    {
        public IReadOnlyList<DebugClientEntity> List(string className) => list;

        public DebugClientEntityState? State(int index) => state;
    }

    private sealed class Tiers(params NetTierInfo[] tiers) : INetTiers
    {
        public IReadOnlyList<NetTierInfo> For(NetShellKind shell) => tiers;
        public IReadOnlyList<int> Sizes => tiers.Select(t => t.PayloadBytes).ToArray();
        public int Ceiling(int layoutBytes) => tiers.Select(t => t.PayloadBytes).Where(b => b >= layoutBytes).DefaultIfEmpty(-1).Min();
        public double Fill(int layoutBytes) => 0;
        public bool NearCeiling(int layoutBytes) => false;
    }

    [Fact]
    public void One_entity_reads_its_states_and_interpolation()
    {
        var (route, _) = TwoEntities();
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(entities: route));
        using var json = JsonDocument.Parse(client.Answer(DebugChannelQuery.Entity, "7"));
        var root = json.RootElement;

        Assert.Equal("ss_crate", root.GetProperty("class").GetString());
        var state = Assert.Single(root.GetProperty("states").EnumerateArray());
        Assert.True(state.GetProperty("live").GetBoolean());
        // The positive arm: the values written above, decoded through the
        // declared layout, not zeros.
        var fields = state.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!);
        Assert.Equal("37", fields["Index"].GetProperty("value").GetString());
        Assert.Equal(10, fields["Index"].GetProperty("bits").GetInt32());
        Assert.True(fields["Active"].GetProperty("present").GetBoolean());

        var ring = Assert.Single(root.GetProperty("interpolated").EnumerateArray());
        Assert.Equal(2, ring.GetProperty("depth").GetInt32());
        Assert.Equal(4f, ring.GetProperty("newest")[2].GetSingle());
    }

    [Theory]
    [InlineData("8", "no client entity at that index")]
    [InlineData("x", "the entity index is not a number")]
    public void An_entity_that_is_not_there_says_why(string argument, string error)
    {
        var (route, _) = TwoEntities();
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(entities: route));
        using var json = JsonDocument.Parse(client.Answer(DebugChannelQuery.Entity, argument));
        Assert.Equal(error, json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void The_entity_answer_renders_exactly()
    {
        var entity = new DebugClientEntity(7, 0x0002_0007u, "ss_crate", "models/props/crate.mdl", 12,
            new DebugClientVec3(1, 2, 3), new DebugClientVec3(1.5f, 2, 3), false, 2, 1, 0, true, 2, true, false, 5, 4);
        var state = new DebugClientEntityState(entity, new DebugClientVec3(0, 90, 0), new DebugClientVec3(0, 89.5f, 0),
            [new DebugClientNetStateBlock("func_occluder", "OccluderNetState", true, 8,
                [new DebugClientNetValue("Index", "uint", 0, 10, true, "37", "0x25")])],
            [new DebugClientInterpolated("Origin", 2, 1.1f, new DebugClientVec3(1, 2, 4))]);

        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(entities: new FixedEntities([], state)));
        Assert.Equal(DebugApiClientGoldenTests.Golden("entity"), client.Answer(DebugChannelQuery.Entity, "7"));
    }

    [Fact]
    public void The_entities_answer_renders_exactly()
    {
        var entity = new DebugClientEntity(
            Index: 7, Handle: 0x0002_0007u, ClassName: "ss_crate", Model: "models/props/crate.mdl", ModelIndex: 12,
            Origin: new DebugClientVec3(1, 2, 3), RenderOrigin: new DebugClientVec3(1.5f, 2, 3), Dormant: false,
            InterpolationDepth: 2, ShadowType: 1, ShadowChoice: 0, HasShadow: true, RenderGroup: 2, Drawn: true,
            ViewModel: false, PolicyInputs: 5, PolicyOutputs: 4);

        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(entities: new FixedEntities([entity])));
        Assert.Equal(DebugApiClientGoldenTests.Golden("entities"), client.Answer(DebugChannelQuery.Entities, "ss_crate"));
    }
}

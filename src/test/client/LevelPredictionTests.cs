using System.Text.RegularExpressions;

using Xunit;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// The level's prediction switch, stock CHLClient::LevelInitPreEntity's
/// cl_predict arm (cdll_client_int.cpp:1646-1661), decided in C# since lane
/// M1-B: a multiplayer game turns prediction on, a single-player one turns it
/// off, each only when the bounded value says otherwise.
/// </summary>
public class LevelPredictionTests
{
    private sealed class Command : IClientPredictCommand
    {
        public List<bool> Sent { get; } = [];
        public void SetPredict(bool predict) => Sent.Add(predict);
    }

    private static (LevelPrediction Rule, Command Sent) Rule(int maxClients, Dictionary<string, string> convars)
    {
        var command = new Command();
        return (new LevelPrediction(() => maxClients, name => convars.GetValueOrDefault(name), command), command);
    }

    [Fact]
    public void Multiplayer_turns_prediction_on_when_it_is_off()
        => Assert.True(LevelPrediction.Switch(maxClients: 2, boundedPredict: 0f));

    [Fact]
    public void Multiplayer_leaves_prediction_on()
        => Assert.Null(LevelPrediction.Switch(maxClients: 16, boundedPredict: 1f));

    [Fact]
    public void Single_player_turns_prediction_off_when_it_is_on()
        => Assert.False(LevelPrediction.Switch(maxClients: 1, boundedPredict: 1f));

    [Fact]
    public void Single_player_leaves_prediction_off()
        => Assert.Null(LevelPrediction.Switch(maxClients: 1, boundedPredict: 0f));

    [Fact]
    public void The_value_is_read_as_GetInt_reads_it_truncated()
        // 0.5 is GetInt's 0: off, so a multiplayer level turns it on.
        => Assert.True(LevelPrediction.Switch(maxClients: 2, boundedPredict: 0.5f));

    [Fact]
    public void A_multiplayer_level_with_prediction_off_queues_cl_predict_1()
    {
        var (rule, sent) = Rule(8, new() { ["cl_predict"] = "0" });

        Assert.True(rule.LevelInitPreEntity());
        Assert.Equal([true], sent.Sent);
    }

    [Fact]
    public void A_single_player_level_with_prediction_on_queues_cl_predict_0()
    {
        var (rule, sent) = Rule(1, new() { ["cl_predict"] = "1" });

        Assert.False(rule.LevelInitPreEntity());
        Assert.Equal([false], sent.Sent);
    }

    [Fact]
    public void A_level_that_already_reads_as_wanted_queues_nothing()
    {
        var (rule, sent) = Rule(8, new() { ["cl_predict"] = "1" });

        Assert.Null(rule.LevelInitPreEntity());
        Assert.Empty(sent.Sent);
    }

    [Fact]
    public void The_servers_sv_client_predict_bounds_the_value_read()
    {
        // The client says 0, the server forces 1: GetInt on the bounded convar
        // answered 1, so a multiplayer level has nothing to switch.
        var (rule, sent) = Rule(8, new() { ["cl_predict"] = "0", ["sv_client_predict"] = "1" });

        Assert.Null(rule.LevelInitPreEntity());
        Assert.Empty(sent.Sent);
    }

    [Fact]
    public void With_no_cl_predict_there_is_nothing_to_switch()
    {
        var (rule, sent) = Rule(1, []);

        Assert.Null(rule.LevelInitPreEntity());
        Assert.Empty(sent.Sent);
    }

    private sealed class Host : IClientRuntimeHost
    {
        public int PreEntity;
        public int PostInitLevel = -2;
        public void Frame(float frameTime) { }
        public void OnStage(int stage) { }
        public void LevelInit(string mapName) { }
        public void LevelShutdown() { }
        public void InvalidateMdlCache() { }
        public bool ParticlesNeedFullReload(int previousMapParticleFiles) => true;
        public string? MapMaterialReloadSet(string nextBsp, string previousBsp) => null;
        public void LevelInitPreEntity() => PreEntity++;
        public void PostInit(int adapterMaxDxLevel) => PostInitLevel = adapterMaxDxLevel;
        public void MapMaterialSearchPath(string? path, string? pathId, bool addToHead) { }
        public void MapMaterialMapAccess(bool open) { }
        public string? MapMaterialReloadPlan(string? subString) => null;
        public int WorldDataChanged(ReadOnlySpan<byte> blob, int updateType, Span<float> plan) => 0;
    }

    private static (ClientRuntimeBinding Binding, Host Host, Crossings Crossings) Binding()
    {
        var host = new Host();
        var log = new RecordingLog();
        var crossings = new Crossings();
        var models = new ModelDescriptors(_ => -1, _ => string.Empty, log);
        var breaks = new BreakModels(_ => -1, _ => -1, _ => null, models, log);
        return (new ClientRuntimeBinding(crossings, log, host, new StandaloneMirror(), models, breaks), host, crossings);
    }

    [Fact]
    public void The_binding_hands_the_level_start_to_the_host_once_and_counts_it()
    {
        var (binding, host, crossings) = Binding();
        long before = crossings.Read(CrossingOp.ClientLevelInitPreEntity);

        binding.LevelInitPreEntity();

        Assert.Equal(1, host.PreEntity);
        Assert.Equal(before + 1, crossings.Read(CrossingOp.ClientLevelInitPreEntity));
    }

    [Fact]
    public void The_binding_hands_PostInit_the_adapters_level_and_counts_it()
    {
        var (binding, host, crossings) = Binding();
        long before = crossings.Read(CrossingOp.ClientPostInit);

        binding.PostInit(95);

        Assert.Equal(95, host.PostInitLevel);
        Assert.Equal(before + 1, crossings.Read(CrossingOp.ClientPostInit));
    }

}

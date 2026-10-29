using System.Text.RegularExpressions;

using Xunit;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// plan_map_load P8: a level re-reads every particle definition file only when
/// it must. Measured on a remote client at 178bfc1, the unconditional re-read
/// was 1.0–1.5 s of DMX parsing per hop.
/// </summary>
public class ParticleReloadPolicyTests
{
    [Fact]
    public void The_first_level_reads_every_file()
        => Assert.True(new ParticleReloadPolicy().NeedsFullReload(-1));

    [Fact]
    public void After_a_level_with_no_map_particles_only_the_precache_is_redone()
        => Assert.False(new ParticleReloadPolicy().NeedsFullReload(0));

    [Fact]
    public void After_a_level_that_replaced_definitions_every_file_is_read_again()
        => Assert.True(new ParticleReloadPolicy().NeedsFullReload(3));

    private sealed class Host(bool answer) : IClientRuntimeHost
    {
        public int Asked = -2;
        public void Frame(float frameTime) { }
        public void OnStage(int stage) { }
        public void LevelInit(string mapName) { }
        public void LevelShutdown() { }
        public void InvalidateMdlCache() { }
        public bool ParticlesNeedFullReload(int previousMapParticleFiles) { Asked = previousMapParticleFiles; return answer; }
        public string? MapMaterialReloadSet(string nextBsp, string previousBsp) => null;
        public void MapMaterialSearchPath(string? path, string? pathId, bool addToHead) { }
        public void MapMaterialMapAccess(bool open) { }
        public string? MapMaterialReloadPlan(string? subString) => null;
        public int WorldDataChanged(ReadOnlySpan<byte> blob, int updateType, Span<float> plan) => 0;
        public void LevelInitPreEntity() { }
        public void PostInit(int adapterMaxDxLevel) { }
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void The_binding_hands_native_the_hosts_answer(bool answer, int expected)
    {
        var host = new Host(answer);
        // The binding's other three collaborators are only touched at level
        // shutdown; inert ones keep this to the slot under test.
        var log = new RecordingLog();
        var crossings = new Crossings();
        var models = new ModelDescriptors(_ => -1, _ => string.Empty, log);
        var breaks = new BreakModels(_ => -1, _ => -1, _ => null, models, log);
        var binding = new ClientRuntimeBinding(crossings, log, host, new StandaloneMirror(), models, breaks);
        long before = crossings.Read(CrossingOp.ClientParticlesNeedFullReload);

        Assert.Equal(expected, binding.ParticlesNeedFullReload(7));
        Assert.Equal(7, host.Asked);
        Assert.Equal(before + 1, crossings.Read(CrossingOp.ClientParticlesNeedFullReload));
    }

}

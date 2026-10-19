using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// C7 particles phase 4: particlemgr.cpp's and particles_new.cpp's convars and
/// commands, managed since both files left client.so. The declarations are
/// held to stock's names, defaults and flags; cs_cl_particle_report to the
/// lines tools/7e-particle-sound-gate reads; cl_dump_particle_stats to the
/// library call.
/// </summary>
public class ParticleCommandTests
{
    // particlemgr.cpp's and particles_new.cpp's declarations, as written there.
    [Theory]
    [InlineData(typeof(DrawParticlesConVar), "r_drawparticles", "1", ConVarFlags.Cheat)]
    [InlineData(typeof(ParticleEffectAabbBufferConVar), "cl_particleeffect_aabb_buffer", "2", ConVarFlags.Cheat)]
    [InlineData(typeof(ParticleShowBBoxConVar), "cl_particle_show_bbox", "0", ConVarFlags.Cheat)]
    [InlineData(typeof(ParticleShowBBoxCostConVar), "cl_particle_show_bbox_cost", "0", ConVarFlags.Cheat)]
    [InlineData(typeof(ParticleTimescaleConVar), "r_particle_timescale", "1.0", ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
    [InlineData(typeof(ParticleRetireCostConVar), "cl_particle_retire_cost", "0", ConVarFlags.Cheat | ConVarFlags.AllowedInCompetitive)]
    [InlineData(typeof(ParticleSimSpikeThresholdConVar), "r_particle_sim_spike_threshold_ms", "5", ConVarFlags.None)]
    [InlineData(typeof(ParticleStatsTriggerCountConVar), "cl_particle_stats_trigger_count", "0", ConVarFlags.None)]
    public void EachConVarIsStocksNameDefaultAndFlags(Type type, string name, string value, ConVarFlags flags)
    {
        ConVarAttribute a = type.GetCustomAttribute<ConVarAttribute>()!;
        Assert.Equal((name, value, flags), (a.Name, a.DefaultValue, a.Flags));
    }

    [Theory]
    [InlineData(typeof(ParticleStatsStartCommand), "cl_particle_stats_start")]
    [InlineData(typeof(ParticleStatsStopCommand), "cl_particle_stats_stop")]
    [InlineData(typeof(DumpParticleStatsCommand), "cl_dump_particle_stats")]
    [InlineData(typeof(ParticleReportCommand), "cs_cl_particle_report")]
    public void EachCommandIsDeclaredUnderStocksName(Type type, string name)
        => Assert.Equal(name, type.GetCustomAttribute<ConCommandAttribute>()!.Name);

    private sealed class CountingLibrary : IParticleLibrary
    {
        public int Dumps;
        public float ThrottleScaling() => 1f;
        public int SystemCount() => 0;
        public string? SystemName(int index) => null;
        public void DumpProfileInformation() => Dumps++;
    }

    [Fact]
    public void DumpParticleStatsAsksTheLibraryToWriteItsProfile()
    {
        var library = new CountingLibrary();
        new DumpParticleStatsCommand(library).Execute(string.Empty);
        Assert.Equal(1, library.Dumps);
    }

    private static ClientParticleSystemRow Row(string name, int particles, Vector cp0)
        => new(name, new ParticleSystemInfo
        {
            Particles = particles,
            ControlPoint0 = cp0,
            ControlPoint1 = new Vector(0, 0, 0),
            SortOrigin = cp0,
        });

    private static bool Nowhere(int index, out Vector origin)
    {
        origin = default;
        return false;
    }

    // The lines tools/7e-particle-sound-gate parses: one per system, then the
    // summary it finds the block by.
    [Fact]
    public void TheReportPrintsEachSystemThenTheSummary()
    {
        List<string> lines = [.. ParticleReportCommand.Lines(
            [Row("env_fire_small_smoke", 5, new Vector(800, 1311.97f, 8)), Row("burning_character", 0, new Vector(-1.26f, 0, 0))],
            string.Empty, Nowhere)];
        Assert.Equal(
        [
            "[SourceSharp] particle system: name 'env_fire_small_smoke' particles 5 cp0 (800.0 1312.0 8.0) cp1 (0.0 0.0 0.0) sort (800.0 1312.0 8.0)\n",
            "[SourceSharp] particle system: name 'burning_character' particles 0 cp0 (-1.3 0.0 0.0) cp1 (0.0 0.0 0.0) sort (-1.3 0.0 0.0)\n",
            "[SourceSharp] particle report: 2 system(s), 1 with particles\n",
        ], lines);
    }

    // With an entity index: where this side thinks the entity is, or that it
    // has none there.
    [Fact]
    public void AnIndexAddsTheClientsOriginForThatEntity()
    {
        static bool At(int index, out Vector origin)
        {
            origin = new Vector(index, 2, 3);
            return index == 7;
        }

        Assert.Equal("[SourceSharp] client entity 7: origin (7.0 2.0 3.0)\n", ParticleReportCommand.Lines([], "7", At).Last());
        Assert.Equal("[SourceSharp] client entity 9: ABSENT on this side\n", ParticleReportCommand.Lines([], " 9 extra", At).Last());
        Assert.Single(ParticleReportCommand.Lines([], "", At));
    }
}

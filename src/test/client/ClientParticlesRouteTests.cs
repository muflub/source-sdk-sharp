using System.Text.Json;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interface;
using SourceSharp.Client.Interop;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's <c>particles</c> route (plan_client_novalve §4.4): the
/// CNewParticleEffect instances the particles shell started, and how many
/// times the definitions were read.
/// </summary>
public class ClientParticlesRouteTests
{
    private sealed class FakeSystems(int definitionReads, params ClientParticleSystemRow[] rows) : IClientParticleSystems
    {
        public IReadOnlyList<ClientParticleSystemRow> Read() => rows;

        public int DefinitionReads() => definitionReads;

        public IReadOnlyList<int> QueryUnanswered() => [14, 0, 0, 0, 0];
    }

    private static ClientParticleSystemRow Row(string name, int kind, uint owner, int mask, bool stopped, int particles)
        => new(name, new ParticleSystemInfo
        {
            Kind = kind, Owner = owner, FollowMask = mask, Stopped = stopped ? 1 : 0, Age = 2.5f,
            Particles = particles, ControlPoint0 = new Vector(10, 20, 30), ControlPoint1 = new Vector(10, 20, 31),
            SortOrigin = new Vector(10, 20, 32),
        });

    private static JsonElement Read(IClientParticleSystems systems, out JsonDocument doc)
    {
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(particles: new ClientDebugParticles(systems)));
        doc = JsonDocument.Parse(client.Answer(DebugChannelQuery.Particles));
        return doc.RootElement;
    }

    [Fact]
    public void An_owned_system_reports_its_owner_and_the_points_kept_at_it()
    {
        var root = Read(new FakeSystems(3, Row("burning_character", 1, 0x2b40189, 0b11, false, 41)), out var doc);
        using (doc)
        {
            Assert.Equal(3, root.GetProperty("definitionReads").GetInt32());
            Assert.Equal(14, root.GetProperty("queryUnanswered").GetProperty("collisionGroup").GetInt32());
            Assert.Equal(1, root.GetProperty("withParticles").GetInt32());
            var s = root.GetProperty("systems")[0];
            Assert.Equal("burning_character", s.GetProperty("name").GetString());
            Assert.Equal(0x2b40189u, s.GetProperty("owner").GetUInt32());
            Assert.Equal("follow", s.GetProperty("attachment").GetString());
            Assert.Equal([0, 1], s.GetProperty("followPoints").EnumerateArray().Select(p => p.GetInt32()));
            Assert.Equal(41, s.GetProperty("particles").GetInt32());
            Assert.Equal([10f, 20f, 31f], s.GetProperty("controlPoint1").EnumerateArray().Select(v => v.GetSingle()));
            Assert.Equal([10f, 20f, 32f], s.GetProperty("sortOrigin").EnumerateArray().Select(v => v.GetSingle()));
        }
    }

    [Fact]
    public void A_one_shot_has_no_owner_and_sits_in_the_world()
    {
        var root = Read(new FakeSystems(1, Row("impact_concrete", 2, 0xFFFFFFFF, 0, false, 0)), out var doc);
        using (doc)
        {
            var s = root.GetProperty("systems")[0];
            Assert.Equal(JsonValueKind.Null, s.GetProperty("owner").ValueKind);
            Assert.Equal("world", s.GetProperty("attachment").GetString());
            Assert.Equal(0, s.GetProperty("followPoints").GetArrayLength());
            Assert.Equal(0, root.GetProperty("withParticles").GetInt32());
        }
    }

    [Fact]
    public void A_system_its_owner_let_go_is_released_and_follows_nothing()
    {
        var root = Read(new FakeSystems(1, Row("env_fire_small", 1, 0x1234, 0, true, 7)), out var doc);
        using (doc)
            Assert.Equal("released", root.GetProperty("systems")[0].GetProperty("attachment").GetString());
    }

    [Fact]
    public void The_particles_answer_renders_exactly()
    {
        var particles = new FakeSystems(2,
            Row("env_fire_small", 1, 0x2b40189, 0b1, false, 22),
            Row("impact_dirt", 2, 0xFFFFFFFF, 0, false, 0));
        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(particles: new ClientDebugParticles(particles)));
        Assert.Equal(DebugApiClientGoldenTests.Golden("particles"), client.Answer(DebugChannelQuery.Particles));
    }
}

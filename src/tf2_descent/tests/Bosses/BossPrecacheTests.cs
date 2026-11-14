using SourceSharp.Abi;
using TF2.Descent.Server.Horde.Boss;
using TF2.Descent.Tests.Horde;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// The boss fight's assets precached as a level loads (HO13; found live: MvM's siren, the giant's
/// entrance and the bomb played "not precached"), and the adds' counts kept per level.
/// </summary>
public sealed class BossPrecacheTests
{
    [Fact]
    public void Every_sound_and_particle_bosses_json_names_is_precached_as_the_level_loads()
    {
        var engine = new RecordingEngine();
        var s = TheRules.Content.Bosses;

        new BossPrecache(new RigRules(), engine).OnLevelInitPreEntity("tf2_descent-5-s1");

        var sounds = engine.Calls.Where(c => c.Member.EndsWith("PrecacheScriptSound", StringComparison.Ordinal)).Select(c => c.ArgAs<string>(0)).ToHashSet();
        var particles = engine.Calls.Where(c => c.Member.EndsWith("PrecacheParticleSystem", StringComparison.Ordinal)).Select(c => c.ArgAs<string>(0)).ToHashSet();
        Assert.Superset(new HashSet<string> { s.Sounds.Intro, s.Sounds.Phase, s.Sounds.Death, s.Sounds.ExitOpens, s.Sounds.TankDrill, s.Sounds.Roof }, sounds);
        Assert.Superset(s.Specials.Select(x => x.Sound).ToHashSet(), sounds);
        Assert.Superset(s.Specials.Where(x => x.Particle.Length > 0).Select(x => x.Particle).Append(s.Sounds.TankDeathParticle).ToHashSet(), particles);
    }

    [Fact]
    public void A_new_level_s_adds_count_from_nothing()
    {
        var first = new BossRig();
        first.Adds.Summon(first.Controller, first.Row, "demoman", 2, 9, first.Now);
        Assert.Equal((2, 2, 0), first.Adds.Counts(first.Controller));

        var second = new BossRig();   // another level's controller, the same service

        Assert.Equal((0, 0, 0), first.Adds.Counts(second.Controller));
    }
}

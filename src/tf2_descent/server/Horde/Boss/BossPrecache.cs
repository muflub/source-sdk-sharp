using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Bosses;

namespace TF2.Descent.Server.Horde.Boss;

/// <summary>
/// The boss fight's sounds and particles, precached as each level loads (HO13): bosses.json names
/// them, so no attribute can (a sound the engine was never told of plays as "not precached", found
/// live on the first boss boot). Every special's sound and particle, and every sound and particle
/// of the fight and the Tank.
/// </summary>
[SideService(Side.Server)]
public sealed class BossPrecache(IHordeRules rules, IEnginePrecache precache) : IGameLifecycle
{
    /// <summary>In the boss band, before the entities spawn.</summary>
    public int LifecycleOrder => ModStages.First + 1330;

    public void OnLevelInitPreEntity(string mapName)
    {
        var (sounds, particles) = Assets(rules.Rules.Bosses.Settings);
        foreach (string s in sounds)
            precache.PrecacheScriptSound(s);
        foreach (string p in particles)
            precache.PrecacheParticleSystem(p);
    }

    /// <summary>The script sounds and particle systems bosses.json names, each once.</summary>
    public static (IReadOnlyList<string> Sounds, IReadOnlyList<string> Particles) Assets(BossSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var o = s.Sounds;
        var sounds = new SortedSet<string>(StringComparer.Ordinal) { o.Intro, o.Phase, o.Death, o.ExitOpens, o.TankStart, o.TankStop, o.TankDrill, o.TankDeath, o.Roof, o.PillarFalls };
        var particles = new SortedSet<string>(StringComparer.Ordinal) { o.TankDeathParticle, o.PillarDust };
        foreach (var sp in s.Specials)
        {
            sounds.Add(sp.Sound);
            if (sp.Particle.Length > 0)
                particles.Add(sp.Particle);
        }

        sounds.Remove("");
        particles.Remove("");
        return ([.. sounds], [.. particles]);
    }
}

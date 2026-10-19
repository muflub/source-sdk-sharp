using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IDebugClientParticles"/> over the particles shell
/// (plan_client_novalve §4.4): the reader names each instance's kind and
/// spells its follow mask out as control-point indices.
/// </summary>
public sealed class ClientDebugParticles(IClientParticleSystems systems) : IDebugClientParticles
{
    private const int Owned = 1;

    /// <summary>The thunk's methods, in its counters' order.</summary>
    private static string? QueryMethod(int i) => i switch
    {
        0 => "collisionGroup",
        1 => "lighting",
        2 => "trace",
        3 => "hitBoxPoints",
        4 => "hitBoxInfo",
        _ => null,
    };

    public DebugClientParticles Read()
    {
        var rows = new List<DebugClientParticleSystem>();
        int withParticles = 0;
        foreach (var (name, info) in systems.Read())
        {
            bool owned = info.Kind == Owned;
            string attachment = !owned ? "world" : info.Stopped != 0 ? "released" : "follow";
            var points = new List<int>();
            for (int p = 0; p < 32; p++)
            {
                if ((info.FollowMask & (1 << p)) != 0)
                    points.Add(p);
            }
            if (info.Particles > 0)
                withParticles++;
            rows.Add(new DebugClientParticleSystem(name, owned ? info.Owner : null, attachment, points, info.Age,
                                                   info.Particles, Point(info.ControlPoint0),
                                                   Point(info.ControlPoint1), Point(info.SortOrigin)));
        }
        var unanswered = systems.QueryUnanswered();
        var counts = new List<DebugCount>(unanswered.Count);
        for (int i = 0; i < unanswered.Count && QueryMethod(i) is { } method; i++)
            counts.Add(new DebugCount(method, unanswered[i]));
        return new DebugClientParticles(systems.DefinitionReads(), withParticles, rows, counts);
    }

    private static DebugClientPoint Point(SourceSharp.Abi.Vector v) => new(v.X, v.Y, v.Z);
}

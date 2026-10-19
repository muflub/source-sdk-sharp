//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

// C7 particles phase 4: particlemgr.cpp's and particles_new.cpp's console
// commands, here since both left client.so.

/// <summary>
/// cl_particle_stats_start: stock's StatsParticlesStart, which is empty in this
/// tree; the command is kept so a config that runs it is not an unknown command.
/// </summary>
[ConCommand("cl_particle_stats_start", Help = "Start or restart particle stats - also dumps to particle_stats.csv")]
public sealed class ParticleStatsStartCommand : IConCommand
{
    public void Execute(string args) { }
}

/// <summary>cl_particle_stats_stop: stock's StatsParticlesStop, empty in this tree.</summary>
[ConCommand("cl_particle_stats_stop", Help = "Stop particle stats, or snapshot this frame - also dumps to particle_stats.csv")]
public sealed class ParticleStatsStopCommand : IConCommand
{
    public void Execute(string args) { }
}

/// <summary>
/// cl_dump_particle_stats (particles_new.cpp): the particle library writes the
/// per-definition timings EndSimulateParticles kept (NewParticleManager's
/// profile commit) to particle_profile.csv.
/// </summary>
[ConCommand("cl_dump_particle_stats", Help = "dump particle profiling info to particle_profile.csv")]
public sealed class DumpParticleStatsCommand(IParticleLibrary library) : IConCommand
{
    public void Execute(string args) => library.DumpProfileInformation();
}

/// <summary>
/// cs_cl_particle_report (plan_entity.md step 7e's instrument, which lived in
/// particlemgr.cpp): every new-style system the client started, its active
/// particles with its children's, its control points 0 and 1 and its sort
/// origin; with an entity index, where this side thinks that entity is. The
/// systems are the ones the dev API's /client/{slot}/particles lists (the
/// shell's watched systems): every system the client starts, but not a cull
/// replacement, and not a name no definition answers. The lines are stock's
/// report's, less its highest control point, which that read does not carry.
/// </summary>
[ConCommand("cs_cl_particle_report",
            Help = "plan_entity.md 7e: every live particle system, its active particle count (with children), its control points 0 and 1, and its sort origin")]
public sealed class ParticleReportCommand(ILog log, IClientParticleSystems systems, IEngineEffects effects) : IConCommand
{
    public void Execute(string args)
    {
        foreach (string line in Lines(systems.Read(), args, effects.ClientEntityOrigin))
            log.Msg(line);
    }

    /// <summary>A client entity's origin by index, or false when this side has none there.</summary>
    public delegate bool OriginOf(int entityIndex, out Vector origin);

    /// <summary>The report's lines, in stock's order: the systems, the summary, then the entity.</summary>
    public static IEnumerable<string> Lines(IReadOnlyList<ClientParticleSystemRow> rows, string args, OriginOf originOf)
    {
        int drawing = 0;
        foreach (ClientParticleSystemRow row in rows)
        {
            ParticleSystemInfo i = row.Info;
            if (i.Particles > 0)
                drawing++;
            yield return $"[SourceSharp] particle system: name '{row.Name}' particles {i.Particles} " +
                         $"cp0 ({V(i.ControlPoint0)}) cp1 ({V(i.ControlPoint1)}) sort ({V(i.SortOrigin)})\n";
        }
        yield return $"[SourceSharp] particle report: {rows.Count} system(s), {drawing} with particles\n";

        string first = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (first.Length == 0)
            yield break;
        // atoi's reading: a leading number, else 0.
        int index = Atoi(first);
        yield return originOf(index, out Vector o)
            ? $"[SourceSharp] client entity {index}: origin ({V(o)})\n"
            : $"[SourceSharp] client entity {index}: ABSENT on this side\n";
    }

    private static string V(Vector v)
        => string.Create(CultureInfo.InvariantCulture, $"{v.X:0.0} {v.Y:0.0} {v.Z:0.0}");

    private static int Atoi(string s)
    {
        int n = 0, sign = 1, i = 0;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
            sign = s[i++] == '-' ? -1 : 1;
        while (i < s.Length && char.IsAsciiDigit(s[i]) && n < 100_000_000)
            n = n * 10 + (s[i++] - '0');
        return sign * n;
    }
}

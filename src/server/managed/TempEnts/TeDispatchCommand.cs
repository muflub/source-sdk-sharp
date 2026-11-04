#if DEBUG
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Fires DT_TEEffectDispatch by effect name, to everyone, and prints the name's
/// string table index: the live names this build sends, or probe names no client
/// effect claims, so the client prints back the string it resolved.
/// </summary>
[ConCommand("cs_sv_te_dispatch",
    Help = "Fire DT_TEEffectDispatch by effect name. cs_sv_te_dispatch <name|live|probe|all> [x y z]  -- " +
           "'live' fires the four names this build's own callers use, 'probe' fires four names no client " +
           "effect claims so the client prints back the string it resolved from each index (needs developer 2), " +
           "'all' does both, interleaved.",
    Flags = ConVarFlags.Cheat)]
public sealed class TeDispatchCommand
{
    private readonly string[] _live = ["Impact", "watersplash", "bloodspray", "GlassImpact"];
    private readonly string[] _probe = ["cs_s4_probe_0", "cs_s4_probe_1", "cs_s4_probe_2", "cs_s4_probe_3"];

    private readonly ILog _log;
    private readonly ITempEnts _temps;
    private readonly IEngineEffects _effects;

    public TeDispatchCommand(ILog log, ITempEnts temps, IEngineEffects effects)
    {
        _log = log;
        _temps = temps;
        _effects = effects;
    }

    /// <summary>The words it takes for a set, and the live names, which are the ones worth firing by hand.</summary>
    private IEnumerable<string> Names() => ["live", "probe", "all", .. _live];

    public void Execute([Complete(From = nameof(Names))] string name, Vector? position = null)
    {
        Vector at = position ?? Vector.Zero;

        bool live = name.Equals("live", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("all", StringComparison.OrdinalIgnoreCase);
        bool probe = name.Equals("probe", StringComparison.OrdinalIgnoreCase)
                     || name.Equals("all", StringComparison.OrdinalIgnoreCase);

        if (!live && !probe)
        {
            Fire(name, at);
            return;
        }

        // Interleaved, so a string table mapping off by one prints a live name for a probe.
        int site = 0;

        for (int i = 0; i < _live.Length; i++)
        {
            if (live)
                Fire(_live[i], at + new Vector(0f, site++ * 48f, 0f));

            if (probe && i < _probe.Length)
                Fire(_probe[i], at + new Vector(0f, site++ * 48f, 0f));
        }
    }

    private void Fire(string name, Vector at)
    {
        var data = new TeEffectData
        {
            Origin = at,
            Start = at + new Vector(0f, 0f, 64f),
            Normal = new Vector(0f, 0f, 1f),
            Scale = 1f,
            Magnitude = 2f,
            Radius = 16f,
            // -1 is "no surface property", the value SendProxy_ShortAddOne exists for.
            SurfaceProp = -1,
        };

        int index = _effects.EffectDispatchIndex(name);
        _temps.DispatchEffect(TeFilters.Broadcast, 0f, index, in data);
        _log.Msg($"cs_sv_te_dispatch: '{name}' -> string table index {index} at ({at.X:g} {at.Y:g} {at.Z:g})\n");
    }
}
#endif

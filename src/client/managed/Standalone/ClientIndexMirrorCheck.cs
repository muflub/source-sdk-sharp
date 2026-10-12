using SourceSharp.Abi;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The divergence check for this side's two push mirrors.
///
/// <para>
/// A mirror's failure mode is not a crash: it is a plausible wrong number,
/// answered confidently forever, and the write-site census that licenses one is
/// a static argument. This is the dynamic half — it walks every live client
/// entity and asks the ENGINE the question the mirror now answers from memory,
/// on a running game, and says how many disagreed.
/// </para>
///
/// <para>
/// IT IS A GATE, NOT A GUARD. It crosses the boundary once per live entity, so
/// running it every frame would put back exactly the traffic the mirrors
/// removed. It is run from the console at a phase gate and nowhere else, which
/// is also why the <c>EntityGetIndex</c> slot is KEPT rather than deleted: it is
/// the reference this compares against.
/// </para>
/// </summary>
public static class IndexMirrorCheck
{
    /// <summary>
    /// Every live client entity's cached index against the engine's answer, and
    /// the dynamic-light key ledger's state.
    /// </summary>
    /// <returns>the report lines, with the verdict last.</returns>
    public static IReadOnlyList<string> Report(IReadOnlyList<ClientStandaloneEntity> live,
                                               IEngineEntities entities,
                                               IDynamicLights lights)
    {
        var lines = new List<string>();

        int agreed = 0;
        int diverged = 0;

        foreach (var entity in live)
        {
            int mirrored = entity.Index;
            int asked = entities.GetIndex(entity.Handle);

            if (mirrored == asked)
            {
                agreed++;
                continue;
            }

            diverged++;
            lines.Add($"DIVERGED {entity.ClassName}: cached index {mirrored}, " +
                      $"EntityGetIndex says {asked}");
        }

        lines.Add($"index mirror: {agreed} agreed, {diverged} DIVERGED " +
                  $"over {live.Count} live client entities");

        lines.Add($"dlight ledger: {lights.LitCount} key(s) lit, " +
                  $"{lights.Refreshes} refresh crossing(s), " +
                  $"{lights.Releases} release crossing(s)");

        lines.Add(diverged == 0 ? "VERDICT ok" : $"VERDICT {diverged} DIVERGED");

        return lines;
    }
}

/// <summary>
/// The console name for <see cref="IndexMirrorCheck.Report"/>.
/// </summary>
[ConCommand("cs_cl_index_mirror",
            Help = "Check this side's push mirrors against the engine: every live client " +
                   "entity's cached index against EntityGetIndex, and the dynamic-light key " +
                   "ledger's tallies. Says VERDICT ok or names what diverged.")]
public sealed class IndexMirrorCheckCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntities _entities;
    private readonly IDynamicLights _lights;

    public IndexMirrorCheckCommand(ILog log, IClientStandalones standalones, IEngineEntities entities,
                              IDynamicLights lights)
    {

        _log = log;
        _standalones = standalones;
        _entities = entities;
        _lights = lights;
    }

    public void Execute(string args)
    {
        foreach (string line in IndexMirrorCheck.Report(_standalones.Live(), _entities, _lights))
            _log.Msg($"[SourceSharp/Client] cs_cl_index_mirror: {line}\n");
    }
}

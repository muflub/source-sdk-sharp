using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_te_fire &lt;class|slug|all&gt; [x y z]</c> — every temp entity in the
/// build, from one line.
///
/// <para>
/// WHY IT EXISTS: gate 3 is headless. The station's button is the right way for
/// a person to set the plot off and is unreachable from debug-api, so without this
/// the coverage the map provides could only be collected by someone standing in
/// it. This fires all forty-three in one line, at the same offsets the station
/// lays them out on, and the client's <c>cs_cl_te_report</c> counters say what
/// arrived.
/// </para>
///
/// <para>
/// IT SUPERSEDES THE PER-LANE SCAFFOLDING (plan 9.1). <c>cs_sv_te_sparks</c>,
/// <c>cs_sv_te_beam</c>, <c>cs_sv_te_decal</c>, <c>cs_sv_te_fire_c</c>,
/// <c>cs_sv_te_fire_e</c>, <c>cs_sv_te_fire_f</c>, <c>cs_sv_te_gore</c>,
/// <c>cs_sv_te_fizz</c>, <c>cs_sv_te_restored</c> and
/// <c>cs_sv_te_energysplash</c> each fired one lane's classes and each said in
/// its own header that it went when this landed. Their argument values are
/// carried into <see cref="TeDemos"/> with the reasoning that chose them;
/// nothing else about them is kept. <c>cs_sv_te_dispatch</c> and
/// <c>cs_cl_te_backend</c> ARE kept, and their headers say why.
/// </para>
///
/// <para>
/// BROADCAST, NOT PVS, and it is the trap this pass was warned about. A PVS
/// filter with no recipients sends nothing and prints nothing, so a plot the
/// player cannot see is indistinguishable from a table that did not pair — and
/// this command exists precisely to fire at a place nobody is standing. Every
/// send here is broadcast unless the caller asks otherwise, so a silent client
/// means something is wrong rather than that the gate was run from the wrong
/// room.
/// </para>
///
/// <para>
/// The sv_cheats gate is on the command, as stock's <c>FCVAR_CHEAT</c> was, and
/// it reads the convar live per invocation through the same fail-closed reading
/// <see cref="ImpulseCheats.CheatsOn"/> gives the impulses: missing or garbage
/// means shut. Note that debug-api inputs do nothing at all with
/// <c>sv_cheats 0</c>, which is the intended answer and not a fault.
/// </para>
/// </summary>
[ConCommand("cs_te_fire",
    Help = "Fire one temp entity, or all forty-three, from C#.\n" +
           "\tcs_te_fire <class|name|all> [x y z] [broadcast|pvs|pas]\n" +
           "\tcs_te_fire list  -- every name it knows\n" +
           "\t(must have sv_cheats)")]
public sealed class TeFireCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly TempEntRegistry _registry;
    private readonly ITeFire _fire;

    public TeFireCommand(ILog log, TempEntRegistry registry, ITeFire fire, IEngineConsole console)
    {

        _log = log;
        _registry = registry;
        _fire = fire;
        CheatsEnabled = () => ImpulseCheats.CheatsOn(NativeConsole.ValueOf(console, "sv_cheats"));
    }

    /// <summary>
    /// A seam so a test can hold the gate open or shut. Its default reads
    /// this side's console (plan_abi_hotreload D8).
    /// </summary>
    public Func<bool> CheatsEnabled { get; set; }

    public void Execute(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            _log.Msg("usage: cs_te_fire <class|name|all|list> [x y z] [broadcast|pvs|pas]\n");
            return;
        }

        if (string.Equals(parts[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            List();
            return;
        }

        if (!CheatsEnabled())
        {
            _log.Msg("cs_te_fire: sv_cheats must be 1\n");
            return;
        }

        var at = Vector.Zero;

        if (parts.Length >= 4
            && float.TryParse(parts[1], out float x)
            && float.TryParse(parts[2], out float y)
            && float.TryParse(parts[3], out float z))
        {
            at = new Vector(x, y, z);
        }

        string? filter = parts.Length >= 5 ? parts[4] : (parts.Length == 2 ? parts[1] : null);

        if (string.Equals(parts[0], "all", StringComparison.OrdinalIgnoreCase))
        {
            FireAll(at, filter);
            return;
        }

        TeDemo? demo = TeDemos.ByClass(parts[0]) ?? TeDemos.BySlug(parts[0]);

        if (demo is null)
        {
            _log.Msg($"cs_te_fire: no temp entity called '{parts[0]}'. " +
                    "'cs_te_fire list' names every one.\n");
            return;
        }

        Fire(demo, at, filter);
    }

    /// <summary>
    /// All forty-three, SPREAD over the same grid the sandbox station lays them
    /// out on — so a run from the console puts each effect where the map puts
    /// it, and the two can be compared.
    /// </summary>
    private void FireAll(Vector at, string? filter)
    {
        int sent = 0;

        for (int i = 0; i < TeDemos.All.Count; i++)
        {
            Vector offset = TeDemos.Offset(i);
            var where = new Vector(at.X + offset.X, at.Y + offset.Y, at.Z + offset.Z);

            if (Fire(TeDemos.All[i], where, filter))
                sent++;
        }

        // THE COUNT IS WHAT MANAGED HANDED TO NATIVE, and the wording says so
        // rather than claiming forty-three effects went out.
        //
        // Managed knows a class is registered — it has a handle — and cannot
        // know whether native will play it. One class in the build is refused at
        // that step today: CTEBaseBeam's ServerClass belongs to the still-native
        // te_basebeam.cpp, so SourceSharp_TempEntPlayback refuses the send with a
        // line of its own immediately above this one. A summary reading "43 of 43
        // sent" over a class that never left would be exactly the count that
        // cannot fail.
        _log.Msg($"cs_te_fire: {sent} of {TeDemos.All.Count} temp entities handed to native " +
                $"from ({at.X:0.##} {at.Y:0.##} {at.Z:0.##}). A class whose ServerClass the " +
                "native build still owns is refused at playback and says so above.\n");
    }

    private bool Fire(TeDemo demo, Vector at, string? filter)
    {
        bool sent = _fire.Send(demo, at,
                               TeFire.FilterNamed(filter, at), delay: 0f);

        _log.Msg($"cs_te_fire: {(sent ? "sent" : "REFUSED")} {demo.ClassName,-26} " +
                $"at ({at.X:0.##} {at.Y:0.##} {at.Z:0.##}) — {demo.What}\n");

        return sent;
    }

    /// <summary>
    /// Every name this knows, with whether the class is registered on this side.
    ///
    /// <para>
    /// The registration column is the half worth having: a name in the book with
    /// no class behind it is a port that did not finish, and it reads exactly
    /// like a working effect until something fires it.
    /// </para>
    /// </summary>
    private void List()
    {
        foreach (TeDemo demo in TeDemos.All)
        {
            TempEntType? type = _registry.Find(demo.ClassName);

            _log.Msg($"cs_te_fire: {demo.Slug,-22} {demo.ClassName,-26} " +
                    $"{(type is { Registered: true } ? $"handle {type.Handle,3}" : "NOT REGISTERED")}" +
                    $"  {demo.What}\n");
        }

        _log.Msg($"cs_te_fire: {TeDemos.All.Count} temp entities, " +
                $"{_registry.Count} declarations registered on this side\n");
    }
}

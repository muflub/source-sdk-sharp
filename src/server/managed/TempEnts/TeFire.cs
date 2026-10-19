using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// Sends one of <see cref="TeDemos"/>' demonstrations.
///
/// <para>
/// THE ONE ROUTE ALL FORTY-THREE SHARE. It looks the class up in the registry,
/// packs a blob with <see cref="TempEntWriter"/> and calls
/// <c>TempEntPlayback</c> — the managed send path — for every class alike.
/// <c>ITempEnts</c> would have been the tidier-looking choice and would cover
/// twenty-one of forty-three: the beams, the sprites and most of the gore reach
/// the wire through native forwards and have no managed method by design, since
/// a method with no caller is a promise that lapses (see that interface's own
/// header). Firing three ways would measure three things and report one number.
/// </para>
///
/// <para>
/// WHAT THAT DOES NOT COVER, said out loud because the commands this replaces
/// did cover it: <c>cs_sv_te_beam</c>, <c>cs_sv_te_fire_c/e</c> and
/// <c>cs_sv_te_gore</c> fired through <c>te-&gt;</c>, so they exercised
/// <c>CTempEntsSystem</c>'s virtual, <c>SuppressTE</c> and the <c>TE_X()</c>
/// forward as well as the table. That chain still has provers — the live callers
/// (<c>env_spark</c>, the explosions, the decals at level load), the gtests, and
/// <c>cs_sv_te_dispatch</c>, which is kept — but it is not this.
/// </para>
///
/// <para>
/// A SERVICE (<see cref="ITeFire"/>), registered in the server module's
/// container over its temp entity registry and engine areas, and taken by
/// <c>cs_te_fire</c> and the sandbox's <c>ss_te_tester</c> through their
/// constructors. It was a static class whose only state was the set of names
/// it had already complained about, shared by every caller in the process;
/// that set now lives as long as the module that owns it.
/// </para>
/// </summary>
public sealed class TeFire : ITeFire
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly TempEntRegistry _registry;
    private readonly IEngineEffects _engine;
    private readonly IEnginePrecache _precache;

    /// <summary>The registry to look classes up in, and the engine to send and precache through.</summary>
    public TeFire(ILog log, TempEntRegistry registry, IEngineEffects engine, IEnginePrecache precache)
    {
        _log = log;
        _registry = registry;
        _engine = engine;
        _precache = precache;
    }

    /// <summary>
    /// Class and model names already reported as unresolvable, so a station
    /// fired every second does not fill a console with the same line.
    /// </summary>
    private readonly HashSet<string> _complained = new(StringComparer.Ordinal);

    /// <summary>
    /// Sends one demonstration, at <paramref name="at"/>.
    /// </summary>
    /// <returns>
    /// False when nothing went — the class is not registered on this side, so
    /// the caller can say that rather than reporting a send that did not happen.
    /// </returns>
    public bool Send(TeDemo demo, Vector at, TeFilter filter, float delay)
    {
        TempEntType? type = _registry.Find(demo.ClassName);

        if (type is not { Registered: true })
        {
            if (_complained.Add(demo.ClassName))
            {
                _log.Warning(
                    $"[SourceSharp] cs_te_fire: '{demo.ClassName}' is not registered on this " +
                    "side, so nothing was sent. The registration line in the console says why " +
                    "it was refused.\n");
            }

            return false;
        }

        Span<byte> blob = stackalloc byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        foreach (TeArg arg in demo.Args)
            Write(ref writer, arg, at);

        _engine.TempEntPlayback(type.Handle, blob, filter, delay);
        return true;
    }

    private void Write(ref TempEntWriter writer, TeArg arg, Vector at)
    {
        switch (arg.Kind)
        {
            case TeArgKind.Int:
                writer.Int(arg.Field, arg.Number);
                break;

            case TeArgKind.SurfaceProp:
                writer.Int(arg.Field, arg.Number + 1);
                break;

            case TeArgKind.Float:
                writer.Float(arg.Field, arg.Value);
                break;

            case TeArgKind.Vector:
                writer.Vector(arg.Field, arg.Relative ? Add(at, arg.Offset) : arg.Offset);
                break;

            case TeArgKind.Element:
                writer.VectorAt(arg.Field, arg.Index,
                                arg.Relative ? Add(at, arg.Offset) : arg.Offset);
                break;

            case TeArgKind.Model:
                writer.Int(arg.Field, ModelIndex(arg.Name));
                break;

            case TeArgKind.Decal:
                writer.Int(arg.Field, _precache.PrecacheDecal(arg.Name));
                break;

            case TeArgKind.EffectName:
                writer.Int(arg.Field, _engine.EffectDispatchIndex(arg.Name));
                break;

            case TeArgKind.NoOwner:
                writer.EHandle(arg.Field, EntityHandle.None);
                break;
        }
    }

    /// <summary>Vector has no operator+ that this can lean on; three adds instead.</summary>
    private static Vector Add(Vector at, Vector offset)
        => new(at.X + offset.X, at.Y + offset.Y, at.Z + offset.Z);

    /// <summary>
    /// A model index for something that should already be precached, and the
    /// diagnostic <c>cs_sv_te_fire_e</c>'s <c>TeFireModelIndex</c> carried,
    /// kept because it is the one thing in that command worth more than the
    /// firing.
    ///
    /// <para>
    /// <c>models/gibs/hgibs.mdl</c>, <c>sprites/gunsmoke.vmt</c> and
    /// <c>sprites/bubble.vmt</c> are <c>CTEBreakModel</c>'s,
    /// <c>CTEPhysicsProp</c>'s, <c>CTESprite</c>'s and <c>CTEFizz</c>'s OWN
    /// precaches, which moved to <see cref="TempEnts.Precache"/> when their
    /// <c>te_*.cpp</c> were evicted from <c>PrecacheTempEnts</c>' list. So a
    /// zero index for one of those is not a detail of the demonstration: it is
    /// that precache having lapsed — the exact failure the whole arrangement
    /// exists to avoid, and one whose only other symptom is a missing model at
    /// runtime.
    /// </para>
    /// </summary>
    private int ModelIndex(string model)
    {
        int index = _precache.PrecacheModel(model);

        if (index <= 0 && _complained.Add(model))
        {
            _log.Warning(
                $"[SourceSharp] cs_te_fire: {model} has no model index. If it is one of " +
                "TempEnts.Precache's three, that precache moved to managed code when its " +
                "te_*.cpp was evicted and this is what it looks like when it did not run.\n");
        }

        return index;
    }

    /// <summary>
    /// The filter a demonstration is sent through, by name.
    ///
    /// <para>
    /// BROADCAST IS THE DEFAULT AND THAT IS THE WHOLE POINT. A PVS filter with
    /// no recipients sends nothing and prints nothing, so a station the player
    /// cannot see looks exactly like a table that did not pair — and this pass
    /// puts forty-three effects on one plot which a player is usually not
    /// standing on. Every effect here is fired for the demonstration rather than
    /// for the simulation, so the filter that cannot have zero recipients is the
    /// right default and the others are available by name for anyone who wants
    /// to watch one behave.
    /// </para>
    /// </summary>
    public static TeFilter FilterNamed(string? name, Vector at)
        => name?.ToLowerInvariant() switch
        {
            "pvs" => TeFilters.Pvs(at),
            "pas" => TeFilters.Pas(at),
            _     => TeFilters.Broadcast,
        };
}

/// <summary>
/// Sends one of <see cref="TeDemos"/>' demonstrations through the managed temp
/// entity path. See <see cref="TeFire"/>.
/// </summary>
public interface ITeFire
{
    /// <summary>
    /// Sends <paramref name="demo"/> at <paramref name="at"/>. False when nothing
    /// went: the class is not registered on this side.
    /// </summary>
    bool Send(TeDemo demo, Vector at, TeFilter filter, float delay);
}

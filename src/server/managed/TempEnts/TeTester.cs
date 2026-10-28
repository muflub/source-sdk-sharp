using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// <c>ss_te_tester</c>: one temp entity, fired where this entity stands.
///
/// <para>
/// WHY IT EXISTS. <c>ss_sandbox</c> keeps one example of every entity the mod
/// implements, and <c>SandboxTests</c> fails the build when a
/// <c>[ScriptedEntity]</c> classname has no example in it. A temp entity has no
/// classname and no factory, so nothing in that machinery could see one: the
/// port landed forty-three of them and nothing in the build could demonstrate
/// that any still fires. This is plan 9.1's answer, and it is deliberately the
/// dullest possible one — an ordinary <c>[ScriptedEntity]</c>, so the whole
/// sandbox apparatus applies to it unchanged.
/// </para>
///
/// <para>
/// IT FIRES AT ITS OWN ORIGIN, which is the only interesting thing about it.
/// Forty-three of them across one plot is forty-three distinguishable effects in
/// one screenshot; forty-three firing at a point named in a keyvalue would be a
/// heap, and forty-three each carrying their own coordinates would be a map that
/// has to be re-laid-out by hand every time the station moves.
/// </para>
///
/// <para>
/// WHAT IT KNOWS ABOUT EACH EFFECT IS NOT HERE. <see cref="TeDemos"/> holds one
/// demonstration per class as data, and <c>cs_te_fire</c> and
/// <c>SandboxTeTests</c> read the same book — so the map, the headless gate and
/// the coverage test cannot disagree about what a <c>CTEExplosion</c> looks
/// like.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SsTeTester)]
public sealed class TeTester : ScriptedEntity
{
    private readonly ITeFire _fire;

    public TeTester(ITeFire fire)
    {
        _fire = fire;
    }

    /// <summary>What a map may set on this entity. See <see cref="TeTesterVars"/>.</summary>
    [ScriptVars] public TeTesterVars Vars = new();

    /// <summary>Fired when the effect went; not fired when the class is not registered.</summary>
    public readonly Output OnFired = new();

    /// <summary>
    /// The demonstration this tester stands for, or null when its <c>te</c>
    /// keyvalue names nothing.
    ///
    /// <para>
    /// A NAME NOTHING CLAIMS IS SAID AT SPAWN and not at the first fire. A
    /// mapper's typo would otherwise be a station that looks finished and does
    /// nothing when its button is pressed, which is the exact shape of failure
    /// the coverage rule exists to make impossible. <c>SandboxTeTests</c> catches
    /// it in the generated map before anyone loads it; this catches it in a map
    /// somebody wrote by hand.
    /// </para>
    /// </summary>
    private TeDemo? Demo => TeDemos.ByClass(Vars.Te) ?? TeDemos.BySlug(Vars.Te);

    public override void OnSpawn()
    {
        if (Demo is null)
        {
            Log.Warning(
                $"[SourceSharp] ss_te_tester \"{Name}\" names te \"{Vars.Te}\", which is not a " +
                "temp entity this build registers. It will fire nothing. The names are the " +
                "class names — CTESparks — or the short ones cs_te_fire takes.\n");
        }
    }

    /// <summary>
    /// Fires this tester's one effect.
    ///
    /// <para>
    /// NO CHEAT GATE, unlike the console commands this pass supersedes. A map
    /// input is not a console command: <c>FCVAR_CHEAT</c> exists so a player
    /// cannot conjure effects on a server that did not ask for them, and a map
    /// that placed this entity did ask. The gate stays on <c>cs_te_fire</c>,
    /// where the reasoning applies.
    /// </para>
    /// </summary>
    [ScriptedEvent("Fire")]
    private void OnFire()
    {
        TeDemo? demo = Demo;

        if (demo is null)
            return;

        bool sent = _fire.Send(demo, Origin,
                               TeFire.FilterNamed(Vars.Filter, Origin), Vars.Delay);

        if (sent)
            OnFired.Fire();
    }

    /// <summary>
    /// Fires EVERY tester in the map, not only this one.
    ///
    /// <para>
    /// It is on the entity rather than only on the command because the station's
    /// reset button has to reach it: a button is the only way a person standing
    /// in the map can set the whole plot off, and a relay wired to forty-three
    /// separate <c>Fire</c> inputs would be forty-three connections the
    /// generator has to keep in step with the book.
    /// </para>
    /// </summary>
    [ScriptedEvent("FireAll")]
    private void OnFireAll()
    {
        foreach (TeTester tester in AllTesters())
            tester.OnFire();
    }

    /// <summary>
    /// Every <c>ss_te_tester</c> alive, this one included.
    ///
    /// <para>
    /// Found through the entity registry rather than kept in a static list, so a
    /// tester removed at runtime stops being fired and a level change does not
    /// leave stale entries behind.
    /// </para>
    /// </summary>
    private IEnumerable<TeTester> AllTesters()
    {
        foreach (EntityHandle handle in Entities.FindAllByClassname(EntityNames.SsTeTester))
        {
            if (FindByHandle<TeTester>(handle) is { } tester)
                yield return tester;
        }
    }
}

/// <summary>
/// The map keys <see cref="TeTester"/> declares.
/// </summary>
[ScriptVars]
public partial class TeTesterVars
{
    /// <summary>
    /// Which temp entity this tester fires: the class name both sides pair on,
    /// <c>"CTESparks"</c>, or the short name <c>cs_te_fire</c> takes.
    ///
    /// <para>
    /// THE CLASS NAME AND NOT AN INDEX, because the coverage rule reads this
    /// value straight out of the compiled map's entity lump and compares it with
    /// the <c>[TempEntity]</c> declarations. An index would have been shorter and
    /// would have made the gate compare a number with a list order.
    /// </para>
    /// </summary>
    public string Te { get; set; } = "";

    /// <summary>
    /// Which recipient filter to send through: <c>broadcast</c> (the default),
    /// <c>pvs</c> or <c>pas</c>.
    ///
    /// <para>
    /// It exists so the PVS path is demonstrable at all — every effect fired
    /// broadcast would leave the filter this build's callers actually use
    /// untested from the map — and it defaults to broadcast because a filter
    /// with no recipients sends nothing and prints nothing, which is
    /// indistinguishable from a table that did not pair.
    /// </para>
    /// </summary>
    public string Filter { get; set; } = "";

    /// <summary>
    /// Seconds the client holds the effect before playing it.
    ///
    /// <para>
    /// The station stagger. Forty-three effects in one frame is one flash; the
    /// generator gives each tester a delay a fraction of a second after its
    /// neighbour, so one button press sweeps the plot and every effect can be
    /// told from the one beside it. <c>cs_te_fire</c> ignores this and fires with
    /// no delay, because a headless gate reads the client's counters straight
    /// after and a stagger would make it read them too early.
    /// </para>
    /// </summary>
    public float Delay { get; set; }
}

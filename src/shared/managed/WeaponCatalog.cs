using System.Collections.Immutable;
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// Rows a mod APPENDS to the weapon table.
///
/// <para>
/// The core's rows are declared once in <see cref="WeaponTypes.All"/> and a mod
/// adds more through this hook; the catalogue both sides read is the core rows
/// followed by every source's rows in a deterministic order, because
/// <see cref="WeaponType"/>'s position IS the wire format — the carried weapon
/// networks WHICH weapon it is as that number (see
/// <see cref="WeaponNetState.WeaponTypeId"/>), so server and client must build
/// the identical list from the identical assembly set.
/// </para>
///
/// <para>
/// A source is found the way every other mod service is: marked
/// <c>[SideService]</c>, collected from the side's container. Its rows are read
/// once, when the side first asks its catalogue a question.
/// </para>
/// </summary>
public interface IWeaponTypeSource
{
    /// <summary>
    /// The rows to append, in the order this source wants them. Names are
    /// compared case-insensitively against the rows already in the table, and a
    /// row whose name another source or the core already claimed is skipped
    /// with a warning — two indices for one classname is the wire-format break
    /// this table has always been arranged against.
    /// </summary>
    IEnumerable<WeaponType> Rows();
}

/// <summary>
/// Rows a mod APPENDS to the ammunition table. The same rule and the same
/// reason as <see cref="IWeaponTypeSource"/>: an ammo type is networked as its
/// index (see <see cref="AmmoCatalog.WireIndex"/>), so both sides must build
/// the identical list.
/// </summary>
public interface IAmmoTypeSource
{
    /// <summary>The rows to append, in the order this source wants them.</summary>
    IEnumerable<AmmoType> Rows();
}

/// <summary>
/// HOW the catalogue sources are ordered — the rule the wire format depends on.
///
/// <para>
/// Sources arrive in container registration order, which depends on the order
/// the assemblies were scanned and registered; two mods loaded in the other
/// order would otherwise append their rows in the other order and hand each
/// other's indices to the wire. Sorting by simple assembly name (ordinal) and
/// keeping registration order within one assembly (a stable sort) makes the
/// resulting table depend on the ASSEMBLY SET alone — which is what both sides
/// agree on, since they load the same <c>mods.yaml</c>.
/// </para>
/// </summary>
internal static class HookOrder
{
    /// <summary>
    /// <paramref name="sources"/> ordered by simple assembly name, ties broken
    /// by the order they were registered in.
    /// </summary>
    public static IReadOnlyList<T> Sorted<T>(IEnumerable<T> sources)
        where T : class
        => sources
            .OrderBy(s => s.GetType().Assembly.GetName().Name ?? string.Empty, StringComparer.Ordinal)
            .ToArray();
}

/// <summary>
/// The weapon table one side actually reads: the core rows, then the mods'.
///
/// <para>
/// THE INDEX IS THE WIRE FORMAT, and this is the object that owns it now. Both
/// sides build one at startup from the same core table and the same registered
/// <see cref="IWeaponTypeSource"/> hooks, and everything — giving, switching,
/// dropping, predicting, the networked <c>WeaponTypeId</c> — resolves through
/// this instance rather than through a static, so there is exactly one list per
/// side and it is the same list on both.
/// </para>
///
/// <para>
/// The core rows keep the indices they had as <see cref="WeaponTypes.All"/>'s
/// positions: mod rows only ever APPEND. A mod cannot renumber the wire, which
/// is the same rule the table has always lived by, now enforced by construction.
/// </para>
/// </summary>
public sealed class WeaponCatalog
{
    // Built on the FIRST read, not at construction — for the same reason
    // Hooks<T> is lazy: the container is not complete while the module is still
    // registering, and a catalogue that collected its sources too early would be
    // a mod's rows silently missing from the wire table on one side only.
    private readonly Func<IReadOnlyList<IWeaponTypeSource>>? _askSources;
    private readonly ILog? _log;
    private Built? _built;

    /// <summary>
    /// The stock table alone — the core rows, and nothing appended. What a
    /// mod-less game behaves as, and what a fact that pins the wire format
    /// compares against, because "the same indices as the core" is only
    /// checkable against a list built with no sources in it.
    /// </summary>
    public static WeaponCatalog Stock() => new(null, null);

    /// <summary>
    /// The table for one side: the core rows plus the sources' rows in
    /// deterministic order, applied the first time anybody reads a row.
    /// </summary>
    /// <param name="askSources">
    /// Asks for the registered <see cref="IWeaponTypeSource"/> hooks — in any
    /// order; the order they are applied in is this class's, not the caller's.
    /// </param>
    /// <param name="log">Where a refused row is reported; never silent.</param>
    public WeaponCatalog(Func<IReadOnlyList<IWeaponTypeSource>>? askSources, ILog? log = null)
    {
        _askSources = askSources;
        _log = log;
    }

    private Built Table => _built ??= Build();

    /// <summary>Every row, in index order: the table itself.</summary>
    public IReadOnlyList<WeaponType> All => Table.Rows;

    /// <summary>How many weapons this side has, core and mods together.</summary>
    public int Count => Table.Rows.Length;

    /// <summary>
    /// The profile for a classname, or null when there is none.
    /// Case-insensitive: classnames come from the engine and from map keyvalues,
    /// and those do not agree about case as reliably as one would like.
    /// </summary>
    public WeaponType? For(string className)
        => className.Length > 0 && Table.ByName.TryGetValue(className, out int at) ? Table.Rows[at] : null;

    /// <summary>
    /// A weapon's position — its WIRE INDEX — or -1. Case-insensitive, like the
    /// classname lookup beside it.
    /// </summary>
    public int IndexOf(string className)
        => className.Length > 0 && Table.ByName.TryGetValue(className, out int at) ? at : -1;

    /// <summary>The row a networked index names, or null for -1 and anything stale.</summary>
    public WeaponType? ByIndex(int index)
        => index >= 0 && index < Table.Rows.Length ? Table.Rows[index] : null;

    /// <summary>Whether this side's table has a weapon of this classname.</summary>
    public bool Contains(string className) => IndexOf(className) >= 0;

    /// <summary>
    /// Hands the table to the native side, which is where the stock attack path
    /// reads fire rates. Called on both sides — the client predicts a shot and
    /// needs the same fire rate to predict it with — and it registers the MOD's
    /// rows too: a native table that stopped at the core rows would leave every
    /// mod weapon firing at whatever the shell's default rate is.
    /// </summary>
    public void Register(string side, IEngineWeapons weapons, ILog log)
    {
        foreach (var weapon in Table.Rows)
            weapons.RegisterWeaponType(weapon.Name, weapon.Interval, weapon.Spread, weapon.Pellets);

        log.Msg($"[SourceSharp/{side}] registered {Count} weapon type(s)\n");
    }

    private Built Build()
    {
        var rows = WeaponTypes.All.ToBuilder();
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rows.Count; i++)
            byName[rows[i].Name] = i;

        if (_askSources is not null)
        {
            foreach (var source in HookOrder.Sorted(_askSources()))
            {
                foreach (var row in source.Rows())
                {
                    if (row.Name.Length == 0)
                    {
                        _log?.Warning($"[SourceSharp] {source.GetType().FullName} offered a weapon row with no name; skipped\n");
                        continue;
                    }

                    // Claimed by the core or by an earlier source: the FIRST
                    // owner keeps the index. Two rows for one classname is a
                    // weapon the client resolves to whichever of the two it
                    // happens to find — the silence this guards.
                    if (byName.ContainsKey(row.Name))
                    {
                        _log?.Warning($"[SourceSharp] {source.GetType().FullName} offered '{row.Name}', " +
                                      "which the table already has; the existing row keeps its index\n");
                        continue;
                    }

                    byName[row.Name] = rows.Count;
                    rows.Add(row);
                }
            }
        }

        return new Built(rows.ToImmutable(), byName);
    }

    /// <summary>The rows and their classname index, materialised on first read.</summary>
    private readonly record struct Built(ImmutableArray<WeaponType> Rows, Dictionary<string, int> ByName);
}

/// <summary>
/// The ammunition table one side actually reads: the core rows, then the mods'.
/// The partner of <see cref="WeaponCatalog"/>, built the same way, for the same
/// reason — a weapon's ammo type crosses the wire as an index into this list
/// (see <see cref="WireIndex"/>).
/// </summary>
public sealed class AmmoCatalog
{
    private readonly Func<IReadOnlyList<IAmmoTypeSource>>? _askSources;
    private readonly ILog? _log;
    private Built? _built;

    /// <summary>The stock table alone — the core rows, and nothing appended.</summary>
    public static AmmoCatalog Stock() => new(null, null);

    /// <inheritdoc cref="WeaponCatalog.WeaponCatalog(Func{IReadOnlyList{IWeaponTypeSource}}, ILog)"/>
    public AmmoCatalog(Func<IReadOnlyList<IAmmoTypeSource>>? askSources, ILog? log = null)
    {
        _askSources = askSources;
        _log = log;
    }

    private Built Table => _built ??= Build();

    /// <summary>Every row, in index order.</summary>
    public IReadOnlyList<AmmoType> All => Table.Rows;

    /// <summary>How many pools this side has, core and mods together.</summary>
    public int Count => Table.Rows.Length;

    /// <summary>
    /// The row for an ammo name, or null. Case-insensitive because the weapon
    /// scripts this table replaced spelled "grenade" and "rpg_round" in
    /// lowercase while the table capitalises, and a miss here is a pool nothing
    /// can ever fill.
    /// </summary>
    public AmmoType? For(string name)
        => name.Length > 0 && Table.ByName.TryGetValue(name, out int at) ? Table.Rows[at] : null;

    /// <summary>
    /// An ammo type's index as the client's native ammo table numbers it, which
    /// is what <c>AmmoDenied</c> carries: registration order counting from ONE,
    /// because stock's CAmmoDef starts at 1 ("client assumes zero is an invalid
    /// ammo type", ammodef.cpp) and the client registers this list in order.
    /// -1 for a name not in the table.
    /// </summary>
    public int WireIndex(string name)
    {
        int at = name.Length > 0 && Table.ByName.TryGetValue(name, out int found) ? found : -1;
        return at < 0 ? -1 : at + 1;
    }

    /// <summary>
    /// Fills the native ammo table. Called at client init only — server.so keeps
    /// no ammo table since nothing there read one (plan_server_novalve.md P5)
    /// and its slot refuses. A weapon naming an unregistered type resolves to
    /// an ammo index of -1, so this runs before anything can be given.
    /// </summary>
    public void Register(string side, IEngineWeapons weapons, ILog log)
    {
        foreach (var ammo in Table.Rows)
        {
            weapons.RegisterAmmoType(ammo.Name, ammo.DamageType, ammo.MaxCarry,
                                     ammo.TracerType, ammo.DamageForce, ammo.Damage);
        }

        log.Msg($"[SourceSharp/{side}] registered {Count} ammo type(s)\n");
    }

    private Built Build()
    {
        var rows = AmmoTypes.All.ToBuilder();
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rows.Count; i++)
            byName[rows[i].Name] = i;

        if (_askSources is not null)
        {
            foreach (var source in HookOrder.Sorted(_askSources()))
            {
                foreach (var row in source.Rows())
                {
                    if (row.Name.Length == 0)
                    {
                        _log?.Warning($"[SourceSharp] {source.GetType().FullName} offered an ammo row with no name; skipped\n");
                        continue;
                    }

                    if (byName.ContainsKey(row.Name))
                    {
                        _log?.Warning($"[SourceSharp] {source.GetType().FullName} offered ammo '{row.Name}', " +
                                      "which the table already has; the existing row keeps its index\n");
                        continue;
                    }

                    byName[row.Name] = rows.Count;
                    rows.Add(row);
                }
            }
        }

        return new Built(rows.ToImmutable(), byName);
    }

    /// <summary>The rows and their name index, materialised on first read.</summary>
    private readonly record struct Built(ImmutableArray<AmmoType> Rows, Dictionary<string, int> ByName);
}

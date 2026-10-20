//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;
using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>
/// Marks a class as a console variable. Discovered at module init, exactly like
/// <see cref="ConCommandAttribute"/>, and bound to a fresh instance.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ConVarAttribute : Attribute
{
    public ConVarAttribute(string name, string defaultValue)
    {
        Name = name;
        DefaultValue = defaultValue;
    }

    public string Name { get; }
    public string DefaultValue { get; }
    public string Help { get; init; } = string.Empty;
    public ConVarFlags Flags { get; init; } = ConVarFlags.None;

    /// <summary>
    /// The lowest value the engine will let this hold. NaN, the default, means
    /// no bound at all.
    ///
    /// NaN rather than a nullable float because an attribute argument has to be
    /// a constant expression and float? is not one. It reads oddly and it is the
    /// only spelling available; what matters is that zero stays a usable bound,
    /// which it would not be if "unset" were spelled as 0.
    ///
    /// This is the ENGINE's clamp, not a check made afterwards. A convar with a
    /// minimum clamps on the way in from wherever the write came — the console,
    /// a config file, a replicated update — so OnChanged only ever sees a value
    /// that is already in range.
    /// </summary>
    public float Min { get; init; } = float.NaN;

    /// <summary>The highest value the engine will let this hold. NaN for none.</summary>
    public float Max { get; init; } = float.NaN;

    /// <summary>
    /// The server-browser tag this convar puts in <c>sv_tags</c> while its
    /// value differs from its default, or null for none. The server applies
    /// it (SourceSharp.Server.ServerTags), by the rule the engine applied to
    /// the game's tagged-convar list.
    /// </summary>
    public string? Tag { get; init; }
}

/// <summary>
/// How the registry registers a convar and a bound convar sets and reads it:
/// the engine in the game, a fake in a test (the Engine table is process-wide).
/// </summary>
/// <summary>
/// <b>Who owns a console variable's name</b> (the user's rule, 2026-09-29): a
/// managed declaration never aliases a convar the engine defines. Each game
/// DLL's registration ORs in its own identifier -- FCVAR_GAMEDLL for server.so,
/// FCVAR_CLIENTDLL for client.so -- and nothing else sets either, so a variable
/// carrying neither was declared by the engine or one of its modules (material
/// system, vgui, ...). A client declaration whose name the server already
/// registered is not refused: that is a replicated pair. Native's
/// convar_ownership.h decided this until plan_server_native_v2 T4e.
/// </summary>
public static class ConVarOwnership
{
    /// <summary>
    /// True when a variable the engine holds, with these flags, belongs to
    /// neither game DLL. <paramref name="existingFlags"/> below zero is "the
    /// engine holds no such variable", which nobody owns yet.
    /// </summary>
    public static bool OwnedByEngine(int existingFlags)
        => existingFlags >= 0
        && (existingFlags & (int)(ConVarFlags.GameDll | ConVarFlags.ClientDll)) == 0;
}

internal interface IConVarBackend
{
    /// <summary>The id of the native registration, or below zero when refused.</summary>
    int Register(string name, string defaultValue, string help, int flags, float? min, float? max);

    /// <summary>
    /// The flags of the variable the engine already holds under a name, or -1
    /// when it holds none: what <see cref="ConVarOwnership.OwnedByEngine"/> reads.
    /// A backend with no engine behind it holds nothing.
    /// </summary>
    int ExistingFlags(string name) => -1;
    void Set(int id, string value);
    string Get(int id);

    /// <summary>The engine's current value of a name, or null when it has none.</summary>
    string? ValueOf(string name);
}

/// <summary>
/// The engine, through one side's console area (plan_abi_hotreload D8): the
/// registry's own, never whichever side is active.
/// </summary>
internal sealed class EngineConVarBackend : IConVarBackend
{
    private readonly IEngineConsole _console;

    public EngineConVarBackend(IEngineConsole console)
        => _console = console ?? throw new ArgumentNullException(nameof(console));

    public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        => _console.RegisterConVar(name, defaultValue, help, flags, min, max);

    public int ExistingFlags(string name) => _console is IConVarFlagReader flags ? flags.FindConVarFlags(name) : -1;
    public void Set(int id, string value) => _console.SetConVarValue(id, value);
    public string Get(int id) => _console.GetConVarValue(id);
    public string? ValueOf(string name) => NativeConsole.ValueOf(_console, name);
}

/// <summary>
/// Non-generic base, so the registry can hold ConVars of differing types.
/// Do not derive from this directly — derive from a typed
/// <see cref="ConVar{T}"/> such as <see cref="IntConVar"/>.
/// </summary>
public abstract class ConVarBase
{
    private int _id = -1;

    /// <summary>
    /// This convar's log: the registry's that declared it (statics to zero:
    /// it was the static Log), set as the registry creates it. One built on its
    /// own prints to standard error.
    /// </summary>
    protected internal ILog Log
    {
        get => _log ??= new StandardErrorLog();
        internal set => _log = value;
    }

    private ILog? _log;

    /// <summary>Console name, filled in at registration.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// What the class declared as its default, and the help text and flags that
    /// went with it.
    ///
    /// Kept on the instance rather than re-read from the attribute on demand:
    /// the development API lists every convar on each poll, and reflecting over
    /// attributes to answer "what is this one's default" would do that work
    /// again every time for something that cannot change after registration.
    /// </summary>
    public string DefaultValue { get; private set; } = string.Empty;

    /// <summary>Help text as declared. Empty if the class gave none.</summary>
    public string Help { get; private set; } = string.Empty;

    /// <summary>Flags as declared.</summary>
    public ConVarFlags Flags { get; private set; } = ConVarFlags.None;

    /// <summary>
    /// The bounds the engine clamps to, or null where there is none. Kept so a
    /// listing can say what a value is allowed to be — which appears nowhere in
    /// a convar's name, its help or its flags, and which a tool offering an edit
    /// otherwise invites someone to fall foul of.
    /// </summary>
    public float? MinValue { get; private set; }

    /// <inheritdoc cref="MinValue"/>
    public float? MaxValue { get; private set; }

    /// <summary>
    /// Whether the engine accepted this convar. A refused one still holds its
    /// declared default and still reads back — see <see cref="ConVarRegistry"/> —
    /// but writing to it goes nowhere, and a tool showing the value ought to say
    /// so rather than present a setting that silently will not take.
    /// </summary>
    public bool IsRegistered => _id >= 0;

    /// <summary>The kind the typed layer parses into. "string", "int", "float" or "bool".</summary>
    public virtual string TypeName => "string";

    /// <summary>Raw engine string. Typed subclasses expose a parsed value instead.</summary>
    /// <summary>
    /// The value as the engine holds it, as text. Public because a caller may
    /// want the value without knowing the convar's type — the development API
    /// reads every convar through this.
    /// </summary>
    public string RawValue { get; private set; } = string.Empty;

    /// <summary>The declared server-browser tag (<see cref="ConVarAttribute.Tag"/>), or null.</summary>
    public string? Tag { get; private set; }

    /// <summary>
    /// Told after every change, whatever its source; the registry sets it for
    /// a tagged convar. On the convar rather than keyed by id so a convar the
    /// engine refused (id -1, as in every test) is heard as well.
    /// </summary>
    internal Action<ConVarBase>? ChangedHook;

    /// <summary>
    /// How this convar reaches its native registration: its registry's engine,
    /// or a test's fake. Null until a registry binds it, which is also the
    /// only way it gets an id to push with.
    /// </summary>
    internal IConVarBackend? Backend;

    /// <summary>
    /// Pushes a raw value to the engine, and makes sure the cache has heard it.
    ///
    /// <para>
    /// The engine's change callback normally updates the cache from inside the
    /// set. It does not while the managed side is still initialising: native
    /// drops every change until the module reports loaded
    /// (SourceSharp_OnConVarChanged), so a write made DURING initialisation —
    /// MovementTuning.Apply's — reached the engine and left this cache at the
    /// declared default. sv_gravity read 600 in the engine and 800 here from
    /// P2.3a on, and anything reading the convar in C# read 800.
    /// </para>
    ///
    /// <para>
    /// So the engine's value is read back, and a change the callback did not
    /// deliver is delivered here. It is the ENGINE's text — a clamp or a
    /// reformat is what gets cached — and a change the callback did deliver is
    /// not delivered twice.
    /// </para>
    /// </summary>
    private protected void SetRaw(string value)
    {
        if (_id < 0 || Backend is null)
            return;

        Backend.Set(_id, value);

        string now = Backend.Get(_id);
        if (!string.Equals(now, RawValue, StringComparison.Ordinal))
            NotifyChanged(now, RawValue);
    }

    /// <summary>
    /// Binds a declaration to its native registration.
    ///
    /// <para>
    /// <paramref name="engineValue"/> is the value the ENGINE actually holds
    /// right now, which is not always the declared default and is the reason
    /// this parameter exists. The cache used to be seeded with the default
    /// unconditionally, and for an archived convar that is WRONG FROM THE FIRST
    /// FRAME: <c>config.cfg</c> is exec'd before the managed module registers,
    /// so the engine is already holding the operator's value while managed code
    /// reports the declared default. Nothing corrects it either, because the
    /// change callback only fires on a CHANGE — setting the convar to the value
    /// the engine already has is a no-op in
    /// <c>ConVar::ChangeStringValue</c> and notifies nobody.
    /// </para>
    ///
    /// <para>
    /// Measured, in a live <c>dm_lockdown</c>: run 1 set
    /// <c>sv_unlockedchapters</c> to 7, the engine archived it, and run 2 came
    /// up with the engine at 7 and managed at its declared 1 — then "setting"
    /// it to 7 changed nothing and left managed at 1 forever.
    /// </para>
    ///
    /// <para>
    /// Null means "no engine to ask" — a refused convar, or a test with no
    /// native side — and the declared default is then the only answer there is.
    /// <see cref="DefaultValue"/> is always the DECLARED one either way: the
    /// two were the same field before and they are different questions.
    /// </para>
    /// </summary>
    internal void Bind(int id, string name, string initialValue,
                       string help = "", ConVarFlags flags = ConVarFlags.None,
                       float? min = null, float? max = null,
                       string? engineValue = null, string? tag = null)
    {
        _id = id;
        Name = name;
        RawValue = engineValue ?? initialValue;
        DefaultValue = initialValue;
        Help = help;
        Flags = flags;
        MinValue = min;
        MaxValue = max;
        Tag = tag;
        OnRawChanged(RawValue, RawValue);
    }

    internal void NotifyChanged(string newValue, string oldValue)
    {
        RawValue = newValue;
        OnRawChanged(newValue, oldValue);
        ChangedHook?.Invoke(this);
    }

    /// <summary>Implemented by the typed layer to re-parse the cached value.</summary>
    private protected abstract void OnRawChanged(string newValue, string oldValue);
}

/// <summary>
/// A ConVar with a typed value. The parsed value is cached and refreshed by the
/// engine's change callback, so reads are free — ConVars are often read per tick.
/// </summary>
public abstract class ConVar<T> : ConVarBase
{
    private T _value = default!;

    /// <summary>Current value, already parsed.</summary>
    public T Value => _value;

    /// <summary>Sets the value through the engine.</summary>
    public void SetValue(T value) => SetRaw(Format(value));

    /// <summary>Parses the engine's string form. Must not throw — return a fallback.</summary>
    protected abstract T Parse(string raw);

    /// <summary>Renders the value in the engine's string form.</summary>
    protected abstract string Format(T value);

    /// <summary>Called after the value changes, from any source.</summary>
    protected virtual void OnChanged(T oldValue, T newValue) { }

    private protected sealed override void OnRawChanged(string newValue, string oldValue)
    {
        T parsedOld = _value;
        _value = Parse(newValue);
        OnChanged(parsedOld, _value);
    }

    public override string ToString() => $"{Name} = {Format(_value)}";
}

/// <summary>String-valued ConVar.</summary>
public abstract class StringConVar : ConVar<string>
{
    public override string TypeName => "string";

    protected override string Parse(string raw) => raw;
    protected override string Format(string value) => value;
}

/// <summary>Integer-valued ConVar. Unparseable values read as 0.</summary>
public abstract class IntConVar : ConVar<int>
{
    public override string TypeName => "int";

    protected override int Parse(string raw)
        => ParseEngineInt(raw);

    /// <summary>
    /// The engine's m_nValue: the integer when the text is one, otherwise
    /// (int)atof, so a declared "1.0" reads 1 (ConVar::InternalSetValue).
    /// </summary>
    internal static int ParseEngineInt(string raw)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return v;
        float f = FloatConVar.Atof(raw);
        return float.IsFinite(f) && f > int.MinValue && f < int.MaxValue ? (int)f : 0;
    }

    protected override string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Float-valued ConVar. Unparseable values read as 0.</summary>
public abstract class FloatConVar : ConVar<float>
{
    public override string TypeName => "float";

    // The engine's own read: atof, which takes the longest numeric prefix and
    // stops there -- stock declares "0.1f", "600.0f", "384.0f" and means 0.1,
    // 600 and 384. A whole-string parse read those as 0.
    protected override float Parse(string raw) => Atof(raw);

    // Through a double, as atof is (strtod, then the carrier's float cast):
    // a straight float parse rounds once and can land one ulp away.
    internal static float Atof(string raw)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            return (float)v;
        for (int end = raw.Length - 1; end > 0; end--)
        {
            if (double.TryParse(raw.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out double prefix))
                return (float)prefix;
        }

        return 0f;
    }

    protected override string Format(float value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Boolean ConVar. Any non-zero number is true, matching Source.</summary>
public abstract class BoolConVar : ConVar<bool>
{
    public override string TypeName => "bool";

    // GetBool is m_nValue != 0: "1.0" is true, "0.5" false.
    protected override bool Parse(string raw) => IntConVar.ParseEngineInt(raw) != 0;

    protected override string Format(bool value) => value ? "1" : "0";
}

/// <summary>
/// What the shell should do with one <c>ConCommandBase</c> at registration —
/// Part 0a #3.
///
/// <para>
/// <b>The shell does one of stock's three steps and this is the other
/// two.</b> <c>CGameDLL_ConVarAccessor</c> (game.cpp:60-98) remembers a
/// replicated ConVar's unlinked default, links the command, then applies any
/// <c>+convar value</c> from the command line or, failing that, restores the
/// remembered default. Linking is engine plumbing and stays native. The
/// other two ask "may this declaration take a command-line value, and what
/// is its declared default" — which is policy about a declaration, and every
/// declaration lives here.
/// </para>
///
/// <para>
/// Dropping those two steps is not a cosmetic loss: <c>+sv_cheats 1</c> and
/// every <c>+convar value</c> a server operator passes is silently ignored,
/// and replicated ConVars link holding whatever they happened to contain.
/// It reads as a config bug for the lifetime of the port and points nowhere
/// near the function responsible.
/// </para>
/// </summary>
/// <param name="Name">The console name, as declared.</param>
/// <param name="DefaultValue">What the declaration says the value starts at.</param>
/// <param name="Flags">The flags as declared.</param>
public readonly record struct ConVarCommandLinePolicy(
    string Name, string DefaultValue, ConVarFlags Flags)
{
    /// <summary>
    /// Whether a command-line value may set this declaration.
    ///
    /// <para>
    /// False only for <see cref="ConVarFlags.Unregistered"/>, which says the
    /// convar is never added to the engine's list at all. Applying a
    /// command-line value to one would write a value that
    /// <c>cvar-&gt;FindVar</c> can never find and no config can ever read
    /// back — a setting that appears to take and does nothing.
    /// </para>
    ///
    /// <para>
    /// Cheat-flagged convars ARE allowed one, and that is stock's behaviour
    /// rather than an oversight: <c>+sv_cheats 1</c> is how a cheat build is
    /// started, and <c>sv_cheats</c> gates the others at use rather than at
    /// registration.
    /// </para>
    /// </summary>
    public bool MayTakeCommandLineValue => (Flags & ConVarFlags.Unregistered) == 0;

    /// <summary>Whether both sides must hold the same value for this one.</summary>
    public bool IsReplicated => (Flags & ConVarFlags.Replicated) != 0;

    /// <summary>
    /// The value to restore when the command line named nothing, or null
    /// when there is nothing to restore.
    ///
    /// <para>
    /// Non-null ONLY for a replicated declaration, which is stock's rule and
    /// not a simplification of it. Stock re-applies the default to a
    /// replicated ConVar after linking so that the SERVER's value wins: the
    /// original bug was a shared convar whose value came from inside an
    /// <c>#if defined(_DEBUG)</c> block, so a debug game DLL against a
    /// release client took the client's number. Doing the same for an
    /// unreplicated convar would clobber anything a config had already set.
    /// </para>
    /// </summary>
    public string? ReplicatedDefault => IsReplicated ? DefaultValue : null;
}

/// <summary>
/// Discovers <see cref="ConVarAttribute"/> classes, registers each with the
/// engine and routes change notifications back.
///
/// IMPORTANT: a ConVar wants declaring on ONE side. Discovery reads every
/// loaded assembly, so a ConVar in SourceSharp.Shared is now found from both
/// ALCs and registered from both DLLs — and Source rejects the same name from
/// client and server ("Parent cvar in server.dll not allowed"), the same trap
/// the verbosity ConVar hit in Part 7. The refusal is survivable rather than
/// silent (see below: the instance binds to its declared default with an id of
/// -1 and says so), but it is still a bug. Declare ConVars in the Client or
/// Server assembly, or give each side a distinct name.
/// </summary>
public sealed class ConVarRegistry
{
    private readonly Dictionary<int, ConVarBase> _byId = new();

    // Every declared ConVar, registered or not. _byId only holds the ones the
    // engine accepted, since it exists to route change notifications by id.
    private readonly List<ConVarBase> _all = new();

    //-------------------------------------------------------------------------
    // Part 0a #3's managed half: what the shell's RegisterConCommandBase needs
    // to know about a declaration and cannot work out for itself.
    //
    // SEPARATE FROM _all, and not an afterthought. A declaration is recorded
    // here BEFORE the native ConVar exists, because the shell asks about it
    // during the construction of that ConVar -- see RegisterAll. _all is filled
    // afterwards, from an instance that has been bound to its engine id, so
    // answering out of _all would answer "I have never heard of it" to every
    // managed convar at exactly the moment the question is asked.
    //
    // Case-insensitive, matching ValueOf and the engine's own convar lookup.
    //-------------------------------------------------------------------------
    private readonly Dictionary<string, ConVarCommandLinePolicy> _policies =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IConVarBackend _backend;

    private static string SideOf(string assembly) =>
        assembly.EndsWith(".Client", StringComparison.Ordinal) ? ".Client"
        : assembly.EndsWith(".Server", StringComparison.Ordinal) ? ".Server" : string.Empty;

    private static bool OtherSides(string a, string b)
    {
        string sa = SideOf(a), sb = SideOf(b);
        return sa.Length > 0 && sb.Length > 0 && sa != sb;
    }

    // Which assembly declared each name (the core's or a mod's), for the
    // duplicate refusal. Case-insensitive, as the engine's lookup is.
    private readonly Dictionary<string, Type> _owners = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A registry over one side's console (plan_abi_hotreload D8).</summary>
    public static ConVarRegistry For(IEngineConsole console) => new(new EngineConVarBackend(console), new ConsoleLog(console));

    /// <param name="backend">Where the declarations go.</param>
    /// <param name="log">
    /// Where a declaration that cannot be made is said (statics to zero);
    /// standard error for a fact with no console.
    /// </param>
    internal ConVarRegistry(IConVarBackend backend, ILog? log = null)
    {
        _backend = backend;
        _log = log ?? new StandardErrorLog();
    }

    private readonly ILog _log;

    /// <summary>
    /// Declares every [ConVar] class in <paramref name="assembly"/>. Throws
    /// after the walk, naming each one, when any declares a name the engine
    /// defines — a declaration to delete, not a state to run in.
    /// </summary>
    public int RegisterAll(Assembly assembly) => RegisterTypes(LoadedAssemblies.Types(assembly, _log));

    /// <summary><see cref="RegisterAll"/> over an explicit list of types; a test's seam.</summary>
    internal int RegisterTypes(IEnumerable<Type> types)
    {
        int count = 0;
        var engineOwned = new List<string>();
        var duplicates = new List<string>();

        foreach (var type in types)
        {
            var attr = type.GetCustomAttribute<ConVarAttribute>();
            if (attr is null)
                continue;

            if (type.IsAbstract || !typeof(ConVarBase).IsAssignableFrom(type))
            {
                _log.Warning($"[SourceSharp] [ConVar({attr.Name})] on {type.Name}, which is abstract or not a ConVarBase; skipped\n");
                continue;
            }

            // ONE DECLARATION PER NAME, across the core and every mod the side
            // loads: a second one would bind a second instance to the same
            // engine convar, and which of the two a reader got would depend on
            // discovery order. Refused, loudly, after the walk.
            // The two sides are two DLLs with a console each: a client and a
            // server declaration of one name are not a conflict (a process that
            // loads both, the cross-side tests, sees both).
            // The same declaration met again (one registry walked twice, as a
            // cross-side fact does over a shared test assembly) is the one
            // already bound, not a second.
            string owner = type.Assembly.GetName().Name ?? "?";
            if (_owners.TryGetValue(attr.Name, out Type? firstType))
            {
                if (firstType == type)
                    continue;
                string first = firstType.Assembly.GetName().Name ?? "?";
                if (!OtherSides(first, owner))
                {
                    duplicates.Add($"'{attr.Name}' ({first} and {owner}: {type.FullName})");
                    continue;
                }
            }
            _owners.TryAdd(attr.Name, type);

            if (Activator.CreateInstance(type) is not ConVarBase instance)
            {
                _log.Warning($"[SourceSharp] could not instantiate convar class {type.Name}; skipped\n");
                continue;
            }

            instance.Log = _log;

            // NaN is how an attribute spells "no bound"; every other float,
            // zero included, is a real one.
            float? min = float.IsNaN(attr.Min) ? null : attr.Min;
            float? max = float.IsNaN(attr.Max) ? null : attr.Max;

            // DECLARED BEFORE IT IS REGISTERED, and the order is load-bearing
            // rather than tidy.
            //
            // Engine.RegisterConVar constructs a real native ConVar, and a
            // ConVar constructed after ConVar_Register has already run does not
            // wait in a list: ConCommandBase::Init hands it straight to the
            // registered accessor, which is the shell's RegisterConCommandBase,
            // which asks THIS registry what the declaration allows. Recorded
            // after the call instead, the shell would be asking about a convar
            // the registry had not heard of yet, and every single managed
            // declaration would take the unknown-name path -- which is the one
            // path that looks like working code.
            _policies[attr.Name] = new ConVarCommandLinePolicy(
                attr.Name, attr.DefaultValue, attr.Flags);

            // A NAME THE ENGINE OWNS IS REFUSED, NOT ALIASED (the user's rule,
            // 2026-09-29), decided here since plan_server_native_v2 T4e: native
            // answers the flags of what it already holds under the name, and
            // a variable carrying neither DLL's bit is the engine's own. No
            // carrier is built; the walk's end turns the list into a startup
            // failure naming every class.
            if (ConVarOwnership.OwnedByEngine(_backend.ExistingFlags(attr.Name)))
            {
                _log.Warning($"[SourceSharp] C# declared convar '{attr.Name}', which the engine defines: refused. "
                           + "Read the engine's by name instead of declaring it.\n");
                engineOwned.Add($"'{attr.Name}' ({type.FullName})");
                continue;
            }

            int id = _backend.Register(attr.Name, attr.DefaultValue, attr.Help,
                                       unchecked((int)attr.Flags), min, max);

            // A refused ConVar is still bound, to its declared default and an id
            // of -1. Dropping it would make Get<T>() return null and the policy
            // reading it silently fall back to whatever its own null-coalesce
            // says — which is a different value from the declared default, and
            // impossible to spot from the outside. Bound-but-unregistered at
            // least behaves as declared.
            //
            // This is also what makes the rules testable without an engine.
            if (id < 0)
                _log.Warning($"[SourceSharp] engine refused ConVar '{attr.Name}'; " +
                               $"using the declared default '{attr.DefaultValue}'\n");
            else
                _byId[id] = instance;

            // READ BACK WHAT THE ENGINE ACTUALLY HAS, rather than assuming it is
            // the default we just asked for. It usually is; for an archived
            // convar whose value came from config.cfg, or one the engine
            // declares itself with a different default, it is not — and the
            // change callback cannot correct it later, because a set to the
            // value already held fires no callback at all. See Bind.
            //
            // One FindVar per declaration at startup, and only for the ones the
            // engine accepted. A refused convar (id < 0) has nothing to read.
            instance.Backend = _backend;
            instance.Bind(id, attr.Name, attr.DefaultValue, attr.Help, attr.Flags, min, max,
                          id >= 0 ? _backend.ValueOf(attr.Name) : null, attr.Tag);
            Adopt(instance);
            count++;
        }

        if (duplicates.Count > 0)
            throw new InvalidOperationException(
                "convars declared twice, by the core and a mod or by two mods: " + string.Join(", ", duplicates) +
                ". A name has one declaration; rename one or read the other by name.");

        if (engineOwned.Count > 0)
            throw new InvalidOperationException(
                "managed code declares convars the engine defines, which would alias the engine's own: " +
                string.Join(", ", engineOwned) + ". Delete the declaration and read the engine's by name.");

        return count;
    }

    /// <summary>
    /// Returns the registered instance of a ConVar class, so a module can hold a
    /// typed reference instead of looking up by name on every read.
    /// </summary>
    /// <summary>
    /// Every convar this registry declared, by name. The development API reads
    /// values back through this — a convar can clamp or reject what it was
    /// given, and echoing the requested value would hide that.
    ///
    /// This is _all rather than _byId, so a convar the engine refused still
    /// appears. Hiding it would be the worst of both: the setting is real,
    /// something is reading it, and it is missing from the one listing meant to
    /// show what exists. It reports IsRegistered false instead.
    /// </summary>
    public IReadOnlyList<ConVarBase> All => _all;

    /// <summary>The current value of a declared convar, or null if it is not one of ours.</summary>
    public string? ValueOf(string name)
    {
        // _all, matching All: a convar the engine refused is still one of ours
        // and still holds a value, and the listing and the single-value read
        // disagreeing about that would be worse than either answer alone.
        foreach (var convar in _all)
        {
            if (string.Equals(convar.Name, name, StringComparison.OrdinalIgnoreCase))
                return convar.RawValue;
        }

        return null;
    }

    public T? Get<T>() where T : ConVarBase
        => _all.OfType<T>().FirstOrDefault();

    /// <summary>
    /// Puts every declared convar into the container, under BOTH keys: its own
    /// class, and the name a player types.
    ///
    /// <para>
    /// Both, because the two are for different callers. A rule in this mod asks
    /// for <c>SpawnHealthConVar</c> — a type, checked by the compiler, and a
    /// rename carries. A tool, a generic entity or anything driven by map data
    /// asks for "sv_spawn_health", because a string is all a map can hold.
    /// Registering only one of them would leave one of those two callers
    /// reaching around the container to get its work done, which is how the
    /// static back-references this replaces got started.
    /// </para>
    ///
    /// <para>
    /// A convar the engine REFUSED is published anyway, exactly as
    /// <see cref="All"/> lists it. It still holds its declared default and
    /// still reads back, and a policy resolving it to null instead would fall
    /// through to whatever its own null-coalesce says — a different value from
    /// the declared default, and impossible to spot from the outside.
    /// </para>
    ///
    /// <para>
    /// Convars are not themselves built through the container, and that is on
    /// purpose: they are the leaves. A convar with a constructor dependency
    /// could only depend on a system that depends on convars, and the mod would
    /// have bought itself a resolution cycle in exchange for nothing.
    /// </para>
    /// </summary>
    public int PublishTo(ServiceContainer services)
    {
        foreach (var convar in _all)
        {
            services.AddInstance(convar.GetType(), convar);
            services.AddNamed(convar.Name, convar);
        }

        return _all.Count;
    }

    /// <summary>
    /// What this side declared about one convar, by name.
    ///
    /// <para>
    /// False for a name this registry did not declare — a native convar, or a
    /// ConCommand, both of which the shell is also asked about. The shell keeps
    /// stock's behaviour for those; this registry answers only for what it
    /// declared, because that is the only thing it knows anything about.
    /// </para>
    /// </summary>
    public bool TryGetCommandLinePolicy(string name, out ConVarCommandLinePolicy policy)
    {
        if (string.IsNullOrEmpty(name))
        {
            policy = default;
            return false;
        }

        return _policies.TryGetValue(name, out policy);
    }

    /// <summary>
    /// Every declaration's command-line policy, for a tool that wants to list
    /// them. The declarations, not the bound instances — see the field.
    /// </summary>
    public IReadOnlyCollection<ConVarCommandLinePolicy> CommandLinePolicies => _policies.Values;

    /// <summary>
    /// A tagged convar changed (<see cref="ConVarAttribute.Tag"/>). Raised from
    /// the change itself, after the convar holds its new value.
    /// </summary>
    public event Action<ConVarBase>? TaggedChanged;

    /// <summary>
    /// Takes a bound convar into the registry: listed in <see cref="All"/>, and
    /// heard by <see cref="TaggedChanged"/> when it carries a tag.
    /// </summary>
    internal void Adopt(ConVarBase convar)
    {
        if (convar.Tag is not null)
            convar.ChangedHook = c => TaggedChanged?.Invoke(c);
        _all.Add(convar);
    }

    /// <summary>Routes an engine change notification. False if the id is unknown.</summary>
    public bool NotifyChanged(int id, string newValue, string oldValue)
    {
        if (!_byId.TryGetValue(id, out var convar))
            return false;

        convar.NotifyChanged(newValue, oldValue);
        return true;
    }
}

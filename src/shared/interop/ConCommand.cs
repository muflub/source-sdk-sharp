using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>
/// Marks a class as a console command. The class is discovered at module init
/// and a fresh instance is bound to the registered command.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ConCommandAttribute : Attribute
{
    public ConCommandAttribute(string name)
    {
        Name = name;
    }

    /// <summary>Console command name, e.g. "cs_hello".</summary>
    public string Name { get; }

    /// <summary>Help text shown by the "help" command.</summary>
    public string Help { get; init; } = string.Empty;

    /// <summary>
    /// The FCVAR word this command is registered with — the same enumeration a
    /// <see cref="ConVarAttribute"/> uses, because the engine has one flag word
    /// and <c>ConCommandBase</c> is what both a command and a convar are.
    ///
    /// <para>
    /// <see cref="ConVarFlags.Cheat"/> is the one that is nearly always meant
    /// and was impossible to say before the ABI carried a flag word: a probe or
    /// a fixture declared without it is runnable with <c>sv_cheats 0</c>, which
    /// is not the command that was being ported. The default is
    /// <see cref="ConVarFlags.None"/> — an unflagged command, which is what
    /// every managed command was when the slot took only a name and a help
    /// string, so nothing that already exists changes meaning.
    /// </para>
    ///
    /// <para>
    /// The DLL identifier bit is NOT declared here.
    /// <see cref="ConVarFlags.GameDll"/> and <see cref="ConVarFlags.ClientDll"/>
    /// are added natively, on the side the registration reached, because this
    /// assembly is loaded into both and a value chosen here would be one value
    /// for both.
    /// </para>
    /// </summary>
    public ConVarFlags Flags { get; init; } = ConVarFlags.None;
}

/// <summary>
/// Implemented by a class carrying <see cref="ConCommandAttribute"/>.
/// </summary>
public interface IConCommand
{
    /// <summary>Invoked when the command runs. <paramref name="args"/> is the raw argument string.</summary>
    void Execute(string args);
}

/// <summary>
/// Discovers <see cref="ConCommandAttribute"/> classes in an assembly, registers
/// each with the engine, and dispatches by the id the engine hands back.
///
/// This replaces per-module bookkeeping of command ids and a chain of
/// comparisons in ConCommandDispatch — a module now just declares command
/// classes and calls <see cref="RegisterAll"/>.
/// </summary>
/// <summary>
/// One registered command, as a listing sees it.
///
/// The declaring type is carried because two commands with similar names are
/// told apart by where they came from far more easily than by their help text,
/// and it costs nothing to keep.
///
/// <para>
/// <paramref name="IsRegistered"/> says whether the ENGINE took it. False means
/// the class exists, was built, and answers to nothing a player can type —
/// which is what a name collision with an engine command looks like from here.
/// It defaults to true so that a caller building one of these by hand, which is
/// only ever a test or a tool, describes a working command unless it says
/// otherwise.
/// </para>
/// </summary>
/// <summary>
/// <paramref name="Flags"/> is the FCVAR word the command was DECLARED with —
/// what <see cref="ConCommandAttribute.Flags"/> said, not what the engine ended
/// up holding. The two differ by exactly the DLL identifier bit, which is added
/// natively on the side the registration reached and is not knowable from here.
/// It is carried because a listing that cannot say a command is a cheat cannot
/// answer the question a listing of console commands is usually asked.
/// </summary>
public readonly record struct ConCommandInfo(string Name, string Help, string DeclaringType,
                                             bool IsRegistered = true,
                                             ConVarFlags Flags = ConVarFlags.None);

public sealed class ConCommandRegistry
{
    // Engine command id -> bound instance. Ids are dense and assigned in
    // registration order, but a dictionary keeps this correct regardless.
    private readonly Dictionary<int, IConCommand> _byId = new();

    // What was registered, in registration order. _byId cannot answer this: it
    // is keyed by an engine id and holds only the instance, so it knows how to
    // dispatch a command but not what any of them are called.
    private readonly List<ConCommandInfo> _registered = new();

    // Console name -> instance, which is the pair the container wants and the
    // only one neither table above holds: _byId is keyed by a number the engine
    // chose, and _registered holds a description rather than the object.
    // Case-insensitive, as the console is.
    private readonly Dictionary<string, IConCommand> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    // Console name -> the DECLARED class, including the names whose instance
    // the container refused. `_byName` cannot answer this: a refused class is
    // in nobody's table, and the dev API's managed capture must be able to ask
    // "is this name a managed command class I may build fresh?" without
    // building it. Names registered by `RegisterNativeBodied` are deliberately
    // absent — a native-bodied name has no managed class whose output is worth
    // capturing (the body is native code), so the capture must fall back to the
    // engine path for them.
    // Case-insensitive, as the console is.
    private readonly Dictionary<string, Type> _declaredByName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every command this registry registered, in registration order.</summary>
    public IReadOnlyList<ConCommandInfo> All => _registered;


    /// <summary>
    /// Registers every attributed command class in every assembly loaded beside
    /// <paramref name="assembly"/>. Each gets a fresh instance. Returns the
    /// number registered.
    ///
    /// <para>
    /// With a <paramref name="services"/> container, a command's constructor is
    /// filled from it — which is what lets <c>ss_spawn_pickup</c> ask for the
    /// pickup bookkeeping instead of a static field the module has to remember
    /// to assign. Without one, a command must be parameterless, as every
    /// command was before the container existed.
    /// </para>
    ///
    /// <para>
    /// Register the CONVARS and the services first. A command asking for
    /// something not yet in the container is refused, and refused at load with
    /// a message naming it — which is the right end of the game to find it out
    /// at, but only if the module put the leaves in first.
    /// </para>
    /// </summary>
    /// <param name="console">
    /// Where the registration is sent: the side's own console, required
    /// (plan_abi_hotreload D8; it defaulted to whichever side was active). It
    /// is also a SEAM, not a second path: there is one call below and a test
    /// can watch it.
    ///
    /// <para>
    /// It is a seam because the argument that matters cannot be observed any
    /// other way. Outside the game the engine answers -1 before it looks at
    /// anything, so a registration that dropped the declared FCVAR word on the
    /// floor and one that carried it are the same -1 — and the registry's own
    /// row is built from the attribute, so it reads the right flags in both
    /// cases. Every check that could be written without this seam passes on a
    /// build that registers every cheat command unflagged.
    /// </para>
    /// </param>
    public int RegisterAll(Assembly assembly, IEngineConsole console, ServiceContainer? services = null)
    {
        ArgumentNullException.ThrowIfNull(console);

        int count = 0;

        foreach (var type in LoadedAssemblies.Types(assembly, new ConsoleLog(console)))
        {
            var attr = type.GetCustomAttribute<ConCommandAttribute>();
            if (attr is null)
                continue;

            if (type.IsAbstract || type.IsInterface)
            {
                console.Warning($"[SourceSharp] [ConCommand({attr.Name})] on abstract type {type.Name}; skipped\n");
                continue;
            }

            if (!typeof(IConCommand).IsAssignableFrom(type))
            {
                console.Warning($"[SourceSharp] [ConCommand({attr.Name})] on {type.Name}, which does not implement IConCommand; skipped\n");
                continue;
            }

            // Past every shape check: this is a managed command class the
            // console owns, whether or not the container can build it today.
            _declaredByName[attr.Name] = type;

            // An EMPTY container rather than a second code path when the
            // caller supplied none. Activator.CreateInstance was what stood
            // here, and it THROWS on a class with no parameterless constructor
            // — so one command declared with a dependency, in a build whose
            // module had not been given a container, took the whole of module
            // init down with it. An empty container refuses the same class with
            // a message naming it, and every other command still registers.
            // A scan with no container builds from an empty one of its own (statics to
            // zero): only a parameterless command comes out, the rule that held
            // before the container existed.
            if (!(services ?? new ServiceContainer()).TryCreate(type, out object? made, out string failure))
            {
                console.Warning($"[SourceSharp] [ConCommand({attr.Name})] on {type.Name} " +
                               $"cannot be built: {failure}\n");
                continue;
            }

            if (made is not IConCommand instance)
            {
                console.Warning($"[SourceSharp] could not instantiate command class {type.Name}; skipped\n");
                continue;
            }

            int id = console.RegisterConCommand(attr.Name, attr.Help, attr.Flags);

            // A refused command is KEPT, exactly as ConVarRegistry keeps a
            // refused convar, and for the same two reasons. It is still a class
            // somebody declared and something may hold a reference to, so
            // dropping it from the listing would hide a command that exists
            // from the one view meant to show what exists. And it is what makes
            // this registry examinable with no engine at all — outside the game
            // every id comes back -1, so a registry that only kept the accepted
            // ones would be empty in every test ever written against it.
            //
            // Only _byId is gated, because that is the dispatch table: an id of
            // -1 is not an id, and the engine will never dispatch to it anyway.
            if (id < 0)
                console.Warning($"[SourceSharp] engine refused ConCommand '{attr.Name}'\n");
            else
                _byId[id] = instance;

            _byName[attr.Name] = instance;
            _registered.Add(new ConCommandInfo(attr.Name, attr.Help, type.Name, id >= 0, attr.Flags));
            count++;
        }

        return count;
    }

    /// <summary>
    /// Registers ONE command a table declares rather than a class: a name, help
    /// text and flag word, and the instance that answers it. For a family of
    /// commands one service answers (the client's input: +forward, -forward and
    /// sixty more), where a class per command would say the same thing sixty
    /// times. Refused commands are kept, as <see cref="RegisterAll"/> keeps them.
    /// </summary>
    public bool Register(string name, string help, ConVarFlags flags, IConCommand instance, IEngineConsole console)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(console);

        int id = console.RegisterConCommand(name, help, flags);
        if (id < 0)
            console.Warning($"[SourceSharp] engine refused ConCommand '{name}'\n");
        else
            _byId[id] = instance;

        _byName[name] = instance;
        _registered.Add(new ConCommandInfo(name, help, instance.GetType().Name, id >= 0, flags));
        return id >= 0;
    }

    /// <summary>
    /// Registers commands DECLARED here and ANSWERED natively —
    /// plan_server_novalve.md P2.3c. Each declaration is a name, help text and
    /// flag word (the table is SourceSharp.Server.NativeCommands); the native
    /// carrier the registration builds looks the name up among its
    /// <c>SS_NATIVE_COMMAND</c> bodies (native_command_body.h) and, when it
    /// finds one, runs it without crossing back here.
    ///
    /// <para>
    /// The instance bound to each is <see cref="NativeBodiedCommand"/>, which is
    /// reached only when the carrier found NO body — a declaration whose native
    /// half is missing from this build — and says so rather than doing nothing.
    /// </para>
    /// </summary>
    public int RegisterNativeBodied(IEnumerable<ConCommandAttribute> declarations,
                                    IEngineConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        int count = 0;

        foreach (ConCommandAttribute decl in declarations)
        {
            var instance = new NativeBodiedCommand(decl.Name, console);
            int id = console.RegisterConCommand(decl.Name, decl.Help, decl.Flags);

            if (id < 0)
                console.Warning($"[SourceSharp] engine refused ConCommand '{decl.Name}'\n");
            else
                _byId[id] = instance;

            _byName[decl.Name] = instance;
            _registered.Add(new ConCommandInfo(decl.Name, decl.Help, nameof(NativeBodiedCommand),
                                               id >= 0, decl.Flags));
            count++;
        }

        return count;
    }

    /// <summary>The instance bound to a console name, or null if there is none.</summary>
    public IConCommand? ByName(string name)
        => _byName.TryGetValue(name, out var command) ? command : null;

    /// <summary>
    /// The declared class behind a console name, or null when the name is not a
    /// managed command class this registry owns. Native-bodied names (see
    /// <see cref="RegisterNativeBodied"/>) answer null: they are managed NAMES
    /// with native OUTPUT, and running their carrier into a capture sink would
    /// answer "no body" and capture none of the real counters.
    /// </summary>
    public Type? DeclaredType(string name)
        => _declaredByName.TryGetValue(name, out var type) ? type : null;

    /// <summary>
    /// Puts every registered command into the container under the name a player
    /// types.
    ///
    /// <para>
    /// By NAME and not by type, which is the opposite of what convars do, and
    /// the asymmetry is real rather than an oversight. A convar class is a
    /// value somebody wants to read, so asking for it by type is the natural
    /// thing and the compiler checks it. A command class is a verb the console
    /// invokes; code holding a reference to one in order to call
    /// <see cref="IConCommand.Execute"/> by hand is code that wanted a method,
    /// not a command. What the name registration is actually for is a tool
    /// listing what exists, and for the one legitimate caller: something that
    /// runs a command a map or a config named.
    /// </para>
    ///
    /// <para>
    /// Refused commands are published too, matching <see cref="All"/> — see the
    /// note in <see cref="RegisterAll"/> for why they are kept at all. The
    /// listing and the by-name lookup disagreeing about whether a command
    /// exists would be worse than either answer alone.
    /// </para>
    /// </summary>
    public int PublishTo(ServiceContainer services)
    {
        foreach (var pair in _byName)
            services.AddNamed(pair.Key, pair.Value);

        return _byName.Count;
    }

    /// <summary>
    /// Routes an engine dispatch to the bound instance. Returns false if the id
    /// is unknown, so the caller can log rather than fail silently.
    /// </summary>
    public bool Dispatch(int commandId, string args)
    {
        if (!_byId.TryGetValue(commandId, out var command))
            return false;

        command.Execute(args);
        return true;
    }
}

/// <summary>
/// A command declared in C# whose body is native (<c>SS_NATIVE_COMMAND</c>).
/// The native carrier runs the body itself, so <see cref="Execute"/> is
/// reached only when this build has no body of that name.
/// </summary>
public sealed class NativeBodiedCommand : IConCommand
{
    private readonly IEngineConsole _console;

    public NativeBodiedCommand(string name, IEngineConsole console)
    {
        Name = name;
        _console = console;
    }

    public string Name { get; }

    public void Execute(string args)
        => _console.Warning($"[SourceSharp] '{Name}' is declared with a native body, and this " +
                          "build has none (SS_NATIVE_COMMAND); nothing was done\n");
}

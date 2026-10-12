using SourceSharp.Abi;
using SourceSharp.Server.Interface;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IDebugConsole"/> over the server's convar and command registries
/// and the engine's console (group G1). The composition passes each source as
/// a function; NativeConsole over the engine console in the game.
/// </summary>
public sealed class DebugConsoleHook(
    Func<IReadOnlyList<ConVarBase>> convars,
    Func<IReadOnlyList<ConCommandInfo>> commands,
    Func<string, string?> managedValue,
    Func<IReadOnlyList<NativeConsoleEntry>> native,
    Func<string, string?> nativeValue,
    Action<string> execute) : IDebugConsole
{
    private readonly Func<IReadOnlyList<ConVarBase>> _convars = convars ?? throw new ArgumentNullException(nameof(convars));
    private readonly Func<IReadOnlyList<ConCommandInfo>> _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    private readonly Func<string, string?> _managedValue = managedValue ?? throw new ArgumentNullException(nameof(managedValue));
    private readonly Func<IReadOnlyList<NativeConsoleEntry>> _native = native ?? throw new ArgumentNullException(nameof(native));
    private readonly Func<string, string?> _nativeValue = nativeValue ?? throw new ArgumentNullException(nameof(nativeValue));
    private readonly Action<string> _execute = execute ?? throw new ArgumentNullException(nameof(execute));

    /// <summary>Over the engine console: the game's own table and its own command line.</summary>
    public static DebugConsoleHook Over(IEngineConsole console, Func<IReadOnlyList<ConVarBase>> convars,
                                  Func<IReadOnlyList<ConCommandInfo>> commands, Func<string, string?> managedValue)
        => new(convars, commands, managedValue,
               () => NativeConsole.Snapshot(console), name => NativeConsole.ValueOf(console, name),
               console.ExecuteServerCommand);

    public void Execute(string command) => _execute(command);

    public string? ValueOf(string name) => _managedValue(name) ?? _nativeValue(name);

    public IReadOnlyList<DebugConVar> ConVars()
        => _convars().Select(c => new DebugConVar(
                c.Name, c.RawValue, c.DefaultValue, c.Help, c.TypeName, ConVarFlagNames.Describe(c.Flags),
                c.MinValue, c.MaxValue, c.IsRegistered, c.GetType().Name))
            .ToArray();

    public IReadOnlyList<DebugConCommand> Commands()
        => _commands().Select(c => new DebugConCommand(
                c.Name, c.Help, c.DeclaringType, ConVarFlagNames.Describe((int)c.Flags), c.IsRegistered))
            .ToArray();

    public IReadOnlyList<DebugNativeEntry> NativeEntries()
        => _native().Select(e => new DebugNativeEntry(
                e.Name, e.Value, e.DefaultValue, e.Help, e.FlagNames, e.IsCommand, e.Min, e.Max, e.DeclaredBy))
            .ToArray();
}

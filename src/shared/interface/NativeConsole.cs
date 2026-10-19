namespace SourceSharp.Abi;

/// <summary>
/// One convar or command the ENGINE registered, as a listing sees it.
///
/// A separate type from <see cref="ConVarBase"/> and <see cref="ConCommandInfo"/>
/// rather than a shared one, because the two are not the same thing wearing
/// different hats. (The FLAGS are shared — see <see cref="ConVarFlags"/> — since
/// a C# declaration can ask for any bit the engine's own can.) Ours are managed objects: a declared type, a class that owns
/// them, a cached value kept current by a change callback, and a write path that
/// goes straight to the object. These are a copy of what the engine's table said
/// at the moment it was walked — no declared type, no owning class, and a value
/// that is only as fresh as the last walk.
///
/// Presenting them as the same kind of thing is what would make a debugging tool
/// lie: it would offer a checkbox for a native convar nobody ever said was a
/// bool, and it would show a stale value as though it were live.
/// </summary>
/// <param name="Name">The name as the console knows it.</param>
/// <param name="Value">Current value; empty for a command, which holds none.</param>
/// <param name="DefaultValue">What it was registered with; empty for a command.</param>
/// <param name="Help">Help text as registered, truncated to what the ABI carries.</param>
/// <param name="Flags">FCVAR_* bits, exactly as registered.</param>
/// <param name="IsCommand">True for a ConCommand, false for a ConVar.</param>
/// <param name="Min">The lowest value it clamps to, or null if it is unbounded.</param>
/// <param name="Max">The highest, likewise.</param>
public readonly record struct NativeConsoleEntry(
    string Name,
    string Value,
    string DefaultValue,
    string Help,
    int Flags,
    bool IsCommand,
    float? Min = null,
    float? Max = null);

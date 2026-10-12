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
    float? Max = null)
{
    /// <summary>Whether the value differs from what it was registered with.</summary>
    public bool IsModified => !IsCommand && Value != DefaultValue;

    /// <summary>The flag bits spelled out, e.g. "GameDll, Cheat".</summary>
    public string FlagNames => ConVarFlagNames.Describe(Flags);

    /// <summary>
    /// Which DLL registered it, as far as the flags say.
    ///
    /// The engine records this and nothing else about where a convar came from,
    /// so it is the honest answer to "who declared this" for something that has
    /// no C# class behind it — and it is the answer that matters most, since it
    /// separates the game's own settings from the engine's thousands.
    /// </summary>
    public string DeclaredBy
        => (Flags & (int)ConVarFlags.GameDll) != 0 ? "server.dll"
         : (Flags & (int)ConVarFlags.ClientDll) != 0 ? "client.dll"
         : "engine";
}

/// <summary>
/// A read-only proxy over the engine's own console registry.
///
/// This exists because everything else in this codebase that knows about a
/// convar knows about it because C# declared it. The engine's own thousands were
/// unreachable from managed code: the development API could RUN any of them
/// through the console and could list none of them, so the one view meant to
/// show what exists showed a mod's sixty and left out the rest of the game.
///
/// A proxy rather than a mirror. Nothing here is cached across calls and nothing
/// is registered: each snapshot is what the engine's table said at the moment it
/// was walked, and it is thrown away as soon as it has been rendered. Holding
/// onto it would mean showing values that have since changed and names of
/// convars that have since gone away with the DLL that registered them.
///
/// GAME THREAD ONLY. Unlike the managed registries, which are managed objects
/// holding a cached value and can be listed from anywhere, this reads live
/// engine state.
/// </summary>
public static unsafe class NativeConsole
{
    /// <summary>
    /// Everything the engine has registered: its own, the game DLLs', and the
    /// convars this mod declared, which the engine holds alongside the rest and
    /// cannot tell apart from them.
    ///
    /// Empty when the engine is not there — a managed test has no registry to
    /// walk, and an empty listing is the truthful answer rather than a throw.
    /// </summary>
    public static IReadOnlyList<NativeConsoleEntry> Snapshot(IEngineConsole console)
    {
        int count = console.Snapshot();

        if (count <= 0)
            return Array.Empty<NativeConsoleEntry>();

        var entries = new List<NativeConsoleEntry>(count);

        for (int i = 0; i < count; i++)
        {
            if (console.SnapshotEntry(i, out var entry))
                entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// One native convar's value, or null if the engine knows no such convar.
    ///
    /// Null for a COMMAND too, which is not a gap: the caller asked for a value
    /// and a command has none. Telling the two apart is the listing's job.
    /// </summary>
    public static string? ValueOf(IEngineConsole console, string name) => console.FindConVarValue(name);
}

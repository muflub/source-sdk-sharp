namespace SourceSharp.Abi;

/// <summary>
/// What a <see cref="NativeConsoleEntry"/> says beyond its fields.
///
/// <para>
/// Extension methods rather than members since plan_shim_areas_split phase 4:
/// the entry is in a seam's signature (<c>IEngineConsole.SnapshotEntry</c>), so
/// it lives with the seam in an interface assembly, which holds only data. The
/// reading of it stays here, beside the walk that fills it.
/// </para>
/// </summary>
public static class NativeConsoleEntryReading
{
    /// <summary>Whether the value differs from what it was registered with.</summary>
    public static bool IsModified(this NativeConsoleEntry entry) => !entry.IsCommand && entry.Value != entry.DefaultValue;

    /// <summary>The flag bits spelled out, e.g. "GameDll, Cheat".</summary>
    public static string FlagNames(this NativeConsoleEntry entry) => ConVarFlagNames.Describe(entry.Flags);

    /// <summary>
    /// Which DLL registered it, as far as the flags say.
    ///
    /// The engine records this and nothing else about where a convar came from,
    /// so it is the honest answer to "who declared this" for something that has
    /// no C# class behind it — and it is the answer that matters most, since it
    /// separates the game's own settings from the engine's thousands.
    /// </summary>
    public static string DeclaredBy(this NativeConsoleEntry entry)
        => (entry.Flags & (int)ConVarFlags.GameDll) != 0 ? "server.dll"
         : (entry.Flags & (int)ConVarFlags.ClientDll) != 0 ? "client.dll"
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

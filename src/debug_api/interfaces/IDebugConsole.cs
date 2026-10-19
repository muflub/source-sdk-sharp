namespace Debug.Api.Server.Interfaces;

/// <summary>A convar the server's managed code declares (group G1).</summary>
/// <param name="FlagNames">Its flags spelled out, e.g. "Archive".</param>
/// <param name="Registered">Whether the engine accepted it.</param>
/// <param name="DeclaredBy">The declaring C# type's name.</param>
public sealed record DebugConVar(
    string Name, string Value, string Default, string Help, string Type, string FlagNames,
    float? Min, float? Max, bool Registered, string DeclaredBy);

/// <summary>A console command the server's managed code declares (group G1).</summary>
/// <param name="FlagNames">Its declared flags spelled out.</param>
public sealed record DebugConCommand(string Name, string Help, string DeclaredBy, string FlagNames, bool Registered);

/// <summary>One row of the engine's own console table (group G1).</summary>
/// <param name="DeclaredBy">Which DLL registered it, as far as its flags say.</param>
public sealed record DebugNativeEntry(
    string Name, string Value, string Default, string Help, string FlagNames, bool IsCommand,
    float? Min, float? Max, string DeclaredBy);

/// <summary>The server's console (group G1).</summary>
public interface IDebugConsole
{
    /// <summary>Runs <paramref name="command"/> as a server command. Game thread only.</summary>
    void Execute(string command);

    /// <summary>Every convar the server's managed code declares; any thread.</summary>
    IReadOnlyList<DebugConVar> ConVars();

    /// <summary>Every command the server's managed code declares; any thread.</summary>
    IReadOnlyList<DebugConCommand> Commands();

    /// <summary>The engine's whole console table. Game thread only.</summary>
    IReadOnlyList<DebugNativeEntry> NativeEntries();

    /// <summary>
    /// A convar's value, from the managed registry first and then the
    /// engine's, or null when neither knows the name. Game thread only.
    /// </summary>
    string? ValueOf(string name);
}

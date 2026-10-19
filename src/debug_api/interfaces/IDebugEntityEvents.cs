namespace Debug.Api.Server.Interfaces;

/// <summary>An output that fired (group G4).</summary>
/// <param name="Handle">The firing entity's handle; <c>0xFFFFFFFF</c> is none.</param>
/// <param name="Index">Its entity index, or -1 when its handle is not valid.</param>
/// <param name="ClassName">Its classname.</param>
/// <param name="Output">The output's name.</param>
/// <param name="Value">The value it carried, or null.</param>
public sealed record DebugOutputFired(uint Handle, int Index, string ClassName, string Output, string? Value);

/// <summary>What the server's entities do that the dev API is told about as it happens (group G4).</summary>
public interface IDebugEntityEvents
{
    /// <summary>
    /// Calls <paramref name="fired"/> for every output that fires from now on,
    /// on the game thread, until the result is disposed.
    /// </summary>
    IDisposable Subscribe(Action<DebugOutputFired> fired);
}

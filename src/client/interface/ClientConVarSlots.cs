namespace SourceSharp.Abi;

/// <summary>
/// IShimConVarsClient (plan_client_novalve C8): convars the managed client
/// watches, and the values it decides for native objects the engine reads
/// through their vtable.
/// </summary>
public interface IClientConVarSlots
{
    /// <summary>
    /// Watches <paramref name="name"/>: its changes arrive through ConVarChanged
    /// under the answered id, which is at least <see cref="ClientConVarSlots.WatchIdBase"/>.
    /// -1 when no such convar exists (or the client is not attached).
    /// </summary>
    int Watch(string name);

    /// <summary>Stores a decided value native reads under <paramref name="name"/>; false when nothing reads it.</summary>
    bool SetValue(string name, float value);
}

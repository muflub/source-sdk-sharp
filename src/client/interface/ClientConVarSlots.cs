namespace SourceSharp.Abi;

/// <summary>
/// IShimConVarsClient (plan_client_novalve C8): the values the managed client
/// decides for native objects the engine reads through their vtable.
/// </summary>
public interface IClientConVarSlots
{
    /// <summary>Stores a decided value native reads under <paramref name="name"/>; false when nothing reads it.</summary>
    bool SetValue(string name, float value);
}

namespace SourceSharp.Abi;

/// <summary>
/// Implemented by a module that owns a <see cref="ConVarRegistry"/>, so the
/// bootstrap can route change notifications without knowing the module type.
/// </summary>
public interface IConVarHost
{
    void OnConVarChanged(int id, string newValue, string oldValue);
}

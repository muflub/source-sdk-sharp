namespace SourceSharp.Abi;

/// <summary>
/// What the server's IManagedUserMessages binding needs from its side's
/// user-message registry: the key and payload size of an engine id, for the
/// engine's <c>GetUserMessageInfo</c>. An interface here so the binding
/// (Server.Interop) does not reference the registry's assembly
/// (plan_side_isolation §9.1, (c)); <c>UserMessageRegistry</c> (SourceSharp.Shared)
/// implements it.
/// </summary>
public interface IUserMessageCatalog
{
    /// <summary>
    /// The key and size (-1 for variable length) of engine id
    /// <paramref name="id"/>; false when no message has that id.
    /// </summary>
    bool TryDescribe(int id, out string key, out int size);
}

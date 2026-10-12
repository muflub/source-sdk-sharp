namespace SourceSharp.Abi;

/// <summary>
/// What the client's IManagedUserMessagesClient binding needs from its side's
/// user-message receiver: one dispatch per engine message. An interface here
/// so the binding (Client.Interop) does not reference the receiver's assembly
/// (plan_side_isolation §9.1, (c)); <c>UserMessageReceiver</c> (SourceSharp.Shared)
/// implements it.
/// </summary>
public interface IUserMessageDispatch
{
    /// <summary>
    /// Hands engine message <paramref name="id"/>, its payload copied out
    /// whole as one <see cref="BitBlock"/>, to its managed handler. False when
    /// it was not dispatched.
    /// </summary>
    bool Dispatch(int id, ReadOnlySpan<byte> block);
}

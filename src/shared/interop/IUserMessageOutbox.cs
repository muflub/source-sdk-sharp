namespace SourceSharp.Abi;

/// <summary>
/// The native half of the server's send (plan_side_isolation §2.2a): the
/// bound IShimUserMessages slot, implemented over the server's routing in
/// SourceSharp.Server.Interop. Native carries out what C# decided.
/// </summary>
public interface IUserMessageOutbox
{
    /// <summary>
    /// Hands the engine message <paramref name="id"/> with
    /// <paramref name="payload"/>, to <paramref name="player"/>'s slot or to
    /// everyone for 0. False when the slot holds no player (nothing was sent)
    /// or the server's area is not attached.
    /// </summary>
    bool Send(int id, ReadOnlySpan<byte> payload, int player, bool reliable);
}

using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// Something solid prediction's movement traces must meet besides the world and the players
/// (<see cref="PredictionPlayerClip"/>): the client's engine trace hits no entity, so an entity
/// that the server's traces stop at (one answering its own traces, <c>ICustomTraceTarget</c>) is
/// clipped in here, or the client walks through what the server stops it at and every bump
/// mispredicts. A hook chain: a mod's member clips what it owns; each member orders itself
/// (<see cref="IOrderedHook"/>).
/// </summary>
public interface IPredictionTraceClip
{
    /// <summary>
    /// The hull [<paramref name="mins"/>, <paramref name="maxs"/>] swept from
    /// <paramref name="start"/> to <paramref name="end"/> under <paramref name="mask"/> against
    /// what this member owns: true with <paramref name="hit"/> (its fraction, end, plane, solidity
    /// and entity) when something stops it, false when nothing does.
    /// </summary>
    bool Clip(in Vector start, in Vector end, in Vector mins, in Vector maxs, int mask, out MoveTrace hit);
}

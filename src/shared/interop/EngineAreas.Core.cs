// The engine, split into the areas it actually has.
// 
// An interface per area rather than one for all 273 members, because the point
// is what a class ASKS FOR: a trigger that takes IEngineDamage has said what it
// does to whoever walks in, and a reader of its constructor knows without
// opening the body. One giant interface would say only "it touches the engine".
//
// Every one of these is implemented twice over: by the real binding, which is
// one call per member into Engine's function-pointer table, and by whatever a
// test hands over instead. THAT is the reason for the whole exercise. Today a
// test that calls the engine gets 0 or empty back — silently, because there is
// no engine in a test process — so an entity test can assert what an entity
// computed but never what it asked the engine to DO. A check that cannot fail
// reads exactly like one that passes.

namespace SourceSharp.Abi;

/// <summary>
/// Sends a user message by its key (plan_side_isolation §2.2a): the side's
/// registry turns the key into the engine id and the bytes go out through
/// IShimUserMessages. Not an <c>IEngine*</c> area: the side's composition
/// builds it over its registry (SourceSharp.Shared's UserMessageSender).
/// </summary>
public interface IUserMessageSender
{
    /// <summary>
    /// Sends <paramref name="payload"/> under <paramref name="key"/> to
    /// <paramref name="player"/>'s slot, or to everyone for 0. A key no
    /// declaration registers, a payload over the engine's limit, or a slot with
    /// no player in it is refused with a warning and nothing is sent.
    /// </summary>
    void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true);
}

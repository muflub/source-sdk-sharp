using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <b>THE WIRE THE SOUNDSCAPE DEFECT WAS MISSING</b> — plan_entity.md step
/// 8e-sound.
///
/// <para>
/// Step 7e measured both ends of the soundscape mechanism as present and
/// correct with nothing between them: <c>Soundscapes.cs</c> decided, the
/// server's <c>PlayerSetSoundscape</c> slot had an empty body, and the client's
/// <c>Soundscape_Update</c> had no caller. 255 soundscapes parsed on
/// <c>dm_lockdown</c>, a player standing inside one the server had chosen, and
/// the client's current soundscape <c>-1 [none]</c>.
/// </para>
///
/// <para>
/// These facts are about the middle. Each one round-trips a real payload
/// through the real writer and the real reader, because the failure this class
/// exists to prevent is not a crash — it is a field written in one order and
/// read in another, which arrives as a valid number in the wrong slot and puts
/// a positioned sound in the wrong wall.
/// </para>
/// </summary>
public class SoundscapeMessageTests
{
    /// <summary>
    /// Writes a payload and delivers it, handing back what the sink was told.
    /// </summary>
    private static (int Index, int EntIndex, int Bits, Vector[] Positions) RoundTrip(
        int soundscapeIndex, int entIndex, int localBits, params Vector[] positions)
    {
        Span<byte> payload = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        int length = SoundscapeMessage.PayloadOf(soundscapeIndex, entIndex, localBits, positions).Write(payload);

        Assert.True(length > 0, "the payload fits");

        (int, int, int, Vector[]) seen = default;

        ((IUserMessage)new SoundscapeMessage((i, e, b, p) => seen = (i, e, b, (Vector[])p.Clone())))
            .OnReceived(new UserMessageReader(payload[..length]));

        return seen;
    }

    [Fact]
    public void TheSoundscapeIndexSurvivesTheWire()
    {
        Assert.Equal(227, RoundTrip(227, 29, 0).Index);
    }

    /// <summary>
    /// The entity index is what <c>C_SoundscapeSystem::UpdateAudioParams</c>
    /// tests before it starts anything, so losing it is silence rather than an
    /// error.
    /// </summary>
    [Fact]
    public void TheEntityIndexSurvivesTheWire()
    {
        Assert.Equal(29, RoundTrip(227, 29, 0).EntIndex);
    }

    /// <summary>
    /// Leaving a volume is <c>index -1, entIndex 0</c>, and a payload that
    /// mangled either would be indistinguishable from the defect this closed.
    /// </summary>
    [Fact]
    public void LeavingIsCarriedAsMinusOneAndZero()
    {
        var seen = RoundTrip(-1, 0, 0);

        Assert.Equal(-1, seen.Index);
        Assert.Equal(0, seen.EntIndex);
    }

    [Fact]
    public void TheLocalBitsSurviveTheWire()
    {
        Assert.Equal(0b1010_0001, RoundTrip(227, 29, 0b1010_0001,
            new Vector(1, 2, 3), default, default, default,
            default, new Vector(4, 5, 6), default, new Vector(7, 8, 9)).Bits);
    }

    /// <summary>
    /// <b>Positions are indexed by bit, not packed</b> — the property that
    /// decides which wall a positioned sound comes out of.
    /// </summary>
    /// <remarks>
    /// A soundscape script names its positions by number: <c>position2</c> is
    /// the third slot whether or not the first two are set. Packing the set ones
    /// would compile, round-trip and put every sound after an unset slot in the
    /// wrong place — which nobody would report as a bug, because a soundscape
    /// coming from the wrong side of a room sounds like a soundscape.
    /// </remarks>
    [Fact]
    public void APositionLandsInTheSlotItsBitNames()
    {
        var seen = RoundTrip(227, 29, 1 << 5,
            default, default, default, default, default, new Vector(-3657, 3583, 9), default, default);

        Assert.Equal(new Vector(-3657, 3583, 9), seen.Positions[5]);
    }

    /// <summary>And an unset slot carries nothing, rather than the next set one.</summary>
    [Fact]
    public void AnUnsetSlotIsNotFilledFromTheNextSetOne()
    {
        var seen = RoundTrip(227, 29, 1 << 5,
            default, default, default, default, default, new Vector(-3657, 3583, 9), default, default);

        Assert.Equal(default, seen.Positions[0]);
        Assert.Equal(default, seen.Positions[4]);
        Assert.Equal(default, seen.Positions[6]);
    }

    /// <summary>
    /// Only the named positions are on the wire, which is the whole size
    /// argument for a message over a networked struct.
    /// </summary>
    [Fact]
    public void OnlyTheNamedPositionsAreWritten()
    {
        Span<byte> none = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        Span<byte> one = stackalloc byte[UserMessageSender.MaxPayloadBytes];

        int lenNone = SoundscapeMessage.PayloadOf(227, 29, 0, ReadOnlySpan<Vector>.Empty).Write(none);
        int lenOne = SoundscapeMessage.PayloadOf(227, 29, 1, stackalloc Vector[] { new(1, 2, 3) }).Write(one);

        // Two ints, the bits, the positions' count byte; then 12 per position.
        Assert.Equal(10, lenNone);
        Assert.Equal(10 + 12, lenOne);
    }

    /// <summary>The layout, pinned once: a change to it has to change this line.</summary>
    [Fact]
    public void TheSoundscapeLayoutIsPinned()
    {
        Span<byte> buf = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        int n = SoundscapeMessage.PayloadOf(-1, 2, 1 << 1, stackalloc Vector[] { default, new(1, 0, 0) }).Write(buf);

        Assert.Equal(new byte[]
        {
            0xFF, 0xFF, 0xFF, 0xFF,  2, 0, 0, 0,  0x02,  0x01,
            0x00, 0x00, 0x80, 0x3F,  0, 0, 0, 0,  0, 0, 0, 0,
        }, buf[..n].ToArray());
    }

    /// <summary>A count of positions that disagrees with the bits is not a push this build wrote.</summary>
    [Fact]
    public void APositionCountThatDisagreesWithTheBitsIsDropped()
    {
        var handler = new SoundscapeMessage();
        var wrong = new SoundscapePush { SoundscapeIndex = 1, EntIndex = 1, LocalBits = 0b11, Positions = [default] };

        Span<byte> buf = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        int n = wrong.Write(buf);
        ((IUserMessage)handler).OnReceived(new UserMessageReader(buf[..n]));

        Assert.Equal(0, handler.Applied);
    }

    /// <summary>
    /// The counter goes up even when nothing is listening, so "the message
    /// arrived and no sink was installed" is a readable state.
    /// </summary>
    /// <remarks>
    /// Which is the state on the SERVER by design — the registry does not even
    /// construct handlers there — and the state on a client between load and
    /// wiring. A counter that only moved when a sink was present could not tell
    /// that apart from a message that never arrived, which is the exact
    /// ambiguity step 7e's push counter was added to remove on the other side.
    /// </remarks>
    [Fact]
    public void AMessageWithNoSinkIsStillCounted()
    {
        Span<byte> payload = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        int length = SoundscapeMessage.PayloadOf(227, 29, 0, ReadOnlySpan<Vector>.Empty).Write(payload);

        var handler = new SoundscapeMessage();
        long before = handler.Applied;
        ((IUserMessage)handler).OnReceived(new UserMessageReader(payload[..length]));

        Assert.Equal(before + 1, handler.Applied);
    }

    [Fact]
    public void TheContainersHandlerSendsAPushToTheRegisteredSink()
    {
        // The client's handler is built by its container over the sink the
        // client module registers; it used to be a static delegate the module
        // installed after the fact.
        Span<byte> payload = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        int length = SoundscapeMessage.PayloadOf(227, 29, 0, ReadOnlySpan<Vector>.Empty).Write(payload);

        int landedIndex = -1;
        var services = new ServiceContainer().AddInstance<SoundscapeMessage.SoundscapeSink>(
            (index, _, _, _) => landedIndex = index);
        services.AddInstance<ILog>(new RecordingLog());
        UserMessageRegistry.AddHandlersTo(services, typeof(SoundscapeMessage).Assembly, new RecordingLog());

        ((IUserMessage)services.Require<SoundscapeMessage>()).OnReceived(new UserMessageReader(payload[..length]));

        Assert.Equal(227, landedIndex);
    }

    [Fact]
    public void TwoHandlersDoNotShareASinkOrACount()
    {
        // The property the static sink and counter lacked.
        Span<byte> payload = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        int length = SoundscapeMessage.PayloadOf(227, 29, 0, ReadOnlySpan<Vector>.Empty).Write(payload);

        int landed = 0;
        var mine = new SoundscapeMessage((_, _, _, _) => landed++);
        var theirs = new SoundscapeMessage();

        ((IUserMessage)theirs).OnReceived(new UserMessageReader(payload[..length]));

        Assert.True(landed == 0, "another handler's push does not reach this handler's sink");
        Assert.True(mine.Applied == 0 && theirs.Applied == 1, "and each counts only its own");
    }

    /// <summary>
    /// <b>The message's id is its key's place in key order</b>
    /// (plan_side_isolation P2b): <c>SourceSharp:Soundscape</c> sits where the
    /// ordinal sort of every key puts it, whatever was declared after it, and
    /// the SsUserMessages table carries that id to each client.
    /// </summary>
    [Fact]
    public void TheSoundscapeMessageTakesItsKeysPlaceInKeyOrder()
    {
        var registry = new UserMessageRegistry(new RecordingLog(), receives: false);
        registry.RegisterAll(TestServices.ServerGame);

        var keys = registry.Messages.Select(m => m.Name).Order(StringComparer.Ordinal).ToList();
        var soundscape = registry.Messages.Single(m => m.Name == "SourceSharp:Soundscape");

        Assert.Equal(keys.IndexOf("SourceSharp:Soundscape"), soundscape.Index);
        Assert.Equal(soundscape.Index, registry.IdOf("SourceSharp:Soundscape"));
    }

    /// <summary>And it is handled, which is what makes it more than a name.</summary>
    [Fact]
    public void TheSoundscapeMessageHasAClientHandler()
    {
        var registry = new UserMessageRegistry(new RecordingLog());
        registry.RegisterAll(TestServices.ServerGame);

        var declaration = registry.Messages.Single(m => m.Name == "SourceSharp:Soundscape");

        Assert.True(declaration.Dispatched,
                    "a declaration with no handler would register the name and drop every push");
    }
}

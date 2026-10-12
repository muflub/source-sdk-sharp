using System;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE WORD A TELEPORT WRITES (<c>SourceSharp.Abi.TeleportDiscontinuity</c>,
/// and <c>ContractBaseline.OpenDefects</c>'s last entry).
///
/// <para>
/// These are facts about the STATE. What the state is FOR — an entity drawn
/// where it went instead of on the way there — is measured in
/// <c>InterpolationTests</c>, off the render tail, with a control arm. Both
/// halves are needed: a counter with no reader is dead state and a reader with
/// no counter never fires, which is exactly what the ledger entry said.
/// </para>
/// </summary>
public class TeleportDiscontinuityTests
{
    private static byte[] Block() => new byte[EngineBlock.SizeBytes];

    /// <summary>
    /// Stock's <c>NOINTERP_PARITY_MAX</c>, <c>game/shared/shareddefs.h:963</c>,
    /// and the two bits it implies. Written down rather than derived, so
    /// widening the counter without widening the wire prop is a red fact.
    /// </summary>
    [Fact]
    public void TheParityIsStocksFourValuesInTwoBits()
    {
        Assert.Equal(4, TeleportDiscontinuity.ParityMax);
        Assert.Equal(2, TeleportDiscontinuity.ParityBits);
        Assert.Equal(TeleportDiscontinuity.ParityMax, 1 << TeleportDiscontinuity.ParityBits);
    }

    [Fact]
    public void TheCounterWrapsAtTheParityRatherThanGrowing()
    {
        Assert.Equal(1, TeleportDiscontinuity.Next(0));
        Assert.Equal(2, TeleportDiscontinuity.Next(1));
        Assert.Equal(3, TeleportDiscontinuity.Next(2));
        Assert.Equal(0, TeleportDiscontinuity.Next(3));
    }

    /// <summary>
    /// A value that somehow arrived outside the two bits still lands inside
    /// them. The wire cannot deliver one — the prop is two bits wide — but the
    /// block is memory, and a negative here would stay negative for ever under
    /// C#'s remainder.
    /// </summary>
    [Fact]
    public void ANonsenseCounterIsBroughtBackInsideTheTwoBits()
    {
        Assert.InRange(TeleportDiscontinuity.Next(-1), 0, 3);
        Assert.InRange(TeleportDiscontinuity.Next(int.MaxValue), 0, 3);
        Assert.InRange(TeleportDiscontinuity.Next(int.MinValue), 0, 3);
    }

    [Fact]
    public void BumpingWritesTheNextValueIntoTheBlock()
    {
        byte[] block = Block();

        Assert.True(TeleportDiscontinuity.Bump(block, out int frame));

        Assert.Equal(1, frame);
        Assert.Equal(1, TeleportDiscontinuity.FrameOf(block));
    }

    [Fact]
    public void BumpingFourTimesReturnsToWhereItStarted()
    {
        byte[] block = Block();

        for (int i = 0; i < TeleportDiscontinuity.ParityMax; i++)
            TeleportDiscontinuity.Bump(block, out _);

        Assert.Equal(0, TeleportDiscontinuity.FrameOf(block));
    }

    /// <summary>
    /// A span that is not a whole block is REFUSED rather than written past.
    /// <c>Engine.GetEntityBlock</c> answers empty for an entity that is not on
    /// this hierarchy, and a teleport of an SDK entity goes through
    /// <c>CBaseEntity::Teleport</c>, which bumps its own counter the stock way.
    /// </summary>
    [Fact]
    public void AShortSpanIsRefusedRatherThanWritten()
    {
        Assert.False(TeleportDiscontinuity.Bump(Span<byte>.Empty, out int frame));
        Assert.Equal(0, frame);

        var truncated = new byte[EngineBlock.SizeBytes - 1];
        Assert.False(TeleportDiscontinuity.Bump(truncated, out _));
        Assert.All(truncated, b => Assert.Equal(0, b));

        Assert.False(TeleportDiscontinuity.HasTeleported(truncated));
    }

    [Fact]
    public void AFreshBlockHasNotTeleported()
        => Assert.False(TeleportDiscontinuity.HasTeleported(Block()));

    [Fact]
    public void ABumpedBlockHasTeleportedUntilItIsAcknowledged()
    {
        byte[] block = Block();

        TeleportDiscontinuity.Bump(block, out _);
        Assert.True(TeleportDiscontinuity.HasTeleported(block));

        TeleportDiscontinuity.Acknowledge(block);
        Assert.False(TeleportDiscontinuity.HasTeleported(block));
        Assert.Equal(TeleportDiscontinuity.FrameOf(block), TeleportDiscontinuity.SeenBy(block));
    }

    /// <summary>
    /// A SECOND JUMP AFTER AN ACKNOWLEDGED ONE IS STILL A JUMP, which a flag
    /// that was cleared and re-set could get wrong in either direction.
    /// </summary>
    [Fact]
    public void ASecondTeleportIsNoticedAfterTheFirstWasAcknowledged()
    {
        byte[] block = Block();

        TeleportDiscontinuity.Bump(block, out _);
        TeleportDiscontinuity.Acknowledge(block);

        TeleportDiscontinuity.Bump(block, out _);
        Assert.True(TeleportDiscontinuity.HasTeleported(block));
    }

    /// <summary>
    /// A CLIENT THAT MISSED THREE UPDATES STILL SEES THE JUMP. This is the whole
    /// reason the wire carries a counter and not a "do not interpolate" flag: a
    /// flag is only true for the one snapshot that sets it.
    /// </summary>
    [Fact]
    public void AJumpIsStillVisibleToAClientThatMissedTheSnapshotCarryingIt()
    {
        byte[] block = Block();

        // Three teleports the client never heard about, then the update it does
        // receive. 1, 2, 3 -- none of them equal to the 0 it last acted on.
        TeleportDiscontinuity.Bump(block, out _);
        TeleportDiscontinuity.Bump(block, out _);
        TeleportDiscontinuity.Bump(block, out _);

        Assert.True(TeleportDiscontinuity.HasTeleported(block));
    }

    /// <summary>
    /// FOUR MISSED TELEPORTS ARE INVISIBLE, and that is stock's own limit rather
    /// than a bug introduced here: the counter is two bits, so a client that
    /// missed exactly a multiple of four jumps sees the value it already had.
    /// Recorded as a fact so the trade-off is stated rather than discovered.
    /// </summary>
    [Fact]
    public void ExactlyFourMissedTeleportsWrapToTheValueTheClientAlreadyHad()
    {
        byte[] block = Block();

        for (int i = 0; i < 4; i++)
            TeleportDiscontinuity.Bump(block, out _);

        Assert.False(TeleportDiscontinuity.HasTeleported(block));
    }

    /// <summary>
    /// THE OFFSET IS THE WIRE'S OFFSET. It is what the SendProp and the RecvProp
    /// are both built from and what the change mark announces; a mark computed
    /// one way and a prop built another matches no prop at all in
    /// <c>MapPropOffsetsToIndices</c>, and the value still arrives — by the slow
    /// path, for ever.
    /// </summary>
    [Fact]
    public void TheFrameOffsetIsTheHeadFieldsOwnOffset()
    {
        Assert.Equal(
            EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.InterpolationFrame)),
            TeleportDiscontinuity.FrameOffset);

        Assert.Equal(
            (int)Marshal.OffsetOf<EngineBlockHead>(nameof(EngineBlockHead.InterpolationFrame)),
            TeleportDiscontinuity.FrameOffset);
    }

    /// <summary>
    /// And the two words are distinct. One offset for both would make
    /// <see cref="TeleportDiscontinuity.Acknowledge"/> a no-op and
    /// <see cref="TeleportDiscontinuity.HasTeleported"/> always false — a build
    /// in which nothing ever teleports and nothing says so.
    /// </summary>
    [Fact]
    public void TheCounterAndTheAcknowledgementAreDifferentWords()
    {
        Assert.NotEqual(TeleportDiscontinuity.FrameOffset, TeleportDiscontinuity.SeenOffset);
        Assert.Equal(TeleportDiscontinuity.FrameOffset + sizeof(int), TeleportDiscontinuity.SeenOffset);
    }

    /// <summary>
    /// A bump touches ITS FOUR BYTES AND NO OTHERS. The block is one allocation
    /// shared with the engine's own storage — the server tail holds a
    /// <c>CPlayerState</c> the engine writes through — so a write that ran long
    /// would corrupt state nothing here can see.
    /// </summary>
    [Fact]
    public void ABumpWritesFourBytesAndLeavesTheRestOfTheBlockAlone()
    {
        byte[] block = Block();
        for (int i = 0; i < block.Length; i++)
            block[i] = 0x5A;

        byte[] before = (byte[])block.Clone();

        TeleportDiscontinuity.Bump(block, out _);

        for (int i = 0; i < block.Length; i++)
        {
            bool inField = i >= TeleportDiscontinuity.FrameOffset
                        && i < TeleportDiscontinuity.FrameOffset + sizeof(int);

            if (!inField)
                Assert.Equal(before[i], block[i]);
        }
    }
}

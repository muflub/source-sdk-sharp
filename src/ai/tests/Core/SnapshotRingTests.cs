using Xunit;

namespace SourceSharp.Ai.Tests.Core;

public class SnapshotRingTests
{
    private static readonly ActorId Bot = new(1, 1);

    private static SnapshotRing Ring() => new(8, 4, 4);

    private static void Publish(SnapshotRing ring, int tick)
    {
        AiSnapshot s = ring.BeginWrite(new Tick(tick));
        s.Add(Bot, default);
        ring.Publish(s);
    }

    /// <summary>
    /// A new level counts its ticks from low again: before a reset the ring
    /// refuses them (the live gate's second pass: after two map changes the
    /// senses threw every tick and no bot decided), after it they publish and
    /// the old level's are gone.
    /// </summary>
    [Fact]
    public void AfterAResetANewLevelsLowTicksPublish()
    {
        SnapshotRing ring = Ring();
        Publish(ring, 5000);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.BeginWrite(new Tick(12)));
        ring.Reset();
        Publish(ring, 12);
        Assert.True(ring.TryGet(new Tick(12), out _), "the new level's tick 12 is readable");
        Assert.False(ring.TryGet(new Tick(5000), out _), "the old level's tick is gone");
    }

    [Fact]
    public void APublishedTickIsReadable()
    {
        SnapshotRing ring = Ring();
        Publish(ring, 5);
        Assert.True(ring.TryGet(new Tick(5), out _), "tick 5 is readable");
    }

    [Fact]
    public void AnUnpublishedTickIsNotReadable()
    {
        SnapshotRing ring = Ring();
        ring.BeginWrite(new Tick(5));
        Assert.False(ring.TryGet(new Tick(5), out _), "a snapshot being written is not readable");
    }

    [Fact]
    public void ATickEightTicksOldHasLeftTheRing()
    {
        SnapshotRing ring = Ring();
        for (int t = 0; t <= 8; t++)
            Publish(ring, t);
        Assert.Equal((false, true), (ring.TryGet(new Tick(0), out _), ring.TryGet(new Tick(1), out _)));
    }

    [Fact]
    public void AnOlderTickCannotBeWrittenAfterANewerOne()
    {
        SnapshotRing ring = Ring();
        Publish(ring, 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.BeginWrite(new Tick(4)));
    }

    [Fact]
    public void ASlotWithNoBotHasNoView()
    {
        SnapshotRing ring = Ring();
        Publish(ring, 5);
        ring.TryGet(new Tick(5), out AiSnapshot s);
        Assert.Equal((true, false), (s.ViewFor(1) is not null, s.ViewFor(2) is not null));
    }

    [Fact]
    public void APublishedSnapshotRefusesMoreViews()
    {
        SnapshotRing ring = Ring();
        Publish(ring, 5);
        ring.TryGet(new Tick(5), out AiSnapshot s);
        Assert.Throws<InvalidOperationException>(() => s.Add(new ActorId(2, 1), default));
    }

    [Fact]
    public void RewritingASlotClearsItsOldViews()
    {
        SnapshotRing ring = Ring();
        Publish(ring, 1);
        AiSnapshot s = ring.BeginWrite(new Tick(9));
        ring.Publish(s);
        Assert.Null(s.ViewFor(1));
    }
}

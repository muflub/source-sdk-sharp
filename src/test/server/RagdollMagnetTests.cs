using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Where a ragdoll magnet reaches, and which way it pulls.
///
/// Worth testing because a mistake in it is invisible in play: a magnet that
/// measures the wrong distance simply does not catch the body, and a body that
/// is not caught falls the way it was always going to. There is nothing to
/// notice — which is also why the bar magnet's odd geometry is pinned here
/// rather than tidied. Its reach is a SQUARE cross-section and its caps are
/// inclusive, both of which look like bugs and are what every stock map was
/// built against.
///
/// <para>
/// The subject is a pure function of its arguments, so the split from one
/// 12-assertion <c>[Fact]</c> is one fact per geometry with nothing shared but
/// the origin.
/// </para>
/// </summary>
public class RagdollMagnetTests
{
    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.01f;

    private static bool Near(Vector a, Vector b) => (a - b).Length < 0.01f;

    private static readonly Vector Origin = new(0f, 0f, 0f);

    /// <summary>A bar magnet from the origin 100 units along X — a railing.</summary>
    private static readonly Vector BarEnd = new(100f, 0f, 0f);

    /// <summary>A VERTICAL bar — a stairwell.</summary>
    private static readonly Vector ShaftTop = new(0f, 0f, 200f);

    //-------------------------------------------------------------------------
    // A point magnet is a plain sphere: the distance is the distance.
    //-------------------------------------------------------------------------

    [Fact]
    public void APointMagnetMeasuresStraightLineDistance()
        => Assert.True(Near(PhysRagdollMagnet.DistanceTo(new Vector(30f, 40f, 0f), Origin, Vector.Zero, bar: false),
                 50f), "a point magnet measures straight-line distance");

    [Fact]
    public void APointMagnetPullsTowardsItselfAtFullStrength()
        => Assert.True(Near(PhysRagdollMagnet.PullOn(new Vector(0f, 0f, 100f), Origin, Vector.Zero, bar: false, force: 250f),
                 new Vector(0f, 0f, -250f)), "a point magnet pulls towards itself at full strength");

    //-------------------------------------------------------------------------
    // The bar magnet.
    //-------------------------------------------------------------------------

    [Fact]
    public void ABarMagnetMeasuresAcrossItsLineNotAlongIt()
        => Assert.True(Near(PhysRagdollMagnet.DistanceTo(new Vector(50f, 0f, 20f), Origin, BarEnd, bar: true),
                 20f), "a bar magnet measures across its line, not along it");

    [Fact]
    public void ABarMagnetsReachIsSquareNotRound()
    {
        // A point offset 20 in both directions is 28.28 away from the line and
        // 20 away from this magnet, because the larger of the two offsets is
        // what stock returns.
        Assert.True(Near(PhysRagdollMagnet.DistanceTo(new Vector(50f, 20f, 20f), Origin, BarEnd, bar: true),
                 20f), "a bar magnet's reach is square, not round");
    }

    [Fact]
    public void PastTheFarCapABarMagnetDoesNotReachAtAll()
        => Assert.True(PhysRagdollMagnet.DistanceTo(new Vector(140f, 0f, 5f), Origin, BarEnd, bar: true)
                == float.MaxValue, "past the far cap a bar magnet does not reach at all");

    [Fact]
    public void BehindTheNearCapABarMagnetDoesNotReachAtAll()
        => Assert.True(PhysRagdollMagnet.DistanceTo(new Vector(-40f, 0f, 5f), Origin, BarEnd, bar: true)
                == float.MaxValue, "behind the near cap a bar magnet does not reach at all");

    [Fact]
    public void LevelWithACapIsStillInsideTheBar()
    {
        // Inclusive caps: level with the end is inside, not outside.
        Assert.True(Near(PhysRagdollMagnet.DistanceTo(new Vector(100f, 0f, 12f), Origin, BarEnd, bar: true),
                 12f), "level with a cap is still inside the bar");
    }

    [Fact]
    public void ABarMagnetPullsSidewaysOntoItsLine()
        => Assert.True(Near(PhysRagdollMagnet.PullOn(new Vector(50f, 0f, 60f), Origin, BarEnd, bar: true, force: 100f),
                 new Vector(0f, 0f, -100f)), "a bar magnet pulls sideways onto its line");

    [Fact]
    public void PastTheEndABarMagnetPullsBackTowardsItsCap()
    {
        // Past the end, the pull aims at the cap rather than at the line
        // extended — the segment is clamped. Stock only asks this of points it
        // has already decided are within reach, but a magnet asked anyway must
        // not pull a body off the end of the railing.
        Assert.True(Near(PhysRagdollMagnet.PullOn(new Vector(200f, 0f, 0f), Origin, BarEnd, bar: true, force: 10f),
                 new Vector(-10f, 0f, 0f)), "past the end a bar magnet pulls back towards its cap");
    }

    //-------------------------------------------------------------------------
    // A VERTICAL bar is the case mathlib's VectorVectors has no world up to
    // cross against and answers with a fixed pair. The reach must still be
    // measured across the shaft.
    //-------------------------------------------------------------------------

    [Fact]
    public void AVerticalBarMagnetStillMeasuresAcrossItself()
        => Assert.True(Near(PhysRagdollMagnet.DistanceTo(new Vector(0f, 15f, 100f), Origin, ShaftTop, bar: true),
                 15f), "a vertical bar magnet still measures across itself");

    [Fact]
    public void AVerticalBarMagnetReachesTheSameDistanceEitherWayAcross()
        => Assert.True(Near(PhysRagdollMagnet.DistanceTo(new Vector(15f, 0f, 100f), Origin, ShaftTop, bar: true),
                 PhysRagdollMagnet.DistanceTo(new Vector(0f, 15f, 100f), Origin, ShaftTop, bar: true)), "a vertical bar magnet reaches the same distance either way across");

    //-------------------------------------------------------------------------
    // A bar with both ends in the same place, which is what a magnet sitting at
    // the world origin with no `axis` key comes to. Stock answers zero — its
    // normalize of a zero-length axis yields a zero vector and every measurement
    // off it collapses — so the magnet reads as touching everything and wins any
    // ranking it is in. Kept rather than corrected: it is a mis-keyed map's
    // behaviour, and changing it would change where bodies land in a map that
    // has been living with it.
    //-------------------------------------------------------------------------

    [Fact]
    public void ABarMagnetWithNoAxisReadsAsTouchingEverything()
        => Assert.True(Near(PhysRagdollMagnet.DistanceTo(new Vector(0f, 0f, 10f), Origin, Origin, bar: true), 0f), "a bar magnet with no axis reads as touching everything, as stock does");

    //-------------------------------------------------------------------------
    // The roster: the module's live magnets, which a magnet joins on spawn and
    // leaves on removal. It was a static list, one for the process.

    private static readonly EntityHandle Magnet = new(0x0051_0051u);

    private static ScriptedEntityRegistry Registry(IEntityRoster<PhysRagdollMagnet> magnets)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PhysRagdollMagnet).Assembly);
        registry.Services = new ServiceContainer()
            .AddInstance<IEntityRoster<PhysRagdollMagnet>>(magnets);
        return registry;
    }

    [Fact]
    public void AMagnetJoinsTheRosterItWasGivenAndLeavesItOnRemoval()
    {
        var magnets = new EntityRoster<PhysRagdollMagnet>();
        var registry = Registry(magnets);

        registry.Spawn(Magnet, EntityNames.PhysRagdollmagnet);
        Assert.True(magnets.Count == 1 && ReferenceEquals(magnets[0], registry.Get(Magnet)),
                    "a spawned magnet is in its module's roster");

        registry.Removed(Magnet);
        Assert.True(magnets.Count == 0, "and a removed one is not");
    }

    [Fact]
    public void TwoRostersDoNotShareMagnets()
    {
        var mine = new EntityRoster<PhysRagdollMagnet>();
        var theirs = new EntityRoster<PhysRagdollMagnet>();

        Registry(theirs).Spawn(Magnet, EntityNames.PhysRagdollmagnet);

        Assert.True(theirs.Count == 1, "the magnet joined the roster its module gave it");
        Assert.True(mine.Count == 0, "and no other");
        Assert.Null(PhysRagdollMagnet.Best(mine, new EntityHandle(0x0001_0001u), Origin));
    }
}

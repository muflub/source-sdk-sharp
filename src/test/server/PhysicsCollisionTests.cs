using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE PHYSICS EVENT RECORD'S LAYOUT, from the managed side.
///
/// <para>
/// physics_shell.cpp static_asserted the size and every offset from the C++
/// side, and this file read those asserts back, until plan_physics_bones_native_zero
/// 2.3 deleted the native ring and its record. Managed's callbacks and
/// PhysicsFrame build every record now; the numbers below are the layout the
/// record had, hand-copied at bc21e4d46.
/// </para>
/// </summary>
public class PhysicsEventLayoutTests
{
    [Fact]
    public void TheRecordIsOneHundredAndNinetySixBytes()
    {
        // 100 until physics stock parity C6 appended the impact-damage motion
        // tail: pre and post linear velocity, pre angular velocity and inertia,
        // per side (24 floats).
        Assert.Equal(196, Marshal.SizeOf<PhysicsEvent>());
    }

    [Fact]
    public void TheDeclaredSizeConstantAgreesWithTheActualSize()
    {
        // A constant nothing compares against is a comment. This is what makes
        // PhysicsEvent.SizeInBytes a fact.
        Assert.Equal(PhysicsEvent.SizeInBytes, Marshal.SizeOf<PhysicsEvent>());
    }

    [Theory]
    // The head, unchanged since step 3a.
    [InlineData("KindRaw", 0)]
    [InlineData("Flags", 4)]
    [InlineData("Entity0", 8)]
    [InlineData("Entity1", 12)]
    [InlineData("Object0", 16)]
    [InlineData("Object1", 20)]
    [InlineData("SurfaceProps0", 24)]
    [InlineData("SurfaceProps1", 28)]
    [InlineData("PointX", 32)]
    [InlineData("NormalX", 44)]
    [InlineData("Speed", 56)]
    [InlineData("Energy", 60)]
    // The collision tail, appended by lane p4-physics.
    [InlineData("DeltaCollisionTime", 64)]
    [InlineData("MoveType0", 68)]
    [InlineData("MoveType1", 72)]
    [InlineData("Mass0", 76)]
    [InlineData("Mass1", 80)]
    [InlineData("GameFlags0", 84)]
    [InlineData("GameFlags1", 88)]
    [InlineData("Static0", 92)]
    [InlineData("Static1", 96)]
    // The motion tail, appended by physics stock parity C6.
    [InlineData("PreVelocity0X", 100)]
    [InlineData("PostVelocity0X", 112)]
    [InlineData("PreAngularVelocity0X", 124)]
    [InlineData("Inertia0X", 136)]
    [InlineData("PreVelocity1X", 148)]
    [InlineData("PostVelocity1X", 160)]
    [InlineData("PreAngularVelocity1X", 172)]
    [InlineData("Inertia1X", 184)]
    public void EveryFieldIsWhereNativePutIt(string field, int offset)
    {
        Assert.Equal(offset, (int)Marshal.OffsetOf<PhysicsEvent>(field));
    }

    [Fact]
    public void NoEntityIsNotZero()
    {
        // Zero is a valid packed handle. "A default handle is entry zero" has
        // cost this project two lanes' measurements.
        Assert.NotEqual(0u, PhysicsEvent.NoEntity);
        Assert.Equal(0xFFFFFFFFu, PhysicsEvent.NoEntity);
    }

    [Fact]
    public void AZeroedRecordIsNotAKind()
    {
        PhysicsEvent ev = default;
        Assert.Equal(PhysicsEventKind.None, ev.Kind);
    }

    [Fact]
    public void AKindThisBuildDoesNotKnowIsNamedRatherThanCast()
    {
        var ev = new PhysicsEvent { KindRaw = PhysicsEvent.KindCount };
        Assert.Equal(PhysicsEventKind.Unknown, ev.Kind);
    }

    [Fact]
    public void TheKindCountIsSeventeen()
    {
        // Sixteen kinds and None: the count native's SS_PHYSEVENT_KIND_COUNT
        // had when the ring was deleted (bc21e4d46).
        Assert.Equal(17u, PhysicsEvent.KindCount);
    }

    [Fact]
    public void AnEventWithNoEntityOnEitherSideIsNotASelfCollision()
    {
        // Both sides "none" compare EQUAL, and a naive Entity0 == Entity1 would
        // call that a ragdoll hitting itself and suppress a real response for
        // half a second.
        var ev = new PhysicsEvent
        {
            Entity0 = PhysicsEvent.NoEntity,
            Entity1 = PhysicsEvent.NoEntity,
        };
        Assert.False(ev.IsSelfCollision);
    }

    [Fact]
    public void EntityZeroIsAnEntityAndNotAnAbsence()
    {
        var ev = new PhysicsEvent { Entity0 = 0u, Entity1 = 0u };
        Assert.True(ev.HasEntity0);
        Assert.True(ev.IsSelfCollision);
    }

}

/// <summary>
/// <see cref="PhysicsCollisionRules"/> — every clause of
/// <c>CBaseEntity::VPhysicsCollision</c> and the three functions under it, one
/// behaviour per fact.
/// </summary>
public class PhysicsCollisionRulesTests
{
    /// <summary>An impact that passes every gate, for a fact to spoil one clause of.</summary>
    private static PhysicsEvent Impact() => new()
    {
        KindRaw = (uint)PhysicsEventKind.PostCollision,
        Entity0 = 3,
        Entity1 = 4,
        MoveType0 = (int)MoveType.VPhysics,
        MoveType1 = (int)MoveType.None,
        Speed = 400f,
        DeltaCollisionTime = 1.0f,
        Mass0 = 800f,
        Mass1 = 0f,
        Static1 = 1,
        PointX = 10f, PointY = 20f, PointZ = 30f,
        NormalZ = 1f,
    };

    private const int Wood = 'W';

    //-- Responds ------------------------------------------------------------

    [Fact]
    public void AnImpactBetweenTwoOrdinaryThingsResponds()
    {
        Assert.True(PhysicsCollisionRules.Responds(Impact(), Wood, Wood));
    }

    [Fact]
    public void AKindOtherThanPostCollisionNeverResponds()
    {
        // The drain carries five kinds. Only one of them has a collision tail,
        // and the rest have a ZEROED one -- so a rule that read the tail without
        // checking the kind would read a start-touch as a stationary impact
        // between two massless objects and decide from it.
        PhysicsEvent ev = Impact();
        ev.KindRaw = (uint)PhysicsEventKind.StartTouch;
        Assert.False(PhysicsCollisionRules.Responds(ev, Wood, Wood));
    }

    [Fact]
    public void NeitherSideSimulatedByVPhysicsDoesNotRespond()
    {
        PhysicsEvent ev = Impact();
        ev.MoveType0 = (int)MoveType.Push;
        ev.MoveType1 = (int)MoveType.Walk;
        Assert.False(PhysicsCollisionRules.Responds(ev, Wood, Wood));
    }

    [Fact]
    public void EitherSideSimulatedByVPhysicsIsEnough()
    {
        PhysicsEvent ev = Impact();
        ev.MoveType0 = (int)MoveType.Push;
        ev.MoveType1 = (int)MoveType.VPhysics;
        Assert.True(PhysicsCollisionRules.Responds(ev, Wood, Wood));
    }

    [Fact]
    public void ARagdollHittingItselfTwiceInHalfASecondRespondsOnce()
    {
        PhysicsEvent ev = Impact();
        ev.Entity1 = ev.Entity0;
        ev.DeltaCollisionTime = 0.4f;
        Assert.False(PhysicsCollisionRules.Responds(ev, Wood, Wood));
    }

    [Fact]
    public void ARagdollHittingItselfAfterHalfASecondResponds()
    {
        PhysicsEvent ev = Impact();
        ev.Entity1 = ev.Entity0;
        ev.DeltaCollisionTime = 0.6f;
        Assert.True(PhysicsCollisionRules.Responds(ev, Wood, Wood));
    }

    [Fact]
    public void TwoDIFFERENTEntitiesAreNotFilteredOnTheHalfSecond()
    {
        // The self-collision interval is LONGER than the sound's own, and it
        // applies only when both sides are the same entity. A rule that applied
        // it to every pair would silence four fifths of a busy floor.
        PhysicsEvent ev = Impact();
        ev.DeltaCollisionTime = 0.4f;
        Assert.True(PhysicsCollisionRules.Responds(ev, Wood, Wood));
    }

    [Fact]
    public void AHiddenMaterialOnEitherSideIsSilent()
    {
        Assert.False(PhysicsCollisionRules.Responds(Impact(), PhysicsCollisionRules.MaterialHidden, Wood));
        Assert.False(PhysicsCollisionRules.Responds(Impact(), Wood, PhysicsCollisionRules.MaterialHidden));
    }

    //-- Sound ---------------------------------------------------------------

    [Fact]
    public void AnImpactBelowSeventyInchesPerSecondMakesNoSound()
    {
        PhysicsEvent ev = Impact();
        ev.Speed = 69.9f;
        Assert.Null(PhysicsCollisionRules.Sound(ev));
    }

    [Fact]
    public void AnImpactWithinFiftyMillisecondsOfTheLastMakesNoSound()
    {
        PhysicsEvent ev = Impact();
        ev.DeltaCollisionTime = 0.049f;
        Assert.Null(PhysicsCollisionRules.Sound(ev));
    }

    [Fact]
    public void TheVolumeIsQuadraticInSpeed()
    {
        // 160 in/s is HALF of full-volume speed and a quarter of the volume,
        // not half of it. A linear curve passes a "louder when faster" check
        // and is audibly wrong.
        PhysicsEvent ev = Impact();
        ev.Speed = 160f;
        CollisionSound? sound = PhysicsCollisionRules.Sound(ev);
        Assert.NotNull(sound);
        Assert.Equal(0.25f, sound!.Value.Volume, 4);
    }

    [Fact]
    public void TheVolumeIsClampedAtOne()
    {
        PhysicsEvent ev = Impact();
        ev.Speed = 4000f;
        Assert.Equal(1.0f, PhysicsCollisionRules.Sound(ev)!.Value.Volume, 4);
    }

    [Fact]
    public void ASelfCollisionSoundsOnTheBodyChannel()
    {
        PhysicsEvent ev = Impact();
        ev.Entity1 = ev.Entity0;
        Assert.Equal(CollisionSoundChannel.Body, PhysicsCollisionRules.Sound(ev)!.Value.Channel);
    }

    [Fact]
    public void TwoEntitiesSoundOnTheStaticChannel()
    {
        Assert.Equal(CollisionSoundChannel.Static, PhysicsCollisionRules.Sound(Impact())!.Value.Channel);
    }

    //-- Shake ---------------------------------------------------------------

    [Fact]
    public void AHeavyThingHittingAStaticThingShakesTheScreen()
    {
        Assert.NotNull(PhysicsCollisionRules.Shake(Impact(), 0));
    }

    [Fact]
    public void ALightThingDoesNotShakeTheScreen()
    {
        PhysicsEvent ev = Impact();
        ev.Mass0 = PhysicsCollisionRules.LargeObjectMass - 1f;
        Assert.Null(PhysicsCollisionRules.Shake(ev, 0));
    }

    [Fact]
    public void NothingShakesAgainstSomethingThatIsNotStatic()
    {
        PhysicsEvent ev = Impact();
        ev.Static1 = 0;
        Assert.Null(PhysicsCollisionRules.Shake(ev, 0));
    }

    [Fact]
    public void AnObjectStuckInsideAnotherDoesNotShakeTheScreen()
    {
        // FVPHYSICS_PENETRATING means the solver is EJECTING it, not that it
        // hit anything. Shaking here shakes the screen for a bug, repeatedly.
        PhysicsEvent ev = Impact();
        ev.GameFlags0 = PhysicsGameFlags.Penetrating;
        Assert.Null(PhysicsCollisionRules.Shake(ev, 0));
    }

    [Fact]
    public void ASlowImpactDoesNotShakeTheScreen()
    {
        PhysicsEvent ev = Impact();
        ev.Speed = PhysicsCollisionRules.ShakeMinSpeed;
        Assert.Null(PhysicsCollisionRules.Shake(ev, 0));
    }

    [Fact]
    public void AnImpactSoonAfterTheLastDoesNotShakeAgain()
    {
        PhysicsEvent ev = Impact();
        ev.DeltaCollisionTime = PhysicsCollisionRules.ShakeMinDelta;
        Assert.Null(PhysicsCollisionRules.Shake(ev, 0));
    }

    [Fact]
    public void TheShakeIsAskedAboutOneSideAtATime()
    {
        // Side 1 in this fixture is the static thing: massless, and the "other"
        // side of it is not static. Asking about it must answer no even though
        // asking about side 0 answers yes.
        Assert.NotNull(PhysicsCollisionRules.Shake(Impact(), 0));
        Assert.Null(PhysicsCollisionRules.Shake(Impact(), 1));
    }

    [Fact]
    public void TheAmplitudeIsStocksFormula()
    {
        // 800 kg at 400 in/s:
        //   impulse   = 400 * 800                       = 320000
        //   amplitude = 320000 * (0.2 / (30 * 500))     = 4.2666...
        CollisionShake shake = PhysicsCollisionRules.Shake(Impact(), 0)!.Value;
        Assert.Equal(320000f * (0.2f / (30f * 500f)), shake.Amplitude, 3);
    }

    [Fact]
    public void AVeryHeavyThingShakesNoHarderThanATwoTonneOne()
    {
        PhysicsEvent heavy = Impact();
        heavy.Mass0 = 50000f;

        PhysicsEvent ceiling = Impact();
        ceiling.Mass0 = PhysicsCollisionRules.LargeObjectMassCeiling;

        Assert.Equal(PhysicsCollisionRules.Shake(ceiling, 0)!.Value.Amplitude,
                     PhysicsCollisionRules.Shake(heavy, 0)!.Value.Amplitude, 3);
    }

    [Fact]
    public void TheShakeRadiusIsSixtyTimesItsAmplitude()
    {
        CollisionShake shake = PhysicsCollisionRules.Shake(Impact(), 0)!.Value;
        Assert.Equal(shake.Amplitude * 60f, shake.Radius, 3);
    }

    [Fact]
    public void TheShakeIsCentredOnTheContactPoint()
    {
        CollisionShake shake = PhysicsCollisionRules.Shake(Impact(), 0)!.Value;
        Assert.Equal(new Vector(10f, 20f, 30f), shake.Origin);
    }

    //-- Dust ----------------------------------------------------------------

    [Fact]
    public void SandDustsAboveTwoHundred()
    {
        Assert.True(PhysicsCollisionRules.Dust(PhysicsCollisionRules.MaterialSand, 200f));
        Assert.False(PhysicsCollisionRules.Dust(PhysicsCollisionRules.MaterialSand, 199f));
    }

    [Fact]
    public void DirtDustsAtSandsThreshold()
    {
        Assert.True(PhysicsCollisionRules.Dust(PhysicsCollisionRules.MaterialDirt, 200f));
        Assert.False(PhysicsCollisionRules.Dust(PhysicsCollisionRules.MaterialDirt, 199f));
    }

    [Fact]
    public void ConcreteNeedsMoreSpeedThanSand()
    {
        // The two thresholds are DIFFERENT numbers and a single one would pass
        // any test that only asked "does concrete dust".
        Assert.False(PhysicsCollisionRules.Dust(PhysicsCollisionRules.MaterialConcrete, 339f));
        Assert.True(PhysicsCollisionRules.Dust(PhysicsCollisionRules.MaterialConcrete, 340f));
    }

    [Fact]
    public void EveryOtherMaterialNeverDusts()
    {
        Assert.False(PhysicsCollisionRules.Dust(Wood, 100000f));
        Assert.False(PhysicsCollisionRules.Dust('M', 100000f));
        Assert.False(PhysicsCollisionRules.Dust(0, 100000f));
    }

    [Fact]
    public void MaterialZeroIsNotSand()
    {
        // An unknown surfaceprop answers Material 0, and 0 must never fall into
        // a material arm. This is the "a default is a valid value" trap, from
        // the surfaceprop end.
        Assert.NotEqual(0, PhysicsCollisionRules.MaterialSand);
        Assert.False(PhysicsCollisionRules.Dust(0, 5000f));
    }
}

/// <summary>
/// <see cref="PhysicsCollisionResponse"/> — the thin half, over fakes. What
/// these facts are about is DISPATCH: which events reach the rules, how many
/// times, and what comes out.
/// </summary>
public class PhysicsCollisionResponseTests
{
    /// <summary>
    /// The world, recorded. Three members, because the seam is three members
    /// wide — which is the point of the seam.
    /// </summary>
    private sealed class FakeWorld : IPhysicsCollisionWorld
    {
        public Dictionary<int, int> Materials { get; } = new();

        public List<CollisionShake> Shakes { get; } = new();

        public List<(Vector Origin, Vector Direction, float Size, float Speed)> Dusts { get; } = new();

        public MoveSurface Surface(int surfaceProps)
            => Materials.TryGetValue(surfaceProps, out int m)
                ? new MoveSurface { Valid = true, Material = m }
                : default;

        public void ScreenShake(in CollisionShake shake) => Shakes.Add(shake);

        public void DustEffect(Vector origin, Vector direction, float size, float speed)
            => Dusts.Add((origin, direction, size, speed));

        public List<(EntityHandle Entity, CollisionSoundChannel Channel, int Surface, int Hit, float Volume, float Speed)> Sounds { get; } = new();

        public void ImpactSound(EntityHandle entity, CollisionSoundChannel channel, int surfaceProps,
                                int surfacePropsHit, float volume, float speed)
            => Sounds.Add((entity, channel, surfaceProps, surfacePropsHit, volume, speed));
    }

    /// <summary>
    /// A heavy managed crate on side 0 hitting a static concrete floor on side
    /// 1, fast enough for both a shake and dust. The solver called it a
    /// collision, as PhysicsCollisionImpacts records for a real impact.
    /// </summary>
    private static PhysicsEvent Impact() => new()
    {
        KindRaw = (uint)PhysicsEventKind.PostCollision,
        Entity0 = 3,
        Entity1 = PhysicsEvent.NoEntity,
        SurfaceProps0 = 11,
        SurfaceProps1 = 12,
        MoveType0 = (int)MoveType.VPhysics,
        Speed = 400f,
        DeltaCollisionTime = 1.0f,
        Mass0 = 800f,
        Static1 = 1,
        PointX = 1f, PointY = 2f, PointZ = 3f,
        NormalZ = 1f,
        Flags = PhysicsEvent.FlagCollision,
    };

    //-------------------------------------------------------------------------
    // C3, the impact sound. Stock's CBaseEntity::VPhysicsCollision runs for each
    // side and calls PhysCollisionSound(this, pObjects[index], channel,
    // surfaceProps[index], surfaceProps[otherIndex], dt, speed): each entity
    // plays its own surface against the other's, on CHAN_BODY when it hit
    // itself and CHAN_STATIC otherwise, at speed^2/320^2 clamped to 1.
    //-------------------------------------------------------------------------
    [Fact]
    public void EachManagedSideQueuesItsOwnSurfaceAgainstTheOthers()
    {
        var r = New(out var world);
        var ev = Impact();
        ev.Entity1 = 4;
        ev.MoveType1 = (int)MoveType.VPhysics;
        r.Respond(in ev);

        Assert.Equal(2, world.Sounds.Count);
        Assert.Contains((new EntityHandle(3), CollisionSoundChannel.Static, 11, 12, 1f, 400f), world.Sounds);
        Assert.Contains((new EntityHandle(4), CollisionSoundChannel.Static, 12, 11, 1f, 400f), world.Sounds);
    }

    [Fact]
    public void ASideWithNoManagedEntityQueuesNoSound()
    {
        var r = New(out var world);
        r.Respond(Impact());

        var sound = Assert.Single(world.Sounds);
        Assert.Equal(new EntityHandle(3), sound.Entity);
    }

    [Fact]
    public void TheVolumeIsTheSquareOfTheSpeedOver320()
    {
        var r = New(out var world);
        var ev = Impact();
        ev.Speed = 160f;
        r.Respond(in ev);

        var sound = Assert.Single(world.Sounds);
        Assert.Equal(0.25f, sound.Volume, 4);
    }

    [Fact]
    public void ASelfCollisionPlaysOnTheBodyChannel()
    {
        var r = New(out var world);
        var ev = Impact();
        ev.Entity1 = 3;
        ev.MoveType1 = (int)MoveType.VPhysics;
        r.Respond(in ev);

        Assert.All(world.Sounds, s => Assert.Equal(CollisionSoundChannel.Body, s.Channel));
        Assert.NotEmpty(world.Sounds);
    }

    [Fact]
    public void ASlowImpactQueuesNoSound()
    {
        var r = New(out var world);
        var ev = Impact();
        ev.Speed = 60f;   // under stock's 70 in/s
        r.Respond(in ev);

        Assert.Empty(world.Sounds);
    }

    [Fact]
    public void EveryKindIsCountedEvenWhenNothingActsOnIt()
    {
        // Touch, wake and sleep have no consumer yet. Counting them is what
        // stops "there is a carrier and nobody knows" happening a second time --
        // which is the whole reason this lane exists.
        var r = New(out _);
        r.Respond(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.StartTouch });
        r.Respond(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Wake });
        r.Respond(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Wake });

        Assert.Equal(3, r.Events);
        Assert.Equal(1, r.SeenOfKind(PhysicsEventKind.StartTouch));
        Assert.Equal(2, r.SeenOfKind(PhysicsEventKind.Wake));
        Assert.Equal(0, r.SeenOfKind(PhysicsEventKind.PostCollision));
    }

    [Fact]
    public void ResetZeroesTheKindCountersToo()
    {
        var r = New(out _);
        r.Respond(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Wake });
        Assert.Equal(1, r.SeenOfKind(PhysicsEventKind.Wake));
        r.Reset();
        Assert.Equal(0, r.Events);
        Assert.Equal(0, r.SeenOfKind(PhysicsEventKind.Wake));
    }


    [Fact]
    public void AManagedCrateHittingAConcreteFloorShakesAndDusts()
    {
        var r = New(out FakeWorld world);
        r.Respond(Impact());

        Assert.Equal(1, r.Responded);
        Assert.Single(world.Shakes);
        Assert.Single(world.Dusts);
    }

    [Fact]
    public void TheDustIsThrownAgainstTheMaterialOfTheOTHERSide()
    {
        // The crate is wood and the floor is concrete. Reading the crate's own
        // material would dust against the crate and never against the floor,
        // which is a bug the game shows as "props never raise dust" and which
        // no shape of "does dust ever happen" test can see.
        var r = New(out FakeWorld world);
        world.Materials[12] = 'W';                      // the FLOOR is wood now
        world.Materials[11] = PhysicsCollisionRules.MaterialConcrete;   // the CRATE is concrete
        r.Respond(Impact());

        Assert.Empty(world.Dusts);
    }

    [Fact]
    public void ASideWithNoManagedEntityGetsNoResponseOfItsOwn()
    {
        // Side 1 of the fixture is the world: NoEntity. Stock runs its response
        // once per side, so a rule that ignored the entity would emit twice for
        // a crate hitting a floor.
        var r = New(out FakeWorld world);
        world.Materials[11] = PhysicsCollisionRules.MaterialConcrete;
        r.Respond(Impact());

        Assert.Single(world.Shakes);
    }

    [Fact]
    public void TwoManagedCratesEachGetTheirOwnResponse()
    {
        var r = New(out FakeWorld world);
        PhysicsEvent ev = Impact();
        ev.Entity1 = 4;
        ev.Mass1 = 800f;
        ev.Static0 = 1;                                  // each is static to the other
        r.Respond(in ev);

        Assert.Equal(2, world.Shakes.Count);
    }

    [Fact]
    public void AHiddenSurfaceProducesNothingAtAll()
    {
        var r = New(out FakeWorld world);
        world.Materials[12] = PhysicsCollisionRules.MaterialHidden;
        r.Respond(Impact());

        Assert.Equal(0, r.Responded);
        Assert.Empty(world.Shakes);
        Assert.Empty(world.Dusts);
    }

    [Fact]
    public void AnImpactSoundIsQueuedAndCounted()
    {
        // This was "decided and counted as unplayed" until the engine slot
        // landed (physics stock parity C3): the counter now counts what went.
        var r = New(out var world);
        r.Respond(Impact());
        Assert.Equal(1, r.SoundsQueued);
        Assert.Single(world.Sounds);
    }

    [Fact]
    public void AStartTouchNeverReachesTheCollisionRules()
    {
        // Every kind but PostCollision carries a ZEROED collision tail, so one
        // that reached the rules would read as a stationary impact between two
        // massless objects -- and Responds' movetype gate would be the only
        // thing between it and a dust puff at the origin.
        var r = New(out FakeWorld world);
        PhysicsEvent ev = Impact();
        ev.KindRaw = (uint)PhysicsEventKind.StartTouch;
        r.Respond(in ev);

        Assert.Equal(0, r.Responded);
        Assert.Empty(world.Shakes);
        Assert.Empty(world.Dusts);
        Assert.Equal(1, r.SeenOfKind(PhysicsEventKind.StartTouch));
    }

    [Fact]
    public void DustGoesUpForAFlatFloor()
    {
        var ev = new PhysicsEvent { NormalZ = 1f };
        Vector d = PhysicsCollisionResponse.DustDirection(ev);
        Assert.Equal(1f, d.Length, 4);
        Assert.True(d.Z > 0.99f);
    }

    [Fact]
    public void DustOffAWallLeansAwayFromTheWallAndUpward()
    {
        var ev = new PhysicsEvent { NormalX = 1f };
        Vector d = PhysicsCollisionResponse.DustDirection(ev);
        Assert.Equal(1f, d.Length, 4);
        Assert.True(d.X > 0f, "it leaves the wall");
        Assert.True(d.Z > 0f, "and it rises");
    }

    [Fact]
    public void ADownwardNormalIsFlippedRatherThanDrivenIntoTheGround()
    {
        var ev = new PhysicsEvent { NormalZ = -1f };
        Vector d = PhysicsCollisionResponse.DustDirection(ev);
        Assert.True(d.Z > 0f);
    }

    [Fact]
    public void ADegenerateNormalStillProducesAUnitVector()
    {
        // The engine ASSERTS on a direction that is not unit length, and a
        // record with no contact data carries a zeroed normal. Straight up is
        // the answer; a zero vector is a debug-build assert in the middle of a
        // physics frame.
        Vector d = PhysicsCollisionResponse.DustDirection(default);
        Assert.Equal(1f, d.Length, 4);
    }

    private static PhysicsCollisionResponse New(out FakeWorld world)
    {
        world = new FakeWorld();
        world.Materials[11] = 'W';
        world.Materials[12] = PhysicsCollisionRules.MaterialConcrete;
        return new PhysicsCollisionResponse(world);
    }
}

/// <summary>
/// <c>cs_sv_physics_events</c> — the managed half of the ring's measurement.
///
/// <para>
/// The reason it has facts at all: the number it prints is the only one that
/// can distinguish "the sink is bound" from "managed code ran". The native
/// counter increments BEFORE the call and the sink itself tests the ABI slot,
/// so a null slot leaves the native reading looking perfectly healthy.
/// </para>
/// </summary>
public class PhysicsEventsCommandTests
{
    private sealed class FakeConsole : IEngineConsole
    {
        public List<string> Lines { get; } = new();

        public void Msg(string message) => Lines.Add(message);

        public string All => string.Concat(Lines);

        public void ExecuteServerCommand(string command) { }
        public string? FindConVarValue(string name) => null;
        public string GetConVarValue(int id) => string.Empty;
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => 0;
        public int RegisterConVar(string name, string defaultValue, string help, int flags,
                                  float? min = null, float? max = null) => 0;
        public void SendClientCommand(EntityHandle player, string command) { }
        public void SetConVarValue(int id, string value) { }
        public void SetConVarValueByName(string name, string value) { }
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }

    private sealed class NullWorld : IPhysicsCollisionWorld
    {
        public MoveSurface Surface(int surfaceProps) => default;
        public void ScreenShake(in CollisionShake shake) { }
        public void DustEffect(Vector origin, Vector direction, float size, float speed) { }
        public void ImpactSound(EntityHandle entity, CollisionSoundChannel channel, int surfaceProps,
                                int surfacePropsHit, float volume, float speed) { }
    }

    private static (PhysicsCollisionResponse, FakeConsole, PhysicsEventsCommand) New()
    {
        var response = new PhysicsCollisionResponse(new NullWorld());
        var console = new FakeConsole();
        var collisionServices = TestServices.Services();
        var router = new PhysicsEventRouter(new ScriptedEntityRegistry(TestEngines.Server()), collisionServices.Require<PhysicsDamageQueue>(),
                                            collisionServices.Require<FluidTouches>(), collisionServices.Require<FrictionSounds>(), collisionServices.Require<PlayerShadowRecovery>(), collisionServices.Require<IGameClock>(), collisionServices.Require<PhysicsEntitySolver>());
        return (response, console, new PhysicsEventsCommand(response, router, console));
    }

    // The dev API's /counters?group=physics (debug-api lane, item 3): the
    // numbers cs_sv_physics_events prints, from the same objects, as data.
    private static long Counter(IReadOnlyList<Debug.Api.Server.Interfaces.DebugCounter> counters, string name)
        => counters.Single(c => c.Name == name).Value;

    [Fact]
    public void The_physics_counter_group_counts_events_by_kind()
    {
        var (response, _, _) = New();
        var router = new PhysicsEventRouter(new ScriptedEntityRegistry(TestEngines.Server()), TestServices.Services().Require<PhysicsDamageQueue>(),
                                            TestServices.Services().Require<FluidTouches>(), TestServices.Services().Require<FrictionSounds>(),
                                            TestServices.Services().Require<PlayerShadowRecovery>(), TestServices.Services().Require<IGameClock>(),
                                            TestServices.Services().Require<PhysicsEntitySolver>());
        response.Respond(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Wake });
        response.Respond(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Wake });
        response.Respond(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Sleep });

        var counters = DebugCounterGroups.Physics(response, router);
        Assert.Equal(3, Counter(counters, "events"));
        Assert.Equal(2, Counter(counters, "seen.Wake"));
    }

    [Fact]
    public void The_physics_counter_group_has_no_drains_counter()
    {
        // The ring that fed it is gone (plan_physics_bones_native_zero 2.3), so
        // a "drains" counter could only ever read 0: it went with Drain.
        var (response, _, _) = New();
        var router = new PhysicsEventRouter(new ScriptedEntityRegistry(TestEngines.Server()), TestServices.Services().Require<PhysicsDamageQueue>(),
                                            TestServices.Services().Require<FluidTouches>(), TestServices.Services().Require<FrictionSounds>(),
                                            TestServices.Services().Require<PlayerShadowRecovery>(), TestServices.Services().Require<IGameClock>(),
                                            TestServices.Services().Require<PhysicsEntitySolver>());
        var counters = DebugCounterGroups.Physics(response, router);

        Assert.Contains(counters, c => c.Name == "events");
        Assert.DoesNotContain(counters, c => c.Name == "drains");
    }

    [Fact]
    public void The_response_has_no_drain_entry()
    {
        // Respond is the one way in; a Drain with no producer was a second.
        Assert.NotNull(typeof(PhysicsCollisionResponse).GetMethod(nameof(PhysicsCollisionResponse.Respond)));
        Assert.Null(typeof(PhysicsCollisionResponse).GetMethod("Drain"));
        Assert.Null(typeof(PhysicsCollisionResponse).GetProperty("Drains"));
    }

    [Fact]
    public void TheReportPrintsNoDrainsLine()
    {
        var (_, console, command) = New();
        command.Execute("");

        Assert.Contains("events", console.All);
        Assert.DoesNotContain("drains", console.All);
    }

    private sealed class OpenSolverWorld : IEntitySolverWorld
    {
        public bool PairDisabled(EntityHandle a, EntityHandle b) => false;
        public void SetPairEnabled(EntityHandle a, EntityHandle b, bool enabled) { }
        public CollisionGroup GetGroup(EntityHandle entity) => CollisionGroup.None;
        public void SetGroup(EntityHandle entity, CollisionGroup group) { }
        public void RecheckContactPoints(EntityHandle entity) { }
        public bool Exists(EntityHandle entity) => true;
    }

    [Fact]
    public void The_physics_counter_group_counts_the_debris_entity_solvers()
    {
        // A9's live check reads these: stuck debris handed to the solver.
        var (response, _, _) = New();
        var solver = new PhysicsEntitySolver(new OpenSolverWorld(), TestServices.Services().Require<IGameClock>());
        var router = new PhysicsEventRouter(new ScriptedEntityRegistry(TestEngines.Server()), TestServices.Services().Require<PhysicsDamageQueue>(),
                                            TestServices.Services().Require<FluidTouches>(), TestServices.Services().Require<FrictionSounds>(),
                                            TestServices.Services().Require<PlayerShadowRecovery>(), TestServices.Services().Require<IGameClock>(),
                                            solver);
        solver.Solve(new EntityHandle(1), new EntityHandle(2), PhysicsEvent.SolverDebris1, 0f);

        var counters = DebugCounterGroups.Physics(response, router);
        Assert.Equal(1, Counter(counters, "entitySolversCreated"));
        Assert.Equal(1, Counter(counters, "entitySolversActive"));
    }

    [Fact]
    public void The_physics_counter_group_names_every_kind_even_at_zero()
    {
        var (response, _, _) = New();
        var router = new PhysicsEventRouter(new ScriptedEntityRegistry(TestEngines.Server()), TestServices.Services().Require<PhysicsDamageQueue>(),
                                            TestServices.Services().Require<FluidTouches>(), TestServices.Services().Require<FrictionSounds>(),
                                            TestServices.Services().Require<PlayerShadowRecovery>(), TestServices.Services().Require<IGameClock>(),
                                            TestServices.Services().Require<PhysicsEntitySolver>());
        var counters = DebugCounterGroups.Physics(response, router);
        foreach (PhysicsEventKind kind in Enum.GetValues<PhysicsEventKind>())
            Assert.Equal(0, Counter(counters, $"seen.{kind}"));
    }

    [Fact]
    public void WakeAndSleepAreConsumedNow()
    {
        // Physics stock parity step 3: the router hands them to the entity.
        var (response, console, command) = New();
        response.Respond(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Wake });
        response.Respond(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Sleep });
        command.Execute("");

        foreach (string kind in new[] { "Wake", "Sleep" })
        {
            string line = console.Lines.Single(l => l.TrimStart().StartsWith(kind));
            Assert.DoesNotContain("carried, nothing consumes it", line);
        }
    }

    [Fact]
    public void TheReportSaysHowManyPhysicsUpdatesWereDelivered()
    {
        var (_, console, command) = New();
        command.Execute("");

        Assert.Contains("updates delivered", console.All);
    }

    [Fact]
    public void ACarriedKindNobodyConsumesIsMarkedAsSuch()
    {
        // The whole lane is about a carrier with no consumer that nothing named.
        // Touch, wake and sleep are in that state today and the report says so
        // rather than listing them beside the ones that are acted on.
        var (response, console, command) = New();
        response.Respond(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.StartTouch });
        command.Execute("");

        Assert.Contains("StartTouch", console.All);
        Assert.Contains("carried, nothing consumes it", console.All);
    }

    [Fact]
    public void TheKindThatISConsumedIsNotMarkedAsCarried()
    {
        var (response, console, command) = New();
        response.Respond(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.PostCollision });
        command.Execute("");

        string line = console.Lines.Single(l => l.Contains("PostCollision"));
        Assert.DoesNotContain("carried, nothing consumes it", line);
    }

    [Fact]
    public void AKindThatNeverArrivedIsNotListedAtAll()
    {
        // A table of thirteen zeroes is a table nobody reads. Absence means
        // zero, the way Crossings' own report says it does.
        var (_, console, command) = New();
        command.Execute("");

        Assert.DoesNotContain("ConstraintBroken", console.All);
    }

    [Fact]
    public void TheQueuedImpactSoundsAreReportedAsANumber()
    {
        var (_, console, command) = New();
        command.Execute("");

        Assert.Contains("impact sounds", console.All);
    }

    [Fact]
    public void ResetZeroesTheReport()
    {
        var (response, console, command) = New();
        response.Respond(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Wake });
        command.Execute("reset");
        command.Execute("");

        Assert.Contains("counters zeroed", console.All);
        Assert.Contains("events           0", console.All);
        Assert.DoesNotContain("Wake", console.All);
    }
}

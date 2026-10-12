using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ClientTempEntFrame"/> — <c>C_LocalTempEntity::Frame</c>, and
/// behaviour ledger rows C4 and C5.
///
/// <para>
/// WHAT THESE ARE ABOUT AND WHAT THEY ARE NOT. <c>TempEntPoolTests</c> is the
/// LIST: allocation, eviction, expiry, the re-entrant spawn. These are the
/// PHYSICS: where a gib is after a frame, what it did when it hit a wall, and
/// which of stock's two trace filters it asked for. The two were one class
/// until this lane and the seam between them exists so that a test about a free
/// list does not have to set up a bounce factor.
/// </para>
///
/// <para>
/// FOUR OF THEM READ STOCK OFF DISK, which is the only kind of check that can
/// catch a wrong-but-consistent constant. A managed constant compared against a
/// managed constant agrees with itself whatever is in it; the numbers here are
/// compared against <c>c_te_legacytempents.cpp</c>, <c>bspflags.h</c> and
/// <c>sourcesharp.h</c> as they are in the tree. The fifth, the prop and door
/// sets against <c>props.cpp</c> and <c>doors.cpp</c>, left for
/// SourceSharp.OracleTests (TempEntFrameOracleTests) when the farm stopped
/// linking those two uncompiled files.
/// </para>
/// </summary>
public class TempEntFrameTests
{
    //-------------------------------------------------------------------------
    // The rig.
    //-------------------------------------------------------------------------

    /// <summary>A clock a test drives.</summary>
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    /// <summary>
    /// A client entity registry holding whatever a test puts in it, by index.
    ///
    /// <para>
    /// The real one fills its index table at creation from a networked entity;
    /// this one is handed the answers, because what row C4 is about is the
    /// QUESTION being asked and answered, not how the table got filled.
    /// </para>
    /// </summary>
    private sealed class Standalones : IClientStandalones
    {
        private readonly Dictionary<int, ClientStandaloneEntity> _byIndex = new();

        public Standalones At(int index, ClientStandaloneEntity entity)
        {
            _byIndex[index] = entity;
            return this;
        }

        public IReadOnlyList<ClientStandaloneEntity> Live() => _byIndex.Values.ToList();

        public ClientStandaloneEntity? GetByIndex(int entityIndex)
            => _byIndex.TryGetValue(entityIndex, out var e) ? e : null;
    }

    /// <summary>A managed client entity that is whatever classname it is told.</summary>
    private sealed class Named : ClientStandaloneEntity
    {
        private readonly string _className;

        public Named(IEngineEntityRender render, string className) : base(render)
            => _className = className;

        public override string ClassName => _className;
    }

    private readonly record struct Rig(
        ClientTempEntFrame Frame,
        RecordingEngine Engine,
        Clock Clock,
        Standalones Standalones, RecordingLog Log, BounceSounds Sounds);

    /// <summary>The bounce sounds the frame asked for, with the inputs it gave.</summary>
    private sealed class BounceSounds : ITempEntBounceSounds
    {
        public readonly List<(int HitSound, float Damp, Vector Velocity, Vector Origin)> Calls = [];

        public void PlaySound(int hitSound, float damp, in Vector velocity, in Vector origin)
            => Calls.Add((hitSound, damp, velocity, origin));
    }

    /// <summary>
    /// One tempent in slot 0, with the transform and statics a test hands it.
    ///
    /// <para>
    /// The engine double is STRICT, which is the point: anything the port reads
    /// that a test did not stub is an error naming the member, so a branch that
    /// quietly starts asking the engine a new question cannot pass by getting a
    /// zero. The members named below are the ones every step touches.
    /// </para>
    /// </summary>
    private static Rig Rigged(TempEntTransform transform, TempEntStatics statics,
                              float gravity = 600f, float now = 10f)
    {
        var log = new RecordingLog();
        var engine = Fake();

        engine.Returns("Effects.TempEntPoolReadTransform", true);
        engine.Returns("Effects.TempEntPoolReadTransform.Transform", transform);
        engine.Returns("Effects.TempEntPoolReadStatics", true);
        engine.Returns("Effects.TempEntPoolReadStatics.Statics", statics);
        engine.Returns("Effects.TempEntPoolWriteTransform", true);
        engine.Returns("Effects.CurrentGravity", gravity);

        var clock = new Clock { Now = now };
        var standalones = new Standalones();

        var sounds = new BounceSounds();

        return new Rig(new ClientTempEntFrame(log, engine, engine, engine, clock, sounds, standalones),
                       engine, clock, standalones, log, sounds);
    }

    /// <summary>
    /// <c>TempEntSpawn.Prepared</c>'s statics, which is the state a tempent is
    /// in the instant it is built: bounce factor ONE, no sound, no owner.
    /// </summary>
    private static TempEntStatics Prepared() => new()
    {
        BounceFactor = 1f,
        HitSound = 0,
        FrameMax = 0f,
        FrameRate = 0f,
        TentOffset = Vector.Zero,
        OwnerEntityIndex = -1,
        FlickerFrame = 0,
        HasImpactEffect = 0,
    };

    private static TempEntLife Live(TempEntFlags flags, float die = 1000f) => new()
    {
        Die = die,
        Flags = (int)flags,
        FadeSpeed = 0.5f,
        RenderAmt = 255,
        Priority = TempEntPriority.Low,
        ClientIndex = -1,
        RenderMode = (int)RenderMode.Normal,
        Alpha = 255,
    };

    /// <summary>The transform the port last wrote back, which is what native draws.</summary>
    private static TempEntTransform Written(RecordingEngine engine)
        => engine.Last("Effects.TempEntPoolWriteTransform")!.Value.ArgAs<TempEntTransform>(1);

    private static bool Near(float a, float b, float tolerance = 0.0005f)
        => MathF.Abs(a - b) <= tolerance;

    /// <summary>Runs a body with the log captured, and hands back what was said.</summary>
    private static List<string> Saying(Rig rig, Action body)
    {
        // What the frame said: its own log, the rig's (statics to zero).
        int before = rig.Log.Lines.Count;
        body();
        return rig.Log.Lines.Skip(before).Select(l => l.Text).ToList();
    }

    //-------------------------------------------------------------------------
    // Integration.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A tempent with no flags at all moves by its velocity and by nothing
    /// else — no gravity, because it did not ask for any.
    /// </summary>
    [Fact]
    public void APlainTempEntMovesByItsVelocityAndFallsNowhere()
    {
        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(10f, 20f, 30f),
        }, Prepared());

        var life = Live(TempEntFlags.None);

        Assert.True(rig.Frame.Step(0, 0.5f, 0, 1, ref life), "nothing about it asked to die");

        var t = Written(rig.Engine);

        Assert.True(Near(t.Origin.X, 5f) && Near(t.Origin.Y, 10f) && Near(t.Origin.Z, 115f),
            $"it moved by velocity * frametime and no further: {t.Origin}");
        Assert.True(Near(t.Velocity.Z, 30f),
            $"and nothing pulled it down, because FTENT_GRAVITY is not set: {t.Velocity.Z}");
    }

    /// <summary>
    /// Acceleration is added to the velocity BEFORE the move, so the frame it
    /// arrives on is already faster. That ordering is stock's first two
    /// statements and is invisible to a test that only checks the velocity.
    /// </summary>
    [Fact]
    public void TheAccelerationIsAppliedBeforeTheMoveAndNotAfterIt()
    {
        var rig = Rigged(new TempEntTransform
        {
            Origin = Vector.Zero,
            Velocity = new Vector(0f, 0f, 0f),
            Acceleration = new Vector(100f, 0f, 0f),
        }, Prepared());

        var life = Live(TempEntFlags.None);
        rig.Frame.Step(0, 0.5f, 0, 1, ref life);

        var t = Written(rig.Engine);

        Assert.True(Near(t.Velocity.X, 50f), $"the velocity took the acceleration: {t.Velocity.X}");
        Assert.True(Near(t.Origin.X, 25f),
            $"and the move used the NEW velocity, not the old one -- 25 units, not 0: {t.Origin.X}");
    }

    /// <summary>
    /// A <c>FTENT_GRAVITY</c> tempent is pulled down by the engine's own
    /// gravity, and a <c>FTENT_SLOWGRAVITY</c> one by half of it.
    /// </summary>
    [Fact]
    public void GravityComesFromTheEngineAndSlowGravityIsHalfOfIt()
    {
        var rig = Rigged(new TempEntTransform(), Prepared(), gravity: 800f);
        var life = Live(TempEntFlags.Gravity);

        rig.Frame.Step(0, 0.25f, 0, 1, ref life);

        Assert.True(Near(Written(rig.Engine).Velocity.Z, -200f),
            $"-frametime * GetCurrentGravity(): {Written(rig.Engine).Velocity.Z}");

        var slow = Rigged(new TempEntTransform(), Prepared(), gravity: 800f);
        var slowLife = Live(TempEntFlags.SlowGravity);

        slow.Frame.Step(0, 0.25f, 0, 1, ref slowLife);

        Assert.True(Near(Written(slow.Engine).Velocity.Z, -100f),
            $"and FTENT_SLOWGRAVITY is exactly half: {Written(slow.Engine).Velocity.Z}");
    }

    /// <summary>
    /// A sine-wave tempent oscillates about its ACCUMULATORS and advances them,
    /// which is why they are in the transform rather than only in the spawn
    /// record.
    /// </summary>
    [Fact]
    public void ASineWaveTempEntAdvancesItsAccumulatorsAndWandersAboutThem()
    {
        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(500f, 500f, 0f),
            Velocity = new Vector(10f, 20f, 4f),
            SineX = 100f,
            SineY = 200f,
            SpriteScale = 1f,
        }, Prepared(), now: 0f);

        var life = Live(TempEntFlags.SineWave);
        rig.Frame.Step(0, 0.5f, 0, 1, ref life);

        var t = Written(rig.Engine);

        Assert.True(Near(t.SineX, 105f) && Near(t.SineY, 210f),
            $"the accumulators moved by velocity * frametime: {t.SineX}, {t.SineY}");

        // curtime is 0, so the first term is sin( vz ) = sin( 4 ) and the second
        // sin( vz + 0 * 5.5 + 0.7 ) = sin( 4.7 ). Computed here from the SAME
        // numbers the port has, which is a shape this suite's own notes warn
        // about -- so what it pins is that the accumulator is the CENTRE and the
        // wobble is added to it, not the origin the tempent came in with.
        Assert.True(MathF.Abs(t.Origin.X - 105f) <= 10f,
            $"and the place is the accumulator plus a wobble of at most 10 * scale, "
          + $"NOT the 500 it came in with: {t.Origin.X}");
        Assert.True(MathF.Abs(t.Origin.Y - 210f) <= 8f,
            $"likewise on Y, at most 8 * scale: {t.Origin.Y}");
        Assert.True(Near(t.Origin.Z, 2f),
            $"and Z rises by velocity.z * frametime with no wobble at all: {t.Origin.Z}");
    }

    /// <summary>
    /// An animating sprite that runs off its last frame ASKS TO DIE, and one
    /// set to loop wraps instead. That return value is the only thing
    /// <c>Frame</c> answers, and it is what frees the tempent.
    /// </summary>
    [Fact]
    public void AnAnimatingSpriteRunsOffItsLastFrameAndAsksToDieUnlessItLoops()
    {
        var statics = Prepared();
        statics.FrameMax = 4f;
        statics.FrameRate = 10f;

        var rig = Rigged(new TempEntTransform { Frame = 3.5f }, statics);
        var life = Live(TempEntFlags.SprAnimate);

        Assert.True(!rig.Frame.Step(0, 0.5f, 0, 1, ref life),
            "3.5 + 0.5 * 10 is past a FrameMax of 4, and this one does not loop");
        Assert.True(life.Die == 0f, $"and it set die to zero on the way out: {life.Die}");

        var looping = Rigged(new TempEntTransform { Frame = 3.5f }, statics);
        var loopLife = Live(TempEntFlags.SprAnimate | TempEntFlags.SprAnimateLoop);

        Assert.True(looping.Frame.Step(0, 0.5f, 0, 1, ref loopLife),
            "with FTENT_SPRANIMATELOOP it survives");
        Assert.True(Near(Written(looping.Engine).Frame, 0.5f),
            $"and wraps to the fractional part, C's truncation toward zero: "
          + $"{Written(looping.Engine).Frame}");
    }

    /// <summary>
    /// A <c>FTENT_ROTATE</c> gib tumbles at its angular velocity, and a
    /// <c>FTENT_ALIGNTOMOTION</c> one points where it is going instead. They
    /// are the two arms of one <c>else</c>, so a port that ran both would be
    /// invisible to a test that only checked one.
    /// </summary>
    [Fact]
    public void RotateTumblesAndAlignToMotionPointsWhereItIsGoing()
    {
        var rig = Rigged(new TempEntTransform
        {
            Angles = new QAngle(10f, 20f, 30f),
            AngVelocity = new QAngle(100f, 200f, 300f),
        }, Prepared());

        var life = Live(TempEntFlags.Rotate);
        rig.Frame.Step(0, 0.1f, 0, 1, ref life);

        var t = Written(rig.Engine);

        Assert.True(Near(t.Angles.Pitch, 20f) && Near(t.Angles.Yaw, 40f) && Near(t.Angles.Roll, 60f),
            $"angles + angular velocity * frametime, all three: {t.Angles}");

        var aligned = Rigged(new TempEntTransform
        {
            Angles = new QAngle(10f, 20f, 30f),
            Velocity = new Vector(0f, 100f, 0f),
            AngVelocity = new QAngle(100f, 200f, 300f),
        }, Prepared());

        var alignedLife = Live(TempEntFlags.AlignToMotion);
        aligned.Frame.Step(0, 0.1f, 0, 1, ref alignedLife);

        var a = Written(aligned.Engine).Angles;

        Assert.True(Near(a.Pitch, 0f) && Near(a.Yaw, 90f) && Near(a.Roll, 0f),
            $"VectorAngles of (0,100,0) is a yaw of 90 and nothing else -- and the angular "
          + $"velocity was NOT also applied, because the two are one else: {a}");
    }

    /// <summary>
    /// A player attachment sits at the player plus its offset, and a client
    /// index that names nobody leaves it where it was — which is stock's own
    /// <c>if ( IClientEntity *pClient = ... )</c> and not an error.
    /// </summary>
    [Fact]
    public void APlayerAttachmentFollowsThePlayerPlusItsOffsetOrStaysPut()
    {
        var statics = Prepared();
        statics.TentOffset = new Vector(0f, 0f, 40f);

        var rig = Rigged(new TempEntTransform { Origin = new Vector(1f, 2f, 3f) }, statics);
        rig.Engine.Returns("Effects.ClientEntityOrigin", true);
        rig.Engine.Returns("Effects.ClientEntityOrigin.Origin", new Vector(500f, 600f, 0f));

        var life = Live(TempEntFlags.PlyrAttachment);
        life.ClientIndex = 2;

        rig.Frame.Step(0, 0.1f, 0, 1, ref life);

        var t = Written(rig.Engine);

        Assert.True(Near(t.Origin.X, 500f) && Near(t.Origin.Y, 600f) && Near(t.Origin.Z, 40f),
            $"the player's origin plus tentOffset, which is where AttachTentToPlayer put it: "
          + $"{t.Origin}");

        var gone = Rigged(new TempEntTransform { Origin = new Vector(1f, 2f, 3f) }, statics);
        gone.Engine.Returns("Effects.ClientEntityOrigin", false);
        gone.Engine.Returns("Effects.ClientEntityOrigin.Origin", Vector.Zero);

        var goneLife = Live(TempEntFlags.PlyrAttachment);
        goneLife.ClientIndex = 2;

        Assert.True(gone.Frame.Step(0, 0.1f, 0, 1, ref goneLife),
            "a player who has left is not an error");
        Assert.True(Near(Written(gone.Engine).Origin.Z, 3f),
            $"and the tempent simply does not move: {Written(gone.Engine).Origin}");
    }

    //-------------------------------------------------------------------------
    // The collision arm.
    //-------------------------------------------------------------------------

    private static ClientTraceResult Hit(float fraction, Vector end, Vector normal,
                                         int entityIndex = 0, int surfaceFlags = 0)
        => new(true, fraction, end, normal, 0, entityIndex, 42, surfaceFlags);

    private static ClientTraceResult Miss(Vector end)
        => new(false, 1f, end, Vector.Zero, 0, -1, -1, 0);

    /// <summary>
    /// <c>FTENT_COLLIDEWORLD</c> traces WORLD-ONLY and with MASK_SOLID, from
    /// where the tempent was to where it now is.
    ///
    /// <para>
    /// The filter is the half that matters: a hit-all trace here would stop a
    /// gib on a player, which stock's world-only filter cannot do. It is why
    /// the trace has named modes at all.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWorldCollisionTracesWorldOnlyWithMaskSolidFromWhereItWas()
    {
        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
        }, Prepared());

        rig.Engine.Returns("WorldQuery.ClientTraceLine", Miss(new Vector(0f, 0f, 90f)));

        var life = Live(TempEntFlags.CollideWorld);
        rig.Frame.Step(0, 0.1f, 0, 1, ref life);

        Assert.True(rig.Engine.CountOf("WorldQuery.ClientTraceLine") == 1, "one trace");

        var call = rig.Engine.Last("WorldQuery.ClientTraceLine")!.Value;

        Assert.True(call.ArgAs<Vector>(0).Z == 100f,
            $"from where it WAS, not from where it now is: {call.ArgAs<Vector>(0)}");
        Assert.True(Near(call.ArgAs<Vector>(1).Z, 90f),
            $"to where the velocity has just put it: {call.ArgAs<Vector>(1)}");
        Assert.True(call.ArgAs<TraceMask>(2) == TraceMask.Solid,
            $"MASK_SOLID, which is what stock traces with and is not MASK_SHOT: "
          + $"{call.ArgAs<TraceMask>(2)}");
        Assert.True(call.ArgAs<TraceFilter>(3) == TraceFilter.WorldOnly,
            $"and CTraceFilterWorldOnly, which is the whole point of the mode existing: "
          + $"{call.ArgAs<TraceFilter>(3)}");
    }

    /// <summary>
    /// <c>FTENT_COLLIDEALL</c> traces with the SIMPLE filter and passes the
    /// tempent's OWNER through it, so a projectile does not hit whoever fired
    /// it on its first frame.
    /// </summary>
    [Fact]
    public void TheCollideAllTraceUsesTheSimpleFilterAndPassesTheOwnerThrough()
    {
        var statics = Prepared();
        statics.OwnerEntityIndex = 7;

        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
        }, statics);

        rig.Engine.Returns("WorldQuery.ClientTraceLine", Miss(new Vector(0f, 0f, 90f)));

        var life = Live(TempEntFlags.CollideAll);
        rig.Frame.Step(0, 0.1f, 0, 1, ref life);

        var call = rig.Engine.Last("WorldQuery.ClientTraceLine")!.Value;

        Assert.True(call.ArgAs<TraceFilter>(3) == TraceFilter.Simple,
            $"CTraceFilterSimple, which is NOT hit-all with one entity removed -- it also "
          + $"applies collision groups and the game rules: {call.ArgAs<TraceFilter>(3)}");
        Assert.True(call.ArgAs<int>(4) == 7,
            $"and the owner goes through it as an INDEX, never a handle (row C8): "
          + $"{call.ArgAs<int>(4)}");
    }

    /// <summary>
    /// A gib that hits a wall is put at the impact point, reflects, and loses
    /// speed to its bounce factor — halved, because it is a falling one.
    /// </summary>
    [Fact]
    public void AGibBouncesOffAWallAndLosesSpeedToItsBounceFactor()
    {
        var statics = Prepared();
        statics.BounceFactor = 0.8f;

        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            // The settle window is `vz >= gravity * 3`, and this frame's gravity
            // is -60, so anything slower than -180 lands rather than bounces.
            // -500 is well outside it.
            Velocity = new Vector(0f, 0f, -500f),
        }, statics, gravity: 600f);

        // A floor, hit half way, and much too fast to settle on it.
        rig.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(0f, 0f, 1f)));

        var life = Live(TempEntFlags.CollideWorld | TempEntFlags.Gravity);
        rig.Frame.Step(0, 0.1f, 0, 1, ref life);

        var t = Written(rig.Engine);

        Assert.True(Near(t.Origin.Z, 95f), $"it is put at the impact point: {t.Origin.Z}");

        // damp = 0.8 * 0.5 = 0.4 (halved because it is a gravity tempent).
        // Reflected: -500 -> +500. Damped: 500 * 0.4 = 200.
        Assert.True(Near(t.Velocity.Z, 200f),
            $"reflected through the floor normal and damped by bounceFactor * 0.5: {t.Velocity.Z}");
        Assert.True((life.Flags & (int)TempEntFlags.Gravity) != 0,
            "it is still falling -- it bounced, it did not settle");
    }

    /// <summary>
    /// A gib that lands SLOWLY on a floor stops dead: no bounce, no rotation,
    /// no more tracing, and pitch and roll flattened.
    ///
    /// <para>
    /// This is the branch that makes a pile of gibs lie still instead of
    /// jittering, and it is four flag clears and two angle writes that a test
    /// asserting only the velocity would miss.
    /// </para>
    /// </summary>
    [Fact]
    public void AGibThatLandsSlowlyOnAFloorStopsDeadAndLiesFlat()
    {
        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Angles = new QAngle(45f, 60f, 75f),
            // gravity for this frame is -600 * 0.1 = -60, so `>= gravity * 3`
            // is >= -180: this is slow enough to settle.
            Velocity = new Vector(0f, 0f, -20f),
        }, Prepared(), gravity: 600f);

        rig.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 99f), new Vector(0f, 0f, 1f)));

        var life = Live(TempEntFlags.CollideWorld | TempEntFlags.Gravity | TempEntFlags.Rotate);
        rig.Frame.Step(0, 0.1f, 0, 1, ref life);

        var t = Written(rig.Engine);

        Assert.True(Near(t.Velocity.Z, 0f),
            $"damp is zero, so it neither reflects nor slides: {t.Velocity.Z}");

        foreach (var cleared in new[]
                 {
                     TempEntFlags.Rotate, TempEntFlags.Gravity, TempEntFlags.SlowGravity,
                     TempEntFlags.CollideWorld, TempEntFlags.SmokeTrail,
                 })
        {
            Assert.True((life.Flags & (int)cleared) == 0,
                $"{cleared} is cleared when a gib settles, or it goes on spinning where it lies");
        }

        Assert.True(Near(t.Angles.Pitch, 0f) && Near(t.Angles.Roll, 0f),
            $"pitch and roll are flattened so it lies on the floor: {t.Angles}");

        // AND THE YAW IS SCALED, NOT ZEROED, which is a corner of stock's own
        // control flow rather than an obvious rule: a settled gib has damp 0, so
        // the reflection is skipped -- but `if ( damp != 1 )` is a SEPARATE test
        // and still fires, scaling all three angles by 0.9. Sixty becomes
        // fifty-four. A port that folded the two tests into an if/else would
        // leave the yaw at sixty and look perfectly reasonable.
        Assert.True(Near(t.Angles.Yaw, 54f),
            $"the yaw is 60 * 0.9, because damp != 1 fires even when damp is 0: {t.Angles.Yaw}");
    }

    /// <summary>
    /// Gravity is NOT applied on a frame the tempent collided on. Stock's
    /// <c>if ( traceFraction == 1 )</c>, and without it a settled gib is pushed
    /// through the floor it just landed on.
    /// </summary>
    [Fact]
    public void AFrameThatCollidedAppliesNoGravity()
    {
        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
        }, Prepared(), gravity: 600f);

        // A WALL, not a floor: normal.z is 0, so the settle branch cannot fire
        // and the only thing that can change the Z velocity is the reflection.
        rig.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(1f, 0f, 0f)));

        var life = Live(TempEntFlags.CollideWorld | TempEntFlags.Gravity);
        rig.Frame.Step(0, 0.1f, 0, 1, ref life);

        // damp = 1 * 0.5. The normal is on X, so Z reflects to nothing and is
        // scaled: -100 * 0.5 = -50. With gravity it would be -110.
        Assert.True(Near(Written(rig.Engine).Velocity.Z, -50f),
            $"damped only, with no -60 of gravity added on top: "
          + $"{Written(rig.Engine).Velocity.Z}");
    }

    /// <summary>
    /// The bounce SOUND is played when the tempent has one and not when it does
    /// not — which is stock's <c>if ( hitSound )</c>, and is the difference
    /// between a shell casing that tinkles and one that is silent.
    /// </summary>
    [Fact]
    public void TheBounceSoundIsPlayedOnlyWhenTheTempEntHasOneAndCarriesTheDamping()
    {
        var statics = Prepared();
        statics.HitSound = 6;
        statics.BounceFactor = 0.6f;

        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
        }, statics);

        rig.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(1f, 0f, 0f)));

        var life = Live(TempEntFlags.CollideWorld);
        rig.Frame.Step(0, 0.1f, 0, 1, ref life);

        Assert.True(rig.Sounds.Calls.Count == 1, $"it made a noise: {rig.Sounds.Calls.Count} call(s)");

        var call = rig.Sounds.Calls[0];

        Assert.True(call.HitSound == 6, $"with its own hit sound: {call.HitSound}");
        Assert.True(Near(call.Damp, 0.6f),
            $"and the DAMPING the arm computed goes with it, which is what stock's own call "
          + $"site passes: {call.Damp}");
        var silent = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
        }, Prepared());

        silent.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(1f, 0f, 0f)));

        var silentLife = Live(TempEntFlags.CollideWorld);
        silent.Frame.Step(0, 0.1f, 0, 1, ref silentLife);

        Assert.True(silent.Sounds.Calls.Count == 0,
            "and a tempent with hitSound 0 makes none");
    }

    /// <summary>
    /// The bounce sound hears what stock's <c>PlaySound( this, damp )</c> read
    /// off the tempent at that line of <c>C_LocalTempEntity::Frame</c>: the
    /// origin already moved to the trace's end (<c>SetLocalOrigin( trace.endpos )</c>
    /// is above the call) and the velocity BEFORE the bounce reflects and damps
    /// it (that is below the call).
    /// </summary>
    [Fact]
    public void TheBounceSoundHearsTheContactPointAndTheVelocityBeforeTheBounce()
    {
        var statics = Prepared();
        statics.HitSound = 6;
        statics.BounceFactor = 0.6f;

        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(30f, 0f, -100f),
        }, statics);

        rig.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(1f, 0f, 0f)));

        var life = Live(TempEntFlags.CollideWorld);
        rig.Frame.Step(0, 0.1f, 0, 1, ref life);

        Assert.True(rig.Sounds.Calls.Count == 1, $"it made a noise: {rig.Sounds.Calls.Count} call(s)");

        var call = rig.Sounds.Calls[0];

        Assert.True(Near(call.Origin.Z, 95f) && Near(call.Origin.X, 0f),
            $"at the contact point, where stock had already moved it: {call.Origin}");
        Assert.True(Near(call.Velocity.X, 30f) && Near(call.Velocity.Z, -100f),
            $"with the velocity it hit at, before the reflection off +X and the damping: {call.Velocity}");
    }

    /// <summary>
    /// <c>FTENT_ATTACHTOTARGET</c> sticks to the world and DIES on the skybox,
    /// which is the one surface bit this arm tests.
    /// </summary>
    [Fact]
    public void AttachToTargetSticksToTheWorldAndDiesOnTheSky()
    {
        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
            Acceleration = new Vector(0f, 0f, -10f),
        }, Prepared());

        rig.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(0f, 0f, 1f), entityIndex: 0));

        var life = Live(TempEntFlags.CollideAll | TempEntFlags.AttachToTarget);
        rig.Frame.Step(0, 0.1f, 0, 1, ref life);

        var t = Written(rig.Engine);

        Assert.True(t.Velocity.Z == 0f && t.Acceleration.Z == 0f,
            $"it stops moving entirely: {t.Velocity}, {t.Acceleration}");
        Assert.True((life.Flags & (int)(TempEntFlags.CollideAll | TempEntFlags.CollideWorld)) == 0,
            "and stops tracing, because it is not going anywhere again");
        Assert.True(life.Die == 1000f, "and it lives out its life where it stuck");

        var sky = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, 100f),
        }, Prepared(), now: 55f);

        sky.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 105f), new Vector(0f, 0f, -1f),
                entityIndex: 0, surfaceFlags: ClientTempEntFrame.SurfSky));

        var skyLife = Live(TempEntFlags.CollideAll | TempEntFlags.AttachToTarget);
        sky.Frame.Step(0, 0.1f, 0, 1, ref skyLife);

        Assert.True(skyLife.Die == 55f,
            $"a tempent that hit the skybox has nothing to attach to and dies now: {skyLife.Die}");
        Assert.True((skyLife.Flags & (int)TempEntFlags.FadeOut) == 0,
            "and does not fade out on the way");
    }

    /// <summary>
    /// <c>FTENT_COLLIDEKILL</c> dies on impact and does not fade.
    /// </summary>
    [Fact]
    public void CollideKillDiesOnImpactWithoutFading()
    {
        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
        }, Prepared(), now: 33f);

        rig.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(0f, 0f, 1f)));

        var life = Live(TempEntFlags.CollideWorld | TempEntFlags.CollideKill | TempEntFlags.FadeOut);
        rig.Frame.Step(0, 0.1f, 0, 1, ref life);

        Assert.True(life.Die == 33f, $"die = curtime: {life.Die}");
        Assert.True((life.Flags & (int)TempEntFlags.FadeOut) == 0, "and FTENT_FADEOUT is cleared");
    }

    //-------------------------------------------------------------------------
    // Behaviour ledger row C4: the two casts, asked.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A <c>FTENT_COLLIDEPROPS</c> gib PASSES THROUGH a managed entity that is
    /// neither a prop nor a door, and BOUNCES OFF one that is.
    ///
    /// <para>
    /// This is behaviour ledger row C4 in one test, and both directions are
    /// here because only the pair can fail: with both casts hardcoded false —
    /// which is what this build did — every entity read as "not a prop", so the
    /// gib bounced off all of them. A test that only checked the prop would
    /// pass against a port that answered "prop" to everything.
    /// </para>
    /// </summary>
    [Fact]
    public void ACollidePropsGibBouncesOffAManagedPropAndPassesThroughAManagedPlayer()
    {
        // A PLAYER: neither cast, so stock lets the gib through untouched.
        var through = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
        }, Prepared());

        through.Standalones.At(3, new Named(through.Engine, EntityNames.SsPlayer));
        through.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(0f, 0f, 1f), entityIndex: 3));

        var throughLife = Live(TempEntFlags.CollideProps);
        through.Frame.Step(0, 0.1f, 0, 1, ref throughLife);

        Assert.True(Near(Written(through.Engine).Velocity.Z, -100f),
            $"a player is not a prop and not a door, so the gib carries on through it "
          + $"unreflected: {Written(through.Engine).Velocity.Z}");

        // A PROP: stock's first cast, so the gib bounces.
        var bounced = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
        }, Prepared());

        bounced.Standalones.At(3, new Named(bounced.Engine, EntityNames.PropDynamicOrnament));
        bounced.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(0f, 0f, 1f), entityIndex: 3));

        var bouncedLife = Live(TempEntFlags.CollideProps);
        bounced.Frame.Step(0, 0.1f, 0, 1, ref bouncedLife);

        Assert.True(Near(Written(bounced.Engine).Velocity.Z, 100f),
            $"and a dynamic prop reflects it: {Written(bounced.Engine).Velocity.Z}");
    }

    /// <summary>
    /// A gib that PASSES THROUGH something also skips the rest of the frame —
    /// no dlight, no gravity, no wind.
    ///
    /// <para>
    /// Stock's <c>return true</c> for that case is a return from <c>Frame</c>
    /// and not from the collision block, and the difference is invisible to
    /// every other test here: a port that only skipped the bounce would still
    /// move the gib to the right place and still reflect nothing. What it would
    /// get wrong is one frame of gravity, on anything flying through a crowd,
    /// which is a difference nobody would ever trace back to this line.
    /// </para>
    /// </summary>
    [Fact]
    public void PassingThroughSomethingSkipsTheRestOfTheFrameAndNotOnlyTheBounce()
    {
        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
        }, Prepared(), gravity: 600f);

        rig.Standalones.At(3, new Named(rig.Engine, EntityNames.SsPlayer));
        rig.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(0f, 0f, 1f), entityIndex: 3));

        var life = Live(TempEntFlags.CollideProps | TempEntFlags.Gravity | TempEntFlags.WindBlown);

        Assert.True(rig.Frame.Step(0, 0.1f, 0, 1, ref life), "it survives passing through");

        var t = Written(rig.Engine);

        Assert.True(Near(t.Velocity.Z, -100f),
            $"no gravity was applied on the frame it passed through -- not the -160 it would "
          + $"have had: {t.Velocity.Z}");
        Assert.True(rig.Engine.CountOf("Effects.ClientWindSpeed") == 0,
            "and the wind was not even asked for, because stock's return is from Frame");
    }

    /// <summary>
    /// Row C4's remaining half, said out loud once per level: an index this
    /// side owns no managed entity for cannot be asked either question.
    /// </summary>
    [Fact]
    public void AnEntityWithNoManagedHalfIsTreatedAsAPropAndRowC4IsSaidOncePerLevel()
    {
        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
        }, Prepared());

        rig.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(0f, 0f, 1f), entityIndex: 11));

        var life = Live(TempEntFlags.CollideProps);

        List<string> said = Saying(rig, () =>
        {
            rig.Frame.Step(0, 0.1f, 0, 1, ref life);

            var again = Live(TempEntFlags.CollideProps);
            rig.Frame.Forget(0);
            rig.Frame.Step(0, 0.1f, 0, 2, ref again);
        });

        Assert.True(Near(Written(rig.Engine).Velocity.Z, 100f),
            $"it bounces, which is what this build already did and is right for the "
          + $"prop_dynamic and doors the row is named for: {Written(rig.Engine).Velocity.Z}");

        var rowLines = said.Where(l => l.Contains("row C4")).ToList();

        Assert.True(rowLines.Count == 1,
            $"said ONCE for two collisions, not once per gib: {rowLines.Count}");
        Assert.True(rowLines[0].Contains("dynamic_cast<CDynamicProp*>")
                 && rowLines[0].Contains("dynamic_cast<CBaseDoor*>"),
            "and it names both casts, so a reader can find the code it is about");
        Assert.True(rowLines[0].Contains("prop_dynamic"),
            "and says which classes are still unanswerable, which is what closing it needs");

        // A new level says it again, because it is missing again in this map.
        List<string> afterLevel = Saying(rig, () =>
        {
            rig.Frame.LevelInit();
            rig.Frame.Forget(0);

            var third = Live(TempEntFlags.CollideProps);
            rig.Frame.Step(0, 0.1f, 0, 3, ref third);
        });

        Assert.True(afterLevel.Count(l => l.Contains("row C4")) == 1,
            "and once more after a level change, for IClientTempEntLevel's reason");
    }

    //-------------------------------------------------------------------------
    // Behaviour ledger row C5.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Row C5, refused and loud. Nothing in this build sets an impact effect,
    /// so this arm is unreachable in the game — which is exactly why it is
    /// pinned here: a lane that deleted it would turn a tracked gap into an
    /// untracked one and no other test would notice.
    /// </summary>
    [Fact]
    public void TheImpactEffectIsRefusedAndRowC5IsSaidOncePerLevel()
    {
        var statics = Prepared();
        statics.HasImpactEffect = 1;

        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
        }, statics);

        rig.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(0f, 0f, 1f)));

        var life = Live(TempEntFlags.CollideWorld);

        List<string> said = Saying(rig, () =>
        {
            rig.Frame.Step(0, 0.1f, 0, 1, ref life);

            rig.Frame.Forget(0);
            var again = Live(TempEntFlags.CollideWorld);
            rig.Frame.Step(0, 0.1f, 0, 2, ref again);
        });

        var rowLines = said.Where(l => l.Contains("row C5")).ToList();

        Assert.True(rowLines.Count == 1, $"said once per level, not per bounce: {rowLines.Count}");
        Assert.True(rowLines[0].Contains("m_pszImpactEffect"),
            "and it names the field the row points at");
        Assert.True(rowLines[0].Contains("C_BasePlayer") && rowLines[0].Contains("TEAM"),
            "and the TEAM NUMBER off a C_BasePlayer, which is the capability that is missing "
          + "rather than the effect itself");
        Assert.True(rowLines[0].Contains("tempent_verdict_test.cpp"),
            "and the test that records the branch being unreachable, so the next reader does "
          + "not have to re-measure it");
    }

    /// <summary>
    /// A tempent WITHOUT an impact effect — every one in this build — says
    /// nothing at all. The other half of the pair above: a refusal that fired
    /// for everything would be noise, and a count-only test cannot tell them
    /// apart.
    /// </summary>
    [Fact]
    public void AnOrdinaryBounceSaysNothingAboutRowC5()
    {
        var rig = Rigged(new TempEntTransform
        {
            Origin = new Vector(0f, 0f, 100f),
            Velocity = new Vector(0f, 0f, -100f),
        }, Prepared());

        rig.Engine.Returns("WorldQuery.ClientTraceLine",
            Hit(0.5f, new Vector(0f, 0f, 95f), new Vector(0f, 0f, 1f)));

        var life = Live(TempEntFlags.CollideWorld);

        List<string> said = Saying(rig, () => rig.Frame.Step(0, 0.1f, 0, 1, ref life));

        Assert.True(said.Count == 0,
            $"a gib bouncing off a wall is silent: {string.Join(" | ", said)}");
    }

    //-------------------------------------------------------------------------
    // Wind, flicker, and the takeover.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A wind-blown tempent is dragged towards the wind on X and Y and CLAMPS
    /// rather than overshooting — in both directions, because stock's loop has
    /// two arms and a port could have written one.
    /// </summary>
    [Fact]
    public void AWindBlownTempEntIsDraggedTowardsTheWindAndClampsInBothDirections()
    {
        var rig = Rigged(new TempEntTransform
        {
            // X is far below the wind and will accelerate; Y is just above it
            // and would overshoot without the clamp.
            Velocity = new Vector(-1000f, 12f, 500f),
        }, Prepared());

        rig.Engine.Returns("Effects.ClientWindSpeed", new Vector(100f, 10f, 999f));

        var life = Live(TempEntFlags.WindBlown);
        rig.Frame.Step(0, 0.1f, 0, 1, ref life);

        var v = Written(rig.Engine).Velocity;

        Assert.True(Near(v.X, -995f),
            $"below the wind it accelerates by frametime * TENT_WIND_ACCEL and no more: {v.X}");
        Assert.True(Near(v.Y, 10f),
            $"above it, it decelerates and CLAMPS to the wind rather than shooting past to 7: "
          + $"{v.Y}");
        Assert.True(Near(v.Z, 500f),
            $"and Z is untouched, because stock's loop runs over two axes -- wind does not "
          + $"lift: {v.Z}");
    }

    /// <summary>
    /// A <c>FTENT_FLICKER</c> tempent lights a dynamic light on ITS OWN frame
    /// of the 0..31 counter and on no other.
    /// </summary>
    [Fact]
    public void FlickerLightsADlightOnItsOwnFrameAndOnNoOther()
    {
        var statics = Prepared();
        statics.FlickerFrame = 7;

        var rig = Rigged(new TempEntTransform { Origin = new Vector(1f, 2f, 3f) }, statics);
        var life = Live(TempEntFlags.Flicker);

        rig.Frame.Step(0, 0.1f, frameNumber: 6, frameCount: 1, life: ref life);
        Assert.True(rig.Engine.CountOf("Visibility.DynamicLightUpdate") == 0,
            "frame six is not frame seven");

        rig.Frame.Step(0, 0.1f, frameNumber: 7, frameCount: 2, life: ref life);
        Assert.True(rig.Engine.CountOf("Visibility.DynamicLightUpdate") == 1, "and frame seven is");

        var light = rig.Engine.Last("Visibility.DynamicLightUpdate")!.Value.ArgAs<DynamicLight>(0);

        Assert.True(light.Key == ClientTeDynamicLight.TeDynamicLightKey,
            $"through LIGHT_INDEX_TE_DYNAMIC, the key stock allocates it with: {light.Key}");
        Assert.True(light.Radius == 60f && light.Red == 255 && light.Green == 120 && light.Blue == 0,
            $"stock's radius and colour: {light.Radius}, {light.Red}/{light.Green}/{light.Blue}");
        Assert.True(Near(light.LiveFor, 0.01f),
            $"and stock's die = curtime + 0.01, which is what makes it a FLICKER: {light.LiveFor}");
        Assert.True(Near(light.OriginX, 1f) && Near(light.OriginZ, 3f),
            $"at the tempent: ({light.OriginX}, {light.OriginY}, {light.OriginZ})");
    }

    /// <summary>
    /// The transform is read ONCE, on takeover, and written EVERY frame. That
    /// is the whole shape the ledger row turned on — a per-field accessor would
    /// have been a dozen crossings a frame — and a test that only checked "the
    /// origin arrived" would pass against the shape that was rejected.
    /// </summary>
    [Fact]
    public void TheTransformIsReadOnceOnTakeoverAndWrittenEveryFrame()
    {
        var rig = Rigged(new TempEntTransform { Velocity = new Vector(10f, 0f, 0f) }, Prepared());
        var life = Live(TempEntFlags.None);

        for (int frame = 0; frame < 4; frame++)
            rig.Frame.Step(0, 0.1f, frame, frame + 1, ref life);

        Assert.True(rig.Engine.CountOf("Effects.TempEntPoolReadTransform") == 1,
            $"read once for four frames: {rig.Engine.CountOf("Effects.TempEntPoolReadTransform")}");
        Assert.True(rig.Engine.CountOf("Effects.TempEntPoolReadStatics") == 1,
            $"and the statics likewise: {rig.Engine.CountOf("Effects.TempEntPoolReadStatics")}");
        Assert.True(rig.Engine.CountOf("Effects.TempEntPoolWriteTransform") == 4,
            $"and written every frame, because that is how it is drawn: "
          + $"{rig.Engine.CountOf("Effects.TempEntPoolWriteTransform")}");

        // FOUR frames of integration, not one: the place has to accumulate on
        // this side, or holding the transform would be pointless.
        Assert.True(Near(Written(rig.Engine).Origin.X, 4f),
            $"and it integrated across all four: {Written(rig.Engine).Origin.X}");
    }

    /// <summary>
    /// A forgotten slot is taken over again from scratch, so the next tenant of
    /// a recycled slot does not inherit the last one's place and velocity.
    /// </summary>
    [Fact]
    public void AForgottenSlotIsTakenOverAgainRatherThanInheritingTheLastTenant()
    {
        var rig = Rigged(new TempEntTransform { Origin = new Vector(0f, 0f, 0f),
                                                Velocity = new Vector(100f, 0f, 0f) }, Prepared());
        var life = Live(TempEntFlags.None);

        rig.Frame.Step(0, 1f, 0, 1, ref life);
        Assert.True(Near(Written(rig.Engine).Origin.X, 100f), "it moved");

        rig.Frame.Forget(0);

        var next = Live(TempEntFlags.None);
        rig.Frame.Step(0, 1f, 0, 2, ref next);

        Assert.True(rig.Engine.CountOf("Effects.TempEntPoolReadTransform") == 2,
            "the slot was taken over again");
        Assert.True(Near(Written(rig.Engine).Origin.X, 100f),
            $"from the record NATIVE holds -- origin 0 plus one second of 100 -- and not from "
          + $"the 200 it would have reached had the old state survived: "
          + $"{Written(rig.Engine).Origin.X}");
    }

    //-------------------------------------------------------------------------
    // The constants, read off stock.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The fast-collision stand-in is stock's own declared default, read out of
    /// <c>c_te_legacytempents.cpp</c>.
    ///
    /// <para>
    /// It is a stand-in because the convar's only reader was the branch this
    /// port replaced, and that branch is gated on a flag nothing in this build
    /// sets. A stand-in for something native owns drifts silently unless
    /// something asserts the agreement, which is what this is.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFastCollisionDefaultIsTheOneStockDeclares()
    {
        string? legacy = RepoLayout.FindSdkFile(Path.Combine(
            "src", "game", "client", "c_te_legacytempents.cpp"));

        if (legacy is null)
        {
            Assert.True(true, "c_te_legacytempents.cpp read for the convar default (SKIPPED)");
            return;
        }

        string text = File.ReadAllText(legacy);

        Match declaration = Regex.Match(text,
            @"ConVar\s+cl_fasttempentcollision\s*\(\s*""cl_fasttempentcollision""\s*,\s*""(\d+)""");

        Assert.True(declaration.Success,
            "c_te_legacytempents.cpp still declares cl_fasttempentcollision. If it has moved to "
          + "managed, this test is what should tell you -- and the stand-in should go with it");

        Assert.True(int.Parse(declaration.Groups[1].Value)
                 == ClientTempEntFrame.FastCollisionFramesDefault,
            $"stock declares {declaration.Groups[1].Value} and the managed stand-in is "
          + $"{ClientTempEntFrame.FastCollisionFramesDefault}");

        // And the reason it may be a constant at all: nothing sets the flag.
        Assert.True(!Regex.IsMatch(text, @"flags\s*\|?=\s*[^;]*FTENT_USEFASTCOLLISIONS"),
            "and nothing in the pool's own file sets FTENT_USEFASTCOLLISIONS, which is why the "
          + "convar was left native and unread. If that changes, move the convar to managed");
    }

    /// <summary>
    /// The wind acceleration is stock's <c>TENT_WIND_ACCEL</c>, read off disk.
    /// </summary>
    [Fact]
    public void TheWindAccelerationIsStocksTentWindAccel()
    {
        string? legacy = RepoLayout.FindSdkFile(Path.Combine(
            "src", "game", "client", "c_te_legacytempents.cpp"));

        if (legacy is null)
        {
            Assert.True(true, "c_te_legacytempents.cpp read for TENT_WIND_ACCEL (SKIPPED)");
            return;
        }

        Match define = Regex.Match(File.ReadAllText(legacy),
            @"#define\s+TENT_WIND_ACCEL\s+([0-9.]+)");

        Assert.True(define.Success, "c_te_legacytempents.cpp still defines TENT_WIND_ACCEL");
        Assert.True(float.Parse(define.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture)
                 == ClientTempEntFrame.WindAccel,
            $"stock says {define.Groups[1].Value} and the port says {ClientTempEntFrame.WindAccel}");
    }

    /// <summary>
    /// <c>SURF_SKY</c> is bspflags.h's, read off disk — the one surface bit the
    /// collision arm branches on, and a bit count could never catch it being
    /// wrong.
    /// </summary>
    [Fact]
    public void TheSkySurfaceBitIsBspflagsOwn()
    {
        string? bspflags = RepoLayout.FindSdkFile(Path.Combine("src", "public", "bspflags.h"));

        if (bspflags is null)
        {
            Assert.True(true, "bspflags.h read for SURF_SKY (SKIPPED)");
            return;
        }

        Match define = Regex.Match(File.ReadAllText(bspflags),
            @"#define\s+SURF_SKY\s+0x([0-9A-Fa-f]+)");

        Assert.True(define.Success, "bspflags.h still defines SURF_SKY");
        Assert.True(Convert.ToInt32(define.Groups[1].Value, 16) == ClientTempEntFrame.SurfSky,
            $"bspflags.h says 0x{define.Groups[1].Value} and the port says "
          + $"0x{ClientTempEntFrame.SurfSky:X4}");
    }

    /// <summary>
    /// <see cref="TempEntStatics"/> mirrors the C struct field for field. They
    /// are one record in two languages written back field by field, so a field
    /// added on one side only misreads everything after it.
    /// </summary>
    [Fact]
    public void TheStaticsStructMatchesTheHeader()
    {
        string? header = RepoLayout.FindSdkFile(Path.Combine(
            "src", "sourcesharp", "host", "sourcesharp.h"));

        if (header is null)
        {
            Assert.True(true, "sourcesharp.h read for the statics check (SKIPPED)");
            return;
        }

        Match block = Regex.Match(File.ReadAllText(header),
            @"typedef struct SourceSharpTempEntStatics_s\s*\{(.*?)\}\s*SourceSharpTempEntStatics_t;",
            RegexOptions.Singleline);

        Assert.True(block.Success, "the header declares SourceSharpTempEntStatics_t");

        string body = block.Groups[1].Value;

        int vectors = Regex.Matches(body, @"float\s+\w+\[\s*3\s*\]").Count;
        int floats = Regex.Matches(body, @"float\s+\w+\s*;").Count;
        int ints = Regex.Matches(body, @"int32_t\s+\w+\s*;").Count;

        Assert.True(vectors == 1,
            $"one three-float member -- tentOffset -- and the header has {vectors}");
        Assert.True(floats == 3,
            $"three scalars: bounceFactor, frameMax and frameRate; the header has {floats}");
        Assert.True(ints == 4,
            $"and four ints: hitSound, the owner index, the flicker frame and row C5's one "
          + $"bit; the header has {ints}");

        Assert.True(vectors + floats + ints == typeof(TempEntStatics).GetFields().Length,
            $"so the two sides have the same number of fields; managed has "
          + $"{typeof(TempEntStatics).GetFields().Length}");
    }

    /// <summary>
    /// The three fields that are <c>Frame</c>'s own working state are still
    /// absent from the transform. <c>TempEntTransformTests</c> asserts this from
    /// the ABI's side; this asserts it from the side that OWNS them, so that
    /// adding one later fails in both places at once.
    /// </summary>
    [Fact]
    public void FramesPrivateWorkingStateStillDoesNotCrossTheAbi()
    {
        string[] transform = typeof(TempEntTransform).GetFields().Select(f => f.Name).ToArray();
        string[] statics = typeof(TempEntStatics).GetFields().Select(f => f.Name).ToArray();

        foreach (string absent in new[] { "PrevOrigin", "LastCollisionOrigin", "LastCollisionFrame" })
        {
            Assert.True(!transform.Contains(absent) && !statics.Contains(absent),
                $"{absent} is ClientTempEntFrame's own working value -- the client's only "
              + "readers of it are inside Frame -- so it crosses in NEITHER record. Adding it "
              + "is a decision about ownership, not a field to slip into whichever struct has "
              + "room");
        }
    }
}

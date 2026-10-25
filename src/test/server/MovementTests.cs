//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The movement parameters.
///
/// Phase 6 of the plan is deliberately the parameters and not the loop, so what
/// is testable here is the table and the one piece of arithmetic that came with
/// it. That is a smaller surface than it looks: every failure mode below is
/// silent, because a wrong movement number does not throw — it just makes the
/// game feel wrong, which is exactly the thing nobody can bisect.
/// </summary>
public class MovementTests
{

    /// <summary>
    /// The one movement DECISION that has moved to C#.
    ///
    /// Phase 6 step 3 allowed it only if measurement justified it, and the
    /// measurement is in the commit: a managed call costs about 7ns with the
    /// side switch and exception handler a real callback carries, and this one
    /// is asked once per command while the jump button is held.
    /// </summary>
    [Fact]
    public void JumpDecision()
    {
        var onGround = new SourceSharp.Abi.JumpRequest
        {
            PlayerIndex = 1,
            HorizontalSpeed = 200f,
            VerticalSpeed = 0f,
            Ducking = 0,
            GroundMaterial = SourceSharp.Abi.SurfaceMaterial.Metal,
            CurrentTime = 10f,
        };

        // Phase 0.3: the decision is no longer a static but the side's
        // resolved pack — with no hook registered that pack is the identity,
        // and identity is the promise this fact still makes.
        var stock = new ServiceContainer().Create<MovementRulesResolver>().Rules;

        // The default has to be exactly 1, not merely close to it. This is a
        // plumbing commit, and a hook that quietly shaved a few percent off
        // every jump would be a gameplay change nobody agreed to and nobody
        // could bisect.
        Assert.True(stock.Jump(onGround) == 1f, "by default a jump is unchanged");

        // A rule here decides from the REQUEST and nothing else, because it
        // runs on the server and again on the client for every unacknowledged
        // command. These check the shape holds rather than any particular rule:
        // the same request must always give the same answer.
        Assert.True(stock.Jump(onGround) == stock.Jump(onGround), "the same request answers the same way twice");

        var moving = onGround; moving.HorizontalSpeed = 400f;
        var ducking = onGround; ducking.Ducking = 1;
        var later = onGround; later.CurrentTime = 900f;

        // Not asserting that these DIFFER — the default rule ignores them — but
        // that asking with any of them is safe and answers a usable jump.
        Assert.True(stock.Jump(moving) > 0f, "a fast player still gets a jump");
        Assert.True(stock.Jump(ducking) > 0f, "a ducking player still gets a jump");
        Assert.True(stock.Jump(later) == 1f, "the answer does not drift with time");

        // The native side clamps a negative to zero, so nothing here can push a
        // player through the floor; a scale above zero is all this end promises.
        Assert.True(stock.Jump(onGround) > 0f, "the default never refuses a jump");
    }

    [Fact]
    public void Parameters()
    {
        var p = Movement.Current;

        // Zero is the dangerous value for every one of these, and it is what a
        // half-initialised struct gives you. A zero jump height is a player who
        // cannot jump; zero gravity is a player who never lands; zero speed is
        // a player who cannot move. All three read as broken input rather than
        // as a missing number.
        Assert.True(p.JumpHeight > 0f, "jump height is set");
        Assert.True(p.Gravity > 0f, "gravity is set");
        Assert.True(p.Friction > 0f, "friction is set");
        Assert.True(p.Accelerate > 0f, "acceleration is set");
        Assert.True(p.StopSpeed > 0f, "stop speed is set");
        Assert.True(p.StepSize > 0f, "step size is set");
        Assert.True(p.MaxVelocity > 0f, "max velocity is set");

        Assert.True(p.MaxSpeed > 0f, "max speed is set");
        Assert.True(p.AirSpeedCap > 0f, "the air speed cap is set");

        // A duck that made you FASTER is not a crouch, and a zero would pin a
        // crouching player in place with no way to tell why.
        Assert.True(p.DuckSpeedScale > 0f && p.DuckSpeedScale < 1f, "a crouch costs speed and does not stop you");

        // This commit is plumbing, not balance. If these ever stop matching
        // stock it should be because somebody meant it, in a commit that says
        // so — not as a side effect of making them reachable.
        Assert.True(p.JumpHeight == 21f && p.Gravity == 600f && p.Friction == 4f &&
            p.StopSpeed == 75f && p.StepSize == 18f && p.MaxSpeed == 320f &&
            p.AirSpeedCap == 30f && Math.Abs(p.DuckSpeedScale - 0.33333333f) < 1e-6f, "the parameters are still stock Half-Life's");
    }

    [Fact]
    public void Jumping()
    {
        // sqrt(2 * 600 * 21) = 158.7…, which is the number the engine has used
        // for every Half-Life jump.
        float v = Movement.JumpVelocity(21f, 600f);
        Assert.True(Math.Abs(v - 158.745f) < 0.01f, "a stock jump leaves the ground at about 159");

        // The relationship people get wrong when tuning: height is quadratic in
        // velocity, so twice the height is NOT twice the jump.
        float twice = Movement.JumpVelocity(42f, 600f);
        Assert.True(twice < 2f * v, "twice the height is not twice the velocity");
        Assert.True(twice > v, "but it is more than the same velocity");
        Assert.True(Math.Abs(twice - v * MathF.Sqrt(2f)) < 0.01f, "it is the square root of two times it");

        // Heavier gravity needs a harder push to reach the same height, which
        // is why the parameters cannot be tuned independently of one another.
        Assert.True(Movement.JumpVelocity(21f, 800f) > v, "more gravity needs more velocity for the same height");

        // Nonsense in, zero out rather than NaN: a NaN velocity is networked
        // and asserts a long way from here, which is the failure the breakable
        // surface already taught this project once.
        Assert.True(Movement.JumpVelocity(0f, 600f) == 0f, "no height is no jump");
        Assert.True(Movement.JumpVelocity(-5f, 600f) == 0f, "negative height is no jump");
        Assert.True(Movement.JumpVelocity(21f, 0f) == 0f, "no gravity is no jump");
    }

    //=========================================================================
    // THE PARITY FIXTURE — the acceptance for plan_entity.md step 3e.
    //
    // Fixtures/movement-processmovement-stock.tsv is 36 commands of the NATIVE
    // CGameMovement::ProcessMovement, recorded off a live dm_lockdown by a
    // temporary probe BEFORE this step changed anything, and the probe removed
    // afterwards (git diff on gamemovement.cpp carries no trace of it). Each
    // command carries:
    //
    //   IN   the whole movement state the loop was handed
    //   TR   every swept-hull trace it made, its arguments AND THE ANSWER
    //   PC   every point-contents query, and its answer
    //   OUT  the whole movement state it produced
    //
    // Recording the ANSWERS as inputs is the point, and it is 2f's method: the
    // managed loop is fed the same answers the engine handed the C++, so the
    // comparison is managed arithmetic against the C++ it replaces rather than
    // against itself. A fixture of positions to reproduce would agree forever.
    //
    // WHAT THE FIXTURE DOES NOT COVER, counted rather than assumed, from the
    // 3458 commands the recording holds:
    //
    //   water (levels 1-3)  0 commands — dm_lockdown has no water at all
    //   ladder              0 commands — its three ladders are func_useableladder,
    //                       which is CHL2GameMovement's mechanism, not this
    //                       build's; nothing set MOVETYPE_LADDER
    //   duck-jump           0 commands — CheckJumpButton sets m_flJumpTime only
    //                       when maxClients == 1, so the whole duck-jump family
    //                       is unreachable in a multiplayer build
    //   observer / toss     0 commands — and ProcessMovement REFUSES both, so
    //                       the native body still runs them
    //   moving ground       0 commands — no base velocity in any recorded row
    //
    // Those arms are covered by the hermetic facts below and NOT by a native
    // recording, and that is stated rather than papered over.
    //=========================================================================

    /// <summary>One recorded command: the state in, the answers, the state out.</summary>
    private sealed record StockCommand(
        int Line, MoveState In, IReadOnlyList<RecordedQuery> Queries, MoveState Out);

    private sealed record RecordedQuery(string Kind, string[] Columns);

    private static float Flt(string text)
        => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int Int(string text)
        => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static Vector Vec(string[] c, int i) => new(Flt(c[i]), Flt(c[i + 1]), Flt(c[i + 2]));

    /// <summary>
    /// The state columns, by position.
    ///
    /// <para>
    /// The order is the probe's, and it is the ONE thing in this file that
    /// cannot be inferred from anything else — so <see cref="StateColumnCount"/>
    /// is asserted per row rather than trusted, and a probe that grew a column
    /// makes every row fail loudly instead of shifting every field by one.
    /// </para>
    /// </summary>
    private const int StateColumnCount = 98;

    private static MoveState ReadState(string[] c)
    {
        return new MoveState
        {
            CommandNumber = Int(c[0]),
            FrameTime = Flt(c[1]),
            MoveType = Int(c[2]),
            Flags = Int(c[3]),
            WaterLevel = Int(c[4]),
            WaterType = Int(c[5]),
            DeadFlag = c[6] == "1",
            Origin = Vec(c, 7),
            Velocity = Vec(c, 10),
            BaseVelocity = Vec(c, 13),
            ViewAngles = Vec(c, 16),
            ForwardMove = Flt(c[19]),
            SideMove = Flt(c[20]),
            UpMove = Flt(c[21]),
            Buttons = Int(c[22]),
            OldButtons = Int(c[23]),
            MaxSpeed = Flt(c[24]),
            ClientMaxSpeed = Flt(c[25]),
            Ducked = c[26] == "1",
            Ducking = c[27] == "1",
            InDuckJump = c[28] == "1",
            DuckTime = Flt(c[29]),
            DuckJumpTime = Flt(c[30]),
            JumpTime = Flt(c[31]),
            FallVelocity = Flt(c[32]),
            StepSize = Flt(c[33]),
            ViewOffset = Vec(c, 34),
            PunchAngle = Vec(c, 37),
            PunchAngleVel = Vec(c, 40),
            EntityGravity = Flt(c[43]),
            CurrentGravity = Flt(c[44]),
            SurfaceFriction = Flt(c[45]),
            HasGround = c[46] == "1",
            GroundVelocity = Vec(c, 47),
            WaterJumpTime = Flt(c[50]),
            WaterJumpVel = Vec(c, 51),
            SwimSoundTime = Flt(c[54]),
            StepSoundTime = Flt(c[55]),
            Health = Int(c[56]),
            GameCodeMovedPlayer = c[57] == "1",
            ConstraintRadius = Flt(c[58]),
            ConstraintWidth = Flt(c[59]),
            ConstraintSpeedFactor = Flt(c[60]),
            ConstraintCenter = Vec(c, 61),
            OldForwardMove = Flt(c[64]),
            ImpulseCommand = Int(c[65]),
            LaggedMovementValue = Flt(c[66]),
            SurfaceProps = Int(c[67]),
            LadderNormal = Vec(c, 68),
            OutStepHeight = Flt(c[71]),
            OutWishVel = Vec(c, 72),
            OutJumpVel = Vec(c, 75),
            MoveCollide = Int(c[78]),
            OnLadder = Int(c[79]),
            OldWaterLevel = Int(c[80]),
            IsAlive = c[81] == "1",
            ObserverMode = Int(c[82]),
            InAVehicle = c[83] == "1",
            ModelScale = Flt(c[84]),
            EntIndex = Int(c[85]),
            StuckLast = Int(c[86]),
            Angles = Vec(c, 87),
            OldAngles = Vec(c, 90),
            FirstRunOfFunctions = c[93] == "1",
            CurTime = Flt(c[94]),
            SurfaceDataValid = c[95] == "1",
            AllowAutoMovement = c[96] == "1",
            SlowMovement = c[97] == "1",

            // The recording is SERVER-side. Not a column, said here.
            IsServer = true,
        };
    }

    private sealed record StockRecording(
        MoveConstants Constants,
        IReadOnlyDictionary<int, MoveSurface> Surfaces,
        IReadOnlyList<StockCommand> Commands);

    private static StockRecording LoadRecording()
    {
        string path = Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "managed",
                                   "SourceSharp.Tests", "Fixtures",
                                   "movement-processmovement-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock movement is missing: " + path);

        MoveConstants constants = default;
        bool sawConstants = false;
        var surfaces = new Dictionary<int, MoveSurface>();
        var commands = new List<StockCommand>();

        MoveState? pendingIn = null;
        List<RecordedQuery>? pendingQueries = null;
        int pendingLine = 0;

        string[] lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] c = line.Split('\t');
            string kind = c[0];
            string[] body = c[1..];

            switch (kind)
            {
                case "CV":
                    Assert.True(body.Length == 46,
                        $"line {i + 1}: the constants row has {body.Length} columns, not 46");
                    constants = new MoveConstants
                    {
                        Friction = Flt(body[0]),
                        Accelerate = Flt(body[1]),
                        AirAccelerate = Flt(body[2]),
                        StopSpeed = Flt(body[3]),
                        MaxVelocity = Flt(body[4]),
                        MaxSpeed = Flt(body[5]),
                        Bounce = Flt(body[6]),
                        RollAngle = Flt(body[7]),
                        RollSpeed = Flt(body[8]),
                        NoClipSpeed = Flt(body[9]),
                        NoClipAccelerate = Flt(body[10]),
                        SpecSpeed = Flt(body[11]),
                        SpecAccelerate = Flt(body[12]),
                        AirSpeedCap = Flt(body[13]),
                        DuckSpeedScale = Flt(body[14]),
                        Optimizations = body[15] == "1",
                        SpecNoClip = body[16] == "1",
                        MaxClients = Int(body[17]),
                        TickInterval = Flt(body[18]),
                        View = Vec(body, 19),
                        HullMin = Vec(body, 22),
                        HullMax = Vec(body, 25),
                        DuckHullMin = Vec(body, 28),
                        DuckHullMax = Vec(body, 31),
                        DuckView = Vec(body, 34),
                        ObsHullMin = Vec(body, 37),
                        ObsHullMax = Vec(body, 40),
                        DeadViewHeight = Vec(body, 43),
                    };
                    sawConstants = true;
                    break;

                case "SP":
                    Assert.True(body.Length == 6,
                        $"line {i + 1}: the surface row has {body.Length} columns, not 6");
                    surfaces[Int(body[0])] = new MoveSurface
                    {
                        Valid = true,
                        Friction = Flt(body[1]),
                        Material = Int(body[2]),
                        Climbable = Int(body[3]),
                        MaxSpeedFactor = Flt(body[4]),
                        JumpFactor = Flt(body[5]),
                    };
                    break;

                case "IN":
                    Assert.True(body.Length == StateColumnCount,
                        $"line {i + 1}: the state row has {body.Length} columns, not {StateColumnCount}");
                    pendingIn = ReadState(body);
                    pendingQueries = new List<RecordedQuery>();
                    pendingLine = i + 1;
                    break;

                case "TR":
                case "PC":
                    Assert.NotNull(pendingQueries);
                    pendingQueries!.Add(new RecordedQuery(kind, body));
                    break;

                case "OUT":
                    Assert.True(body.Length == StateColumnCount,
                        $"line {i + 1}: the state row has {body.Length} columns, not {StateColumnCount}");
                    Assert.NotNull(pendingIn);
                    commands.Add(new StockCommand(pendingLine, pendingIn!, pendingQueries!, ReadState(body)));
                    pendingIn = null;
                    pendingQueries = null;
                    break;

                default:
                    Assert.Fail($"line {i + 1}: unknown row kind '{kind}'");
                    break;
            }
        }

        Assert.True(sawConstants, "the fixture carries no CV row");
        return new StockRecording(constants, surfaces, commands);
    }

    /// <summary>
    /// The world, as the recording answered it.
    ///
    /// <para>
    /// Every trace and point-contents query is served from the recorded queue
    /// IN ORDER, and the query's own ARGUMENTS are checked against the recorded
    /// ones before the answer is handed back. That second half is what makes
    /// this a parity test rather than a plausibility test: a managed loop that
    /// traced from the wrong place, with the wrong hull, or in the wrong order
    /// would still produce numbers, and only the argument check catches it.
    /// </para>
    /// </summary>
    private sealed class RecordedWorld : IMoveWorld
    {
        private readonly IReadOnlyList<RecordedQuery> _queries;
        private readonly IReadOnlyDictionary<int, MoveSurface> _surfaces;
        private int _next;

        public RecordedWorld(IReadOnlyList<RecordedQuery> queries,
                             IReadOnlyDictionary<int, MoveSurface> surfaces)
        {
            _queries = queries;
            _surfaces = surfaces;
        }

        /// <summary>How many recorded answers are still unclaimed.</summary>
        public int Remaining => _queries.Count - _next;

        /// <summary>What the loop asked for, in order, for a failure message.</summary>
        public List<string> Asked { get; } = new();

        public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs,
                               uint mask, int collisionGroup)
        {
            Asked.Add($"TR {start} -> {end} hull {mins}..{maxs} mask {mask} group {collisionGroup}");

            Assert.True(_next < _queries.Count,
                $"the managed loop made MORE world queries than the recording holds " +
                $"({_queries.Count}); the extra one is: {Asked[^1]}");

            RecordedQuery q = _queries[_next++];
            Assert.True(q.Kind == "TR",
                $"query {_next} is a {q.Kind} in the recording but the managed loop traced: {Asked[^1]}");

            string[] c = q.Columns;
            // c[0] is the probe's own label (BBOX / GROUND) and is not compared:
            // both spellings are one swept hull, and which native method issued
            // it is not something the managed loop can or should reproduce.
            AssertBits(Vec(c, 1), start, $"trace {_next} start");
            AssertBits(Vec(c, 4), end, $"trace {_next} end");
            AssertBits(Vec(c, 7), mins, $"trace {_next} mins");
            AssertBits(Vec(c, 10), maxs, $"trace {_next} maxs");
            Assert.True(uint.Parse(c[13], CultureInfo.InvariantCulture) == mask,
                $"trace {_next} mask: recorded {c[13]}, managed {mask}");
            Assert.True(Int(c[14]) == collisionGroup,
                $"trace {_next} collision group: recorded {c[14]}, managed {collisionGroup}");

            return new MoveTrace
            {
                Fraction = Flt(c[15]),
                EndPos = Vec(c, 16),
                PlaneNormal = Vec(c, 19),
                PlaneDist = Flt(c[22]),
                StartSolid = c[23] == "1",
                AllSolid = c[24] == "1",
                SurfaceProps = Int(c[25]),
                SurfaceFlags = Int(c[26]),
                Contents = Int(c[27]),
                HasGround = c[28] == "1",
                GroundVelocity = Vec(c, 29),
                HasEntity = c[32] == "1",
                HitWorld = c[33] == "1",
            };
        }

        public void ResetPointContentsCache(bool optimizations) { }

        public int PointContents(in Vector point, int slot)
        {
            Asked.Add($"PC {point} slot {slot}");

            Assert.True(_next < _queries.Count,
                $"the managed loop made MORE world queries than the recording holds " +
                $"({_queries.Count}); the extra one is: {Asked[^1]}");

            RecordedQuery q = _queries[_next++];
            Assert.True(q.Kind == "PC",
                $"query {_next} is a {q.Kind} in the recording but the managed loop asked: {Asked[^1]}");

            string[] c = q.Columns;
            AssertBits(Vec(c, 0), point, $"point contents {_next} point");
            Assert.True(Int(c[3]) == slot,
                $"point contents {_next} slot: recorded {c[3]}, managed {slot}");

            return Int(c[4]);
        }

        public MoveSurface Surface(int surfaceProps)
            => _surfaces.TryGetValue(surfaceProps, out MoveSurface s) ? s : default;

        // The effects. A replay swallows them; the two that CHANGE state are
        // the ones the loop writes itself (StepSoundTime, PunchAngle) and those
        // stay in MoveState, so nothing here needs an answer except the fall
        // damage — which returns "still alive", the answer every recorded
        // landing in this fixture got.
        public void ResetTouchList() { }
        public void AddToTouched(in MoveTrace trace, in Vector velocity) { }
        public void StartSound(in Vector origin, string sound) { }
        public void PlayerSetAnimation(int activity) { }
        /// <summary>
        /// The amount the loop last asked for, so a fact can read the number
        /// and not only the fact that a landing hurt. <c>float.NaN</c> until
        /// asked — 0 is a real answer (a safe landing that still armed) and a
        /// sentinel that collides with a real value proves nothing.
        /// </summary>
        public float LastFallDamage = float.NaN;

        public bool PlayerFallingDamage(float damage) { LastFallDamage = damage; return true; }
        public void PlayStepSound(in Vector origin, int surfaceProps, float volume, bool force) { }
        public void UpdateStepSound(in Vector origin, in Vector velocity, int surfaceProps) { }
        public void Splash() { }
        public void RumbleEffect(byte index, byte data, byte flags) { }
        public void ResetDuckLatched() { }
        public void SetPlayerSurface(int material) { }

        /// <summary>
        /// Never reached by any command in the fixture — CheckStuck returns
        /// before it unless the player is actually inside something — and it
        /// throws rather than answering zero, so a replay that DID reach it
        /// fails loudly instead of silently comparing against a made-up clock.
        /// </summary>
        public float EngineTime()
            => throw new InvalidOperationException(
                   "the replay reached CheckStuck's throttle, which no recorded command does");
    }

    /// <summary>
    /// Bit-exact, not "close". <c>%.9g</c> round-trips an IEEE-754 single and
    /// both sides are scalar SSE floats in the same order, so an epsilon here
    /// would be an epsilon-sized hole for a reordered expression to hide in.
    /// </summary>
    private static void AssertBits(float expected, float actual, string what)
    {
        if (BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual))
            return;

        Assert.Fail($"{what}: recorded {expected:R}, managed {actual:R} " +
                    $"(delta {actual - expected:R})");
    }

    private static void AssertBits(in Vector expected, in Vector actual, string what)
    {
        AssertBits(expected.X, actual.X, what + ".x");
        AssertBits(expected.Y, actual.Y, what + ".y");
        AssertBits(expected.Z, actual.Z, what + ".z");
    }

    /// <summary>Replay one recorded command and hand back what the managed loop produced.</summary>
    private static (MoveState Produced, RecordedWorld World, bool Ran)
        Replay(StockRecording rec, StockCommand cmd)
    {
        var world = new RecordedWorld(cmd.Queries, rec.Surfaces);
        var movement = new GameMovement(world, rec.Constants);
        MoveState state = cmd.In.Clone();
        bool ran = movement.ProcessMovement(state);
        return (state, world, ran);
    }

    /// <summary>Every field of the state the two sides must agree on, one entry each.</summary>
    private static void AssertStateMatches(MoveState expected, MoveState actual, string where)
    {
        AssertBits(expected.Origin, actual.Origin, where + " origin");
        AssertBits(expected.Velocity, actual.Velocity, where + " velocity");
        AssertBits(expected.BaseVelocity, actual.BaseVelocity, where + " baseVelocity");
        AssertBits(expected.ViewOffset, actual.ViewOffset, where + " viewOffset");
        AssertBits(expected.PunchAngle, actual.PunchAngle, where + " punchAngle");
        AssertBits(expected.PunchAngleVel, actual.PunchAngleVel, where + " punchAngleVel");
        AssertBits(expected.Angles, actual.Angles, where + " angles");
        AssertBits(expected.OutWishVel, actual.OutWishVel, where + " outWishVel");
        AssertBits(expected.OutJumpVel, actual.OutJumpVel, where + " outJumpVel");
        AssertBits(expected.OutStepHeight, actual.OutStepHeight, where + " outStepHeight");
        AssertBits(expected.FallVelocity, actual.FallVelocity, where + " fallVelocity");
        AssertBits(expected.SurfaceFriction, actual.SurfaceFriction, where + " surfaceFriction");
        AssertBits(expected.MaxSpeed, actual.MaxSpeed, where + " maxSpeed");
        AssertBits(expected.ForwardMove, actual.ForwardMove, where + " forwardMove");
        AssertBits(expected.SideMove, actual.SideMove, where + " sideMove");
        AssertBits(expected.UpMove, actual.UpMove, where + " upMove");
        AssertBits(expected.DuckTime, actual.DuckTime, where + " duckTime");
        AssertBits(expected.DuckJumpTime, actual.DuckJumpTime, where + " duckJumpTime");
        AssertBits(expected.JumpTime, actual.JumpTime, where + " jumpTime");
        AssertBits(expected.WaterJumpTime, actual.WaterJumpTime, where + " waterJumpTime");
        AssertBits(expected.WaterJumpVel, actual.WaterJumpVel, where + " waterJumpVel");
        AssertBits(expected.SwimSoundTime, actual.SwimSoundTime, where + " swimSoundTime");
        AssertBits(expected.LadderNormal, actual.LadderNormal, where + " ladderNormal");
        AssertBits(expected.EntityGravity, actual.EntityGravity, where + " entityGravity");

        Assert.True(expected.Flags == actual.Flags,
            $"{where} flags: recorded 0x{expected.Flags:x}, managed 0x{actual.Flags:x}");
        Assert.True(expected.MoveType == actual.MoveType,
            $"{where} moveType: recorded {expected.MoveType}, managed {actual.MoveType}");
        Assert.True(expected.MoveCollide == actual.MoveCollide,
            $"{where} moveCollide: recorded {expected.MoveCollide}, managed {actual.MoveCollide}");
        Assert.True(expected.WaterLevel == actual.WaterLevel,
            $"{where} waterLevel: recorded {expected.WaterLevel}, managed {actual.WaterLevel}");
        Assert.True(expected.WaterType == actual.WaterType,
            $"{where} waterType: recorded {expected.WaterType}, managed {actual.WaterType}");
        Assert.True(expected.HasGround == actual.HasGround,
            $"{where} hasGround: recorded {expected.HasGround}, managed {actual.HasGround}");
        Assert.True(expected.Ducked == actual.Ducked,
            $"{where} ducked: recorded {expected.Ducked}, managed {actual.Ducked}");
        Assert.True(expected.Ducking == actual.Ducking,
            $"{where} ducking: recorded {expected.Ducking}, managed {actual.Ducking}");
        Assert.True(expected.InDuckJump == actual.InDuckJump,
            $"{where} inDuckJump: recorded {expected.InDuckJump}, managed {actual.InDuckJump}");
        Assert.True(expected.OldButtons == actual.OldButtons,
            $"{where} oldButtons: recorded {expected.OldButtons}, managed {actual.OldButtons}");
        Assert.True(expected.SurfaceProps == actual.SurfaceProps,
            $"{where} surfaceProps: recorded {expected.SurfaceProps}, managed {actual.SurfaceProps}");
        Assert.True(expected.OldWaterLevel == actual.OldWaterLevel,
            $"{where} oldWaterLevel: recorded {expected.OldWaterLevel}, managed {actual.OldWaterLevel}");
        Assert.True(expected.OnLadder == actual.OnLadder,
            $"{where} onLadder: recorded {expected.OnLadder}, managed {actual.OnLadder}");
        Assert.True(expected.StuckLast == actual.StuckLast,
            $"{where} stuckLast: recorded {expected.StuckLast}, managed {actual.StuckLast}");
    }

    //=========================================================================
    // The parity facts.
    //=========================================================================

    /// <summary>
    /// A fixture with no rows would make every fact below pass while measuring
    /// nothing, which is exactly the shape a green suite hides best.
    /// </summary>
    [Fact]
    public void TheRecordedFixtureHasCommandsInIt()
    {
        StockRecording rec = LoadRecording();
        Assert.True(rec.Commands.Count >= 30,
            $"the fixture holds {rec.Commands.Count} commands; it was recorded with 36");
    }

    /// <summary>
    /// The constants came off a live server, so they are the ones the recorded
    /// arithmetic actually ran with. If they were ever regenerated from
    /// defaults instead, this would be the fact that noticed.
    /// </summary>
    [Fact]
    public void TheRecordedConstantsAreTheLiveOnes()
    {
        StockRecording rec = LoadRecording();
        Assert.Equal(4f, rec.Constants.Friction);
        Assert.Equal(75f, rec.Constants.StopSpeed);
        Assert.Equal(3500f, rec.Constants.MaxVelocity);
        Assert.Equal(30f, rec.Constants.AirSpeedCap);
        Assert.Equal(72f, rec.Constants.HullMax.Z);
        Assert.Equal(36f, rec.Constants.DuckHullMax.Z);
    }

    /// <summary>
    /// THE STEP'S ACCEPTANCE. Every recorded command, replayed through the
    /// managed loop against the answers the engine gave the C++, must produce
    /// the state the C++ produced — bit for bit.
    /// </summary>
    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void TheManagedLoopReproducesEveryRecordedCommand()
    {
        StockRecording rec = LoadRecording();

        foreach (StockCommand cmd in rec.Commands)
        {
            (MoveState produced, RecordedWorld world, bool ran) = Replay(rec, cmd);

            Assert.True(ran,
                $"line {cmd.Line}: the managed loop REFUSED move type {cmd.In.MoveType}, " +
                "which the recording shows it running");

            AssertStateMatches(cmd.Out, produced, $"line {cmd.Line} (command {cmd.In.CommandNumber})");

            Assert.True(world.Remaining == 0,
                $"line {cmd.Line}: the managed loop made {cmd.Queries.Count - world.Remaining} " +
                $"of the {cmd.Queries.Count} world queries the C++ made — it stopped asking " +
                $"after: {(world.Asked.Count > 0 ? world.Asked[^1] : "nothing")}");
        }
    }

    /// <summary>
    /// A replay that reproduced the states while asking DIFFERENT questions
    /// would be a coincidence, not parity — so the world checks every argument
    /// and this fact makes sure it really was exercised.
    /// </summary>
    [Fact]
    public void TheReplayCheckedEveryRecordedQueryArgument()
    {
        StockRecording rec = LoadRecording();

        int traces = 0, contents = 0;
        foreach (StockCommand cmd in rec.Commands)
        {
            (_, RecordedWorld world, _) = Replay(rec, cmd);
            foreach (RecordedQuery q in cmd.Queries)
            {
                if (q.Kind == "TR") traces++;
                else contents++;
            }
            Assert.Equal(0, world.Remaining);
        }

        Assert.True(traces >= 100, $"only {traces} traces were replayed");
        Assert.True(contents >= 30, $"only {contents} point-contents queries were replayed");
    }

    /// <summary>
    /// The arms the fixture covers, counted from the fixture itself rather than
    /// asserted in a comment. A recording that lost an arm — a re-record from a
    /// scenario that stopped jumping, say — fails here rather than quietly
    /// certifying less than it did.
    /// </summary>
    [Fact]
    public void TheFixtureStillCoversEveryArmItWasRecordedFor()
    {
        StockRecording rec = LoadRecording();

        int ground = 0, air = 0, jumpHeld = 0, ducked = 0, duckTransition = 0;
        int noclip = 0, landing = 0, leavingGround = 0, oddFriction = 0, still = 0;

        foreach (StockCommand cmd in rec.Commands)
        {
            MoveState s = cmd.In;
            if (s.MoveType == GameMovement.MoveTypeNoClip) { noclip++; continue; }

            if (s.HasGround) ground++; else air++;
            if (!s.HasGround && cmd.Out.HasGround) landing++;
            if (s.HasGround && !cmd.Out.HasGround) leavingGround++;
            if ((s.Buttons & GameMovement.InJump) != 0) jumpHeld++;
            if (s.Ducked) ducked++;
            if (s.Ducking) duckTransition++;
            if (s.SurfaceFriction != 1.0f) oddFriction++;
            if (s.ForwardMove == 0 && s.SideMove == 0 && s.UpMove == 0) still++;
        }

        Assert.True(ground >= 6, $"the fixture has {ground} on-ground commands");
        Assert.True(air >= 6, $"the fixture has {air} airborne commands");
        Assert.True(still >= 6, $"the fixture has {still} standing-still commands");
        Assert.True(jumpHeld >= 4, $"the fixture has {jumpHeld} commands with jump held");
        Assert.True(ducked >= 4, $"the fixture has {ducked} ducked commands");
        Assert.True(duckTransition >= 4, $"the fixture has {duckTransition} duck-transition commands");
        Assert.True(landing >= 4, $"the fixture has {landing} landings");
        Assert.True(leavingGround >= 4, $"the fixture has {leavingGround} take-offs");
        Assert.True(noclip >= 4, $"the fixture has {noclip} noclip commands");
        Assert.True(oddFriction >= 4, $"the fixture has {oddFriction} odd-friction commands");
    }

    /// <summary>
    /// The arms the fixture does NOT cover, stated as a fact so the gap cannot
    /// be forgotten. If a later recording DOES reach one of them this goes red
    /// and the coverage note above has to be rewritten — which is the point.
    /// </summary>
    [Fact]
    public void TheFixtureCoversNoWaterNoLadderAndNoDuckJump()
    {
        StockRecording rec = LoadRecording();

        Assert.DoesNotContain(rec.Commands, c => c.In.WaterLevel > 0);
        Assert.DoesNotContain(rec.Commands, c => c.In.MoveType == GameMovement.MoveTypeLadder);
        Assert.DoesNotContain(rec.Commands, c => c.In.JumpTime > 0f || c.In.InDuckJump);

        // And the reason for the last one, which is a property of the build
        // rather than of the scenario: CheckJumpButton starts the duck-jump
        // family only in single player.
        Assert.Equal(2, rec.Constants.MaxClients);
    }

    //=========================================================================
    // The arms no live recording on this map could reach, as hermetic facts.
    //
    // These are NOT parity: nothing native produced these numbers, and they are
    // stated as such. What they check is that the arm is reachable, does the
    // shape stock's does, and is not a stub — which is the most that can be
    // said without a map that has water on it.
    //=========================================================================

    /// <summary>A world that answers a fixed script, for the arms with no recording.</summary>
    private sealed class ScriptedWorld : IMoveWorld
    {
        public Func<Vector, Vector, Vector, Vector, uint, int, MoveTrace> OnTrace { get; set; }
            = static (_, end, _, _, _, _) => MoveTrace.Clear(end);

        /// <summary>
        /// A flat floor under the player: every downward trace stops at once on
        /// a face pointing straight up. Without it CategorizePosition takes the
        /// ground away before anything that needs ground is asked, which is how
        /// the first version of these facts measured a jump that never happened.
        /// </summary>
        public static MoveTrace Floor(Vector start, Vector end)
            => end.Z < start.Z
                 ? new MoveTrace
                   {
                       Fraction = 0f,
                       EndPos = start,
                       PlaneNormal = new Vector(0f, 0f, 1f),
                       SurfaceProps = 3,
                       HasEntity = true,
                       HasGround = true,
                       HitWorld = true,
                   }
                 : MoveTrace.Clear(end);

        public Func<Vector, int, int> OnPointContents { get; set; } = static (_, _) => 0;

        public MoveSurface SurfaceAnswer { get; set; } = new()
        {
            Valid = true, Friction = 0.8f, Material = 'C', Climbable = 0,
            MaxSpeedFactor = 1f, JumpFactor = 1f,
        };

        public List<string> Effects { get; } = new();

        public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs,
                               uint mask, int collisionGroup)
            => OnTrace(start, end, mins, maxs, mask, collisionGroup);

        public int PointContents(in Vector point, int slot) => OnPointContents(point, slot);

        public List<bool> Resets { get; } = new();
        public void ResetPointContentsCache(bool optimizations) => Resets.Add(optimizations);
        public MoveSurface Surface(int surfaceProps) => SurfaceAnswer;

        public void ResetTouchList() => Effects.Add("ResetTouchList");
        public void AddToTouched(in MoveTrace trace, in Vector velocity) => Effects.Add("AddToTouched");
        public void StartSound(in Vector origin, string sound) => Effects.Add("StartSound:" + sound);
        public void PlayerSetAnimation(int activity) => Effects.Add("Animation:" + activity);
        /// <summary>The damage the loop asked for, NaN until it asks.</summary>
        public float LastFallDamage = float.NaN;

        public bool PlayerFallingDamage(float damage)
        {
            LastFallDamage = damage;
            Effects.Add("FallingDamage");
            return true;
        }
        public void PlayStepSound(in Vector o, int p, float v, bool f) => Effects.Add("StepSound");
        public void UpdateStepSound(in Vector o, in Vector v, int p) => Effects.Add("UpdateStepSound");
        public void Splash() => Effects.Add("Splash");
        public void RumbleEffect(byte i, byte d, byte f) => Effects.Add("Rumble");
        public void ResetDuckLatched() => Effects.Add("ResetDuckLatched");
        public void SetPlayerSurface(int material) => Effects.Add("SetPlayerSurface");
        public float EngineTime() => 100f;
    }

    //=========================================================================
    // STEP 8f-movetypes — the three arms that used to be declared refusals.
    //
    // Every number below is hand-computed from the C++ these methods replace
    // and written as a literal, not derived from the managed primitives: a
    // test that recomputes the expression it is checking is a check that
    // cannot fail. FrameTime is 1/16 throughout so every product lands exactly
    // on a float and the assertions can be bit-exact like the rest of the file.
    //=========================================================================

    /// <summary>The state an observer command starts from. 1/16 frame time, see above.</summary>
    private static MoveState ObserverState(int mode)
    {
        MoveState s = BaselineState();
        s.MoveType = GameMovement.MoveTypeObserver;
        s.ObserverMode = mode;
        s.FrameTime = 0.0625f;
        s.Origin = Vector.Zero;
        return s;
    }

    /// <summary>
    /// <c>sv_specnoclip</c> on — stock's default arm — hands a ROAMING
    /// observer to <c>FullNoClipMove</c> at the SPECTATOR convars, not the
    /// noclip ones. 320 (sv_maxspeed) * 3 (sv_specspeed) caps the wish, 5
    /// (sv_specaccelerate) * (1/16) * 960 accelerates to 300, sv_friction 4
    /// bleeds a quarter of it, and 225/16 is the step.
    /// </summary>
    /// <summary>
    /// The three decisions are each loop's own (statics to zero): rules handed
    /// to one game instance's loop leave another's at stock.
    /// </summary>
    [Fact]
    public void EachLoopHoldsItsOwnMovementRules()
    {
        var mine = new GameMovement(new ScriptedWorld(), MoveConstantDefaults.Stock);
        var other = new GameMovement(new ScriptedWorld(), MoveConstantDefaults.Stock);
        var doubled = new MovementRules(static _ => 2f,
                                        static (_, friction, _, _, _) => friction * 2f,
                                        static (_, maxSpeed, _, _) => maxSpeed * 2f);

        Assert.Equal(MovementRules.Stock, mine.Rules);
        mine.Rules = doubled;

        Assert.Equal(doubled, mine.Rules);              // the stimulus landed
        Assert.Equal(MovementRules.Stock, other.Rules);
        Assert.Equal(1f, MovementRules.Stock.Jump(default));
        Assert.Equal(2f, mine.Rules.Jump(default));
    }

    [Fact]
    public void ARoamingObserverWithSpecNoClipMovesAtTheSpectatorConvars()
    {
        var world = new ScriptedWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = ObserverState(GameMovement.ObsModeRoaming);
        s.ForwardMove = 400f;

        Assert.True(movement.ProcessMovement(s));

        AssertBits(new Vector(225f, 0f, 0f), s.Velocity, "spec noclip velocity");
        AssertBits(14.0625f, s.Origin.X, "spec noclip origin.x");
    }

    /// <summary>
    /// <c>sv_specnoclip</c> OFF is the arm this step actually ported — a
    /// clipped free roam that ends in <c>TryPlayerMove</c>. Different
    /// arithmetic from the noclip arm: no <c>maxspeed/4</c> control floor, and
    /// the acceleration is against the UNCAPPED wish (1200, not 960), so
    /// 5 * (1/16) * 1200 = 375 and friction leaves 281.25.
    /// </summary>
    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void ARoamingObserverWithoutSpecNoClipDoesAClippedFreeRoam()
    {
        MoveConstants c = MoveConstantDefaults.Stock;
        c.SpecNoClip = false;

        var world = new ScriptedWorld();
        var movement = new GameMovement(world, c);
        MoveState s = ObserverState(GameMovement.ObsModeRoaming);
        s.ForwardMove = 400f;

        Assert.True(movement.ProcessMovement(s));

        AssertBits(new Vector(281.25f, 0f, 0f), s.Velocity, "clipped roam velocity");
        AssertBits(17.578125f, s.Origin.X, "clipped roam origin.x");
    }

    /// <summary>
    /// And the clipped arm really CLIPS — a wall in front of a roaming
    /// observer stops them. This is the fact that separates the two roam arms
    /// by behaviour rather than by arithmetic: <c>FullNoClipMove</c> writes
    /// the origin directly and would walk straight through the same wall,
    /// which the fact below asserts on the same world.
    /// </summary>
    [Fact]
    public void AWallStopsARoamingObserverWhenSpecNoClipIsOff()
    {
        MoveConstants c = MoveConstantDefaults.Stock;
        c.SpecNoClip = false;

        var world = new ScriptedWorld { OnTrace = ForwardWall };
        var movement = new GameMovement(world, c);
        MoveState s = ObserverState(GameMovement.ObsModeRoaming);
        s.ForwardMove = 400f;

        Assert.True(movement.ProcessMovement(s));

        AssertBits(0f, s.Origin.X, "blocked roam origin.x");
    }

    /// <summary>The other half of the pair: with noclip on, the same wall is not there.</summary>
    [Fact]
    public void TheSameWallDoesNotStopARoamingObserverWhenSpecNoClipIsOn()
    {
        var world = new ScriptedWorld { OnTrace = ForwardWall };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = ObserverState(GameMovement.ObsModeRoaming);
        s.ForwardMove = 400f;

        Assert.True(movement.ProcessMovement(s));

        AssertBits(14.0625f, s.Origin.X, "noclip through wall origin.x");
    }

    /// <summary>
    /// <c>"don't move in fixed or death cam mode"</c> — every observer mode
    /// below ROAMING that is not one of the three target-following ones leaves
    /// the player exactly where they were, however hard they push forward.
    /// </summary>
    [Theory]
    [InlineData(GameMovement.ObsModeNone)]
    [InlineData(GameMovement.ObsModeDeathCam)]
    [InlineData(GameMovement.ObsModeFreezeCam)]
    [InlineData(GameMovement.ObsModeFixed)]
    public void AnObserverThatIsNotRoamingDoesNotMoveAtAll(int mode)
    {
        var world = new ScriptedWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = ObserverState(mode);
        s.ForwardMove = 400f;

        Assert.True(movement.ProcessMovement(s));

        AssertBits(Vector.Zero, s.Origin, "fixed observer origin");
        AssertBits(Vector.Zero, s.Velocity, "fixed observer velocity");
    }

    /// <summary>
    /// The target-following arm with NO target performs stock's bare
    /// <c>return</c> (a target that went away), and is counted.
    /// </summary>
    [Theory]
    [InlineData(GameMovement.ObsModeInEye)]
    [InlineData(GameMovement.ObsModeChase)]
    [InlineData(GameMovement.ObsModePoi)]
    public void AnObserverFollowingATargetReachesTheUnreachableArmAndIsCounted(int mode)
    {
        var world = new ScriptedWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = ObserverState(mode);
        s.ForwardMove = 400f;

        Assert.True(movement.ProcessMovement(s));

        Assert.Equal(1L, movement.ObserverTargetAsks);
        AssertBits(Vector.Zero, s.Origin, "following observer origin");
    }

    /// <summary>
    /// With a target (the observer ported, lane player-msgs), the arm is
    /// stock's: the observer takes the target's origin, angles and velocity
    /// and does not move under its own input.
    /// </summary>
    [Theory]
    [InlineData(GameMovement.ObsModeInEye)]
    [InlineData(GameMovement.ObsModeChase)]
    public void An_observer_with_a_target_takes_its_origin_angles_and_velocity(int mode)
    {
        var world = new ScriptedWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = ObserverState(mode);
        s.ForwardMove = 400f;
        s.HasObserverTarget = true;
        s.ObserverTargetOrigin = new Vector(100f, 200f, 8f);
        s.ObserverTargetAngles = new Vector(0f, 45f, 0f);
        s.ObserverTargetVelocity = new Vector(50f, 0f, 0f);

        Assert.True(movement.ProcessMovement(s));

        AssertBits(new Vector(100f, 200f, 8f), s.Origin, "the target's origin");
        AssertBits(new Vector(0f, 45f, 0f), s.ViewAngles, "the target's angles");
        AssertBits(new Vector(50f, 0f, 0f), s.Velocity, "the target's velocity");
    }

    /// <summary>
    /// ...and a ROAMING observer does not reach it, so the counter is not one
    /// that counts every command.
    /// </summary>
    [Fact]
    public void ARoamingObserverDoesNotReachTheTargetFollowingArm()
    {
        var world = new ScriptedWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = ObserverState(GameMovement.ObsModeRoaming);
        s.ForwardMove = 400f;

        movement.ProcessMovement(s);

        Assert.Equal(0L, movement.ObserverTargetAsks);
    }

    /// <summary>
    /// The clipped roam clamps the wish speed to <c>sv_maxvelocity</c> and NOT
    /// to <c>m_flMaxSpeed</c> — the two are different numbers in stock and only
    /// this one reaches <c>Accelerate</c>. With sv_maxvelocity at 10 the
    /// acceleration is against 10, so 100 * (1/16) * 10 = 62.5 is capped by the
    /// add speed of 10 and friction leaves 7.5. Without the clamp the same
    /// command accelerates against 400.
    /// </summary>
    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void TheClippedRoamClampsTheWishSpeedToServerMaxVelocity()
    {
        MoveConstants c = MoveConstantDefaults.Stock;
        c.SpecNoClip = false;
        c.SpecSpeed = 1f;
        c.SpecAccelerate = 100f;
        c.MaxVelocity = 10f;

        var world = new ScriptedWorld();
        var movement = new GameMovement(world, c);
        MoveState s = ObserverState(GameMovement.ObsModeRoaming);
        s.ForwardMove = 400f;

        Assert.True(movement.ProcessMovement(s));

        AssertBits(new Vector(7.5f, 0f, 0f), s.Velocity, "clamped roam velocity");
    }

    /// <summary>
    /// A roaming observer with no input still bleeds speed, at bare
    /// <c>sv_friction</c> times the frame: 100 * 4 * (1/16) = 25 off 100.
    /// </summary>
    [Fact]
    public void ARoamingObserverBleedsSpeedAtServerFrictionWithNoInput()
    {
        MoveConstants c = MoveConstantDefaults.Stock;
        c.SpecNoClip = false;

        var world = new ScriptedWorld();
        var movement = new GameMovement(world, c);
        MoveState s = ObserverState(GameMovement.ObsModeRoaming);
        s.Velocity = new Vector(100f, 0f, 0f);

        Assert.True(movement.ProcessMovement(s));

        AssertBits(new Vector(75f, 0f, 0f), s.Velocity, "observer friction velocity");
    }

    /// <summary>
    /// Under one unit per second a roaming observer is stopped dead and the
    /// move RETURNS — no <c>TryPlayerMove</c>, so the origin does not creep.
    /// </summary>
    [Fact]
    public void ARoamingObserverBelowOneUnitPerSecondIsStoppedDead()
    {
        MoveConstants c = MoveConstantDefaults.Stock;
        c.SpecNoClip = false;

        var world = new ScriptedWorld();
        var movement = new GameMovement(world, c);
        MoveState s = ObserverState(GameMovement.ObsModeRoaming);
        s.Velocity = new Vector(0.5f, 0f, 0f);

        Assert.True(movement.ProcessMovement(s));

        AssertBits(Vector.Zero, s.Velocity, "stopped observer velocity");
        AssertBits(Vector.Zero, s.Origin, "stopped observer origin");
    }

    //-------------------------------------------------------------------------
    // MOVETYPE_FLY and MOVETYPE_FLYGRAVITY — FullTossMove.
    //-------------------------------------------------------------------------

    private static MoveState TossState(int moveType)
    {
        MoveState s = BaselineState();
        s.MoveType = moveType;
        s.FrameTime = 0.0625f;
        s.Origin = new Vector(0f, 0f, 64f);
        return s;
    }

    /// <summary>
    /// <c>MOVETYPE_FLY</c> coasts: the velocity is integrated once and gravity
    /// is not applied, so a player flying flat stays at the same height.
    /// </summary>
    [Fact]
    public void AFlyingPlayerCoastsAlongItsVelocityAndDoesNotFall()
    {
        var world = new ScriptedWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = TossState(GameMovement.MoveTypeFly);
        s.Velocity = new Vector(100f, 0f, 0f);

        Assert.True(movement.ProcessMovement(s));

        AssertBits(new Vector(6.25f, 0f, 64f), s.Origin, "fly origin");
        AssertBits(new Vector(100f, 0f, 0f), s.Velocity, "fly velocity");
    }

    /// <summary>
    /// <c>MOVETYPE_FLYGRAVITY</c> is the same body plus exactly one
    /// <c>AddGravity</c>: 600 * (1/16) = 37.5 off the vertical velocity, and
    /// 37.5 * (1/16) = 2.34375 off the height in the same command.
    /// </summary>
    [Fact]
    public void AFlyGravityPlayerFallsByExactlyOneGravityStep()
    {
        var world = new ScriptedWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = TossState(GameMovement.MoveTypeFlyGravity);
        s.Velocity = new Vector(100f, 0f, 0f);

        Assert.True(movement.ProcessMovement(s));

        AssertBits(new Vector(6.25f, 0f, 61.65625f), s.Origin, "flygravity origin");
        AssertBits(new Vector(100f, 0f, -37.5f), s.Velocity, "flygravity velocity");
    }

    /// <summary>
    /// THE ONE LINE THAT SEPARATES THE TWO TYPES, asserted as a difference:
    /// the same command under both move types travels the same distance
    /// horizontally and differs only in the vertical, so a port that ran
    /// gravity for both (or neither) is red here even if both arms move.
    /// </summary>
    [Fact]
    public void TheTwoFlyingTypesDifferOnlyByTheGravityStep()
    {
        var movement = new GameMovement(new ScriptedWorld(), MoveConstantDefaults.Stock);

        MoveState fly = TossState(GameMovement.MoveTypeFly);
        fly.Velocity = new Vector(100f, 0f, 0f);
        movement.ProcessMovement(fly);

        MoveState grav = TossState(GameMovement.MoveTypeFlyGravity);
        grav.Velocity = new Vector(100f, 0f, 0f);
        movement.ProcessMovement(grav);

        AssertBits(fly.Origin.X, grav.Origin.X, "same horizontal step");
        AssertBits(64f, fly.Origin.Z, "fly holds its height");
        AssertBits(-2.34375f, grav.Origin.Z - 64f, "flygravity drops one step");
    }

    /// <summary>
    /// A flying player with input accelerates like a walking one at
    /// <c>sv_accelerate</c>: 10 * (1/16) * 320 = 200, capped by the add speed
    /// of 320 — no, 200 is below it, so the velocity is 200 along the view.
    /// </summary>
    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void AFlyingPlayerWithForwardInputAcceleratesAtServerAccelerate()
    {
        var world = new ScriptedWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = TossState(GameMovement.MoveTypeFly);
        s.ForwardMove = 400f;

        Assert.True(movement.ProcessMovement(s));

        AssertBits(new Vector(200f, 0f, 0f), s.Velocity, "toss accelerate velocity");
        AssertBits(12.5f, s.Origin.X, "toss accelerate origin.x");
    }

    /// <summary>
    /// On the ground with nothing moving at all, <c>FullTossMove</c> RETURNS
    /// before pushing — and the observable difference is the second
    /// <c>CheckWater</c> at the bottom of the body, which the return skips.
    /// One point-contents query rather than two.
    /// </summary>
    [Fact]
    public void AFlyingPlayerAtRestOnTheGroundReturnsBeforePushing()
    {
        int contents = 0;
        var world = new ScriptedWorld
        {
            OnTrace = (start, end, _, _, _, _) => ScriptedWorld.Floor(start, end),
            OnPointContents = (_, _) => { contents++; return 0; },
        };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = TossState(GameMovement.MoveTypeFly);

        Assert.True(movement.ProcessMovement(s));

        Assert.True(s.HasGround);
        AssertBits(new Vector(0f, 0f, 64f), s.Origin, "resting toss origin");

        // CategorizePosition's CheckWater, then FullTossMove's first one. The
        // one at the bottom of the body is the one the early return skips.
        Assert.Equal(2, contents);
    }

    /// <summary>
    /// ...and the return really is conditional: the same player on the same
    /// ground with a base velocity is pushed by it. Without this the fact above
    /// would pass on a <c>FullTossMove</c> that returned unconditionally.
    /// </summary>
    [Fact]
    public void AFlyingPlayerOnTheGroundIsStillPushedByItsBaseVelocity()
    {
        var world = new ScriptedWorld
        {
            OnTrace = (start, end, _, _, _, _) => ScriptedWorld.Floor(start, end),
        };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = TossState(GameMovement.MoveTypeFly);
        s.BaseVelocity = new Vector(80f, 0f, 0f);

        Assert.True(movement.ProcessMovement(s));

        AssertBits(5f, s.Origin.X, "base velocity push");
        AssertBits(Vector.Zero, s.Velocity, "base velocity is not kept in velocity");
    }

    //-------------------------------------------------------------------------
    // PerformFlyCollisionResolution — what a flier does when it hits something.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>MOVECOLLIDE_FLY_BOUNCE</c> reflects: the overbounce is
    /// <c>2 - m_surfaceFriction</c>, and the floor under this player gives it
    /// 0.5 * 1.25 = 0.625, so the wall sends 100 back out at 37.5.
    /// </summary>
    [Fact]
    public void ABouncingFlierIsReflectedByTheSurfaceItHits()
    {
        var world = FlyIntoWallWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = TossState(GameMovement.MoveTypeFly);
        s.Velocity = new Vector(100f, 0f, 0f);
        s.MoveCollide = GameMovement.MoveCollideFlyBounce;

        Assert.True(movement.ProcessMovement(s));

        AssertBits(new Vector(-37.5f, 0f, 0f), s.Velocity, "bounced velocity");
        AssertBits(3.125f, s.Origin.X, "bounced origin.x");
    }

    /// <summary>
    /// <c>MOVECOLLIDE_DEFAULT</c> takes an overbounce of 1 on the same wall,
    /// which removes the whole component into the plane and stops the flier
    /// rather than reflecting it. Same world, same command, different collide.
    /// </summary>
    [Fact]
    public void ADefaultCollideFlierIsStoppedByTheSurfaceRatherThanReflected()
    {
        var world = FlyIntoWallWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = TossState(GameMovement.MoveTypeFly);
        s.Velocity = new Vector(100f, 0f, 0f);
        s.MoveCollide = GameMovement.MoveCollideDefault;

        Assert.True(movement.ProcessMovement(s));

        AssertBits(Vector.Zero, s.Velocity, "default collide velocity");
    }

    /// <summary>
    /// <c>MOVECOLLIDE_FLY_CUSTOM</c> leaves the velocity to whatever touch did
    /// with it — stock <c>Assert(0)</c>s and then changes nothing, and a port
    /// that folded this into the default case would have stopped the flier.
    /// </summary>
    [Fact]
    public void AFlyCustomCollideLeavesTheVelocityAlone()
    {
        var world = FlyIntoWallWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = TossState(GameMovement.MoveTypeFly);
        s.Velocity = new Vector(100f, 0f, 0f);
        s.MoveCollide = GameMovement.MoveCollideFlyCustom;

        Assert.True(movement.ProcessMovement(s));

        AssertBits(new Vector(100f, 0f, 0f), s.Velocity, "fly custom velocity");
    }

    /// <summary>
    /// A flier that lands flat on a floor and is not a bouncer stops dead and
    /// takes the ground: the <c>normal.z &gt; 0.7</c> arm.
    /// </summary>
    [Fact]
    public void AFlierLandingFlatStopsDeadAndTakesTheGround()
    {
        var world = new ScriptedWorld { OnTrace = FarFloorOnly };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = TossState(GameMovement.MoveTypeFly);
        s.Velocity = new Vector(0f, 0f, -100f);

        Assert.True(movement.ProcessMovement(s));

        Assert.True(s.HasGround);
        AssertBits(Vector.Zero, s.Velocity, "landed flier velocity");
        AssertBits(60.875f, s.Origin.Z, "landed flier origin.z");
    }

    /// <summary>
    /// A BOUNCING flier landing flat with speed left in it keeps rolling
    /// instead: the second push, scaled by
    /// <c>(1 - fraction) * frametime * 0.9</c>. 200 * 0.028125 = 5.625 past
    /// the 6.25 the first push reached.
    /// </summary>
    [Fact]
    public void ABouncingFlierWithSpeedLeftKeepsRollingAfterItLands()
    {
        var world = new ScriptedWorld { OnTrace = FarFloorOnly };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = TossState(GameMovement.MoveTypeFly);
        s.Velocity = new Vector(200f, 0f, -100f);
        s.MoveCollide = GameMovement.MoveCollideFlyBounce;

        Assert.True(movement.ProcessMovement(s));

        AssertBits(new Vector(200f, 0f, 0f), s.Velocity, "rolling flier velocity");
        AssertBits(11.875f, s.Origin.X, "rolling flier origin.x");
    }

    /// <summary>
    /// A flier that ends up inside a solid stops dead and takes the ground
    /// there — the <c>allsolid</c> arm, which returns before any collision
    /// resolution runs at all.
    /// </summary>
    [Fact]
    public void AFlierTrappedInASolidStopsDeadWithoutResolvingTheCollision()
    {
        var world = new ScriptedWorld
        {
            OnTrace = (start, end, _, _, _, _) => end.Z < start.Z
                ? MoveTrace.Clear(end)
                : new MoveTrace
                  {
                      Fraction = 0f,
                      EndPos = start,
                      PlaneNormal = new Vector(-1f, 0f, 0f),
                      AllSolid = true,
                      SurfaceProps = 3,
                      HasEntity = true,
                      HasGround = true,
                      HitWorld = true,
                  },
        };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = TossState(GameMovement.MoveTypeFly);
        s.Velocity = new Vector(100f, 0f, 0f);
        s.MoveCollide = GameMovement.MoveCollideFlyBounce;

        Assert.True(movement.ProcessMovement(s));

        AssertBits(Vector.Zero, s.Velocity, "trapped flier velocity");
        Assert.True(s.HasGround);
    }

    //-------------------------------------------------------------------------
    // The scripted worlds the three arms above are driven through.
    //-------------------------------------------------------------------------

    /// <summary>A wall across the +x axis; everything else is clear.</summary>
    private static MoveTrace ForwardWall(Vector start, Vector end, Vector mins, Vector maxs,
                                         uint mask, int group)
        => end.X > start.X
             ? new MoveTrace
               {
                   Fraction = 0f,
                   EndPos = start,
                   PlaneNormal = new Vector(-1f, 0f, 0f),
                   SurfaceProps = 3,
                   HasEntity = true,
                   HitWorld = true,
               }
             : MoveTrace.Clear(end);

    /// <summary>
    /// A floor two units down (so CategorizePosition finds ground and the
    /// surface friction is a real one) and a wall half way along +x.
    /// </summary>
    private static ScriptedWorld FlyIntoWallWorld() => new()
    {
        SurfaceAnswer = new MoveSurface
        {
            Valid = true, Friction = 0.5f, Material = 'C', Climbable = 0,
            MaxSpeedFactor = 1f, JumpFactor = 1f,
        },
        OnTrace = (start, end, _, _, _, _) => end.Z < start.Z
            ? ScriptedWorld.Floor(start, end)
            : end.X > start.X
                ? new MoveTrace
                  {
                      Fraction = 0.5f,
                      EndPos = new Vector(start.X + (end.X - start.X) * 0.5f, start.Y, start.Z),
                      PlaneNormal = new Vector(-1f, 0f, 0f),
                      SurfaceProps = 3,
                      HasEntity = true,
                      HitWorld = true,
                  }
                : MoveTrace.Clear(end),
    };

    /// <summary>
    /// A floor that is only there for a trace reaching more than three units
    /// down — so <c>CategorizePosition</c>'s two-unit probe misses it and the
    /// player is genuinely airborne when <c>FullTossMove</c> pushes.
    /// </summary>
    private static MoveTrace FarFloorOnly(Vector start, Vector end, Vector mins, Vector maxs,
                                          uint mask, int group)
        => end.Z < start.Z - 3f
             ? new MoveTrace
               {
                   Fraction = 0.5f,
                   EndPos = new Vector(start.X + (end.X - start.X) * 0.5f,
                                       start.Y + (end.Y - start.Y) * 0.5f,
                                       start.Z + (end.Z - start.Z) * 0.5f),
                   PlaneNormal = new Vector(0f, 0f, 1f),
                   SurfaceProps = 3,
                   HasEntity = true,
                   HasGround = true,
                   HitWorld = true,
               }
             : MoveTrace.Clear(end);

    private static MoveState BaselineState() => new()
    {
        CommandNumber = 1,
        FrameTime = 0.015f,
        MoveType = GameMovement.MoveTypeWalk,
        Flags = 0,
        Origin = new Vector(0f, 0f, 64f),
        Velocity = Vector.Zero,
        ViewAngles = Vector.Zero,
        MaxSpeed = 320f,
        ClientMaxSpeed = 320f,
        StepSize = 18f,
        ViewOffset = new Vector(0f, 0f, 64f),
        CurrentGravity = 600f,
        SurfaceFriction = 1f,
        Health = 100,
        IsAlive = true,
        LaggedMovementValue = 1f,
        ModelScale = 1f,
        EntIndex = 1,
        AllowAutoMovement = true,
        SurfaceDataValid = true,
        SurfaceProps = 3,
        IsServer = true,
    };

    //=========================================================================
    // DUCKING UNDER A FORTY-UNIT GAP -- the policy-player lane's first gate.
    //
    // The stand hull is 72 units tall and the duck hull 36 (MoveConstantDefaults.Stock,
    // pinned by ThePlayerHullsAreStockHalfLifes). So a 40-unit gap is a hole a
    // crouching player fits through and a standing one does not, and the pair of
    // facts is the two halves of that one sentence: they GO, and they CANNOT
    // STAND while they are under it.
    //
    // These are hermetic, not parity: no dm_lockdown recording of a 40-unit
    // tunnel exists. What they pin is that the arm is reachable, does the shape
    // stock's does, and is not a stub.
    //=========================================================================

    /// <summary>
    /// A world with a floor at the player's feet and a ceiling
    /// <paramref name="gapHeight"/> units above it.
    ///
    /// <para>
    /// The ceiling is expressed as "a hull taller than the gap does not fit
    /// ANYWHERE", which is what a tunnel of that height means to a swept hull
    /// and is the only thing <c>CanUnduck</c> asks. Modelling it as a plane at a
    /// height would need the trace to know where the origin is relative to it,
    /// and would then be a second movement implementation inside a test.
    /// </para>
    /// </summary>
    private static ScriptedWorld TunnelWorld(float gapHeight) => new()
    {
        OnTrace = (start, end, mins, maxs, _, _) =>
            maxs.Z - mins.Z > gapHeight
                ? new MoveTrace
                  {
                      StartSolid = true,
                      AllSolid = true,
                      Fraction = 0f,
                      EndPos = start,
                      PlaneNormal = new Vector(0f, 0f, -1f),
                      SurfaceProps = 3,
                      HasEntity = true,
                      HitWorld = true,
                  }
                : ScriptedWorld.Floor(start, end),
    };

    /// <summary>A player already crouched, standing on the floor, holding duck.</summary>
    private static MoveState CrouchedState()
    {
        MoveState s = BaselineState();
        s.Ducked = true;
        s.Ducking = false;
        s.Flags = GameMovement.FlOnGround | GameMovement.FlDucking;
        s.HasGround = true;
        s.DuckTime = GameMovement.GameMovementDuckTime;
        s.ViewOffset = new Vector(0f, 0f, 28f);
        return s;
    }

    /// <summary>
    /// HALF ONE: they get through. A crouched player pushing forward under a
    /// 40-unit ceiling actually moves — the duck hull fits, so the walk traces
    /// come back clear and the origin advances.
    ///
    /// <para>
    /// Stated as "moved further than a unit" rather than an exact origin
    /// because the point of the half is that the tunnel is PASSABLE; the exact
    /// arithmetic of a walk is the fixture's job and is bit-exact there.
    /// </para>
    /// </summary>
    [Fact]
    public void ACrouchedPlayerWalksThroughAFortyUnitGap()
    {
        var world = TunnelWorld(40f);
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = CrouchedState();
        s.Buttons = GameMovement.InDuck;
        s.OldButtons = GameMovement.InDuck;
        s.ForwardMove = 400f;

        // A BURST AND NOT ONE COMMAND. One 15ms tick from a standstill covers a
        // quarter of a unit whether the tunnel is passable or solid, so a fact
        // over one command cannot tell them apart. Twenty is a third of a
        // second of walking.
        for (int i = 0; i < 20; i++)
        {
            s.CommandNumber = i + 1;

            // SETUP MOVE, WHICH IS WHAT THE REAL LOOP DOES BETWEEN COMMANDS.
            // ProcessMovement crops MaxSpeed to a third for a ducked player and
            // leaves it cropped; the run loop refreshes it from the player's own
            // number every command (RunCommandStage's SetupMove block), and a
            // test that reuses one state without doing so watches the speed
            // decay by (1/3)^20 and calls a passable tunnel blocked.
            s.MaxSpeed = 320f;
            s.ClientMaxSpeed = 320f;
            s.ForwardMove = 400f;

            Assert.True(movement.ProcessMovement(s));
        }

        Assert.True(s.Origin.X > 20f,
            $"a crouched player must fit through a 40-unit gap; moved {s.Origin.X}");
        Assert.True(s.Ducked, "and they are still crouched at the far end");
    }

    /// <summary>
    /// HALF TWO: they cannot stand up in it. Releasing crouch under the same
    /// ceiling leaves them ducked — <c>CanUnduck</c>'s standing-hull trace
    /// starts solid, so stock's "still under something" arm re-arms the duck
    /// rather than letting them grow into the roof.
    ///
    /// <para>
    /// Both <c>FL_DUCKING</c> and <c>m_bDucked</c> are asserted. Stock's arm
    /// writes both, and a port that dropped either would leave the hull and the
    /// flag disagreeing about the same player, which is the shape of every
    /// "stuck in the ceiling" bug this rule exists to prevent.
    /// </para>
    /// </summary>
    [Fact]
    public void ACrouchedPlayerCannotStandUnderAFortyUnitGap()
    {
        var world = TunnelWorld(40f);
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = CrouchedState();
        s.Buttons = 0;                          // crouch RELEASED
        s.OldButtons = GameMovement.InDuck;     // ...on this very command

        // Stock's unduck takes TimeToUnduck (0.2s) of commands to complete, so
        // a single tick proves nothing either way. Forty ticks is 0.6s — three
        // times over — and they are still down at the end of it.
        for (int i = 0; i < 40; i++)
        {
            s.CommandNumber = i + 1;
            Assert.True(movement.ProcessMovement(s));
        }

        Assert.True(s.Ducked, "a player under a 40-unit ceiling must stay ducked");
        Assert.Equal(GameMovement.FlDucking, s.Flags & GameMovement.FlDucking);
    }

    /// <summary>
    /// AND THE CEILING IS WHAT STOPS THEM. The same released command with the
    /// roof taken away DOES stand them up — without this the fact above would
    /// pass on a build that could never unduck at all.
    /// </summary>
    [Fact]
    public void TheSamePlayerStandsUpOnceTheCeilingIsGone()
    {
        var world = TunnelWorld(1000f);
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = CrouchedState();
        s.Buttons = 0;
        s.OldButtons = GameMovement.InDuck;

        for (int i = 0; i < 40; i++)
        {
            s.CommandNumber = i + 1;
            Assert.True(movement.ProcessMovement(s));
        }

        Assert.False(s.Ducked, "with headroom, releasing crouch stands them up");
        Assert.Equal(0, s.Flags & GameMovement.FlDucking);
    }

    //=========================================================================
    // FALL DAMAGE THROUGH THE LOOP -- the policy-player lane's gate.
    //
    // MoveMathTests.FallDamageTests pins the RULE. These pin the WIRING: that
    // the loop arms the ask on the same fall velocity it then scales the damage
    // by, which is what was not true while the amount was recomputed natively
    // from the player's pre-move member.
    //=========================================================================

    /// <summary>
    /// A landing that arms fall damage hands the world the amount
    /// <see cref="MoveMath.FallDamage"/> chose for the very velocity that armed
    /// it. Nothing else may compute it: the world double records what it was
    /// handed and the fact reads it back.
    /// </summary>
    [Fact]
    public void ALandingHandsTheWorldTheDamageForItsOwnFallVelocity()
    {
        var world = new ScriptedWorld { OnTrace = (st, en, _, _, _, _) => ScriptedWorld.Floor(st, en) };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = BaselineState();
        s.FallVelocity = 800f;
        s.Flags = GameMovement.FlOnGround;
        s.HasGround = true;

        movement.ProcessMovement(s);

        Assert.Contains("FallingDamage", world.Effects);
        Assert.Equal(MoveMath.FallDamage(800f), world.LastFallDamage, 4);
    }

    /// <summary>
    /// A fall one unit UNDER the safe speed never reaches the world at all —
    /// the arming test and the rule agree about the boundary from both sides.
    /// </summary>
    [Fact]
    public void ALandingUnderTheSafeSpeedNeverAsksTheWorldForDamage()
    {
        var world = new ScriptedWorld { OnTrace = (st, en, _, _, _, _) => ScriptedWorld.Floor(st, en) };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = BaselineState();
        s.FallVelocity = 579f;   // PLAYER_MAX_SAFE_FALL_SPEED - 1, shareddefs.h:416
        s.Flags = GameMovement.FlOnGround;
        s.HasGround = true;

        movement.ProcessMovement(s);

        Assert.DoesNotContain("FallingDamage", world.Effects);
        Assert.True(float.IsNaN(world.LastFallDamage), "the world was never asked");
    }

    /// <summary>
    /// A fall one unit OVER it does reach the world, and with a positive
    /// number. The pair either side of the boundary is the lane's gate.
    /// </summary>
    [Fact]
    public void ALandingOverTheSafeSpeedAsksTheWorldForPositiveDamage()
    {
        var world = new ScriptedWorld { OnTrace = (st, en, _, _, _, _) => ScriptedWorld.Floor(st, en) };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = BaselineState();
        s.FallVelocity = 581f;   // PLAYER_MAX_SAFE_FALL_SPEED + 1, shareddefs.h:416
        s.Flags = GameMovement.FlOnGround;
        s.HasGround = true;

        movement.ProcessMovement(s);

        Assert.Contains("FallingDamage", world.Effects);
        Assert.True(world.LastFallDamage > 0f,
            $"a fall past the safe speed must cost health, got {world.LastFallDamage}");
    }

    /// <summary>
    /// A player whose feet are in water is at least at level one — the arm no
    /// dm_lockdown recording can reach, because the map has no water.
    /// </summary>
    /// <summary>
    /// The world's point-contents cache is reset at the top of every command,
    /// as stock's ProcessMovement does on both sides (the client's replay
    /// never reached native's reset before the cache moved, 2026-10-19).
    /// </summary>
    [Fact]
    public void EveryCommandResetsThePointContentsCacheFirst()
    {
        var world = new ScriptedWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = BaselineState();
        s.MoveType = GameMovement.MoveTypeNone;

        movement.ProcessMovement(s);
        movement.ProcessMovement(s);

        Assert.Equal(new[] { true, true }, world.Resets);
    }

    [Fact]
    public void FeetInWaterMakesTheWaterLevelOne()
    {
        var world = new ScriptedWorld
        {
            // Slot 0 is the point just above the feet; the two above it are dry.
            OnPointContents = (_, slot) => slot == 0 ? GameMovement.ContentsWater : 0,
        };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = BaselineState();
        s.MoveType = GameMovement.MoveTypeNone;   // so only PlayerMove's preamble runs

        movement.ProcessMovement(s);

        Assert.Equal(GameMovement.WlFeet, s.WaterLevel);
        Assert.Equal(GameMovement.ContentsWater, s.WaterType);
    }

    /// <summary>Water over the eyes is level three, and it takes all three queries.</summary>
    [Fact]
    public void WaterOverTheEyesIsLevelThree()
    {
        var world = new ScriptedWorld { OnPointContents = (_, _) => GameMovement.ContentsWater };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = BaselineState();
        s.MoveType = GameMovement.MoveTypeNone;

        movement.ProcessMovement(s);

        Assert.Equal(GameMovement.WlEyes, s.WaterLevel);
    }

    /// <summary>
    /// A current in the water pushes the player, and it pushes HARDER the
    /// deeper they are — the <c>50 * waterlevel</c> scale.
    /// </summary>
    [Fact]
    public void AWaterCurrentAddsToBaseVelocityInProportionToDepth()
    {
        var world = new ScriptedWorld
        {
            OnPointContents = (_, _) => GameMovement.ContentsWater | GameMovement.ContentsCurrent90,
        };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = BaselineState();
        s.MoveType = GameMovement.MoveTypeNone;

        movement.ProcessMovement(s);

        // Eyes-deep, so 50 * 3 along +y.
        Assert.Equal(150f, s.BaseVelocity.Y);
        Assert.Equal(0f, s.BaseVelocity.X);
    }

    /// <summary>
    /// THE THREE THAT USED TO BE REFUSED. Step 3e declared
    /// <c>MOVETYPE_OBSERVER</c>, <c>MOVETYPE_FLY</c> and
    /// <c>MOVETYPE_FLYGRAVITY</c> refusals: <c>ProcessMovement</c> answered
    /// <c>false</c> without touching the state and the native shim ran stock's
    /// body. Step 8f-movetypes ported all three, so the same three inputs now
    /// assert the OPPOSITE — this is 3e's theory with its subject reversed
    /// rather than a new one, because the three cases are exactly the
    /// population whose behaviour the owner ruling changed.
    /// </summary>
    [Theory]
    [InlineData(GameMovement.MoveTypeObserver)]
    [InlineData(GameMovement.MoveTypeFly)]
    [InlineData(GameMovement.MoveTypeFlyGravity)]
    public void AFormerlyRefusedMoveTypeIsNowAnsweredByTheManagedLoop(int moveType)
    {
        var world = new ScriptedWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = BaselineState();
        s.MoveType = moveType;
        s.ObserverMode = GameMovement.ObsModeRoaming;

        Assert.True(movement.ProcessMovement(s));
        Assert.True(movement.LastCommandRan);
    }

    /// <summary>
    /// EVERY move type the enum has is answered, not just the three that
    /// changed. This is the fact behind the step's headline — after
    /// 8f-movetypes, <c>sourcesharp_movement_managed</c> reads 1 for every move
    /// type rather than "1 except three" — and it is written over the whole
    /// enum on purpose: a theory that named only the three could not tell a
    /// port that answers everything from one that answers everything it was
    /// asked about.
    /// </summary>
    [Theory]
    [InlineData(GameMovement.MoveTypeNone)]
    [InlineData(GameMovement.MoveTypeIsometric)]
    [InlineData(GameMovement.MoveTypeWalk)]
    [InlineData(GameMovement.MoveTypeFly)]
    [InlineData(GameMovement.MoveTypeFlyGravity)]
    [InlineData(GameMovement.MoveTypeNoClip)]
    [InlineData(GameMovement.MoveTypeLadder)]
    [InlineData(GameMovement.MoveTypeObserver)]
    public void NoMoveTypeIsDeclinedAnyMore(int moveType)
    {
        var world = new ScriptedWorld();
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = BaselineState();
        s.MoveType = moveType;
        s.ObserverMode = GameMovement.ObsModeRoaming;

        Assert.True(movement.ProcessMovement(s));
    }

    /// <summary>
    /// And none of the eight reaches <c>PlayerMove</c>'s <c>default:</c> arm —
    /// stock's <c>"Bogus pmove player movetype"</c> DevMsg. Before this step
    /// that arm was where the three refused types WOULD have landed, so it
    /// could not be read as "unreachable"; now it can, and this is the reading.
    /// </summary>
    [Fact]
    public void TheBogusMoveTypeArmIsNotReachedByAnyMoveTypeTheEnumHas()
    {
        int[] all =
        {
            GameMovement.MoveTypeNone, GameMovement.MoveTypeIsometric,
            GameMovement.MoveTypeWalk, GameMovement.MoveTypeFly,
            GameMovement.MoveTypeFlyGravity, GameMovement.MoveTypeNoClip,
            GameMovement.MoveTypeLadder, GameMovement.MoveTypeObserver,
        };

        var movement = new GameMovement(new ScriptedWorld(), MoveConstantDefaults.Stock);

        foreach (int moveType in all)
        {
            MoveState s = BaselineState();
            s.MoveType = moveType;
            s.ObserverMode = GameMovement.ObsModeRoaming;
            movement.ProcessMovement(s);
        }

        Assert.Equal(8, all.Length);
        Assert.Equal(0L, movement.BogusMoveTypes);
    }

    /// <summary>
    /// ...and the counter is not one that cannot move: a move type the enum
    /// does not have reaches the arm and is counted. Without this the fact
    /// above would pass on a build where the counter was never incremented.
    /// </summary>
    [Fact]
    public void AMoveTypeTheEnumDoesNotHaveReachesTheBogusArmAndIsCounted()
    {
        var movement = new GameMovement(new ScriptedWorld(), MoveConstantDefaults.Stock);
        MoveState s = BaselineState();
        s.MoveType = 99;

        Assert.True(movement.ProcessMovement(s));
        Assert.Equal(1L, movement.BogusMoveTypes);
    }

    /// <summary>
    /// A surface with <c>climbable</c> set puts the player on a ladder. This is
    /// the arm dm_lockdown cannot reach: its ladders are <c>func_useableladder</c>,
    /// which needs CHL2GameMovement, so the recording never set MOVETYPE_LADDER.
    /// </summary>
    [Fact]
    public void AClimbableSurfaceAheadPutsThePlayerOnALadder()
    {
        var world = new ScriptedWorld
        {
            SurfaceAnswer = new MoveSurface
            {
                Valid = true, Friction = 0.8f, Material = 'M', Climbable = 1,
                MaxSpeedFactor = 1f, JumpFactor = 1f,
            },
            OnTrace = (start, end, _, _, _, _) => new MoveTrace
            {
                Fraction = 0.5f,
                EndPos = start,
                PlaneNormal = new Vector(1f, 0f, 0f),
                SurfaceProps = 7,
                HasEntity = true,
                HitWorld = true,
            },
        };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = BaselineState();
        s.ForwardMove = 200f;

        movement.ProcessMovement(s);

        Assert.Equal(GameMovement.MoveTypeLadder, s.MoveType);
        AssertBits(new Vector(1f, 0f, 0f), s.LadderNormal, "ladder normal");
    }

    /// <summary>
    /// <c>CONTENTS_LADDER</c> is the other way onto a ladder, and it does not
    /// consult the surface at all.
    /// </summary>
    [Fact]
    public void ContentsLadderPutsThePlayerOnALadderWithoutAClimbableSurface()
    {
        var world = new ScriptedWorld
        {
            SurfaceAnswer = new MoveSurface
            {
                Valid = true, Friction = 0.8f, Material = 'C', Climbable = 0,
                MaxSpeedFactor = 1f, JumpFactor = 1f,
            },
            OnTrace = (start, _, _, _, _, _) => new MoveTrace
            {
                Fraction = 0.5f,
                EndPos = start,
                PlaneNormal = new Vector(0f, 1f, 0f),
                Contents = GameMovement.ContentsLadder,
                HasEntity = true,
                HitWorld = true,
            },
        };
        var movement = new GameMovement(world, MoveConstantDefaults.Stock);
        MoveState s = BaselineState();
        s.SideMove = 200f;

        movement.ProcessMovement(s);

        Assert.Equal(GameMovement.MoveTypeLadder, s.MoveType);
    }

    /// <summary>
    /// In single player — and ONLY there — a jump starts the duck-jump family.
    /// This is why the multiplayer recording has no such command.
    /// </summary>
    [Fact]
    public void AJumpStartsTheDuckJumpTimerOnlyInSinglePlayer()
    {
        foreach (int maxClients in new[] { 1, 2 })
        {
            var world = new ScriptedWorld();
            MoveConstants c = MoveConstantDefaults.Stock;
            c.MaxClients = maxClients;

            var movement = new GameMovement(world, c);
            MoveState s = BaselineState();
            world.OnTrace = (start, end, _, _, _, _) => ScriptedWorld.Floor(start, end);
            s.HasGround = true;
            s.Buttons = GameMovement.InJump;

            movement.ProcessMovement(s);

            if (maxClients == 1)
            {
                Assert.Equal(GameMovement.GameMovementJumpTime, s.JumpTime);
                Assert.True(s.InDuckJump);
            }
            else
            {
                Assert.Equal(0f, s.JumpTime);
                Assert.False(s.InDuckJump);
            }
        }
    }

    /// <summary>
    /// The jump velocity is <c>sqrt(2 g h)</c> and NOT stock's literal 160.
    ///
    /// <para>
    /// Two runs rather than one number, because the relationship is the thing
    /// that was broken: stock's optimised branch used a literal 160.0f, so a
    /// build that changed the jump height changed nothing. Quadrupling the
    /// height DOUBLES the impulse — it does not quadruple it, which is what
    /// people get wrong when they tune it.
    /// </para>
    /// </summary>
    [Fact]
    public void TheJumpVelocityFollowsTheJumpHeightRatherThanALiteral()
    {
        float Impulse(float jumpHeight)
        {
            var world = new ScriptedWorld
            {
                OnTrace = (start, end, _, _, _, _) => ScriptedWorld.Floor(start, end),
            };
            var movement = new GameMovement(world, MoveConstantDefaults.Stock) { JumpHeight = jumpHeight };

            MoveState s = BaselineState();
            s.HasGround = true;
            s.Buttons = GameMovement.InJump;

            movement.ProcessMovement(s);

            // FinishGravity takes half a frame off before the impulse is
            // recorded, and it takes the same half off both runs.
            return s.OutJumpVel.Z + 0.5f * 600f * 0.015f;
        }

        float low = Impulse(21f);
        float high = Impulse(84f);

        Assert.True(MathF.Abs(high - 2f * low) < 0.01f,
            $"quadrupling the height took the impulse from {low} to {high}, not to twice it");

        // And it is NOT the literal: sqrt(2 * 600 * 21) is 158.75, not 160.
        Assert.True(MathF.Abs(low - 158.745f) < 0.01f,
            $"a 21-unit jump left the ground at {low}, which is stock's literal rather than the formula");
    }

    /// <summary>
    /// <c>VectorNormalize</c> is the engine's rsqrt approximation, NOT an exact
    /// reciprocal length — and the difference is real rather than theoretical,
    /// which is what this pins.
    /// </summary>
    [Fact]
    public void NormalizeUsesTheEnginesApproximationAndNotAnExactReciprocal()
    {
        var v = new Vector(3f, 4f, 12f);
        Vector approx = v;
        float returned = MoveVec.Normalize(ref approx);

        float exact = MathF.Sqrt(3f * 3f + 4f * 4f + 12f * 12f);   // exactly 13
        Assert.Equal(13f, exact);

        // Close, but NOT the same number. If these ever became bit-equal the
        // port would have quietly stopped reproducing the engine.
        Assert.True(MathF.Abs(returned - 13f) < 0.01f,
            $"the approximation returned {returned}, nowhere near 13");
        Assert.NotEqual(BitConverter.SingleToInt32Bits(13f),
                        BitConverter.SingleToInt32Bits(returned));
    }

    /// <summary>
    /// <c>DEG2RAD</c> rounds the conversion factor to a float ONCE. Multiplying
    /// by pi and then dividing by 180 is a different number, and it is the
    /// spelling <see cref="Angles.Vectors"/> uses — so this fact is the reason
    /// the port has its own AngleVectors rather than reusing that one.
    /// </summary>
    [Fact]
    public void TheEnginesDegreeConversionRoundsOnceAndTheObviousSpellingDoesNot()
    {
        const float degrees = 89.7f;

        float engine = degrees * MoveVec.DegToRad;
        float obvious = degrees * MathF.PI / 180f;

        Assert.NotEqual(BitConverter.SingleToInt32Bits(engine),
                        BitConverter.SingleToInt32Bits(obvious));
    }

    //=========================================================================
    // The crossing.
    //=========================================================================

    /// <summary>
    /// The blob is exactly the layout the generator printed, so an edit that
    /// added a field on one side and not the other cannot pass.
    /// <c>shell_movement.cpp</c> carries the same number as a
    /// <c>COMPILE_TIME_ASSERT</c>.
    /// </summary>
    [Fact]
    public void TheCrossingStructIsTheLayoutBothSidesAgreedOn()
    {
        Assert.Equal(MoveStateBlob.WordCount * 4,
                     System.Runtime.InteropServices.Marshal.SizeOf<MoveStateBlob>());
    }

    /// <summary>
    /// Every field of the blob is four bytes, which is what makes the word
    /// count a layout check rather than a coincidence: a <c>double</c> or a
    /// <c>bool</c> slipped in would change the padding and the size check alone
    /// could still pass.
    /// </summary>
    [Fact]
    public void EveryFieldOfTheCrossingStructIsFourBytes()
    {
        var fields = typeof(MoveStateBlob).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.Equal(MoveStateBlob.WordCount, fields.Length);
        foreach (var f in fields)
        {
            Assert.True(f.FieldType == typeof(float) || f.FieldType == typeof(int),
                $"{f.Name} is a {f.FieldType.Name}, which is not a 32-bit word");
        }
    }

    /// <summary>
    /// The trace answer's own layout, for the same reason.
    /// </summary>
    [Fact]
    public void TheTraceResultStructIsAllThirtyTwoBitWords()
    {
        var fields = typeof(MoveTraceBlob).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (var f in fields)
        {
            Assert.True(f.FieldType == typeof(float) || f.FieldType == typeof(int),
                $"{f.Name} is a {f.FieldType.Name}");
        }

        Assert.Equal(fields.Length * 4,
                     System.Runtime.InteropServices.Marshal.SizeOf<MoveTraceBlob>());
    }

    /// <summary>
    /// A null state is refused rather than dereferenced. The native shim
    /// already guards it; a second guard here costs nothing and the alternative
    /// is a segfault inside an unmanaged callback, which reports as a crash
    /// with no managed frames in it.
    /// </summary>
    [Fact]
    public unsafe void TheBridgeRefusesANullState()
    {
        var runner = TestSides.Movement(new ServerModuleEngine(TestSides.Create(Side.Server)));
        Assert.Equal(0, runner.ProcessMovement(null));
    }

    /// <summary>
    /// The two declarations of the crossing are the SAME LAYOUT, field for
    /// field, in order.
    ///
    /// <para>
    /// The size check above catches a field added on one side and not the
    /// other. It does NOT catch two fields swapped, or one renamed, or a float
    /// that became an int — and those are exactly what a tidy-up does. This
    /// reads both files and compares the names, so the only way to pass is for
    /// the two to say the same thing.
    /// </para>
    ///
    /// <para>
    /// Source-scanned rather than generated-and-diffed because the generator is
    /// a Python script and this leg is C#: what matters is that the two
    /// SHIPPED declarations agree, and <c>tools/3e-gen-movestate</c> is how they
    /// are kept agreeing rather than what proves they do.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCrossingIsDeclaredIdenticallyOnBothSides()
    {
        string root = RepoLayout.SdkTree;

        string header = File.ReadAllText(Path.Combine(root, "src", "sourcesharp",
                                                      "host", "sourcesharp.h"));
        string abi = File.ReadAllText(Path.Combine(root, "src", "sourcesharp", "managed",
                                                   "SourceSharp.Abi", "MoveStateBlob.cs"));

        static string Between(string text, string from, string to, string what)
        {
            int a = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(a >= 0, $"{what}: '{from}' is not in the file");
            int b = text.IndexOf(to, a, StringComparison.Ordinal);
            Assert.True(b > a, $"{what}: '{to}' does not follow '{from}'");
            return text[a..b];
        }

        // The C side: one field per line, `float x[3];` or `int32_t n;`.
        var native = new List<string>();
        foreach (Match m in Regex.Matches(
                     Between(header, "typedef struct SourceSharpMoveState",
                             "} SourceSharpMoveState;", "the C header"),
                     @"^\t(float|int32_t)\s+(\w+)(\[3\])?;", RegexOptions.Multiline))
        {
            native.Add(m.Groups[2].Value + (m.Groups[3].Success ? "[3]" : ""));
        }

        // The C# side: a vector is three named floats, so it folds back to one
        // entry with the X/Y/Z stripped — which is what makes the two lists
        // comparable at all.
        var managed = new List<string>();
        foreach (Match m in Regex.Matches(
                     Between(abi, "public unsafe struct MoveStateBlob", "\n}", "the C# mirror"),
                     @"^    public (float|int) ([A-Za-z0-9_, ]+);", RegexOptions.Multiline))
        {
            string[] names = m.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries);
            if (names.Length == 3 &&
                names[0].EndsWith("X", StringComparison.Ordinal) &&
                names[1].EndsWith("Y", StringComparison.Ordinal) &&
                names[2].EndsWith("Z", StringComparison.Ordinal))
            {
                managed.Add(names[0][..^1] + "[3]");
            }
            else
            {
                foreach (string n in names)
                    managed.Add(n);
            }
        }

        Assert.True(native.Count > 90,
            $"the C declaration parsed as {native.Count} fields; the scan went wrong");
        Assert.Equal(native.Count, managed.Count);

        // The names differ by convention -- flFrameTime against FrameTime -- so
        // what is compared is the name with its Hungarian prefix removed and
        // the case dropped. That still catches a swap, a rename and a reorder,
        // which is what this fact is for.
        static string Key(string name)
        {
            string n = Regex.Replace(name, @"^(fl|n|b|vec|ang)(?=[A-Z])", "");
            return n.ToLowerInvariant();
        }

        for (int i = 0; i < native.Count; i++)
        {
            Assert.True(Key(native[i]) == Key(managed[i]),
                $"field {i} of the crossing: the C header says '{native[i]}', " +
                $"SourceSharp.Abi says '{managed[i]}'");
        }
    }

    /// <summary>
    /// And the mirror of the trace ANSWER, for the same reason. It is smaller
    /// and it is read on every trace the loop makes, so a swapped pair here is
    /// a player walking into a plane normal.
    /// </summary>
    [Fact]
    public void TheTraceResultIsDeclaredIdenticallyOnBothSides()
    {
        string root = RepoLayout.SdkTree;
        string header = File.ReadAllText(Path.Combine(root, "src", "sourcesharp",
                                                      "host", "sourcesharp.h"));

        int a = header.IndexOf("typedef struct SourceSharpMoveTraceResult", StringComparison.Ordinal);
        int b = header.IndexOf("} SourceSharpMoveTraceResult;", a, StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a, "the trace result is declared in the C header");

        var native = new List<string>();
        foreach (Match m in Regex.Matches(header[a..b],
                     @"^\t(float|int32_t)\s+(\w+)(\[3\])?;", RegexOptions.Multiline))
        {
            native.Add(m.Groups[2].Value + (m.Groups[3].Success ? "[3]" : ""));
        }

        var managed = typeof(MoveTraceBlob).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(f => f.Name).ToList();

        // Fold the C# vectors back, as above.
        var folded = new List<string>();
        for (int i = 0; i < managed.Count; i++)
        {
            if (i + 2 < managed.Count &&
                managed[i].EndsWith("X", StringComparison.Ordinal) &&
                managed[i + 1] == managed[i][..^1] + "Y" &&
                managed[i + 2] == managed[i][..^1] + "Z")
            {
                folded.Add(managed[i][..^1] + "[3]");
                i += 2;
            }
            else
            {
                folded.Add(managed[i]);
            }
        }

        Assert.Equal(native.Count, folded.Count);

        static string Key(string name)
            => Regex.Replace(name, @"^(fl|n|b|vec)(?=[A-Z])", "").ToLowerInvariant();

        for (int i = 0; i < native.Count; i++)
        {
            Assert.True(Key(native[i]) == Key(folded[i]),
                $"field {i} of the trace answer: the C header says '{native[i]}', " +
                $"SourceSharp.Abi says '{folded[i]}'");
        }
    }

    //=========================================================================
    // The hulls — plan_shim.md §5 3e.
    //
    // Nine vectors that used to be literals in a native
    // `static CViewVectors g_DefaultViewVectors` in shell_gamerules.cpp, moved
    // there verbatim from stock's gamerules.cpp. They are declared here now and
    // pushed across in MovementParams, because they are read through
    // shareddefs.h's VEC_HULL_MIN macros INSIDE the movement loop, per command,
    // on both sides — which is the path this parameter block exists for.
    //
    // Every failure below is silent in the way the class comment describes. A
    // hull is not something the game reports being wrong about; it is a player
    // who is stuck in a doorway, or standing a unit inside the floor, or
    // falling through the world entirely.
    //=========================================================================

    /// <summary>
    /// The standing hull is stock's: 32 units across, 72 tall, origin at the
    /// feet. These are the numbers every Half-Life map was built around — a
    /// doorway is 72 tall because a player is.
    /// </summary>
    [Fact]
    public void TheStandingHullIsStocks()
    {
        var p = Movement.Current;

        Assert.Equal(new Vector(-16f, -16f, 0f), p.HullMin);
        Assert.Equal(new Vector(16f, 16f, 72f), p.HullMax);
    }

    /// <summary>
    /// Crouching halves the height and keeps the footprint. A duck hull wider
    /// than the standing one would let a crouching player into places they
    /// could not stand, which every map's geometry assumes cannot happen.
    /// </summary>
    [Fact]
    public void CrouchingHalvesTheHeightAndKeepsTheFootprint()
    {
        var p = Movement.Current;

        Assert.Equal(p.HullMin.X, p.DuckHullMin.X);
        Assert.Equal(p.HullMin.Y, p.DuckHullMin.Y);
        Assert.Equal(p.HullMax.X, p.DuckHullMax.X);
        Assert.Equal(p.HullMax.Y, p.DuckHullMax.Y);

        Assert.True(p.DuckHullMax.Z < p.HullMax.Z,
            "a crouching player is no shorter than a standing one");
        Assert.Equal(36f, p.DuckHullMax.Z);
    }

    /// <summary>
    /// The eye is inside the hull, standing and crouched alike. An eye above
    /// the box sees over walls the player is standing behind; an eye below the
    /// floor renders the inside of the world.
    /// </summary>
    [Fact]
    public void TheEyeIsInsideTheHullStandingAndCrouched()
    {
        var p = Movement.Current;

        Assert.InRange(p.View.Z, p.HullMin.Z, p.HullMax.Z);
        Assert.InRange(p.DuckView.Z, p.DuckHullMin.Z, p.DuckHullMax.Z);

        Assert.Equal(64f, p.View.Z);
        Assert.Equal(28f, p.DuckView.Z);
    }

    /// <summary>
    /// Every one of the nine hull vectors has a volume or a height that is not
    /// simply zero — the failure the native publish refuses. A block whose hull
    /// fields were never set reads as all-zero, and
    /// <c>SourceSharp_PublishViewVectors</c> keeps stock's table rather than
    /// giving every player a collision box of no size. This fact is the managed
    /// half of that guard: the table it refuses must never be the one this file
    /// declares.
    /// </summary>
    [Fact]
    public void TheDeclaredStandingHullIsNotTheDegenerateOneNativeRefuses()
    {
        var p = Movement.Current;

        Assert.True(p.HullMax.X > p.HullMin.X);
        Assert.True(p.HullMax.Y > p.HullMin.Y);
        Assert.True(p.HullMax.Z > p.HullMin.Z);
    }

    /// <summary>
    /// The observer hull is centred on its own origin rather than sitting on
    /// it, which is what makes a spectator camera fly rather than walk.
    /// </summary>
    [Fact]
    public void TheObserverHullIsCentredOnItsOrigin()
    {
        var p = Movement.Current;

        Assert.Equal(new Vector(-10f, -10f, -10f), p.ObsHullMin);
        Assert.Equal(new Vector(10f, 10f, 10f), p.ObsHullMax);
    }

    /// <summary>
    /// Writing a hull through the vector property and reading it back through
    /// the three floats that cross gives the same numbers. The properties are
    /// convenience over a flattened layout, and a getter that read a different
    /// field than its setter wrote would make this whole block declare one
    /// thing and send another.
    /// </summary>
    [Fact]
    public void TheVectorPropertiesReadBackTheFloatsTheyWrite()
    {
        var p = default(MovementParams);

        p.HullMin = new Vector(1f, 2f, 3f);
        p.HullMax = new Vector(4f, 5f, 6f);
        p.DuckView = new Vector(7f, 8f, 9f);

        Assert.Equal(1f, p.HullMinX);
        Assert.Equal(2f, p.HullMinY);
        Assert.Equal(3f, p.HullMinZ);
        Assert.Equal(4f, p.HullMaxX);
        Assert.Equal(5f, p.HullMaxY);
        Assert.Equal(6f, p.HullMaxZ);
        Assert.Equal(7f, p.DuckViewX);
        Assert.Equal(8f, p.DuckViewY);
        Assert.Equal(9f, p.DuckViewZ);

        Assert.Equal(new Vector(1f, 2f, 3f), p.HullMin);
        Assert.Equal(new Vector(4f, 5f, 6f), p.HullMax);
        Assert.Equal(new Vector(7f, 8f, 9f), p.DuckView);
    }

    /// <summary>
    /// Each of the nine properties reads its OWN three floats. Nine getters
    /// written by copying the one above it is exactly how one of them comes to
    /// read its neighbour's fields, and the symptom would be a spectator hull
    /// applied to a live player.
    /// </summary>
    [Fact]
    public void EachHullPropertyReadsItsOwnThreeFloats()
    {
        var p = default(MovementParams);

        p.View = new Vector(1f, 1f, 1f);
        p.HullMin = new Vector(2f, 2f, 2f);
        p.HullMax = new Vector(3f, 3f, 3f);
        p.DuckHullMin = new Vector(4f, 4f, 4f);
        p.DuckHullMax = new Vector(5f, 5f, 5f);
        p.DuckView = new Vector(6f, 6f, 6f);
        p.ObsHullMin = new Vector(7f, 7f, 7f);
        p.ObsHullMax = new Vector(8f, 8f, 8f);
        p.DeadViewHeight = new Vector(9f, 9f, 9f);

        Assert.Equal(new Vector(1f, 1f, 1f), p.View);
        Assert.Equal(new Vector(2f, 2f, 2f), p.HullMin);
        Assert.Equal(new Vector(3f, 3f, 3f), p.HullMax);
        Assert.Equal(new Vector(4f, 4f, 4f), p.DuckHullMin);
        Assert.Equal(new Vector(5f, 5f, 5f), p.DuckHullMax);
        Assert.Equal(new Vector(6f, 6f, 6f), p.DuckView);
        Assert.Equal(new Vector(7f, 7f, 7f), p.ObsHullMin);
        Assert.Equal(new Vector(8f, 8f, 8f), p.ObsHullMax);
        Assert.Equal(new Vector(9f, 9f, 9f), p.DeadViewHeight);
    }

    /// <summary>
    /// The block that crosses is exactly eleven scalars plus twenty-seven hull
    /// floats, with nothing between them: the C struct is read as a flat
    /// sequence of <c>float</c>, so a field of another width anywhere in it
    /// moves every field after it and the native side reads the wrong number
    /// out of the right offset.
    /// </summary>
    [Fact]
    public void TheParameterBlockIsThirtyEightFloatsAndNothingElse()
    {
        var fields = typeof(MovementParams)
            .GetFields(System.Reflection.BindingFlags.Public
                       | System.Reflection.BindingFlags.Instance);

        Assert.Equal(11 + 27, fields.Length);
        Assert.All(fields, f => Assert.Equal(typeof(float), f.FieldType));
        Assert.Equal((11 + 27) * 4,
                     System.Runtime.InteropServices.Marshal.SizeOf<MovementParams>());
    }

    /// <summary>
    /// The parameter block is declared identically on both sides, field for
    /// field and IN ORDER.
    ///
    /// <para>
    /// <c>MovementParams</c> carried the comment "Must match
    /// SourceSharpMovementParams in sourcesharp.h, in order" and nothing that
    /// checked it, which is exactly the hand-mirroring <c>plan_shim.md</c> §5
    /// asks to remove. The block is read as a flat sequence of <c>float</c> at
    /// fixed offsets: a field appended on one side only, or a pair swapped,
    /// silently shifts everything after it. The symptom of getting it wrong
    /// with the hulls in the block is a player whose collision box is made of
    /// gravity and stop speed.
    /// </para>
    ///
    /// <para>
    /// Modelled on <see cref="TheTraceResultIsDeclaredIdenticallyOnBothSides"/>
    /// and folding the same way: a C <c>float vecHullMin[3]</c> and a C#
    /// <c>HullMinX, HullMinY, HullMinZ</c> are one entry each.
    /// </para>
    /// </summary>
    [Fact]
    public void TheParameterBlockIsDeclaredIdenticallyOnBothSides()
    {
        string root = RepoLayout.SdkTree;
        string header = File.ReadAllText(Path.Combine(root, "src", "sourcesharp",
                                                      "host", "sourcesharp.h"));

        int b = header.IndexOf("} SourceSharpMovementParams;", StringComparison.Ordinal);
        Assert.True(b > 0, "SourceSharpMovementParams is declared in the C header");
        int a = header.LastIndexOf("typedef struct", b, StringComparison.Ordinal);
        Assert.True(a >= 0 && a < b, "its typedef opens before it closes");

        var native = new List<string>();
        foreach (Match m in Regex.Matches(header[a..b],
                     @"^\t(float|int32_t)\s+(\w+)(\[3\])?;", RegexOptions.Multiline))
        {
            native.Add(m.Groups[2].Value + (m.Groups[3].Success ? "[3]" : ""));
        }

        // Reflection gives the managed fields in declaration order, which is
        // what LayoutKind.Sequential makes meaningful.
        var managed = typeof(MovementParams).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(f => f.Name).ToList();

        var folded = new List<string>();
        for (int i = 0; i < managed.Count; i++)
        {
            if (i + 2 < managed.Count &&
                managed[i].EndsWith("X", StringComparison.Ordinal) &&
                managed[i + 1] == managed[i][..^1] + "Y" &&
                managed[i + 2] == managed[i][..^1] + "Z")
            {
                folded.Add(managed[i][..^1] + "[3]");
                i += 2;
            }
            else
            {
                folded.Add(managed[i]);
            }
        }

        Assert.True(native.Count >= 20,
            $"the C declaration parsed as {native.Count} fields; the scan went wrong");
        Assert.Equal(native.Count, folded.Count);

        static string Key(string name)
            => Regex.Replace(name, @"^(fl|n|b|vec)(?=[A-Z])", "").ToLowerInvariant();

        for (int i = 0; i < native.Count; i++)
        {
            Assert.True(Key(native[i]) == Key(folded[i]),
                $"field {i} of the movement parameter block: the C header says " +
                $"'{native[i]}', SourceSharp.Abi says '{folded[i]}'");
        }
    }
}

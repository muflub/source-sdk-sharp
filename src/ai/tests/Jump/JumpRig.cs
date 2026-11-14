using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Tests;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The blast-jump simulator as a fact builds it (plan_bots_teams_tf2_part_g G.5 step 3): the
/// mod's class table over its own Soldier script, every convar the mod's movement rules read at
/// its declared default, and engine seams that refuse every question.
/// </summary>
internal static class JumpRig
{
    /// <summary>The stock rocket launcher's script numbers (tf_weapon_rocketlauncher: Damage 90, TimeFireDelay 0.8).</summary>
    internal const float RocketDamage = 90f, RocketRefire = 0.8f;

    /// <summary>The server's tick.</summary>
    internal const float TickSeconds = BlastJumpSims.TickSeconds;

    internal const int InForward = 1 << 3, InJump = GameMovement.InJump, InDuck = GameMovement.InDuck, InAttack = GameMovement.InAttack;

    /// <summary>The move a pressed forward key sends: TF2's cl_forwardspeed.</summary>
    internal const float ForwardSpeed = 450f;

    /// <summary>The game's files: the Soldier's class script, as the mod's tests hold it, and nothing else.</summary>
    private sealed class ClassScripts : IEngineFiles
    {
        public int GameFileSize(string path) => ReadGameFileBytes(path)?.Length ?? -1;

        public string? ReadGameFile(string path) => null;

        public byte[]? ReadGameFileBytes(string path)
            => path == TFClassTable.ScriptName(TFClass.Soldier) + ".txt"
                ? File.ReadAllBytes(RepoLayout.Repo("src", "tf2", "tests", "TestData", "playerclasses", "soldier.txt"))
                : null;

        public byte[]? ReadFileOnPath(string path, string pathId) => pathId == "GAME" ? ReadGameFileBytes(path) : null;
    }

    private sealed class NoLog : ILog
    {
        public void Msg(string message)
        {
        }

        public void Warning(string message)
        {
        }
    }

    /// <summary>An engine seam that answers nothing: any question a simulation asks it is the fact's failure.</summary>
    public class Refusing : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => throw new InvalidOperationException($"the simulation asked the engine: {method?.DeclaringType?.Name}.{method?.Name}");
    }

    internal static TFClassTable Classes() => new(new NoLog(), new ClassScripts());

    internal static ConVarRegistry ConVars() => new(new NamedConVars(new Dictionary<string, string>()));

    internal static IEnginePlayers NoPlayers() => DispatchProxy.Create<IEnginePlayers, Refusing>();

    internal static IEngineEntities NoEntities() => DispatchProxy.Create<IEngineEntities, Refusing>();

    /// <summary>The factory as the game's container would build it, over <paramref name="convars"/>.</summary>
    internal static BlastJumpSims Sims(ConVarRegistry? convars = null)
    {
        ConVarRegistry v = convars ?? ConVars();
        return new BlastJumpSims(Classes(), new AttributeHooks(), NoPlayers(), NoEntities(),
                                 v.Declare<TFScoutAirDashCountConVar>(), v.Declare<TFSpyMaxCloakedSpeedConVar>(),
                                 v.Declare<TFClampBackSpeedConVar>(), v.Declare<TFClampBackSpeedMinConVar>(),
                                 v.Declare<TFPlayerMovementRestartFreezeConVar>(), v.Declare<TFMovementAircurrentFrictionMultConVar>(),
                                 v.Declare<TFMovementLostFootingRestickConVar>(), v.Declare<TFMovementLostFootingFrictionConVar>(),
                                 v.Declare<TFMaxChargeSpeedConVar>(), v.Declare<TFParachuteMaxSpeedXYConVar>(),
                                 v.Declare<TFParachuteMaxSpeedZConVar>(), v.Declare<TFParachuteMaxSpeedOnfireZConVar>(),
                                 v.Declare<TFClampAirducksConVar>(), v.Declare<TFParachuteAirControlConVar>(),
                                 v.Declare<TFDemomanChargeFrametimeScalingConVar>());
    }

    /// <summary>What a walked run did, worked out from its track: the first time off the ground and the first back on it.</summary>
    internal sealed record Walked(JumpPoint[] Track, int Takeoff, int Landing, Vector From, Vector To, float Apex, float AirSeconds)
    {
        public bool Landed => Landing >= 0;
    }

    /// <summary>Runs <paramref name="parts"/> end to end through the movement alone, from <paramref name="start"/>.</summary>
    internal static Walked Walk(VoxelColumns columns, JumpStart start, params JumpCommand[][] parts)
    {
        SoldierMover mover = Sims().Mover(columns);
        mover.Reset(start);
        var track = new List<JumpPoint>();
        foreach (JumpCommand command in parts.SelectMany(p => p))
        {
            mover.Step(command);
            track.Add(mover.Point);
        }

        int takeoff = track.FindIndex(p => !p.OnGround);
        int landing = takeoff < 0 ? -1 : track.FindIndex(takeoff, p => p.OnGround);
        Vector from = takeoff <= 0 ? start.Origin : track[takeoff - 1].Origin;
        int last = landing >= 0 ? landing : track.Count - 1;
        float top = takeoff < 0 ? from.Z : track.Skip(takeoff).Take(last - takeoff + 1).Max(p => p.Origin.Z);
        return new Walked([.. track], takeoff, landing, from, track[last].Origin, top - from.Z, takeoff < 0 ? 0f : (last - takeoff) * TickSeconds);
    }

    /// <summary>A simulator over <paramref name="columns"/> with the stock rocket launcher.</summary>
    internal static BlastJumpSim Sim(VoxelColumns columns) => Sims().Create(columns, JumpLauncher.Rocket(RocketDamage, RocketRefire));

    /// <summary>The Soldier's run speed, from the mod's class table.</summary>
    internal static float RunSpeed => Classes()[TFClass.Soldier].MaxSpeed;

    /// <summary>The pilot's numbers for the Soldier: the mod's air, his run speed, the server's tick, and a view that turns <paramref name="turn"/> degrees a second.</summary>
    internal static JumpPilotLimits Limits(float turn = 360f) => JumpPilotLimits.Of(TFViewVectors.Params, RunSpeed, TickSeconds, turn);

    /// <summary>A pilot in the loop over a simulator of <paramref name="columns"/>.</summary>
    internal static JumpFlier Flier(VoxelColumns columns, float turn = 360f) => new(Sim(columns), Limits(turn));

    /// <summary>An open field 4096 by 2048 and 4096 tall, its floor at 0: room for any flight.</summary>
    internal static VoxelColumns Field() => VoxelColumns.Of(0f, 0f, 16f, 256, 128, static (_, _) => [new FreeSpan(0f, 4096f)]);

    /// <summary>A place on the field's floor, west of its middle.</summary>
    internal static Vector OnTheField(float x = 800f, float y = 1024f) => new(x, y, VoxelMoveWorld.DistEpsilon);

    /// <summary>What a piloted flight sent and where each command left the body, with its outcome.</summary>
    internal sealed record Piloted(JumpOutcome Outcome, List<JumpCommand> Sent, List<JumpPoint> Track)
    {
        /// <summary>The command that jumps, and the first that holds the trigger.</summary>
        public int Jump => Sent.FindIndex(c => (c.Buttons & InJump) != 0);

        public int Trigger => Sent.FindIndex(c => (c.Buttons & InAttack) != 0);

        /// <summary>The command after which the body was back on the ground: the first after the jump.</summary>
        public int Touch => Track.FindIndex(Jump + 1, p => p.OnGround);
    }

    /// <summary>Flies <paramref name="recipe"/> from its own start with the pilot in the loop, keeping the commands and the track.</summary>
    internal static Piloted Pilot(JumpFlier flier, JumpRecipe recipe, JumpStart? start = null, int maxCommands = 700)
    {
        var sent = new List<JumpCommand>();
        var track = new List<JumpPoint>();
        return new Piloted(flier.Fly(recipe, start ?? recipe.Start(), maxCommands, track, sent), sent, track);
    }

    /// <summary>
    /// The same flight unsteered: the commands the pilot sent, with no move after the jump's, flown through the
    /// same simulator. Its approach, its jump and its shot are the piloted flight's.
    /// </summary>
    internal static JumpFlight Unsteered(JumpFlier flier, JumpRecipe recipe, Piloted piloted)
    {
        int jump = piloted.Jump;
        JumpCommand[] plain = [.. piloted.Sent.Select((c, i) => i > jump ? c with { ForwardMove = 0f, SideMove = 0f } : c)];
        return flier.Sim.Fly(recipe.Start(), plain);
    }

    /// <summary>The jump's reach in open air, as the simulator gives it today: what a fact sizes its ledges and gaps by.</summary>
    internal static JumpEnvelope Envelope(JumpSearchSettings? settings = null)
        => JumpEnvelope.Measure(Flier(JumpEnvelope.OpenAir()), settings ?? new JumpSearchSettings());

    /// <summary>
    /// The field with everything from x = <see cref="LedgeEdge"/> east raised to <paramref name="height"/>: a ledge
    /// across the way, its top and the floor before it open to the sky.
    /// </summary>
    internal static VoxelColumns LedgeField(float height)
        => VoxelColumns.Of(0f, 0f, 16f, 256, 128, (x, _) => [new FreeSpan(x * 16f >= LedgeEdge ? height : 0f, 8192f)]);

    internal const float LedgeEdge = 1600f;

    /// <summary>A jump onto <see cref="LedgeField"/>'s ledge: from <paramref name="standoff"/> before its edge to <paramref name="inset"/> past it.</summary>
    internal static JumpAsk OntoTheLedge(float height, float standoff = 160f, float inset = 64f, float tolerance = 24f, JumpFamilies families = JumpFamilies.Both, uint seed = 1)
        => new(new Vector(LedgeEdge - standoff, 1024f, VoxelMoveWorld.DistEpsilon), new Vector(LedgeEdge + inset, 1024f, height + VoxelMoveWorld.DistEpsilon), tolerance, families, seed);

    /// <summary>A search over <paramref name="columns"/> that knows nothing of walking.</summary>
    internal static JumpSearch Search(VoxelColumns columns, JumpSearchSettings? settings = null, IJumpWalk? walk = null)
        => new(Flier(columns), walk ?? new NoJumpWalk(), settings ?? new JumpSearchSettings());

    /// <summary>
    /// A graph over <paramref name="columns"/> for a fact's hand-built world: a cell a column's span that a
    /// crouched body is no taller than (for a crouched body alone when a standing one is), a walk both ways between
    /// neighbours whose floors are a step apart or less, and a drop from a floor to a lower neighbour within the
    /// safe drop, where the air beside the floor is open.
    /// </summary>
    internal static NavGraph GraphOf(VoxelColumns columns)
    {
        NavNumbers n = TFNavProfile.ModNumbers;
        var b = new NavGraphBuilder();
        var cells = new Dictionary<(int X, int Y, int Span), CellId>();
        float voxel = columns.VoxelSize;
        for (int y = 0; y < columns.Depth; y++)
        {
            for (int x = 0; x < columns.Width; x++)
            {
                ReadOnlySpan<FreeSpan> spans = columns.Spans(x, y);
                for (int i = 0; i < spans.Length; i++)
                {
                    float room = spans[i].Top - spans[i].Bottom;
                    if (room >= n.CrouchHeight)
                    {
                        ClearanceClasses classes = room >= n.StandHeight ? ClearanceClasses.Stand | ClearanceClasses.Crouch : ClearanceClasses.Crouch;
                        cells[(x, y, i)] = b.AddCell(new NavCell(columns.OriginX + x * voxel, columns.OriginY + y * voxel, columns.OriginX + (x + 1) * voxel,
                                                                 columns.OriginY + (y + 1) * voxel, spans[i].Bottom, classes, NavCellFlags.None, 1f));
                    }
                }
            }
        }

        foreach (((int x, int y, int i), CellId cell) in cells.OrderBy(c => c.Value.Index))
        {
            float floor = columns.Spans(x, y)[i].Bottom;
            foreach ((int dx, int dy) in ((int, int)[])[(1, 0), (0, 1), (-1, 0), (0, -1)])
            {
                ReadOnlySpan<FreeSpan> beside = columns.Spans(x + dx, y + dy);
                for (int j = 0; j < beside.Length; j++)
                {
                    if (!cells.TryGetValue((x + dx, y + dy, j), out CellId other))
                        continue;
                    float down = floor - beside[j].Bottom;
                    if (MathF.Abs(down) <= n.StepHeight)
                        b.AddEdge(cell, other, NavEdgeType.Walk);
                    else if (down > 0f && down <= n.SafeDrop && beside[j].Top > floor)
                        b.AddEdge(cell, other, NavEdgeType.Drop, NavEdgeCosts.Default.Drop);
                }
            }
        }

        return b.Build();
    }

    /// <summary>The distance between two places over the ground.</summary>
    internal static float Flat(Vector a, Vector b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// A room 1024 by 512 and 1024 tall on 16-unit columns, its floor at 0; <paramref name="spans"/>
    /// answers a column that is not the open room's.
    /// </summary>
    internal static VoxelColumns Room(Func<int, int, FreeSpan[]?>? spans = null)
        => VoxelColumns.Of(0f, 0f, 16f, 64, 32, (x, y) => spans?.Invoke(x, y) ?? [new FreeSpan(0f, 1024f)]);

    /// <summary>Standing on the room's floor at (<paramref name="x"/>, <paramref name="y"/>), looking east.</summary>
    internal static JumpStart Standing(float x = 200f, float y = 256f, float z = VoxelMoveWorld.DistEpsilon)
        => new(new Vector(x, y, z), Vector.Zero, Vector.Zero, Ducked: false, OnGround: true);

    /// <summary><paramref name="count"/> commands of one view, move and buttons.</summary>
    internal static JumpCommand[] Hold(int count, int buttons = 0, float pitch = 0f, float yaw = 0f, float forward = 0f)
        => [.. Enumerable.Repeat(new JumpCommand(new Vector(pitch, yaw, 0f), forward, 0f, 0f, buttons), count)];
}

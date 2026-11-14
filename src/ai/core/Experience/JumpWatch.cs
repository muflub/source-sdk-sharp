using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>Why a flight a player made was not a jump to learn (<see cref="JumpWatch"/>).</summary>
public enum JumpThrow : byte
{
    /// <summary>Under <see cref="JumpWatch.MinAirSeconds"/> in the air.</summary>
    Short,

    /// <summary>Over <see cref="JumpWatch.MaxAirSeconds"/> in the air.</summary>
    Long,

    /// <summary>No jump pressed, no blast of its own, no jump in the air: a fall off a ledge, or carried.</summary>
    Fall,

    /// <summary>Hurt by another player, or blasted by one (as the game says), around the takeoff or in the air.</summary>
    Pushed,

    /// <summary>Its speed jumped in the air with nothing of its own to explain it (an airblast, a shove).</summary>
    Kicked,

    /// <summary>The body moved further in one command than anything but the game moves it (a teleport, a respawn).</summary>
    Moved,

    /// <summary>It died in the air.</summary>
    Died,

    /// <summary>It came down in water.</summary>
    Wet,

    /// <summary>It caught a ladder, or stopped walking (noclip, an observer).</summary>
    Ladder,

    /// <summary>It came down on another player.</summary>
    OnPlayer,
}

/// <summary>
/// Sees the jumps players make (plan_bots_teams_tf2_part_g G.6, lane G6a): each command of a watched player, with its body
/// as the command finds it, joins a run of the player's ground (the last <see cref="LookbackSeconds"/> of commands kept)
/// or of its flight. A flight begins at the first read off the ground (dry and walking, after a read on it) and ends at
/// the first read on the ground again, where it is handed over as a <see cref="ShownJumpRecipe"/>, or thrown away with a
/// reason (<see cref="JumpThrow"/>, counted in <see cref="Thrown"/>): see the remarks of each rule below.
///
/// <para>It is told players by their slot, which it keeps only to join a slot's commands into a flight: what it hands
/// over says nothing of who. Pure and deterministic: the same calls in the same order give the same jumps. One thread
/// (the server's game thread). <paramref name="tickSeconds"/> is a command's time.</para>
/// </summary>
public sealed class JumpWatch(double tickSeconds)
{
    /// <summary>The least time in the air of a jump to learn: the plan's floor (a step, a hop over a rail is none).</summary>
    public const double MinAirSeconds = 0.4d;

    /// <summary>The most: a longer flight is a fall into a pit or a stuck body, and the trace is bounded by it.</summary>
    public const double MaxAirSeconds = 6d;

    /// <summary>How far before the takeoff the trace begins, and how long a blast or a hurt before it counts towards it.</summary>
    public const double LookbackSeconds = 0.5d;

    /// <summary>The commands before the takeoff a jump press (or a blast of its own) is the takeoff's within.</summary>
    public const int PressWindow = 3;

    /// <summary>The commands a kick in the air may follow what explains it by (the body read lags a push by one).</summary>
    public const int KickWindow = 3;

    /// <summary>A rise in speed over this from one read in the air to the next is a kick.</summary>
    public const float KickSpeed = 100f;

    /// <summary>A rise in upward speed over this from one read in the air to the next is a kick (gravity only takes it).</summary>
    public const float KickRise = 50f;

    /// <summary>A move over this between two reads is the game's (as a bot's jump's <c>JumpEnd.Moved</c>).</summary>
    public const float MoveUnits = 256f;

    private struct Run
    {
        // The last read.
        public bool Known;
        public AiVector LastOrigin, LastVelocity;
        public byte LastButtons;
        public int Commands;

        // The ground: a valid takeoff, its body and its last commands.
        public bool Grounded;
        public AiVector GroundOrigin, GroundVelocity;
        public bool GroundDucked;
        public ShownJumpCommand[] Ring;
        public int RingCount, RingHead;
        public int OwnBlastAt, OtherAt, ShotAt, OwnHurtAt, AirPressAt;
        public JumpBlast OwnBlastKind;
        public int GroundCost;

        // A flight.
        public bool Flying;
        public JumpThrow? Throw;
        public List<ShownJumpCommand> Trace;
        public int TraceBase, Air, Cost, TakeoffIndex, JumpIndex, ShotIndex, BlastIndex;
        public ShownJumpFlags Flags;
        public JumpBlast Blasts;
        public bool AirDucked;
        public AiVector Takeoff, Launch, Blasted;
    }

    private Run[] _runs = [];
    private readonly long[] _thrown = new long[(int)JumpThrow.OnPlayer + 1];

    /// <summary>The commands of the air a jump to learn lasts at least.</summary>
    public int MinAirCommands => (int)Math.Ceiling(MinAirSeconds / tickSeconds - 1e-6);

    /// <summary>The commands of the air a jump to learn lasts at most.</summary>
    public int MaxAirCommands => (int)Math.Floor(MaxAirSeconds / tickSeconds + 1e-6);

    /// <summary>The commands kept from before the takeoff.</summary>
    public int LookbackCommands => (int)Math.Round(LookbackSeconds / tickSeconds);

    /// <summary>Jumps handed over since the watch was made.</summary>
    public long Landed { get; private set; }

    /// <summary>Flights thrown away since the watch was made, by why.</summary>
    public long Thrown(JumpThrow why) => _thrown[(int)why];

    /// <summary>Whether the player in <paramref name="slot"/> is in a flight (its landing is the next read on the ground).</summary>
    public bool Flying(int slot) => (uint)slot < (uint)_runs.Length && _runs[slot].Flying;

    /// <summary>
    /// The player in <paramref name="slot"/> runs <paramref name="command"/>, its body as the command finds it
    /// <paramref name="body"/>, and the game says <paramref name="blast"/> of the blasts on it. A flight it lands from
    /// is added to <paramref name="landed"/> when it is a jump to learn.
    /// </summary>
    public void Command(int slot, in ShownJumpBody body, in ShownJumpCommand command, JumpBlast blast, List<ShownJumpRecipe> landed)
    {
        ArgumentNullException.ThrowIfNull(landed);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        if (slot >= _runs.Length)
            Array.Resize(ref _runs, Math.Max(slot + 1, _runs.Length * 2));
        ref Run run = ref _runs[slot];
        if (run.Commands == 0)
            Forget(ref run);
        int at = ++run.Commands;

        // Moved by the game: whatever was under way is no jump, and the ground starts again here.
        if (run.Known && (body.Origin - run.LastOrigin).Length > MoveUnits)
        {
            if (run.Flying)
                End(ref run, JumpThrow.Moved);
            run.Grounded = false;
            run.RingCount = 0;
        }

        if (run.Flying)
            Fly(ref run, at, in body, in command, blast, landed);
        else if (!body.OnGround && !body.Wet && body.Walking && run.Grounded)
            Begin(ref run, at, in body, in command, blast);
        else
            Ground(ref run, at, in body, in command, blast);

        run.Known = true;
        run.LastOrigin = body.Origin;
        run.LastVelocity = body.Velocity;
        run.LastButtons = command.Buttons;
    }

    /// <summary>The player in <paramref name="slot"/> fired (told after the command that fired ran): the shot is that command.</summary>
    public void Fired(int slot)
    {
        if ((uint)slot >= (uint)_runs.Length)
            return;
        ref Run run = ref _runs[slot];
        if (run.Commands == 0)
            return;
        run.ShotAt = run.Commands;
        if (run.Flying && run.ShotIndex < 0)
        {
            run.ShotIndex = run.Commands - run.TraceBase;
            run.Flags |= ShownJumpFlags.ShotSeen;
        }
    }

    /// <summary>
    /// The player in <paramref name="slot"/> was hurt for <paramref name="amount"/>: by another player
    /// (<paramref name="byOther"/>: the flight is someone else's doing), or by itself or the world (its cost; a blast of its
    /// own, <paramref name="ownBlast"/>, also lifts it).
    /// </summary>
    public void Hurt(int slot, int amount, bool byOther, bool ownBlast)
    {
        if ((uint)slot >= (uint)_runs.Length)
            return;
        ref Run run = ref _runs[slot];
        if (run.Commands == 0)
            return;
        int at = run.Commands;
        if (byOther)
        {
            run.OtherAt = at;
            if (run.Flying)
                run.Throw ??= JumpThrow.Pushed;
            return;
        }

        bool stale = at - run.OwnHurtAt > LookbackCommands;
        run.OwnHurtAt = at;
        if (ownBlast)
            (run.OwnBlastAt, run.OwnBlastKind) = (at, run.OwnBlastKind | JumpBlast.Own);
        if (run.Flying)
        {
            run.Cost += Math.Max(0, amount);
            if (ownBlast)
                Blasted(ref run, at, JumpBlast.Own);
            return;
        }

        // On the ground: the hurts of the last look back count towards the next takeoff.
        if (stale)
            run.GroundCost = 0;
        run.GroundCost += Math.Max(0, amount);
    }

    /// <summary>The player in <paramref name="slot"/> died: a flight under way is thrown away, and its ground starts again at its spawn.</summary>
    public void Died(int slot)
    {
        if ((uint)slot >= (uint)_runs.Length)
            return;
        ref Run run = ref _runs[slot];
        if (run.Flying)
            End(ref run, JumpThrow.Died);
        Forget(ref run);
    }

    /// <summary>The player in <paramref name="slot"/> left (another may take the slot): everything of it is dropped, nothing counted.</summary>
    public void Leave(int slot)
    {
        if ((uint)slot < (uint)_runs.Length)
            Forget(ref _runs[slot]);
    }

    /// <summary>Nothing is gathered any more (the store off, the round over, the level gone): every run is dropped, none counted.</summary>
    public void Clear()
    {
        for (int i = 0; i < _runs.Length; i++)
            Forget(ref _runs[i]);
    }

    private void Ground(ref Run run, int at, in ShownJumpBody body, in ShownJumpCommand command, JumpBlast blast)
    {
        if ((blast & (JumpBlast.Own | JumpBlast.OwnSticky)) != 0)
            (run.OwnBlastAt, run.OwnBlastKind) = (at, run.OwnBlastKind | (blast & (JumpBlast.Own | JumpBlast.OwnSticky)));
        if ((blast & JumpBlast.Other) != 0)
            run.OtherAt = at;

        // Only a dry, walking body on the ground is a takeoff: one swimming, on a ladder or spawned in the air is not.
        if (!body.OnGround || body.Wet || !body.Walking)
        {
            run.Grounded = false;
            run.RingCount = 0;
            return;
        }

        run.Grounded = true;
        run.GroundOrigin = body.Origin;
        run.GroundVelocity = body.Velocity;
        run.GroundDucked = body.Ducked;
        int size = LookbackCommands;
        if (run.Ring is null || run.Ring.Length != size)
            (run.Ring, run.RingCount, run.RingHead) = (new ShownJumpCommand[size], 0, 0);
        run.Ring[run.RingHead] = command;
        run.RingHead = (run.RingHead + 1) % size;
        run.RingCount = Math.Min(size, run.RingCount + 1);
    }

    private void Begin(ref Run run, int at, in ShownJumpBody body, in ShownJumpCommand command, JumpBlast blast)
    {
        int lookback = LookbackCommands;
        run.Trace ??= new List<ShownJumpCommand>(lookback + MaxAirCommands + 2);
        run.Trace.Clear();
        int size = run.Ring.Length;
        for (int i = 0; i < run.RingCount; i++)
            run.Trace.Add(run.Ring[(run.RingHead - run.RingCount + i + size) % size]);
        run.TraceBase = at - run.RingCount;
        run.TakeoffIndex = run.Trace.Count;
        run.Trace.Add(command);
        run.Flying = true;
        run.Throw = null;
        run.Air = 1;
        run.Flags = run.GroundDucked ? ShownJumpFlags.DuckedAtTakeoff : ShownJumpFlags.None;
        run.Blasts = JumpBlast.None;
        run.AirDucked = body.Ducked;
        run.Takeoff = run.GroundOrigin;
        run.Launch = body.Velocity;
        run.Blasted = default;
        run.JumpIndex = run.ShotIndex = run.BlastIndex = -1;
        run.Cost = at - run.OwnHurtAt <= lookback ? run.GroundCost : 0;
        run.GroundCost = 0;

        // The jump: a press in the commands just before the body left the ground.
        for (int i = Math.Max(0, run.TakeoffIndex - PressWindow); i < run.TakeoffIndex; i++)
        {
            if (run.Trace[i].Holds(ShownJumpCommand.Jump))
            {
                run.JumpIndex = i;
                run.Flags |= ShownJumpFlags.JumpPressed;
                break;
            }
        }

        if (at - run.ShotAt <= lookback)
        {
            run.ShotIndex = run.ShotAt - run.TraceBase;
            run.Flags |= ShownJumpFlags.ShotSeen;
        }

        // Someone else's doing around the takeoff: no jump of the player's.
        if (at - run.OtherAt <= lookback || (blast & JumpBlast.Other) != 0)
            run.Throw = JumpThrow.Pushed;

        // A blast of its own that lifted it: told on this read, or on the ground just before.
        JumpBlast own = blast & (JumpBlast.Own | JumpBlast.OwnSticky);
        if (own == JumpBlast.None && at - run.OwnBlastAt <= PressWindow + 1)
            own = run.OwnBlastKind;
        if (own != JumpBlast.None)
        {
            Blasted(ref run, at, own);
            run.Blasted = body.Velocity;
        }

        run.OwnBlastKind = JumpBlast.None;
    }

    private void Fly(ref Run run, int at, in ShownJumpBody body, in ShownJumpCommand command, JumpBlast blast, List<ShownJumpRecipe> landed)
    {
        if (run.Trace.Count < LookbackCommands + MaxAirCommands + 2)
            run.Trace.Add(command);
        if ((blast & JumpBlast.Other) != 0)
            run.Throw ??= JumpThrow.Pushed;
        if ((blast & (JumpBlast.Own | JumpBlast.OwnSticky)) is var own and not JumpBlast.None)
            Blasted(ref run, at, own);

        if (body.Wet)
        {
            End(ref run, JumpThrow.Wet);
            Ground(ref run, at, in body, in command, blast);
            return;
        }

        if (!body.Walking)
        {
            End(ref run, JumpThrow.Ladder);
            Ground(ref run, at, in body, in command, blast);
            return;
        }

        if (body.OnGround)
        {
            Land(ref run, in body, landed);
            Ground(ref run, at, in body, in command, blast);
            return;
        }

        run.Air++;
        run.AirDucked |= body.Ducked;
        if (command.Holds(ShownJumpCommand.Jump) && (run.LastButtons & ShownJumpCommand.Jump) == 0)
            run.AirPressAt = at;

        // A kick: the body sped up as nothing in the air does by itself.
        float faster = body.Velocity.Length - run.LastVelocity.Length, rise = body.Velocity.Z - run.LastVelocity.Z;
        if (faster > KickSpeed || rise > KickRise)
        {
            if ((run.Blasts & (JumpBlast.Own | JumpBlast.OwnSticky)) != 0 || at - run.OwnBlastAt <= KickWindow)
            {
                if (run.Blasted == default)
                    run.Blasted = body.Velocity;
            }
            else if (at - run.AirPressAt <= KickWindow)
                run.Flags |= ShownJumpFlags.AirJump;
            else
                run.Throw ??= JumpThrow.Kicked;
        }
    }

    private void Blasted(ref Run run, int at, JumpBlast own)
    {
        run.Blasts |= own;
        run.Flags |= (own & JumpBlast.Own) != 0 ? ShownJumpFlags.OwnBlast : ShownJumpFlags.None;
        run.Flags |= (own & JumpBlast.OwnSticky) != 0 ? ShownJumpFlags.OwnSticky : ShownJumpFlags.None;
        if (run.BlastIndex < 0)
            run.BlastIndex = at - run.TraceBase;
    }

    private void Land(ref Run run, in ShownJumpBody body, List<ShownJumpRecipe> landed)
    {
        if (body.OnPlayer)
            run.Throw ??= JumpThrow.OnPlayer;
        if (run.Air < MinAirCommands)
            run.Throw ??= JumpThrow.Short;
        if (run.Air > MaxAirCommands)
            run.Throw ??= JumpThrow.Long;
        ShownJumpKind? kind = (run.Blasts & JumpBlast.Own) != 0 ? ShownJumpKind.Blast
            : (run.Blasts & JumpBlast.OwnSticky) != 0 ? ShownJumpKind.Sticky
            : (run.Flags & ShownJumpFlags.AirJump) != 0 ? ShownJumpKind.Double
            : (run.Flags & ShownJumpFlags.JumpPressed) != 0 ? (run.AirDucked ? ShownJumpKind.Crouch : ShownJumpKind.Plain)
            : null;
        if (kind is null)
            run.Throw ??= JumpThrow.Fall;
        if (run.Throw is { } why)
        {
            End(ref run, why);
            return;
        }

        ShownJumpFlags flags = run.Flags | (body.Ducked ? ShownJumpFlags.DuckedAtLanding : ShownJumpFlags.None);
        landed.Add(new ShownJumpRecipe(kind!.Value, flags, run.Takeoff, body.Origin, run.GroundVelocity, run.Launch, run.Blasted, run.LastVelocity,
                                       run.Air, run.Cost, run.TakeoffIndex, run.JumpIndex, run.ShotIndex, run.BlastIndex, [.. run.Trace]));
        Landed++;
        run.Flying = false;
    }

    private void End(ref Run run, JumpThrow why)
    {
        _thrown[(int)why]++;
        run.Flying = false;
        run.Throw = null;
    }

    private static void Forget(ref Run run)
    {
        ShownJumpCommand[] ring = run.Ring;
        List<ShownJumpCommand> trace = run.Trace;
        run = default;
        run.Ring = ring;
        run.Trace = trace;
        (run.OwnBlastAt, run.OtherAt, run.ShotAt, run.OwnHurtAt, run.AirPressAt) = (int.MinValue / 2, int.MinValue / 2, int.MinValue / 2, int.MinValue / 2, int.MinValue / 2);
    }
}

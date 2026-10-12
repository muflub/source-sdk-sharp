//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The standalone player's usercmd queue and the loop that runs it —
/// plan_entity.md §3.10, step 3d.
///
/// <para>
/// <b>WHAT MOVED, AND WHY IT IS ONE CROSSING.</b> The queue used to be
/// <c>CStandalonePlayer::QueueUsercmds</c> and the loop
/// <c>CStandalonePlayer::RunQueuedCommands</c>/<c>RunOneCommand</c>, and the
/// loop reached managed code THREE TIMES PER COMMAND —
/// <c>OnStandalonePlayerCommand</c> at phase 0 and phase 1, then
/// <c>OnPlayerRunCommand</c>. A packet carrying eight commands therefore cost
/// twenty-four transitions. Here the whole decoded packet arrives in ONE
/// crossing (<c>PlayerCommands</c>), the frame's run is ONE more
/// (<c>PlayerRunCommands</c>), and those three per-command calls are ordinary
/// managed method calls inside <see cref="Run"/>. Neither crossing grows with
/// the number of commands.
/// </para>
///
/// <para>
/// <b>WHAT DID NOT MOVE.</b> <c>usercmd.cpp</c> — the bit-level delta decoding
/// of a <c>CUserCmd</c> off the wire — stays C++ (plan_entity.md §2 and §6):
/// it is an engine ABI and nothing about it is a decision. So native decodes,
/// hands this side a blittable <see cref="UserCmd"/> array, and keeps the
/// <c>CUserCmd</c>s. What crosses is the <b>flatten</b>, which IS a decision:
/// how far dropped packets are covered, how far back the backup commands
/// reach, and the order the queue runs in. See <see cref="Plan"/>.
/// </para>
///
/// <para>
/// <b>WHY IT LIVES IN SourceSharp.Abi</b> and not beside <c>SsPlayer</c>: this
/// is the one assembly where both the engine table and <see cref="ISideModule"/>
/// are visible, which is the same reason step 3e's movement loop lives here.
/// Nothing in it is game policy — the game policy it invokes is the module's,
/// through the interface.
/// </para>
///
/// <para>
/// <b>ONE PER SERVER COMPOSITION</b> (statics to zero): the per-player queues,
/// the batch counters, the decode's magnitude reader and the shadow's run
/// state are this instance's. The server module builds it, registers it, and
/// hands it to its player-command binding and the commands that read it.
/// <see cref="Plan"/> stays static: it is a pure function.
/// </para>
/// </summary>
public sealed unsafe class PlayerCommandQueue
{
    /// <summary>
    /// <c>CMD_MAXBACKUP</c> — shell_gameclients.cpp. The most commands one
    /// packet can carry, backup included, and therefore the largest plan
    /// <see cref="Plan"/> can be asked to write from a single packet's own
    /// commands.
    /// </summary>
    public const int MaxBackup = 64;

    /// <summary>
    /// Stock's cut-off: beyond this many dropped packets nothing is replayed at
    /// all. <c>CBasePlayer::ProcessUsercmds</c> uses the same 24 and for the
    /// same reason — a client that has been gone this long is resynchronised
    /// rather than caught up.
    /// </summary>
    public const int MaxDroppedPackets = 24;

    /// <summary>
    /// The plan entry meaning "replay the last command this player sent, one
    /// tick later" — stock's <c>m_LastCmd.tick_count++</c> arm.
    ///
    /// <para>
    /// <b>-1 AND NOT 0.</b> 0 is a real index, the newest command in the
    /// packet; a sentinel that collides with a valid value is the failure this
    /// tree keeps meeting (a default handle WAS entry zero, worldspawn, until
    /// 2026-10-05). This one cannot collide because no index is negative.
    /// </para>
    /// </summary>
    public const int ReplayLast = -1;

    /// <summary>
    /// THE FLATTEN, which is the decision that crosses.
    ///
    /// <para>
    /// A port of <c>CStandalonePlayer::QueueUsercmds</c>'s body, which is
    /// itself the flatten <c>CBasePlayer::PhysicsSimulate</c> performs: dropped
    /// packets beyond what the backup commands can cover replay the last known
    /// command, then the backup commands cover what they can, then the packet's
    /// own commands run — oldest first.
    /// </para>
    ///
    /// <para>
    /// The commands arrive NEWEST FIRST, which is the order the delta
    /// compression decodes them in, so "oldest first" counts down. Entries are
    /// indices into that array, or <see cref="ReplayLast"/>.
    /// </para>
    ///
    /// <para>
    /// SIZE THEN FILL: answers how many entries the plan has and writes nothing
    /// unless all of them fit, which is the contract the rest of this ABI
    /// already uses. A caller with a big enough buffer never sees the
    /// difference.
    /// </para>
    /// </summary>
    public static int Plan(int numCmds, int totalCmds, int droppedPackets, Span<int> order)
    {
        if (numCmds < 0 || totalCmds < numCmds)
            return 0;

        int backup = totalCmds - numCmds;
        int count = 0;

        if (droppedPackets < MaxDroppedPackets)
        {
            int dropped = droppedPackets;

            // Beyond what the packet's backup commands can cover there is
            // nothing to replay but the last command that arrived.
            while (dropped > backup)
            {
                Write(order, count++, ReplayLast);
                dropped--;
            }

            // Then the backup commands themselves, oldest first. They sit
            // AFTER the new ones in the array because the array is newest
            // first.
            while (dropped > 0)
            {
                Write(order, count++, numCmds + dropped - 1);
                dropped--;
            }
        }

        // The packet's own commands, oldest first.
        for (int i = numCmds - 1; i >= 0; i--)
            Write(order, count++, i);

        return count;

        static void Write(Span<int> into, int at, int value)
        {
            if (at < into.Length)
                into[at] = value;
        }
    }

    /// <summary>
    /// The <c>PlayerCommands</c> crossing: one decoded packet, queued.
    ///
    /// <para>
    /// Answers the plan's length. When it does not fit in <paramref name="order"/>
    /// nothing is queued and nothing is written — the caller sizes and calls
    /// again — so a short buffer can never leave the two queues disagreeing.
    /// </para>
    /// </summary>
    public int Queue(EntityHandle player, UserCmd* cmds, int numCmds, int totalCmds,
                            int droppedPackets, int* order, int orderMax)
    {
        if (cmds == null || order == null || orderMax < 0)
            return -1;

        var plan = new Span<int>(order, orderMax);
        int count = Plan(numCmds, totalCmds, droppedPackets, plan);

        // SIZE THEN FILL. Nothing has been queued yet, so answering the need is
        // a complete no-op and the caller may simply call again.
        if (count > orderMax)
            return count;

        var q = QueueFor(player);

        for (int i = 0; i < count; i++)
        {
            int index = plan[i];

            if (index == ReplayLast)
            {
                // Native does exactly this to its own CUserCmd copy, from the
                // same starting value, so the two queues stay the same commands
                // in the same order.
                q.Last.Cmd.TickCount++;
                q.Add(q.Last.Cmd);
            }
            else if ((uint)index < (uint)totalCmds)
            {
                q.Add(cmds[index]);
            }
        }

        if (numCmds > 0)
            q.Last = new DecodedUserCmd { Cmd = cmds[0] };

        _batches++;
        _queued += count;
        q.Stamp = _batches;

        PruneStaleQueues();

        return count;
    }

    /// <summary>
    /// Drops the queues of players who are gone.
    ///
    /// <para>
    /// <b>NOT "EVERY EMPTY QUEUE".</b> A live player's queue is empty for most
    /// of every tick — <see cref="Run"/> clears it — and the
    /// <c>LastCmd</c> it still holds is what the next dropped packet replays.
    /// Dropping an empty queue would therefore throw away live state on the
    /// commonest path. What identifies a player who is gone is a queue that has
    /// not been WRITTEN to for a long time, and a connected client writes one
    /// every packet.
    /// </para>
    ///
    /// <para>
    /// This exists because nothing else here is told about a disconnect;
    /// <see cref="Forget"/> is the explicit route and plan_entity.md step 3f,
    /// which owns <c>ClientDisconnect</c>, should call it. Until then this
    /// bounds the table rather than letting a long-running server accumulate
    /// one entry per client that ever joined.
    /// </para>
    /// </summary>
    /// <summary>
    /// The most commands one packet can queue: every dropped packet covered,
    /// plus the packet's own backup.
    /// </summary>
    public const int MaxQueuedPerPacket = MaxBackup + MaxDroppedPackets;

    /// <summary>
    /// sv_max_usercmd_move_magnitude, as the server module reads it. Set by the
    /// composition that owns this queue; a decode with nobody to ask is
    /// refused, never guessed.
    /// </summary>
    public Func<float>? MaxMoveMagnitude { get; set; }

    /// <summary>
    /// ONE PACKET, WHOLE — plan_server_novalve.md P2.2, the user's amendment:
    /// what native's ProcessUsercmds did in a loop and then handed over for
    /// planning (decode each command off the wire, clamp its move, flatten,
    /// plan) happens here, and native is answered once with the result.
    ///
    /// <para>
    /// Decodes <paramref name="totalCmds"/> commands from the packet's bits,
    /// and when <paramref name="queue"/> plans and queues them exactly as
    /// <see cref="Queue"/> does. <paramref name="output"/> receives the queued
    /// commands IN RUN ORDER, replays already resolved, so native appends them
    /// to its own queue as they come -- the two queues hold the same commands
    /// and an index names the same one in both. <paramref name="bitsRead"/> is
    /// where the packet's commands end.
    /// </para>
    /// </summary>
    /// <returns>
    /// How many commands were queued (0 when <paramref name="queue"/> is
    /// false), or -1 for a packet that must be refused: counts out of range,
    /// a truncated stream, or a plan that does not fit
    /// <paramref name="output"/>. A refusal queues NOTHING.
    /// </returns>
    public int DecodeAndQueue(EntityHandle player, ReadOnlySpan<byte> data, int startBit,
                                     int dataBits, int numCmds, int totalCmds, int droppedPackets,
                                     bool queue, Span<DecodedUserCmd> output, out int bitsRead)
    {
        bitsRead = startBit;

        // stock's own refusal, and its own message.
        if (totalCmds < 0 || totalCmds >= MaxBackup - 1 || numCmds < 0 || numCmds > totalCmds)
        {
            _log.Msg($"CBasePlayer::ProcessUsercmds: too many cmds {totalCmds} sent for player unknown\n");
            return -1;
        }

        Func<float> magnitude = MaxMoveMagnitude
            ?? throw new InvalidOperationException("PlayerCommandQueue.MaxMoveMagnitude was never set");

        Span<DecodedUserCmd> decoded = stackalloc DecodedUserCmd[MaxBackup];
        int end = UserCmdCodec.Decode(data, startBit, dataBits, totalCmds, magnitude(), decoded);
        if (end < 0)
            return -1;

        bitsRead = end;

        for (int i = 0; i < totalCmds; i++)
            decoded[i].Cmd.StructSize = (uint)sizeof(UserCmd);

        if (!queue)
            return 0;

        Span<int> plan = stackalloc int[MaxQueuedPerPacket];
        int count = Plan(numCmds, totalCmds, droppedPackets, plan);
        if (count > plan.Length || count > output.Length)
            return -1;

        var q = QueueFor(player);
        int written = 0;

        for (int i = 0; i < count; i++)
        {
            int index = plan[i];
            DecodedUserCmd cmd;

            if (index == ReplayLast)
            {
                q.Last.Cmd.TickCount++;
                cmd = q.Last;
            }
            else if ((uint)index < (uint)totalCmds)
            {
                cmd = decoded[index];
            }
            else
            {
                continue;
            }

            q.Add(cmd.Cmd);
            output[written++] = cmd;
        }

        if (numCmds > 0)
            q.Last = decoded[0];

        _batches++;
        _queued += written;
        q.Stamp = _batches;

        PruneStaleQueues();

        return written;
    }

    private void PruneStaleQueues()
    {
        if (_queues.Count <= StaleBatches)
            return;

        List<uint>? gone = null;

        foreach (var pair in _queues)
        {
            if (_batches - pair.Value.Stamp > StaleBatches)
                (gone ??= new List<uint>()).Add(pair.Key);
        }

        if (gone is null)
            return;

        foreach (uint key in gone)
            _queues.Remove(key);
    }

    /// <summary>
    /// How many batches a queue may go unwritten before it is assumed
    /// abandoned. A connected client sends a packet every tick, so this is
    /// generous by orders of magnitude — it is a bound on the table, not a
    /// timeout with meaning.
    /// </summary>
    private const int StaleBatches = 256;

    /// <summary>
    /// The <c>PlayerRunCommands</c> crossing: the frame's queue, run.
    ///
    /// <para>
    /// ONE CROSSING FOR THE WHOLE QUEUE. Per command it drives native's three
    /// stages (the <c>PlayerRunMove</c> slot) and puts the sink's calls
    /// between them, in the places the single native function had them:
    /// prepare, phase 0, move, phase 1, buttons, finish-and-publish. Every
    /// split is argued in <c>PlayerRunMove</c>'s comment in
    /// <c>sourcesharp.h</c>.
    /// </para>
    ///
    /// <para>
    /// <paramref name="nativeCount"/> is what native holds. It must equal what
    /// this side holds: the two queues were filled from one plan and an index
    /// names the same command in both. A disagreement is a defect and is said
    /// out loud rather than papered over, and the run is cut to the shorter of
    /// the two so a drift cannot walk off the end of either.
    /// </para>
    ///
    /// <para>Answers how many commands it ran.</para>
    /// </summary>
    /// <param name="shadowEnabled">
    /// <c>sourcesharp_player_shadow</c>, read by the caller off its own side's
    /// convar (plan_abi_hotreload D7: it was the static <c>PlayerShadow.Switch</c>).
    /// </param>
    public int Run(EntityHandle player, int nativeCount, PlayerRunMoveStages stages,
                          ISideModule? module, IServerModule? serverModule, bool shadowEnabled)
        => Run(player, nativeCount, new ModuleSink(module, serverModule), stages.RunMove,
               shadowEnabled, stages.Api);

    /// <inheritdoc cref="Run(EntityHandle,int,PlayerRunMoveStages,ISideModule,IServerModule,bool)"/>
    /// <param name="sink">
    /// The per-command decisions, narrowed to the two the loop actually makes.
    /// <see cref="ModuleSink"/> is the shipped one and forwards to the side's
    /// module; a GENERIC parameter constrained to the interface so a struct
    /// sink neither boxes nor allocates on a path that runs every frame.
    /// </param>
    /// <param name="runMove">
    /// How to reach native for one command's stage —
    /// <see cref="PlayerRunMoveStages.RunMove"/> in the shipped path.
    ///
    /// <para>
    /// A PARAMETER AND NOT A FIELD. The order this loop puts the decisions in
    /// relative to the two native stages is the whole of what step 3d had to
    /// keep, and an order is only checkable by watching the calls go past. A
    /// static function pointer to the engine table cannot be watched from a
    /// fact, so the loop takes the call it makes; the public overload above
    /// supplies the real one and nothing else may.
    /// </para>
    /// </param>
    /// <param name="shadowEnabled">Whether the player's shadow steps after each command.</param>
    /// <param name="side">The side whose shadow steps; null steps none (a fact's
    /// loop, which has no engine).</param>
    internal int Run<TSink>(EntityHandle player, int nativeCount, TSink sink,
                                   Func<EntityHandle, int, int, int> runMove,
                                   bool shadowEnabled = true, EngineSide? side = null)
        where TSink : IPlayerCommandSink
    {
        var q = QueueFor(player);
        int count = q.Count;

        if (count != nativeCount)
        {
            _log.Warning($"[SourceSharp] player command queues disagree: native {nativeCount}, "
                        + $"managed {count}. Running {Math.Min(count, nativeCount)}. The two are "
                        + "filled from one plan, so this is a defect in PlayerCommands.\n");
            count = Math.Min(count, nativeCount);
        }

        int ran = 0;

        for (int i = 0; i < count; i++)
        {
            UserCmd cmd = q.Commands[i];

            // Stage 0: the command prepared — the current command, the seed
            // SharedRandom* reads, curtime, the view angles, SetupMove. Phase 0
            // runs AFTER it because that is where the native loop ran it, and a
            // phase 0 before it would see no current command and the previous
            // command's seed.
            if (runMove(player, i, 0) == 0)
            {
                // The refusal PlayerRunMove documents. Stopping is right: every
                // later index in this queue names a command native does not
                // have either, and running the module's per-command decisions
                // for a move that did not happen is worse than dropping them.
                _log.Warning($"[SourceSharp] native refused player command {i} of {count}; "
                            + "the rest of this queue is dropped.\n");
                break;
            }

            ran++;

            // Phase 0 — where RunPreThink's decisions live, between the prepare
            // and the move.
            sink.Command(player, in cmd, 0);

            // Stage 1: the move.
            runMove(player, i, 1);

            // Phase 1 — RunPostThink's moment: the command has moved the player
            // and whatever fires from where they NOW stand aims right.
            sink.Command(player, in cmd, 1);

            // The buttons, to the side that decides what they mean.
            sink.Buttons(player, cmd.Buttons);

            // Stage 2: the teardown and the publish, after the decisions, which
            // is where the native loop had them.
            runMove(player, i, 2);

            // AND THE SHADOW FOLLOWS THE PLAYER, which is where stock puts it
            // too: CBasePlayer::PhysicsSimulate calls UpdateVPhysicsPosition
            // after each command has run (player.cpp:3395).
            //
            // It used to be the last line of stage 2, in C++, with the whole
            // state machine behind it. It is here now because this is the loop
            // that knows WHICH command just ran -- stock's loop counter, which
            // the shadow's arrival ramp is measured in -- and because both of
            // its calls are outbound, so the move costs no counted crossing.
            // See PlayerShadow.
            Shadow.Step(side, player, i, shadowEnabled);
        }

        q.Clear();
        _ran += ran;

        return ran;
    }

    /// <summary>
    /// The shipped sink: the side's own module, reached through the two
    /// interfaces it already implements.
    ///
    /// <para>
    /// A STRUCT, so the generic loop above neither boxes it nor allocates one
    /// per frame per player. <c>firstTime</c> is always true on the server —
    /// there is no replay here, which is what <c>bFirstTime</c> in
    /// <c>sourcesharp.h</c> says and what the native loop passed.
    /// </para>
    /// </summary>
    private readonly struct ModuleSink : IPlayerCommandSink
    {
        private readonly ISideModule? _module;
        private readonly IServerModule? _server;

        public ModuleSink(ISideModule? module, IServerModule? server)
        {
            _module = module;
            _server = server;
        }

        public void Command(EntityHandle player, in UserCmd cmd, int phase)
            => _module?.OnStandalonePlayerCommand(player, in cmd, phase, true);

        public void Buttons(EntityHandle player, int buttons)
            => _server?.OnPlayerRunCommand(player, buttons);
    }


    /// <summary>
    /// Everything queued for a player is dropped — a disconnect, a level
    /// change. Native clears its own queue on the same events; a managed queue
    /// left behind would be handed to whoever next occupied the slot.
    /// </summary>
    public void Forget(EntityHandle player) => _queues.Remove(player.Value);

    /// <summary>Every queue, dropped. Level shutdown.</summary>
    public void ForgetAll() => _queues.Clear();

    /// <summary>How deep a player's queue is right now.</summary>
    public int Depth(EntityHandle player)
        => _queues.TryGetValue(player.Value, out var q) ? q.Count : 0;

    /// <summary>
    /// THE READOUT THAT LETS THE STEP'S CLAIM FAIL.
    ///
    /// <para>
    /// "N commands, one crossing" is measured by dividing the commands actually
    /// run by the <c>ServerPlayerCommands</c> crossings that queued them — and
    /// a build that stopped running commands altogether ALSO costs one crossing
    /// per packet. So the number of commands is read out separately, from a
    /// counter incremented in the run loop itself, and a gate that sees zero
    /// commands with a healthy crossing count fails instead of passing.
    /// </para>
    ///
    /// <para>
    /// <c>batches</c> is <c>PlayerCommands</c> calls, <c>queued</c> the plan
    /// entries they produced, <c>ran</c> the commands the loop actually ran.
    /// </para>
    /// </summary>
    public (long Batches, long Queued, long Ran) Counters()
        => (_batches, _queued, _ran);

    /// <summary>Zeroes the readout, so a gate can measure one interval.</summary>
    public void ResetCounters()
    {
        _batches = 0;
        _queued = 0;
        _ran = 0;
    }

    /// <summary>
    /// The player physics shadow's run state this queue steps (statics to
    /// zero): its advance switch and counters, this composition's own.
    /// </summary>
    public PlayerShadow Shadow { get; } = new();

    /// <param name="log">Where a queue disagreement or a refused command is said (statics to zero).</param>
    public PlayerCommandQueue(ILog log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    private readonly ILog _log;

    private long _batches;
    private long _queued;
    private long _ran;

    private readonly Dictionary<uint, PlayerQueue> _queues = new();

    private PlayerQueue QueueFor(EntityHandle player)
    {
        if (!_queues.TryGetValue(player.Value, out var q))
            _queues[player.Value] = q = new PlayerQueue();

        return q;
    }

    /// <summary>
    /// One player's pending commands, in the order the plan chose.
    ///
    /// <para>
    /// A GROWN ARRAY AND NOT A LIST, and cleared by resetting the count rather
    /// than by dropping it: this runs once per packet per player forever, and
    /// the crossing it sits behind exists to make the command path cheap.
    /// </para>
    /// </summary>
    private sealed class PlayerQueue
    {
        public UserCmd[] Commands = new UserCmd[MaxBackup];
        public int Count;
        // The last command the player SENT, decoded -- what a dropped packet
        // replays. The decoded form, so a replay carries the weapon subtype and
        // random seed the native run loop reads, not just the flat fields.
        public DecodedUserCmd Last;

        /// <summary>The batch number this queue was last written on.</summary>
        public long Stamp;

        public void Add(in UserCmd cmd)
        {
            if (Count == Commands.Length)
                Array.Resize(ref Commands, Commands.Length * 2);

            Commands[Count++] = cmd;
        }

        public void Clear() => Count = 0;
    }
}

/// <summary>
/// The per-command decisions <see cref="PlayerCommandQueue.Run"/> makes between
/// native's two stages, narrowed to the two calls it actually makes.
///
/// <para>
/// NARROW ON PURPOSE. The loop used to reach these through three ABI crossings;
/// what replaced them is two ordinary calls, and stating them as two is what
/// lets a fact watch the ORDER — which is the whole of what step 3d had to keep
/// when the loop moved. Handing the loop a whole <see cref="ISideModule"/>
/// would have made the order checkable only against a thirty-method double.
/// </para>
/// </summary>
public interface IPlayerCommandSink
{
    /// <summary>
    /// One command, at a phase. Phase 0 is before the move — where stock's
    /// <c>RunPreThink</c> ran; phase 1 is after it, where <c>RunPostThink</c>
    /// ran the weapon frame and the impulse cheats.
    /// </summary>
    void Command(EntityHandle player, in UserCmd cmd, int phase);

    /// <summary>The command's buttons, to the side that decides what they mean.</summary>
    void Buttons(EntityHandle player, int buttons);
}

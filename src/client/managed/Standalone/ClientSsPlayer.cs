using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of the standalone player (Phase 9a), and its ANIMATOR.
///
/// <para>
/// One exists per player the server networks — including the viewer's own,
/// which is the one that matters: it is the entity 9c4's managed replay moves
/// and 9c5's prediction ring watches.
/// </para>
///
/// <para>
/// What it does with that state is decide how the body should look:
/// <see cref="PlayerAnimation"/> turns the movement into a posture, a rate and
/// a strafe direction, and this class resolves those against the model this
/// particular player is wearing. The split is the same one the viewmodel makes
/// — the rule is pure and testable, the resolve needs a model and a live
/// entity — and it is here rather than in the shell because which gait a
/// player is in is a fact about a game and not about a renderer.
/// </para>
///
/// <para>
/// <b>Driven by the DECODE, and that is a decision worth stating.</b> This runs
/// on every networked update rather than every frame. The cycle itself is a
/// wall clock the shell advances (see <c>SetPlayingSequence</c>), so a gait
/// runs smoothly at the frame rate whatever the update rate is; what samples at
/// the update rate is only the CHOICE of gait and the strafe blend. The
/// alternative was a new per-frame crossing into managed code for every
/// entity, and the sampled values do not move fast enough to pay for one: a
/// player cannot change direction meaningfully inside 50ms.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.SsPlayer)]
public sealed class ClientSsPlayer : ClientStandaloneEntity
{
    /// <summary>
    /// <c>FL_ONGROUND</c> (const.h). Read out of the networked flags word
    /// rather than inferred from the velocity, because a player standing on a
    /// lift going up has an upward velocity and is very much on the ground.
    /// </summary>
    public const uint OnGroundFlag = 1u << 0;

    /// <summary>The pose parameter that carries the strafe direction.</summary>
    public const string MoveYawParameter = "move_yaw";

    /// <summary>
    /// Whether this player's body is animated by the policy at all.
    ///
    /// <para>
    /// ON, and the switch exists for two reasons that are the same reason.
    /// </para>
    ///
    /// <para>
    /// The first is a trap: <c>cs_playseq</c> and <c>cs_setpose</c> are the
    /// probes for looking at a model by hand, and on the player they are
    /// SILENTLY DEFEATED — the animator writes a sequence and move_yaw on every
    /// networked update, which on a listen server is most frames, so a
    /// hand-set pose is gone before the next screenshot. It reports success
    /// and nothing happens, which is the shape of failure this tree has been
    /// bitten by repeatedly.
    /// </para>
    ///
    /// <para>
    /// The second is the gate: <c>tools/player-shot</c> has to photograph a
    /// body that IS animated against the same body that is not, from a camera
    /// that did not move. Without a way to stop the animator there is no
    /// second photograph — and its first version crouched the player instead,
    /// which moves the eye, moves the camera, and changed 94% of the frame on
    /// a build that drew no player at all.
    /// </para>
    ///
    /// <para>
    /// Turning it off leaves the body in whatever pose it last had rather than
    /// resetting it: that is what makes the pair a comparison.
    /// </para>
    ///
    /// <para>
    /// PER PLAYER, not a static. A process-wide flag is state a test can leave
    /// behind for the next one — docs/writing-tests.md's "no globals", which
    /// three tests in this tree have already been caught standing on. The
    /// command below sets it on every live player, which is what a person
    /// typing it means, and each instance still owns its own answer.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; } = true;

    public ClientSsPlayer(IEngineEntityRender render)
        : base(render)
    {
    }

    public override string ClassName => EntityNames.SsPlayer;

    /// <summary>The same type the server writes through, decoded per update.</summary>
    [NetState] public PlayerNetState State = new();

    /// <summary>
    /// How many updates have decoded into <see cref="State"/>.
    ///
    /// <para>
    /// THE PREDICTION'S "the server has spoken" SIGNAL (step 6f), and it is a
    /// count rather than a flag for the reason a flag would need clearing by
    /// its reader: two readers of one flag is one of them silently losing
    /// updates. <see cref="ClientPlayerPrediction"/> remembers the value it last
    /// judged, so it compares exactly once per packet that carried this player
    /// and never twice.
    /// </para>
    /// </summary>
    public int Decodes { get; private set; }

    /// <summary>What was last handed to the renderer, so a gait is not restarted.</summary>
    public int ShownSequence { get; private set; } = -1;

    /// <summary>Which posture that sequence was chosen for. See <see cref="Animate"/>.</summary>
    public PlayerPosture ShownPosture { get; private set; } = PlayerPosture.Idle;

    /// <summary>
    /// The move_yaw pose parameter's index on this player's model, or -1.
    ///
    /// <para>
    /// Resolved once and remembered, because a pose parameter index is a fact
    /// about the model and this runs per player per update. -1 is a legal
    /// answer — a model with no strafe blend animates perfectly well without
    /// one — and is remembered as readily as an index, so a model that has none
    /// is not re-asked twenty times a second forever.
    /// </para>
    /// </summary>
    public int MoveYawIndex { get; private set; } = -1;

    private bool _resolvedMoveYaw;

    public override void OnSpawn()
        => Log.Msg($"[SourceSharp/C# client] {ClassName} arrived\n");

    public override void OnRemoved()
        => Log.Msg($"[SourceSharp/C# client] {ClassName} gone\n");

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        // A model can arrive after the entity does, so the pose parameter is
        // re-asked on a CREATED update rather than only on the first decode.
        if (updateType == DataUpdateType.Created)
            _resolvedMoveYaw = false;

        // The decode has already happened by the time this is called — see
        // DispatchDataChanged — so counting here counts packets that carried
        // this player.
        Decodes++;

        Animate();
    }

    /// <summary>
    /// Puts this player's body into the gait their movement says they are in.
    ///
    /// <para>
    /// Three things happen and only the middle one is engine work. The POLICY
    /// (<see cref="PlayerAnimation.Choose"/>) decides the posture and the
    /// strafe from the movement state; the RESOLVE turns the posture into a
    /// sequence index on this model and asks that sequence how fast the
    /// animator meant it to travel; the HAND-OVER gives the shell a sequence
    /// and a rate.
    /// </para>
    ///
    /// <para>
    /// The hand-over is edge-triggered on the SEQUENCE and not on the whole
    /// pose. A rate that moved is handed over every time — the shell keeps the
    /// running clock when only the rate changes, which is what lets the rate
    /// track a player accelerating without the walk restarting under them. A
    /// posture that RESTARTS (the jump, and only the jump) restarts only when
    /// it is newly entered: a one-shot re-sent twenty times a second never
    /// gets past its first frame, which is the same bug the viewmodel's parity
    /// gate exists to stop, reached from the other direction.
    /// </para>
    /// </summary>
    public void Animate()
    {
        if (!Enabled)
            return;

        bool onGround = (State.Flags & OnGroundFlag) != 0;
        float bodyYaw = Entities.GetAngles(Handle).Y;

        PlayerPose pose = PlayerAnimation.Choose(State.Velocity, bodyYaw,
                                                 onGround, State.Ducked, State.MaxSpeed);

        int sequence = Resolve(pose.Posture);

        if (sequence < 0)
            return;

        float rate = PlayerAnimation.Rate(PlayerAnimation.GroundSpeed(State.Velocity),
                                          Render.SequenceGroundSpeed(Handle, sequence));

        bool restart = PlayerAnimation.Restarts(pose.Posture) && pose.Posture != ShownPosture;

        ShownPosture = pose.Posture;
        ShownSequence = sequence;

        Render.PlaySequence(Handle, sequence, rate, restart);

        if (!_resolvedMoveYaw)
        {
            MoveYawIndex = Render.LookupPoseParameter(Handle, MoveYawParameter);
            _resolvedMoveYaw = true;
        }

        if (MoveYawIndex >= 0)
            Render.SetPoseParameter(Handle, MoveYawIndex, pose.MoveYaw);
    }

    /// <summary>
    /// The posture, as a sequence on THIS player's model.
    ///
    /// <para>
    /// Asks for each of the posture's activities in turn and takes the first
    /// the model answers to. See <see cref="PlayerAnimation.Activities"/> for
    /// why it is a list: a crouched idle is <c>ACT_COVER_LOW</c> on the HL2
    /// citizens and <c>ACT_CROUCHIDLE</c> elsewhere, and a model that has only
    /// one of them must still animate rather than standing in its reference
    /// pose with its arms out.
    /// </para>
    ///
    /// <para>
    /// Answers -1 when the model answers to none of them, and the caller then
    /// leaves the model alone — which is the honest thing to do with something
    /// that has no animation at all, rather than forcing it to sequence 0 and
    /// drawing whatever that happens to be.
    /// </para>
    /// </summary>
    public int Resolve(PlayerPosture posture)
    {
        foreach (string activity in PlayerAnimation.Activities(posture))
        {
            int sequence = Render.LookupSequenceByActivity(Handle, activity);

            if (sequence >= 0)
                return sequence;
        }

        return -1;
    }
}

/// <summary>
/// <c>cs_cl_player_anim 0|1</c> — stop the player's animation policy writing,
/// so a hand-set pose stays put.
///
/// <para>
/// See <see cref="ClientSsPlayer.Enabled"/> for why this exists. In short:
/// with it on, <c>cs_playseq ss_player something</c> reports success and is
/// overwritten within a frame, which is a probe that lies.
/// </para>
/// </summary>
[ConCommand("cs_cl_player_anim",
            Help = "cs_cl_player_anim [0|1] — whether the player's body is animated by " +
                   "the movement policy. Off leaves a hand-set pose alone (cs_playseq, " +
                   "cs_setpose), which on is silently overwritten every update.")]
public sealed class PlayerAnimCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;

    public PlayerAnimCommand(ILog log, IClientStandalones standalones)
    {

        _log = log;
        _standalones = standalones;
    }

    public void Execute(string args)
    {
        foreach (string line in Apply(_standalones.Live(), args.Trim()))
            _log.Msg($"[SourceSharp/Client] {line}\n");
    }

    /// <summary>
    /// The rule, apart from the console: a word that is not "0" turns it on,
    /// an empty word changes nothing and only reports. Pure so that "typing
    /// the command with no argument does not silently enable it" is a thing a
    /// test can hold, rather than something a reader has to trust.
    /// </summary>
    public static IReadOnlyList<string> Apply(IReadOnlyList<ClientStandaloneEntity> live,
                                              string word)
    {
        var lines = new List<string>();
        int touched = 0;

        foreach (var entity in live)
        {
            if (entity is not ClientSsPlayer player)
                continue;

            if (word.Length > 0)
                player.Enabled = word != "0";

            touched++;

            lines.Add($"player {entity.ClassName}: animation policy is "
                      + (player.Enabled
                            ? "ON"
                            : "OFF — cs_playseq and cs_setpose will stay put"));
        }

        if (touched == 0)
            lines.Add("no player on this client to animate");

        return lines;
    }
}

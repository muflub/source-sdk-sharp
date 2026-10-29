using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;
using SourceSharp.Shared.Anim;

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
/// What it does with that state is decide how the body should look. Since
/// phase 0.10 that decision is the SHARED machine's: an
/// <see cref="EngineAnimTarget"/> over this entity's net state and engine
/// arms, driven through <see cref="MultiplayerAnimState.Update"/> with the
/// HL2 posture layer on the chain, is what resolves the gait, the rate and
/// the strafe — the same code the server runs for its copy of this player.
/// The client contributes only the host's duties: which eyes to trust, when
/// to drive, and when the body is allowed to move at all.
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
/// player cannot change direction meaningfully inside 50ms. The machine's
/// clock arguments carry that choice honestly: <c>now</c> is the engine's and
/// <c>frameTime</c> is the time since this player's last drive, so the yaw
/// converge and the gesture autokill timers advance in real seconds whatever
/// the packet rhythm is.
/// </para>
///
/// <para>
/// <b>Which eyes.</b> The machine poses the strafe against the EYES, where
/// the old client animator posed against the body. For the viewer's own
/// player those are the view angles the prediction publishes
/// (<see cref="ViewReadout"/> — stock's own source, the local player's
/// <c>GetAbsVAngles</c>); for everyone else they are the body's own facing,
/// which reduces the strafe to exactly what the old animator measured
/// (travel direction relative to the body) until replicated eye angles ride
/// the wire — a divergence the target's <see cref="IAnimTarget"/> row names.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.Player)]
public sealed class ClientSsPlayer : ClientStandaloneEntity
{
    /// <summary>
    /// <c>FL_ONGROUND</c> (const.h). Read out of the networked flags word
    /// rather than inferred from the velocity, because a player standing on a
    /// lift going up has an upward velocity and is very much on the ground.
    /// </summary>
    public const uint OnGroundFlag = 1u << 0;

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
    /// SILENTLY DEFEATED — the animator writes a sequence and the pose
    /// parameters on every networked update, which on a listen server is most
    /// frames, so a hand-set pose is gone before the next screenshot. It
    /// reports success and nothing happens, which is the shape of failure this
    /// tree has been bitten by repeatedly.
    /// </para>
    ///
    /// <para>
    /// The second is the gate: the before/after photographs of an animated body
    /// have to come from the same camera as the unanimated one, and without a
    /// way to stop the animator there is no second photograph.
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

    // The host's services, all container-supplied and all optional for the
    // tests that build a player by hand: without them the machine is simply
    // never created and the body stays where the last writer left it.
    private readonly IGameClock? _clock;
    private readonly ModelDescriptors? _models;
    private readonly ViewReadout? _view;
    private readonly IEngineHud? _screen;
    private readonly AnimStateLayers? _layers;

    // The machine, built on the first drive — by which time the registry has
    // bound Entities, so the target can take it. One machine per player, held
    // by the player that owns the body (the static-state rule's requirement,
    // not a style choice).
    private EngineAnimTarget? _target;
    private MultiplayerAnimState? _state;
    private float _lastDrive = float.NaN;

    public ClientSsPlayer(IEngineEntityRender render,
                          IGameClock? clock = null, ModelDescriptors? models = null,
                          ViewReadout? view = null, IEngineHud? screen = null,
                          AnimStateLayers? layers = null)
        : base(render)
    {
        _clock = clock;
        _models = models;
        _view = view;
        _screen = screen;
        _layers = layers;
    }

    public override string ClassName => EntityNames.Player;

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

    /// <summary>
    /// What the machine last handed the renderer, so a gait is not restarted.
    /// The read-back of the machine's own record — the ragdoll collapses out
    /// of the stride this names.
    /// </summary>
    public int ShownSequence => _state?.CurrentMainSequence ?? -1;

    /// <summary>
    /// Which posture that sequence was chosen for: the HL2 layer leaves its
    /// answer on the target (<see cref="IAnimTarget.Posture"/>), and this is
    /// the host's read of it.
    /// </summary>
    public PlayerPosture ShownPosture => _target?.Posture ?? PlayerPosture.Idle;

    /// <summary>The client's copy of the machine, or null before it first runs (the server's <c>SsPlayer.AnimStateOrNull</c> twin).</summary>
    public MultiplayerAnimState? AnimStateOrNull => _state;

    public override void OnSpawn()
        => Log.Msg($"[SourceSharp/C# client] {ClassName} arrived\n");

    public override void OnRemoved()
        => Log.Msg($"[SourceSharp/C# client] {ClassName} gone\n");

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        // A model can arrive after the entity does: a CREATED update is the
        // moment to forget the dials and ask again (stock's OnNewModel, driven
        // here because this hierarchy learns about a model at its first
        // replicated update rather than on a SetModel call).
        if (updateType == DataUpdateType.Created)
        {
            _state?.OnNewModel();
            _target?.ClearPoseParameterCache();
        }

        // The decode has already happened by the time this is called — see
        // DispatchDataChanged — so counting here counts packets that carried
        // this player.
        Decodes++;

        Animate();
    }

    /// <summary>
    /// Puts this player's body into the gait their movement says they are in,
    /// by driving the shared machine once.
    ///
    /// <para>
    /// One <see cref="MultiplayerAnimState.Update"/>: the posture decision,
    /// the sequence resolve, the rate, the restart edge and the pose
    /// parameters are all the machine's (see its own comment for the
    /// stock line each one quotes). The hand-over is edge-triggered on the
    /// ACTIVITY exactly as stock's <c>ResetSequence</c> was: a rate that moved
    /// is handed over every drive without restarting the cycle, and a new
    /// gait — including landing into one — restarts it, which is what stock's
    /// deathmatch players do and what the old client animator's
    /// never-restart-any-gait rule diverged from.
    /// </para>
    /// </summary>
    public void Animate()
    {
        if (!Enabled || _clock is null)
            return;

        MultiplayerAnimState? state = EnsureMachine();
        if (state is null)
            return;

        float now = _clock.Now;
        float frameTime = float.IsNaN(_lastDrive) ? 0f : now - _lastDrive;
        _lastDrive = now;

        Vector eyes = EyeAngles();
        state.Update(eyes.Y, eyes.X, now, frameTime);
    }

    /// <summary>
    /// The posture, as a sequence on THIS player's model.
    ///
    /// <para>
    /// Delegates to the shared layer's resolve — the posture's activity list,
    /// walked in order, first answer wins. See
    /// <see cref="Hl2PostureLayer.Activities"/> for why it is a list: a
    /// crouched idle is <c>ACT_COVER_LOW</c> on the HL2 citizens and
    /// <c>ACT_CROUCHIDLE</c> elsewhere, and a model that has only one of them
    /// must still animate rather than standing in its reference pose with its
    /// arms out.
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
        EngineAnimTarget? target = EnsureTarget();
        return target is null ? -1 : Hl2PostureLayer.Resolve(target, posture);
    }

    /// <summary>The machine, built once on the first drive, over this entity.</summary>
    private MultiplayerAnimState? EnsureMachine()
    {
        if (_state is not null)
            return _state;

        EngineAnimTarget? target = EnsureTarget();
        if (target is null)
            return null;

        // The side's composition (the mods' layers and the core's HL2 layer),
        // built exactly as the server builds its copy; a hand-built player
        // without it has the HL2 layer alone.
        _state = new MultiplayerAnimState(
            target, AnimStateParams.Hl2Defaults(),
            _layers?.ChainFor(target) ?? AnimStateLayers.CoreOnly(target));
        return _state;
    }

    /// <summary>
    /// The target, built once. Deferred because the registry hands over
    /// <see cref="ClientStandaloneEntity.Entities"/> only after the
    /// constructor, and the target asks it the moment it is built.
    /// </summary>
    private EngineAnimTarget? EnsureTarget()
    {
        if (_target is not null)
            return _target;

        if (Entities is null)
            return null;

        _target = new EngineAnimTarget(
            Entities, Render, _models,
            () => State,
            EyeAngles,
            sequenceGroundSpeed: sequence => Render.SequenceGroundSpeed(Handle, sequence),
            sequenceCycle: sequence =>
                Render.SequenceState(Handle, out int current, out float cycle, out _, out _, out _)
                    && current == sequence ? cycle : float.NaN,
            arms: new EngineAnimTarget.Arms(
                // The client side's own arms, handed in rather than reached
                // from shared code (the side-audit rule): the pose dials,
                // dormancy, the layer pushes and the shell's rate/restart
                // hand-over all exist here.
                LookupPoseParameter: name => Render.LookupPoseParameter(Handle, name),
                SetPoseParameter: (index, value) => Render.SetPoseParameter(Handle, index, value),
                GetPoseParameter: index => Render.GetPoseParameter(Handle, index),
                IsDormant: () => Render.IsDormant(Handle),
                SetAnimLayer: (slot, sequence, weight, playbackRate, order, restart) =>
                    Render.SetAnimLayer(Handle, slot, sequence, weight, playbackRate, order, restart),
                PlaySequence: (sequence, playbackRate, restart) =>
                    Render.PlaySequence(Handle, sequence, playbackRate, restart)));

        _target.SetHandle(Handle);
        return _target;
    }

    /// <summary>
    /// Whose eyes this machine poses against: the viewer's own published view
    /// angles for the local player (the same read the prediction makes, from
    /// the replayed command), the body's own facing for everyone else.
    /// Re-decided per drive rather than once, because the local index is 0
    /// until the client is actually in a level, like the viewmodel's compare.
    /// </summary>
    private Vector EyeAngles()
    {
        int local = _screen?.LocalPlayerIndex ?? 0;

        if (_view is not null && local > 0 && Index == local)
            return new Vector(_view.Pitch, _view.Yaw, 0f);

        return new Vector(0f, Entities.GetAngles(Handle).Y, 0f);
    }
}

/// <summary>
/// <c>cs_cl_player_anim 0|1</c> — stop the player's animation policy writing,
/// so a hand-set pose stays put.
///
/// <para>
/// See <see cref="ClientSsPlayer.Enabled"/> for why this exists. In short:
/// with it on, <c>cs_playseq</c> and <c>cs_setpose</c> are the probes for
/// looking at a model by hand, and on the player they are silently defeated —
/// the animator writes on every networked update, which on a listen server is
/// most frames.
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

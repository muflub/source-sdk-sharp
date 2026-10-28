using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// The weapon machine's client half: the REPLAY (Phase 10, over Phase 9c4's
/// crossing) and the misprediction meter for the weapon and the viewmodel.
///
/// <para>
/// The same <see cref="WeaponLogic"/> the server runs, run once per REPLAYED
/// command over WORKING COPIES of the two declared states. The basis is the
/// decoded state the server last sent (phase 2 resets the copies to it, once
/// per frame, exactly as the player's native replay restores its pristine
/// blob), and every command's result is saved in a <see cref="PredictionRing"/>
/// whose slot the acknowledgement is judged against.
/// </para>
///
/// <para>
/// DELIBERATELY NOT WRITTEN INTO THE NATIVE BLOB. Stock can scribble
/// predictions over networked memory because PreEntityPacketReceived restores
/// the server's word before every packet decodes; without that restore, a
/// scribbled word the server never re-sends corrupts the client's view
/// permanently — the delta only carries what changed. The working copies keep
/// the prediction entirely on this side of that hazard, and the decoded state
/// stays the server's word by construction.
/// </para>
/// </summary>
public sealed class ClientArsenal : ILocalArsenal, WeaponEffects.IWeaponDrawStates
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;
    private readonly IGameClock _clock;
    private readonly IEngineHud _screen;

    private readonly WeaponNetState _weapon = new();
    private readonly ViewModelNetState _viewModel = new();
    private readonly WeaponLogic _machine;

    private readonly PredictionRing _weaponRing = new();
    private readonly PredictionRing _viewModelRing = new();

    // Scratch for ring saves, sized on first use. Reused: this runs per
    // replayed command per frame.
    private byte[] _weaponScratch = Array.Empty<byte>();
    private byte[] _viewModelScratch = Array.Empty<byte>();

    private ClientSsWeapon? _localWeapon;
    private ClientSsViewModel? _localViewModel;

    // The meter.
    private int _saves, _holds, _tolerated, _mispredicts, _noBasis;
    private int _lastMissCommand = -1;
    private string _lastMissFields = "";
    private int _pokeOffset = -1;
    private int _lastComparedDecode = -1;

    // The side's declared states (statics to zero), which name a mispredicted
    // field.
    private readonly NetStateCatalog _catalog;

    // The side's tables, injected beside the machine they feed; the networked
    // index resolves against THIS list, so it must be the one the server's
    // index was made against.
    private readonly WeaponCatalog _weapons;
    private readonly AmmoCatalog _ammo;

    // The client's view of the world, for the one replayed decision that asks
    // what is in front of the player (the SLAM's wall probe). Null in a test's
    // arsenal built without one, which answers that nothing is.
    private readonly IEngineWorldQuery? _world;

    // What the probe and the swing clip against besides the world.
    private readonly EntityCandidates _candidates = new();

    // The local player's entindex and the replayed command's aim, for that
    // probe: set by the basis reset and by each replayed command.
    private int _localIndex;
    private Vector _commandAngles;

    // The predicted effects (stock's client-side WeaponSound, FireBullets and
    // bludgeon impacts), and the command they are for. Null in a composition
    // without them: the replay then predicts state and plays nothing.
    private readonly IPredictedWeaponFx? _fx;
    private PredictedShot _shot;
    private bool _hasEye;

    public ClientArsenal(ILog log, IClientStandalones standalones, IEngineEntityRender render,
                         IGameClock clock, IEngineHud screen, NetStateCatalog catalog,
                         WeaponCatalog? weapons = null, AmmoCatalog? ammo = null,
                         WeaponBehaviours? behaviours = null, IEngineWorldQuery? world = null,
                         IPredictedWeaponFx? fx = null)
    {
        _fx = fx;
        _log = log;
        _world = world;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _standalones = standalones;
        _render = render;
        _clock = clock;
        _screen = screen;

        // THE SAME TABLES THE SERVER RESOLVED. The replay's machine reads the
        // row the networked WeaponTypeId indexes and asks the row's behaviour
        // key the same questions the server's machine asked, so this side must
        // hold the identical table and the identical behaviour set — which is
        // why both arrive injected from the side's composition rather than
        // being reached through a static the client could fill differently. The
        // defaults are the stock tables, which is what a mod-less game has.
        _weapons = weapons ?? WeaponCatalog.Stock();
        _ammo = ammo ?? AmmoCatalog.Stock();
        _machine = new WeaponLogic(new ClientWeaponArms(this), _weapons,
                                   behaviours ?? WeaponBehaviours.Stock());

        _weapon.BindLocal();
        _viewModel.BindLocal();
    }

    /// <summary>
    /// One crossing from the native replay. Phase 2 is the frame's basis
    /// reset (cmd carries the acknowledged command number); phase 1 is one
    /// replayed command, after its movement.
    /// </summary>
    public void OnPlayerCommand(EntityHandle player, in UserCmd cmd, int phase, bool firstTime)
        => OnPlayerCommand(player, in cmd, phase, firstTime, _clock.Now);

    /// <summary>
    /// The replay's form: <paramref name="commandTime"/> is the replayed
    /// command's own clock, its tick base times the tick interval -- what the
    /// server's run of the same command read as curtime. The engine's curtime
    /// here is the FRAME's, the same for every command a frame replays.
    /// </summary>
    public void OnPlayerCommand(EntityHandle player, in UserCmd cmd, int phase, bool firstTime,
                                float commandTime)
        => OnPlayerCommand(player, in cmd, phase, firstTime, commandTime, eye: null);

    /// <summary>
    /// The replay's form with the predicted EYE (the command's origin plus
    /// the view offset): what the first prediction's shot traces from. A
    /// replay that knows no eye predicts state and fires no predicted shot.
    /// </summary>
    public void OnPlayerCommand(EntityHandle player, in UserCmd cmd, int phase, bool firstTime,
                                float commandTime, Vector? eye)
    {
        if (phase == 2)
        {
            ResetBasis(player, cmd.CommandNumber);
            return;
        }

        if (phase != 1 || _localWeapon is null || _localViewModel is null)
            return;

        if (_weapon.WeaponTypeId < 0)
            return;

        // The shot the arms fire, if this run fires one: from the predicted
        // eye along the command's angles, spread from the command's seed --
        // the server's run of the same command reads the same three.
        _shot = new PredictedShot(player, (int)(player.Value & 0xFFFF), eye ?? default,
                                  new Vector(cmd.Pitch, cmd.Yaw, cmd.Roll),
                                  UserCmdCodec.RandomSeedFor(cmd.CommandNumber));

        // The clock is the command's own, as the server's run of it read. Read
        // from the engine instead it was the frame's curtime, and every timer
        // the machine sets (TimeWeaponIdle, NextPrimaryAttack) mispredicted.
        _commandAngles = new Vector(cmd.Pitch, cmd.Yaw, cmd.Roll);


        // effectsLive is the FIRST prediction only, stock's
        // IsFirstTimePredicted: a replay of a command already predicted plays
        // nothing again.
        _hasEye = eye.HasValue;
        _machine.RunCommand(_weapon, _viewModel, cmd.Buttons,
                            commandTime, effectsLive: firstTime);

        SaveCommand(cmd.CommandNumber);

        // The PREDICTED sequence reaches the renderer here — this is what
        // makes the fire animation start the frame the trigger is pressed
        // rather than a round trip later. Present is edge-triggered, so the
        // replayed commands that changed nothing cost nothing, and the
        // decode's own push of the same (sequence, parity) is a no-op when
        // the prediction held.
        _localViewModel.Present(_viewModel.Sequence, _viewModel.AnimationParity,
                                _viewModel.PlaybackRate);
        _localViewModel.ShowSkin(_viewModel.Skin);
    }

    //-------------------------------------------------------------------------
    // The basis, and the compare it carries.
    //-------------------------------------------------------------------------

    /// <summary>The working weapon state the replay runs, for tests.</summary>
    internal WeaponNetState WorkingWeapon => _weapon;

    /// <inheritdoc/>
    /// <remarks>
    /// The working copy, for the local player's gun, once the replay has reset
    /// its basis to the latest decode: with prediction off, or before the
    /// first replay over a new decode, the copy is behind the server's word.
    /// </remarks>
    public WeaponNetState? PredictedStateOf(ClientSsWeapon weapon)
        => ReferenceEquals(weapon, _localWeapon) && _localViewModel is not null
           && _lastComparedDecode == _localWeapon.Decodes + _localViewModel.Decodes
            ? _weapon
            : null;

    private void ResetBasis(EntityHandle player, int acknowledged)
    {
        // Players hold 1..maxplayers identically on both sides; the low bits
        // of a client handle are its entity-list entry.
        int localIndex = (int)(player.Value & 0xFFFF);
        _localIndex = localIndex;

        FindLocalPair(localIndex);

        if (_localWeapon is null || _localViewModel is null)
            return;

        // A fresh decode means the decoded states hold the server's word and
        // the acknowledged command's slot has something to be judged against.
        int decode = _localWeapon.Decodes + _localViewModel.Decodes;

        if (decode != _lastComparedDecode)
        {
            _lastComparedDecode = decode;
            Compare(acknowledged);
        }

        // The reset itself: the working copies go back to the server's word,
        // so a frame with no new packet re-predicts the same commands from the
        // same start and lands on the same answer.
        ResizeRings();
        CopyState(_localWeapon.State, _weapon);
        CopyState(_localViewModel.State, _viewModel);
    }

    private void Compare(int acknowledged)
    {
        CompareOne(_weaponRing, _localWeapon!.State, EntityNames.SsWeapon, acknowledged);
        CompareOne(_viewModelRing, _localViewModel!.State, EntityNames.SsViewModel, acknowledged);
    }

    private void CompareOne(PredictionRing ring, NetState arrivedState, string className, int acknowledged)
    {
        ReadOnlySpan<byte> predicted = ring.CommandBytes(acknowledged);

        if (predicted.IsEmpty)
        {
            _noBasis++;
            return;
        }

        byte[] arrived = arrivedState.Snapshot();

        if (arrived.Length != predicted.Length)
        {
            _noBasis++;
            return;
        }

        // The engine block comes from the arrival on both sides, as the player
        // compare does it: the server moves a carried weapon and viewmodel to
        // their owner every frame and nothing here predicts that, so the
        // origin's bytes read as "(padding@24..)" mispredictions on every
        // landing.
        byte[] judged = PredictionJudge.OverlayDeclared(predicted, arrived,
            PredictionJudge.DeclaredStart(arrivedState.GetType(), arrived.Length));

        if (judged.AsSpan().SequenceEqual(arrived))
        {
            _holds++;
            ClearWitnessedPoke(ring, acknowledged, className);
            return;
        }

        ClearWitnessedPoke(ring, acknowledged, className);

        int standing = PredictionJudge.Judge(_catalog, NetSchema.TypeIdOf(className),
            judged, arrived, out string fields, out _);

        if (standing == 0)
        {
            _holds++;
            _tolerated++;
            return;
        }

        _mispredicts++;
        _lastMissCommand = acknowledged;
        _lastMissFields = $"{className}: {(fields.Length > 0 ? fields : "(unnamed)")}";

        // REPORTED A FEW TIMES AND THEN COUNTED SILENTLY. A misprediction that
        // repeats does so every command -- twenty times a second, per weapon --
        // and it was drowning the console for anyone looking at anything else.
        // Nothing is lost: _mispredicts keeps the tally and _lastMissFields
        // keeps the most recent one, which is what the report reads.
        if (_mispredicts <= QuietAfter)
        {
            _log.Msg($"[SourceSharp] weapon prediction: MISPREDICTION on '{className}' at command " +
                    $"{acknowledged} — {standing} byte(s) stand: {fields}" +
                    (_mispredicts == QuietAfter
                        ? " (quiet from here; the tally keeps counting)"
                        : "") + "\n");
        }
    }

    /// <summary>
    /// How many mispredictions are reported before the log goes quiet. The
    /// COUNTING never stops — see the note where this is read.
    /// </summary>
    public const int QuietAfter = 3;

    private void ClearWitnessedPoke(PredictionRing ring, int acknowledged, string className)
    {
        if (_pokeOffset < 0 || ring.CommandBytes(acknowledged).IsEmpty)
            return;

        _log.Msg($"[SourceSharp] weapon prediction: poke at offset {_pokeOffset} on " +
                   $"'{className}' was seen by the compare — poke cleared\n");
        _pokeOffset = -1;
    }

    private void SaveCommand(int command)
    {
        if (_weaponScratch.Length > 0 && _weapon.CopyTo(_weaponScratch) == _weaponScratch.Length)
        {
            if (_weaponRing.SaveCommand(command, _weaponScratch))
            {
                _saves++;

                // A poke in flight damages every slot as it is saved, so the
                // next ack has a provably wrong basis — persistent until a
                // compare witnesses it (9c1's one-shot-poke lesson).
                if (_pokeOffset >= 0)
                    _weaponRing.CorruptCommand(command, _pokeOffset);
            }
        }

        if (_viewModelScratch.Length > 0 && _viewModel.CopyTo(_viewModelScratch) == _viewModelScratch.Length)
            _viewModelRing.SaveCommand(command, _viewModelScratch);
    }

    //-------------------------------------------------------------------------
    // Bookkeeping.
    //-------------------------------------------------------------------------

    private void FindLocalPair(int localIndex)
    {
        // Re-resolved per frame, because either entity can arrive, die, or
        // change owners between any two frames.
        //
        // TWO CLASSNAME LOOKUPS AND NOT ONE WALK — plan_entity.md §3.5's
        // find-by-class, lane entlist-client. The pair being looked for is two
        // named classes; walking every live entity to find them asked the
        // directory nothing and cost the population once per frame.
        _localWeapon = null;
        _localViewModel = null;

        foreach (var entity in _standalones.OfClass(EntityNames.SsWeapon))
        {
            if (entity is ClientSsWeapon weapon && weapon.State.OwnerIndex == localIndex)
                _localWeapon = weapon;
        }

        foreach (var entity in _standalones.OfClass(EntityNames.SsViewModel))
        {
            if (entity is ClientSsViewModel viewModel && viewModel.State.OwnerIndex == localIndex)
                _localViewModel = viewModel;
        }
    }

    private void ResizeRings()
    {
        int weaponBytes = _localWeapon!.State.Snapshot().Length;
        int viewModelBytes = _localViewModel!.State.Snapshot().Length;

        if (_weaponRing.BlobBytes != weaponBytes)
        {
            _weaponRing.Init(weaponBytes);
            _weaponScratch = new byte[weaponBytes];
        }

        if (_viewModelRing.BlobBytes != viewModelBytes)
        {
            _viewModelRing.Init(viewModelBytes);
            _viewModelScratch = new byte[viewModelBytes];
        }
    }

    private static void CopyState(NetState from, NetState to)
    {
        // Snapshot allocates; this runs once per frame per state, and the
        // decoded side has no span accessor. Worth a CopyTo pair if profiling
        // ever asks.
        byte[] bytes = from.Snapshot();

        if (bytes.Length > 0)
            to.Decode(bytes);
    }

    //-------------------------------------------------------------------------
    // The HUD's feed (ILocalArsenal). The DECODED state on purpose: it is the
    // server's word, held byte-for-byte by the measured prediction almost
    // every frame anyway, and unlike the replay's working copies it is valid
    // whether or not a replay ran this frame (prediction can be off).
    //-------------------------------------------------------------------------

    public bool TryGetSecondary(out int current, out int max)
    {
        (current, max) = (-1, 0);
        if (_localWeapon is null || _localWeapon.State.WeaponTypeId < 0)
            return false;
        if (_weapons.ByIndex(_localWeapon.State.WeaponTypeId) is not { } type
            || type.SecondaryAmmoName.Length == 0 || _ammo.For(type.SecondaryAmmoName) is not { } ammo)
            return false;
        (current, max) = (_localWeapon.State.SecondaryReserve, ammo.MaxCarry);
        return true;
    }

    public bool TryGetActive(out string name, out int clip, out int reserve)
    {
        name = "";
        clip = -1;
        reserve = -1;

        int local = _screen.LocalPlayerIndex;

        if (local <= 0)
            return false;

        // Re-resolved per ask rather than trusted: the pair the replay found
        // can have been removed or re-owned between frames, and the replay
        // only looks while prediction is running at all.
        FindLocalPair(local);

        if (_localWeapon is null)
            return false;

        var state = _localWeapon.State;

        if (state.WeaponTypeId < 0)
            return false;

        var type = _weapons.ByIndex(state.WeaponTypeId);
        name = type?.Name ?? $"#{state.WeaponTypeId}";
        clip = state.Clip;
        // A weapon that spends no ammunition (the crowbar) has no pool, whatever
        // the networked reserve says (0): -1, as a weapon with no clip has no clip.
        reserve = type is { AmmoName.Length: 0 } ? -1 : state.Reserve;
        return true;
    }

    //-------------------------------------------------------------------------
    // The instruments.
    //-------------------------------------------------------------------------

    public string Report()
    {
        string line =
            $"[SourceSharp] weapon prediction: pair {(_localWeapon is not null && _localViewModel is not null ? "present" : "absent")}, " +
            $"{_weaponRing.BlobBytes}+{_viewModelRing.BlobBytes} byte(s) ringed — {_saves} save(s), " +
            $"{_holds} hold(s) ({_tolerated} tolerated), {_mispredicts} misprediction(s), {_noBasis} no-basis";

        if (_lastMissCommand >= 0)
            line += $"\n  last mismatch: command {_lastMissCommand}: {_lastMissFields}";

        return line;
    }

    /// <summary>
    /// The sequence an animation kind resolves to on the local viewmodel's
    /// current model — the ONE lookup the replay's arms fire with.
    /// </summary>
    //-------------------------------------------------------------------------
    // The physcannon's claws (C_WeaponPhysCannon::ClientThink's
    // UpdateElementPosition): once a frame, the local viewmodel's "active"
    // pose parameter follows the gun's networked m_bOpen.
    //-------------------------------------------------------------------------

    private readonly PhysCannonElements _claws = new();

    /// <summary>The claws' last value, for the facts and the reports.</summary>
    public float ClawPosition { get; private set; }

    public void Frame()
    {
        int local = _screen.LocalPlayerIndex;

        if (local <= 0)
            return;

        FindLocalPair(local);

        if (_localWeapon is null || _localViewModel is null
            || _weapons.ByIndex(_localWeapon.State.WeaponTypeId) is not { Behaviour: PhysCannonBehaviour.Key })
        {
            _claws.Reset();
            return;
        }

        ClawPosition = _claws.Advance(_localWeapon.State.CannonOpen, _clock.Now);

        int pose = _render.LookupPoseParameter(_localViewModel.Handle, PhysCannonElements.PoseParameter);
        if (pose >= 0)
            _render.SetPoseParameter(_localViewModel.Handle, pose, ClawPosition);
    }

    internal int ResolveSequence(WeaponAnim anim)
        => _localViewModel is null
            ? -1
            : WeaponAnimNames.Resolve(_render, _localViewModel.Handle, anim);

    internal int ResolveActivity(string activity)
        => _localViewModel is null ? -1 : _render.LookupSequenceByActivity(_localViewModel.Handle, activity);

    internal float DurationOf(int sequence)
        => sequence < 0 || _localViewModel is null ? 0f : _render.SequenceDuration(_localViewModel.Handle, sequence);

    /// <summary>
    /// Where this command's probes look from: the eye the prediction ran the
    /// command's movement to (stock's Weapon_ShootPosition, which the server's
    /// run of the same command reads), or, for a run told none, the DECODED
    /// origin plus the view offset -- the server's last word, a few units
    /// behind a player still walking.
    /// </summary>
    private bool EyeOfThisCommand(out Vector eye)
    {
        if (_hasEye)
        {
            eye = _shot.Eye;
            return true;
        }

        if (_standalones.GetByIndex(_localIndex) is ClientSsPlayer player)
        {
            eye = player.State.Origin + player.State.ViewOffset;
            return true;
        }

        eye = default;
        return false;
    }

    /// <summary>The SLAM's wall probe, from <see cref="EyeOfThisCommand"/> along the command's angles.</summary>
    internal WallProbe ProbeWall(float reach)
    {
        if (_world is null || !EyeOfThisCommand(out Vector eye))
            return WallProbe.Nothing;

        Vector forward = Angles.Forward(_commandAngles);

        var trace = LineAhead(eye, eye + forward * reach, TraceMask.Solid);

        if (!trace.Hit)
            return WallProbe.Nothing;

        return _standalones.GetByIndex(trace.EntityIndex) is ClientSsPlayer
            ? WallProbe.Character
            : WallProbe.Surface;
    }

    /// <summary>
    /// CBaseHL2MPBludgeonWeapon::Swing's aim, from <see cref="EyeOfThisCommand"/>,
    /// for the predicted hit-or-miss animation, as the server's swing aims:
    /// the ray; then the swing's hull, backed off by its corner radius, kept
    /// only if the swinger roughly faces what it found (the world's origin is
    /// the map's).
    /// </summary>
    internal bool MeleeConnects(float range)
    {
        if (_world is null || !EyeOfThisCommand(out Vector eye))
            return true;

        Vector forward = Angles.Forward(_commandAngles);
        Vector end = eye + forward * range;

        if (LineAhead(eye, end, TraceMask.ShotHull).Hit)
            return true;

        var hull = _world.ClientTraceHullEntities(eye, end - forward * BludgeonSwing.HullRadius,
                                                  BludgeonSwing.Hull * -1f, BludgeonSwing.Hull,
                                                  TraceMask.ShotHull, _localIndex,
                                                  _candidates.AllBut(_localIndex));
        if (!hull.Hit)
            return false;

        Vector target = hull.EntityIndex > 0 ? hull.EntityOrigin : Vector.Zero;
        return (target - eye).Normalized.Dot(forward) >= BludgeonSwing.FacingDot;
    }

    /// <summary>
    /// A line from the local player as the server's would stop: the world's
    /// trace, then the entities before its stop (no client trace sees the
    /// shells, <see cref="EntityCandidates"/>).
    /// </summary>
    private ClientTraceResult LineAhead(Vector from, Vector to, TraceMask mask)
    {
        var world = _world!.ClientTraceLine(from, to, mask, TraceFilter.Simple, _localIndex);
        var entity = _world.ClientClipRayToEntities(from, world.Hit ? world.End : to, mask,
                                                    _candidates.AllBut(_localIndex));
        return entity.Hit ? entity : world;
    }

    /// <summary>Arms the poke: every future weapon-ring save is damaged at the
    /// offset until a compare witnesses it.</summary>
    public bool Poke(int offset)
    {
        if (_weaponRing.BlobBytes == 0 || offset < 0 || offset >= _weaponRing.BlobBytes)
            return false;

        _pokeOffset = offset;
        return true;
    }

    //-------------------------------------------------------------------------
    // The arms: what the machine's decisions do HERE. The first prediction of
    // a command plays the weapon's sounds and its bullets' and swings'
    // impacts (IPredictedWeaponFx), as stock's client does; what flies is the
    // server's. The two asks that write PREDICTED STATE answer from the same
    // model the server read.
    //-------------------------------------------------------------------------

    private sealed class ClientWeaponArms : IWeaponArms
    {
        private readonly ClientArsenal _owner;

        public ClientWeaponArms(ClientArsenal owner) => _owner = owner;

        public void Sound(string soundScript) => _owner._fx?.Sound(in _owner._shot, soundScript);

        public void ClientSound(string soundScript) => Sound(soundScript);

        // A server-only path's sound (an animation event's): not predicted.
        public void ServerSound(string soundScript)
        {
        }

        // No shot without an eye to fire it from (a replay told none).
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees)
        {
            if (_owner._hasEye)
                _owner._fx?.FireBullets(in _owner._shot, in type, pellets, spreadDegrees);
        }

        public void MeleeSwing(in WeaponType type)
        {
            if (_owner._hasEye)
                _owner._fx?.MeleeSwing(in _owner._shot, in type);
        }

        // A projectile is the server's entity; its launch sound is Sound's.
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }

        public int LookupSequence(WeaponAnim anim) => _owner.ResolveSequence(anim);

        // The SLAM's three predicted asks; its spawns stay the server's.
        public int LookupActivity(string activity) => _owner.ResolveActivity(activity);
        public float SequenceDuration(int sequence) => _owner.DurationOf(sequence);
        public WallProbe ProbeWall(float reach) => _owner.ProbeWall(reach);
        public bool MeleeConnects(in WeaponType type) => _owner.MeleeConnects(type.MeleeRange);

        public float AnimDuration(WeaponAnim anim)
        {
            int sequence = LookupSequence(anim);

            if (sequence < 0 || _owner._localViewModel is null)
                return 0f;

            return _owner._render.SequenceDuration(_owner._localViewModel.Handle, sequence);
        }
    }
}

/// <summary>
/// The console door onto the weapon meter — cs_cl_player_predict_report's twin
/// for the pair the player carries.
/// </summary>
[ConCommand("cs_cl_weapon_predict_report",
            Help = "What the weapon and viewmodel prediction rings have seen")]
public sealed class WeaponPredictReportCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly ClientArsenal _arsenal;

    public WeaponPredictReportCommand(ILog log, ClientArsenal arsenal) 

    {

        _log = log;

        _arsenal = arsenal;

    }

    public void Execute(string args) => _log.Msg(_arsenal.Report() + "\n");
}

/// <summary>
/// The provocation that proves the weapon meter can go red: corrupts the
/// weapon ring's saves at one offset until a compare witnesses it.
/// </summary>
[ConCommand("cs_cl_weapon_predict_poke",
            Help = "cs_cl_weapon_predict_poke [offset] — corrupt the weapon's replayed slots until a compare sees it")]
public sealed class WeaponPredictPokeCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly ClientArsenal _arsenal;

    public WeaponPredictPokeCommand(ILog log, ClientArsenal arsenal) 

    {

        _log = log;

        _arsenal = arsenal;

    }

    public void Execute(string args)
    {
        int offset = int.TryParse(args.Trim(), out int parsed) ? parsed : 0;

        _log.Msg(_arsenal.Poke(offset)
            ? $"[SourceSharp] weapon prediction: poking offset {offset} until a compare sees it\n"
            : "[SourceSharp] weapon prediction: nothing ringed at that offset\n");
    }
}

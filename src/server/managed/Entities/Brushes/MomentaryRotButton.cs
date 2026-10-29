//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A wheel. Not a button, whatever the classname says: what a map wants from
/// this is not "it was pressed" but WHERE IT IS, continuously, so a valve can
/// drive a door open by the same fraction the player has turned it.
///
/// That is the whole of what makes it different from func_rot_button, and it is
/// why this is the first brush here that thinks every tick: the Position output
/// carries a number between nought and one, and the number is only useful while
/// it is changing. The shell turns the brush and reports arrival; the fraction
/// is read back off the angles on the way, because the angles are where the
/// truth is once something has blocked or shoved the wheel.
///
/// Position one is "closed" and nought is "open". That reads backwards and it is
/// stock's, both in <c>OutputMovementComplete</c> and in <c>UpdateSelf</c> — a
/// mapper wiring OnFullyClosed to a door expects the wheel wound fully on.
/// </summary>
[ScriptedEntity(EntityNames.MomentaryRotButton, ScriptedEntityKind.Brush)]
public sealed class MomentaryRotButton : ScriptedEntity
{
    public MomentaryRotButton(IGameClock clock, IEnginePrecache precache, IEngineSound sound)
    {
        Clock = clock;
        Precache = precache;
        Sound = sound;
    }

    private readonly IEnginePrecache Precache;

    private readonly IEngineSound Sound;

    /// <summary>
    /// m_sNoise: the <c>sounds</c> key's "Buttons.sndN", set only for a wheel
    /// the player turns (SF_BUTTON_USE_ACTIVATES), as stock's Spawn does. Its
    /// Spawn never runs CBaseButton::Precache, so the lock sounds are never set
    /// and a locked wheel is silent, here as there.
    /// </summary>
    private string _noise = string.Empty;

    /// <summary>what the server thinks the time is: OnUseLocked's half-second.</summary>
    private readonly IGameClock Clock;

    /// <summary>Stock's Spawn makes it SOLID_VPHYSICS.</summary>
    protected override SolidType BrushSolidType => SolidType.VPhysics;

    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;


    /// <summary>moving a brush, and reporting when it arrives.</summary>
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>Where the wheel is, nought to one. The reason this entity exists.</summary>
    public readonly Output Position = new();

    public readonly Output OnPressed = new();
    public readonly Output OnUnpressed = new();
    public readonly Output OnFullyOpen = new();
    public readonly Output OnFullyClosed = new();
    public readonly Output OnReachedPosition = new();
    public readonly Output OnUseLocked = new();

    /// <summary>What a map may set on this entity. See <see cref="MomentaryRotButtonVars"/>.</summary>
    [ScriptVars] public MomentaryRotButtonVars Vars = new();

    /// <summary>
    /// momentary_rot_button's own spawnflags, and only its own. The two axis
    /// bits a wheel also reads are NOT here: those are 64 and 128, they are
    /// shared with func_platrot and func_rotbutton through
    /// <c>CBaseToggle::AxisDir</c>, and they live in
    /// <see cref="RotationAxisFlag"/> for that reason.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000_0000_0000,

        NotSolid = 0b0000_0000_0000_0001,

        /// <summary>Stays wound on rather than winding back. Suppresses the auto-return.</summary>
        Toggle = 0b0000_0000_0010_0000,

        /// <summary>SF_BUTTON_USE_ACTIVATES: a player's +USE turns it, for as long as it is held.</summary>
        UseActivates = 0b0000_0100_0000_0000,  // 0x400

        Locked = 0b0000_1000_0000_0000,  // 0x800

        /// <summary>SF_BUTTON_JIGGLE_ON_USE_LOCKED: a locked wheel jiggles two degrees when used.</summary>
        JiggleOnUseLocked = 0b0010_0000_0000_0000,  // 0x2000
    }

    /// <summary>
    /// Which of the three angles a rotation moves, once the axis flags have been
    /// read. Stock carries this around as a bare 0, 1 or 2 index, and 0 meaning
    /// PITCH rather than "none" is exactly the sort of index that gets a default
    /// arm wrong.
    /// </summary>
    internal enum RotationAxis
    {
        Pitch = 0,
        Yaw = 1,
        Roll = 2,
    }

    /// <summary>What started the movement now under way. Decides what arrival means.</summary>
    private enum Driver { Still, Map }

    /// <summary>Stock's m_pfnMoveDone for the turning a player does: what the move-done timer runs.</summary>
    private enum UseMove { None, Use, Return }

    /// <summary>The 0.1 s move-done timer each use re-arms (stock's SetMoveDoneTime).</summary>
    private const string MoveDoneContext = "MoveDone";

    private const string JiggleContext = "Jiggle";

    private Vector _startAngles;

    /// <summary>
    /// How far the wheel turns end to end, taken from the map's key ONCE, in
    /// <see cref="OnSpawn"/>, which a hot reload does not run again -- so it is
    /// <c>[SaveField]</c>. Uncarried, the rebuilt instance measured its whole
    /// travel as nothing and the fraction the Position output carried after a
    /// <c>cs_sv_reload</c> was nonsense (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private float _travel;

    private Driver _driver;

    /// <summary>Where it has been told to end up, as a fraction.</summary>
    private float _target;

    /// <summary>The last fraction the Position output carried. Stops a per-tick output repeating itself.</summary>
    private float _reported;

    private bool _locked;
    private bool _disabled;
    private bool _updateTarget = true;

    /// <summary>m_lastUsed: held since the last move-done, so a press does not reverse it again.</summary>
    private bool _lastUsed;

    private UseMove _moveDone;

    /// <summary>m_flUseLockedTime: OnUseLocked says so at most every half second.</summary>
    private float _useLockedTime;

    /// <summary>The locked jiggle's next step, and how far it jiggles.</summary>
    private int _jiggleStep;
    private float _jiggleDistance;

    /// <summary>
    /// CBaseButton's and CMomentaryRotButton's ObjectCaps: a wheel +USE turns
    /// is used every command the key is held, and found in the radius.
    /// </summary>
    public override UseCaps ObjectCaps
        => base.ObjectCaps | (Flags.HasFlag(SpawnFlag.UseActivates)
            ? UseCaps.ImpulseUse | UseCaps.ContinuousUse | UseCaps.UseInRadius
            : UseCaps.None);

    public override void OnSpawn()
    {
        SetVisible(true);

        SetSolidity(Flags.HasFlag(SpawnFlag.NotSolid)
            ? BrushSolidity.None
            : BrushSolidity.Solid);
        // CreateVPhysics: a shadow that follows it, which props meet.
        InitShadowPhysics();

        if (Vars.Speed <= 0f)
            Vars.Speed = 100f;

        Vars.Direction = Vars.Direction < 0 ? -1 : 1;
        Vars.StartPosition = Math.Clamp(Vars.StartPosition, 0f, 1f);
        _travel = Vars.Distance;

        // Where the mapper drew it is somewhere ALONG the travel, not at either
        // end of it, so both ends are derived from the drawn angles rather than
        // the other way round.
        _startAngles = OffsetAngles(SpawnAngles, (RotationAxisFlag)SpawnFlags, -_travel * Vars.StartPosition);

        _target = Vars.StartPosition;

        // Nought and one can only be left, so the first press has to go inward
        // from whichever it is. Stock forces the same two cases in Spawn.
        if (Vars.StartPosition <= 0f)
            Vars.Direction = -1;
        else if (Vars.StartPosition >= 1f)
            Vars.Direction = 1;

        _locked = Flags.HasFlag(SpawnFlag.Locked);

        if (Flags.HasFlag(SpawnFlag.UseActivates) && Vars.Sounds != 0)
        {
            _noise = BaseButton.ButtonSound(Vars.Sounds);
            Precache.PrecacheSound(_noise);
        }

        // Nothing is fired here, because stock fires nothing here either: it
        // reports nought at spawn, which its own "has the value changed" guard
        // then swallows. A wheel drawn half-wound therefore says so the first
        // time it moves.
        _reported = Vars.StartPosition;
    }

    //-------------------------------------------------------------------------
    // The arithmetic, which is the interesting half and is kept pure so a test
    // can ask it directly — the engine is the only thing that knows a live
    // brush's angles, and a test has no engine.
    //-------------------------------------------------------------------------

    /// <summary>Which euler component this wheel turns about, as the map's flags say.</summary>
    internal static RotationAxis AxisFor(RotationAxisFlag spawnFlags)
        => spawnFlags.HasFlag(RotationAxisFlag.Roll) ? RotationAxis.Roll
         : spawnFlags.HasFlag(RotationAxisFlag.Pitch) ? RotationAxis.Pitch
         : RotationAxis.Yaw;

    /// <summary>An orientation turned by so many degrees about the wheel's axis.</summary>
    internal static Vector OffsetAngles(Vector angles, RotationAxisFlag spawnFlags, float degrees)
        => AxisFor(spawnFlags) switch
        {
            RotationAxis.Pitch => new Vector(angles.X + degrees, angles.Y, angles.Z),
            RotationAxis.Roll => new Vector(angles.X, angles.Y, angles.Z + degrees),
            _ => new Vector(angles.X, angles.Y + degrees, angles.Z),
        };

    /// <summary>
    /// Where a set of angles sits along the travel, nought to one.
    ///
    /// Dividing by a SIGNED travel is what handles a negative distance: stock
    /// flips its axis vector and takes the absolute value, then multiplies the
    /// result by minus one again, which is the same thing said three times.
    /// </summary>
    internal static float PositionFor(RotationAxisFlag spawnFlags, Vector angles, Vector start, float travel)
    {
        if (travel == 0f)
            return 0f;

        RotationAxis axis = AxisFor(spawnFlags);
        float delta = Component(angles, axis) - Component(start, axis);

        return Math.Clamp(delta / travel, 0f, 1f);
    }

    private static float Component(Vector angles, RotationAxis axis)
        => axis switch
        {
            RotationAxis.Pitch => angles.X,
            RotationAxis.Roll => angles.Z,
            _ => angles.Y,
        };

    /// <summary>Where the wheel actually is, which after a block is not where it was sent.</summary>
    private float CurrentPosition
        => PositionFor((RotationAxisFlag)SpawnFlags, Entities.GetAngles(Handle), _startAngles, _travel);

    //-------------------------------------------------------------------------
    // Inputs.
    //-------------------------------------------------------------------------

    /// <summary>m_vecMoveAng: the unit turn about the wheel's axis, along its travel.</summary>
    private Vector MoveAngles => OffsetAngles(Vector.Zero, (RotationAxisFlag)SpawnFlags, _travel < 0f ? -1f : 1f);

    private Vector EndAngles => OffsetAngles(_startAngles, (RotationAxisFlag)SpawnFlags, _travel);

    [ScriptedEvent] private void OnSetPosition(float fraction) => Travel(fraction, Vars.Speed);

    /// <summary>InputSetPositionImmediately: there at once, and nothing told.</summary>
    [ScriptedEvent] private void OnSetPositionImmediately(float fraction)
    {
        _target = Math.Clamp(fraction, 0f, 1f);
        SetAngles(OffsetAngles(_startAngles, (RotationAxisFlag)SpawnFlags, _travel * _target));
    }

    // Underscored, so they cannot be method names with the "On" stripped. Stock
    // uses them to jiggle a locked wheel without dragging whatever it drives
    // along with it, which is the only reason a wheel would move and not report.
    [ScriptedEvent("_DisableUpdateTarget")] private void OnUpdateTargetDisabled() => _updateTarget = false;
    [ScriptedEvent("_EnableUpdateTarget")] private void OnUpdateTargetEnabled() => _updateTarget = true;

    [ScriptedEvent] private void OnEnable() => _disabled = false;
    [ScriptedEvent] private void OnDisable() => _disabled = true;

    /// <summary>CMomentaryRotButton::Lock: stopped where it stands, nothing pending.</summary>
    [ScriptedEvent] private void OnLock()
    {
        _locked = true;

        Movement.StopMoving(Handle);
        Spin(Vector.Zero);
        StopThinking(MoveDoneContext);
        _moveDone = UseMove.None;
        StopThinking();
        _driver = Driver.Still;
    }

    /// <summary>CMomentaryRotButton::Unlock: the auto-return a tenth of a second on.</summary>
    [ScriptedEvent] private void OnUnlock()
    {
        _locked = false;

        _moveDone = UseMove.Return;
        SetContextThink(MoveDoneContext, 0.1f);
    }

    /// <summary>
    /// CMomentaryRotButton::Use, every command +USE is held: the first use
    /// since it last stopped reverses the direction and presses it; each use
    /// turns it on at its speed for another tenth of a second.
    /// </summary>
    [ScriptedEvent("Use")] private void OnUsed()
    {
        if (_disabled)
            return;

        if (_locked)
        {
            if (UseLocked() && Flags.HasFlag(SpawnFlag.JiggleOnUseLocked))
                StartJiggle();

            return;
        }

        // Reverse our direction and play movement sound every time the player
        // pauses between uses.
        if (!_lastUsed)
        {
            Vars.Direction = -Vars.Direction;

            //Alert that we've been pressed
            OnPressed.Fire();
        }

        bool playSound = !_lastUsed;
        _lastUsed = true;

        UpdateSelf(CurrentPosition, playSound);

        // Think every frame while we are moving, unless a think is pending.
        if (NextThinkDelay() < 0f)
            SetNextThink(0f);
    }

    /// <summary>
    /// CBaseButton::OnUseLocked: OnUseLocked fires, and answers true, at most
    /// once every half second.
    /// </summary>
    private bool UseLocked()
    {
        if (Clock.Now > _useLockedTime)
        {
            OnUseLocked.Fire();
            _useLockedTime = Clock.Now + 0.5f;
            return true;
        }

        return false;
    }

    /// <summary>The jiggle's four SetPositions as stock queues them: when, and how much of the two degrees.</summary>
    private const int JiggleSteps = 4;

    private static (float Delay, float Scale) Jiggle(int step) => step switch
    {
        0 => (0.01f, 1f),
        1 => (0.1f, 0f),
        2 => (0.2f, 0.5f),
        _ => (0.3f, 0f),
    };

    /// <summary>Jiggle two degrees: out, back, half out, back, with the target left alone.</summary>
    private void StartJiggle()
    {
        _jiggleDistance = 2f / MathF.Abs(_travel);
        _updateTarget = false;
        _jiggleStep = 0;
        SetContextThink(JiggleContext, Jiggle(0).Delay);
    }

    private void JiggleThink()
    {
        if (_jiggleStep < JiggleSteps)
        {
            var (delay, scale) = Jiggle(_jiggleStep);
            Travel(scale * _jiggleDistance, Vars.Speed);
            _jiggleStep++;

            // Must be last! And must be late enough to cover the settling time.
            float next = _jiggleStep < JiggleSteps ? Jiggle(_jiggleStep).Delay : 0.5f;
            SetContextThink(JiggleContext, next - delay);
            return;
        }

        _updateTarget = true;
    }

    /// <summary>
    /// CMomentaryRotButton::UpdateSelf: turning on for another tenth of a
    /// second, or stopped and snapped at the end it has reached.
    /// </summary>
    private void UpdateSelf(float value, bool playSound)
    {
        // A map's SetPosition under way gives way to the player.
        if (_driver != Driver.Still)
        {
            Movement.StopMoving(Handle);
            _driver = Driver.Still;
        }

        SetContextThink(MoveDoneContext, 0.1f);

        if (Vars.Direction > 0 && value >= 1f)
        {
            Spin(Vector.Zero);
            SetAngles(EndAngles);
            OnFullyClosed.Fire();
            return;
        }

        if (Vars.Direction < 0 && value <= 0f)
        {
            Spin(Vector.Zero);
            SetAngles(_startAngles);
            OnFullyOpen.Fire();
            return;
        }

        // CMomentaryRotButton::PlaySound, on the turn that starts a use.
        if (playSound && _noise.Length > 0)
            Sound.EntityEmitSound(Handle, _noise);

        Spin(MoveAngles * (Vars.Direction * Vars.Speed));
        _moveDone = UseMove.Use;
    }

    /// <summary>CMomentaryRotButton::UseMoveDone: let go. Stopped, unpressed, and wound back if it returns.</summary>
    private void UseMoveDone()
    {
        Spin(Vector.Zero);

        // Make sure our targets stop where we stopped.
        Report(CurrentPosition);

        // Alert that we've been unpressed
        OnUnpressed.Fire();

        _lastUsed = false;

        if (!Flags.HasFlag(SpawnFlag.Toggle) && Vars.ReturnSpeed > 0f)
        {
            _moveDone = UseMove.Return;
            Vars.Direction = -1;

            // Delay before autoreturn.
            SetContextThink(MoveDoneContext, 0.1f);
        }
        else
        {
            StopThinking();
            _moveDone = UseMove.None;
        }
    }

    /// <summary>CMomentaryRotButton::ReturnMoveDone: winding back at the return speed until it is at the start.</summary>
    private void ReturnMoveDone()
    {
        if (CurrentPosition <= 0f)
        {
            // Got back to the start, stop spinning.
            Spin(Vector.Zero);
            SetAngles(_startAngles);

            Report(0f);

            _moveDone = UseMove.None;
            StopThinking();
        }
        else
        {
            Spin(MoveAngles * -Vars.ReturnSpeed);
            SetContextThink(MoveDoneContext, 0.1f);
            SetNextThink(0.01f);
        }
    }

    //-------------------------------------------------------------------------
    // Turning to a position the map asked for, and reporting while it turns.
    //-------------------------------------------------------------------------

    /// <summary>InputSetPosition: turns to the fraction at a speed, locked or not.</summary>
    private void Travel(float fraction, float speed)
    {
        _target = Math.Clamp(fraction, 0f, 1f);
        _driver = Driver.Map;

        // Every tick, from now until it arrives. The only entity here that does,
        // and the class comment says why.
        //
        // Armed BEFORE the turn is asked for, not after: a wheel already at the
        // fraction it was sent to arrives inside RotateTo, synchronously, and
        // scheduling a think after that returns would leave one running for the
        // rest of the map with nothing to report.
        SetNextThink(0f);

        RotateTo(OffsetAngles(_startAngles, (RotationAxisFlag)SpawnFlags, _travel * _target), speed);
    }

    /// <summary>UpdateThink: the Position output follows the wheel every tick while it turns.</summary>
    public override void OnThink()
    {
        Report(CurrentPosition);
        SetNextThink(0f);
    }

    public override void OnThink(string context)
    {
        if (context == JiggleContext)
        {
            JiggleThink();
            return;
        }

        if (context != MoveDoneContext)
            return;

        switch (_moveDone)
        {
            case UseMove.Use:
                UseMoveDone();
                break;
            case UseMove.Return:
                ReturnMoveDone();
                break;
        }
    }

    /// <summary>
    /// Tells the map where the wheel is, if it has moved and if anything is
    /// listening. Guarded on the value because an output fired sixty-six times a
    /// second carrying the same number is noise a map has to filter.
    /// </summary>
    private void Report(float value)
    {
        if (!_updateTarget || value == _reported)
            return;

        _reported = value;
        Position.Fire(value);
    }

    /// <summary>SetPositionMoveDone and OutputMovementComplete: the map's turn arrived.</summary>
    public override void OnMoveDone()
    {
        // An arrival with nothing under way is not an arrival. Without this a
        // wheel that has stopped would fire its end outputs again every time
        // anything else asked the shell to report a finished movement.
        if (_driver == Driver.Still)
            return;

        // The shell lands exactly on the angles it was given, so the target is
        // the truth here — and reporting it rather than the read-back is what
        // guarantees a wheel wound fully on says one rather than 0.9998.
        Report(_target);
        StopThinking();
        _driver = Driver.Still;

        if (_target >= 1f)
            OnFullyClosed.Fire();
        else if (_target <= 0f)
            OnFullyOpen.Fire();

        OnReachedPosition.Fire();
    }

    /// <summary>
    /// Something is in the way. The wheel stops where it met it rather than
    /// grinding on, because unlike a door it has no reason to reverse: the
    /// player will turn it again.
    /// </summary>
    public override void OnBlocked(EntityHandle other)
    {
        if (_driver == Driver.Still)
            return;

        Movement.StopMoving(Handle);
        StopThinking();

        _driver = Driver.Still;
        _target = CurrentPosition;

        Report(_target);
    }
}

/// <summary>
/// The map keys <see cref="MomentaryRotButton"/> declares.
/// </summary>
[ScriptVars]
public partial class MomentaryRotButtonVars
{
    /// <summary>How far the wheel turns end to end, in degrees. Negative reverses it.</summary>
    public float Distance { get; set; } = 90f;

    /// <summary>The "Buttons.sndN" a turn starts with; 0 for none.</summary>
    public int Sounds { get; set; }

    public float Speed { get; set; } = 100f;

    /// <summary>How fast it winds back on its own. Zero means it stays where it is put.</summary>
    public float ReturnSpeed { get; set; }

    /// <summary>Where along its travel the mapper drew it, as a fraction.</summary>
    public float StartPosition { get; set; }

    /// <summary>Which way the next press turns it. Only the sign is read.</summary>
    [VarName("StartDirection")]
    public int Direction { get; set; } = 1;

    /// <summary>
    /// Stock slams the brush back to SOLID_BSP for a wheel that must not be
    /// approximated by its bounding box. The brush shell is SOLID_BSP already,
    /// so the key is bound to be consumed rather than to be acted on.
    /// </summary>
    public bool SolidBsp { get; set; }
}

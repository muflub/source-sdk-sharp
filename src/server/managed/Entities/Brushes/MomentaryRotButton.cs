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

        Locked = 0b0000_1000_0000_0000,  // 0x800
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
    private enum Driver { Still, Map, Press, Return }

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

    public override void OnSpawn()
    {
        SetVisible(true);

        SetSolidity(Flags.HasFlag(SpawnFlag.NotSolid)
            ? BrushSolidity.None
            : BrushSolidity.Solid);

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

    [ScriptedEvent] private void OnSetPosition(float fraction) => Travel(fraction, Vars.Speed, Driver.Map);

    [ScriptedEvent] private void OnSetPositionImmediately(float fraction)
    {
        _target = Math.Clamp(fraction, 0f, 1f);
        SetAngles(OffsetAngles(_startAngles, (RotationAxisFlag)SpawnFlags, _travel * _target));
        Report(_target);
    }

    // Underscored, so they cannot be method names with the "On" stripped. Stock
    // uses them to jiggle a locked wheel without dragging whatever it drives
    // along with it, which is the only reason a wheel would move and not report.
    [ScriptedEvent("_DisableUpdateTarget")] private void OnUpdateTargetDisabled() => _updateTarget = false;
    [ScriptedEvent("_EnableUpdateTarget")] private void OnUpdateTargetEnabled() => _updateTarget = true;

    [ScriptedEvent] private void OnEnable() => _disabled = false;
    [ScriptedEvent] private void OnDisable() => _disabled = true;

    [ScriptedEvent] private void OnLock()
    {
        _locked = true;

        // Stopped where it stands rather than allowed to coast to the end: a
        // wheel locked halfway is meant to stay halfway.
        Movement.StopMoving(Handle);
        StopThinking();
        _driver = Driver.Still;
    }

    [ScriptedEvent] private void OnUnlock() => _locked = false;

    /// <summary>
    /// A player turning it. One press, one traverse — see the divergence note:
    /// the shell offers the use key as an impulse, so the wheel cannot be turned
    /// by the amount a player holds it for.
    /// </summary>
    [ScriptedEvent("Use")] private void OnUsed()
    {
        if (_disabled)
            return;

        if (_locked)
        {
            OnUseLocked.Fire();
            return;
        }

        // Already being turned by this press. A second press mid-traverse is the
        // player leaning on the key, not a new decision.
        if (_driver == Driver.Press)
            return;

        // Reversing on each fresh press is what makes it a wheel: the same key
        // winds it on and then back off again.
        Vars.Direction = -Vars.Direction;

        OnPressed.Fire();
        Travel(Vars.Direction > 0 ? 1f : 0f, Vars.Speed, Driver.Press);
    }

    //-------------------------------------------------------------------------
    // Turning, and reporting while it turns.
    //-------------------------------------------------------------------------

    private void Travel(float fraction, float speed, Driver driver)
    {
        if (_locked || _disabled)
            return;

        _target = Math.Clamp(fraction, 0f, 1f);
        _driver = driver;

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

    public override void OnThink()
    {
        Report(CurrentPosition);
        SetNextThink(0f);
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

        var driver = _driver;
        _driver = Driver.Still;

        if (driver == Driver.Press)
            OnUnpressed.Fire();

        if (_target >= 1f)
            OnFullyClosed.Fire();
        else if (_target <= 0f)
            OnFullyOpen.Fire();

        OnReachedPosition.Fire();

        // Winding back is the answer to a PRESS only. A map that parks the wheel
        // with SetPosition has said where it wants it, and taking that away again
        // would make the input useless.
        if (driver == Driver.Press && Vars.ReturnSpeed > 0f && !Flags.HasFlag(SpawnFlag.Toggle) && _target > 0f)
            Travel(0f, Vars.ReturnSpeed, Driver.Return);
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

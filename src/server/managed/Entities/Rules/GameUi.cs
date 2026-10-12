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
/// Takes a player's controls away from the game and hands them to the map.
///
/// While one of these holds a player, their keys stop being movement and become
/// outputs: forward fires PressedForward and, on release, UnpressedForward, and
/// the four analog outputs report the same thing as a value a map can do
/// arithmetic on. That is how the SDK builds a turret console or a crane — the
/// controls are a map, not a vehicle class.
///
/// The decision it owns is WHO it holds and WHEN it lets go. Activate names a
/// player or takes the activator; a second player asking is refused rather than
/// stealing the seat; and it releases on its own if the player looks away past
/// the FieldOfView tolerance, which is what stops a console from following
/// somebody who walked off. The spawnflags add two more exits, on +use and on
/// jump, because a player who cannot get out of a chair reports it as being
/// stuck.
///
/// Everything an output reports is derived from the buttons held that tick and
/// the buttons held the tick before. There is no state beyond that, which is why
/// the edge detection is worth reading twice: reporting a press where stock
/// reports a release inverts a crane.
/// </summary>
[ScriptedEntity(EntityNames.GameUi)]
public sealed class GameUi : ScriptedEntity
{
    // The two exits are honoured. The first two flags are not, and are written
    // down rather than dropped because a map carries them whether or not this
    // reads them, and the next person to look needs to know which of the four
    // numbers are live: freezing a player needs FL_ATCONTROLS and holstering
    // their weapon needs the HUD-hiding and weapon-switch path, neither of which
    // managed code can reach. See docs/divergences.md.

    /// <summary>
    /// game_ui's own spawnflags.
    ///
    /// <para>
    /// Half of these are honoured and half are not, and the type is what makes the
    /// split sayable. The two exits work. Freezing a player needs FL_ATCONTROLS and
    /// holstering their weapon needs the HUD-hiding and weapon-switch path, neither
    /// of which managed code can reach -- see docs/divergences.md. They are named
    /// anyway rather than dropped, because a map carries them whether or not this
    /// reads them, and the next person to look needs to know which of the four
    /// numbers are live.
    /// </para>
    /// </summary>
    [Flags]
    internal enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000_0000_0000,

        /// <summary>NOT honoured: needs FL_ATCONTROLS, which managed code cannot reach.</summary>
        FreezePlayer = 0b0000_0000_0010_0000,

        /// <summary>NOT honoured: needs the HUD-hiding and weapon-switch path.</summary>
        HideWeapon = 0b0000_0000_0100_0000,

        UseDeactivates = 0b0000_0000_1000_0000,
        JumpDeactivates = 0b0000_0001_0000_0000,  // 0x100
    }

    /// <summary>What a map may set on this entity. See <see cref="GameUiVars"/>.</summary>
    [ScriptVars] public GameUiVars Vars = new();

    public readonly Output PlayerOn = new();
    public readonly Output PlayerOff = new();

    public readonly Output PressedMoveLeft = new();
    public readonly Output PressedMoveRight = new();
    public readonly Output PressedForward = new();
    public readonly Output PressedBack = new();
    public readonly Output PressedAttack = new();
    public readonly Output PressedAttack2 = new();

    public readonly Output UnpressedMoveLeft = new();
    public readonly Output UnpressedMoveRight = new();
    public readonly Output UnpressedForward = new();
    public readonly Output UnpressedBack = new();
    public readonly Output UnpressedAttack = new();
    public readonly Output UnpressedAttack2 = new();

    public readonly Output XAxis = new();
    public readonly Output YAxis = new();
    public readonly Output AttackAxis = new();
    public readonly Output Attack2Axis = new();

    /// <summary>
    /// Where the buttons held by a player come from.
    ///
    /// <para>
    /// The data already crosses the boundary — the server shell calls
    /// OnPlayerRunCommand once per player per tick with exactly this int — so
    /// this is a seam onto something the module already knows rather than a new
    /// question for native code.
    /// </para>
    ///
    /// <para>
    /// It was a static delegate the module assigned during init, and a null one
    /// read as "nothing held": the entity went on capturing and releasing
    /// players perfectly while reporting that nobody ever pressed a key. That
    /// is the failure this replaces. A constructor parameter cannot be
    /// forgotten, and an entity whose service is missing is refused at spawn
    /// with a message naming it rather than spawning and lying.
    /// </para>
    /// </summary>
    private readonly IPlayerButtons _buttons;

    public GameUi(IPlayerButtons buttons)
    {
        _buttons = buttons;
    }

    private EntityHandle _player;
    private int _lastButtons;
    private bool _forceUpdate;

    private float _xAxis;
    private float _yAxis;
    private float _attackAxis;
    private float _attack2Axis;

    /// <summary>Who is at the controls, or an invalid handle when nobody is.</summary>
    public EntityHandle Player => _player;

    [ScriptedEvent]
    private void OnActivate(InputData input)
    {
        var player = Named(input);
        if (!Entities.IsValid(player))
            return;

        // Somebody else is already at these controls. Stock leaves a TODO here
        // about letting the second player take over; taking the seat out from
        // under the first is worse than refusing, so this refuses too.
        if (Entities.IsValid(_player) && _player.Value != player.Value)
            return;

        _player = player;
        PlayerOn.Fire();

        // Sampled every tick from here, because a press that is missed is a
        // press that never reaches the map.
        SetNextThink(0f);

        // The first sample has nothing to compare against, so it publishes the
        // axes outright rather than waiting for one of them to change. Otherwise
        // a map reading XAxis on activation gets silence until the player moves.
        _forceUpdate = true;
    }

    [ScriptedEvent]
    private void OnDeactivate() => Release();

    public override void OnThink()
    {
        // The player left, or died and was removed. Nothing to sample and nobody
        // to tell, so this is not a release — there are no outputs owed to an
        // entity that is gone.
        if (!Entities.IsValid(_player))
        {
            StopThinking();
            return;
        }

        if (Vars.FieldOfView > -1f && !Watching())
        {
            Release();
            return;
        }

        SetNextThink(0f);

        int buttons = _buttons.Held(_player);

        if (_forceUpdate)
            _lastButtons = buttons;

        // Checked before the outputs are reported, so the tick that lets go does
        // not also announce the key that did it.
        if (Deactivates((SpawnFlag)SpawnFlags, buttons & ~_lastButtons))
        {
            Release();
            return;
        }

        Report(buttons);
    }

    /// <summary>
    /// Turns one tick's buttons into outputs.
    ///
    /// Held state in, edges out: a button in <paramref name="buttons"/> that was
    /// not held last tick is a press, one that was is a release, and one that did
    /// not change says nothing at all. The analog outputs work the other way and
    /// report a level, but still only when it moves — a map wired to XAxis wants
    /// to hear about a change of direction, not the same value sixty-six times a
    /// second.
    /// </summary>
    internal void Report(int buttons)
    {
        int changed = buttons ^ _lastButtons;

        Edge(changed, InputButtons.MoveRight, PressedMoveRight, UnpressedMoveRight);
        Edge(changed, InputButtons.MoveLeft, PressedMoveLeft, UnpressedMoveLeft);
        Edge(changed, InputButtons.Forward, PressedForward, UnpressedForward);
        Edge(changed, InputButtons.Back, PressedBack, UnpressedBack);
        Edge(changed, InputButtons.Attack, PressedAttack, UnpressedAttack);
        Edge(changed, InputButtons.Attack2, PressedAttack2, UnpressedAttack2);

        _lastButtons = buttons;

        Level(XAxis, ref _xAxis, AxisX(buttons));
        Level(YAxis, ref _yAxis, AxisY(buttons));
        Level(AttackAxis, ref _attackAxis, Held(buttons, InputButtons.Attack) ? 1f : 0f);
        Level(Attack2Axis, ref _attack2Axis, Held(buttons, InputButtons.Attack2) ? 1f : 0f);

        _forceUpdate = false;
    }

    /// <summary>
    /// Which way the map is being asked to go, from the strafe keys.
    ///
    /// Right wins when both are held, which is stock's order rather than a
    /// considered one — but it is the order a crane was built against, and
    /// cancelling to zero instead would change what holding both keys does.
    /// </summary>
    internal static float AxisX(int buttons)
        => Held(buttons, InputButtons.MoveRight) ? 1f
         : Held(buttons, InputButtons.MoveLeft) ? -1f
         : 0f;

    /// <summary>Forward and back, on the same rule as <see cref="AxisX"/>.</summary>
    internal static float AxisY(int buttons)
        => Held(buttons, InputButtons.Forward) ? 1f
         : Held(buttons, InputButtons.Back) ? -1f
         : 0f;

    /// <summary>
    /// Whether the keys just pressed are one of the ways out.
    ///
    /// Only the leading edge counts. A player who arrives still holding +use —
    /// which is how most of them arrive, because +use is what put them here —
    /// would otherwise be ejected on the first tick.
    /// </summary>
    internal static bool Deactivates(SpawnFlag spawnFlags, int pressed)
        => (Held(pressed, InputButtons.Use) && spawnFlags.HasFlag(SpawnFlag.UseDeactivates))
        || (Held(pressed, InputButtons.Jump) && spawnFlags.HasFlag(SpawnFlag.JumpDeactivates));

    /// <summary>
    /// Whether a player looking along <paramref name="eyeForward"/> is still
    /// facing the controls, which are <paramref name="toControls"/> away.
    ///
    /// The tolerance is a dot product rather than an angle, so 1 is "exactly at
    /// it" and -1 is "anywhere at all". Compared with the raw offset normalised,
    /// because a distant console and a close one should let go at the same angle.
    /// </summary>
    internal static bool Watching(Vector eyeForward, Vector toControls, float fieldOfView)
        => eyeForward.Dot(toControls.Normalized) >= fieldOfView;

    private bool Watching()
        => Watching(Angles.Forward(Entities.GetEyeAngles(_player)),
                    Origin - Entities.GetWorldSpaceCenter(_player),
                    Vars.FieldOfView);

    /// <summary>
    /// Hands the controls back.
    ///
    /// The axes are zeroed on the way out rather than left where they were: a
    /// crane still holding XAxis 1 after its driver walked away keeps turning,
    /// and a map cannot see that nobody is at the controls except by being told.
    /// </summary>
    private void Release()
    {
        if (Entities.IsValid(_player))
        {
            PlayerOff.Fire();

            XAxis.Fire(0f);
            YAxis.Fire(0f);
            AttackAxis.Fire(0f);
            Attack2Axis.Fire(0f);
        }
        else
        {
            // Reported rather than ignored, as stock reports it: a map that
            // deactivates controls nobody is holding has usually lost track of
            // which of two consoles its player is at.
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': deactivated with nobody at the controls\n");
        }

        _xAxis = 0f;
        _yAxis = 0f;
        _attackAxis = 0f;
        _attack2Axis = 0f;

        _lastButtons = 0;
        _player = EntityHandle.None;

        StopThinking();
    }

    /// <summary>
    /// Which player the input meant: the one it named, or the activator.
    ///
    /// A name that resolves to something that is not a player is refused rather
    /// than fallen back on, because a map naming the wrong entity wants to hear
    /// about it — silently seating the activator instead hides the typo behind
    /// controls that work for whoever happens to be standing there.
    /// </summary>
    private EntityHandle Named(InputData input)
    {
        if (input.Value.Length > 0)
        {
            var found = Entities.FindByName(input.Value);

            if (!Entities.IsValid(found) || !Entities.IsPlayer(found))
            {
                Log.Warning($"[SourceSharp] {ClassName} '{Name}': Activate named '{input.Value}', " +
                               $"which is not a player\n");
                return EntityHandle.None;
            }

            return found;
        }

        if (!Entities.IsValid(input.Activator) || !Entities.IsPlayer(input.Activator))
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': Activate with no player to seat\n");
            return EntityHandle.None;
        }

        return input.Activator;
    }

    private static bool Held(int buttons, InputButtons button) => (buttons & (int)button) != 0;

    private void Edge(int changed, InputButtons button, Output pressed, Output released)
    {
        if (!Held(changed, button))
            return;

        if (Held(_lastButtons, button))
            released.Fire();
        else
            pressed.Fire();
    }

    private void Level(Output output, ref float current, float value)
    {
        if (!_forceUpdate && current == value)
            return;

        current = value;
        output.Fire(value);
    }
}

/// <summary>
/// The map keys <see cref="GameUi"/> declares.
/// </summary>
[ScriptVars]
public partial class GameUiVars
{
    /// <summary>
    /// How far off the entity the player may look before it lets go, as the
    /// cosine of the angle. -1 is stock's "never let go", and it is the default
    /// because a console that releases the player unasked is the surprising
    /// behaviour.
    /// </summary>
    public float FieldOfView { get; set; } = -1f;
}

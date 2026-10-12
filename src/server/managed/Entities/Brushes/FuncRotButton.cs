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
/// A button on a hinge. The same states as func_button, turned rather than
/// pressed — which is the whole difference, so it swings to an angle and back
/// instead of locking out on a timer alone.
/// </summary>
[ScriptedEntity(EntityNames.FuncRotButton, ScriptedEntityKind.Brush)]
public sealed class FuncRotButton : ScriptedEntity
{
    /// <summary>
    /// The shared rotation-axis bits off this entity's spawnflags word.
    /// Separate from any flags of its own: see <see cref="RotationAxisFlag"/>.
    /// </summary>
    private RotationAxisFlag Axis => SpawnFlagsAs<RotationAxisFlag>();

    public readonly Output OnPressed = new();
    public readonly Output OnIn = new();
    public readonly Output OnOut = new();
    public readonly Output OnUseLocked = new();

    /// <summary>What a map may set on this entity. See <see cref="FuncRotButtonVars"/>.</summary>
    [ScriptVars] public FuncRotButtonVars Vars = new();

    private Vector _restAngles;

    /// <summary>
    /// The facing the button turns to when pressed, derived from the map's keys
    /// ONCE, in <see cref="OnSpawn"/>, which a hot reload does not run again --
    /// so it is <c>[SaveField]</c>. Uncarried, the rebuilt instance held
    /// <c>Vector.Zero</c> and every press after a <c>cs_sv_reload</c> slammed
    /// the button flat (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private Vector _pressedAngles;

    private bool _locked;
    private bool _pressed;

    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);

        _locked = Vars.StartDisabled;
        _restAngles = SpawnAngles;

        // Which axis it turns about is a spawnflag rather than a key. Roll wins
        // over pitch and yaw is the default, in that order, because that is the
        // order AxisDir tests them in.
        // CRotButton::Spawn calls CBaseToggle::AxisDir, which reads the DOOR
        // rotate flags -- NOT func_rotating's, which are 4 and 8 and are what
        // this class used to read. A mapper setting "rotate roll" on a rot
        // button got yaw. RotationAxisFlag is the one declaration of those bits
        // now, so there is no second copy left to read the wrong one from.
        _pressedAngles = Axis.HasFlag(RotationAxisFlag.Roll)
            ? new Vector(_restAngles.X, _restAngles.Y, _restAngles.Z + Vars.Distance)
            : Axis.HasFlag(RotationAxisFlag.Pitch)
                ? new Vector(_restAngles.X + Vars.Distance, _restAngles.Y, _restAngles.Z)
                : new Vector(_restAngles.X, _restAngles.Y + Vars.Distance, _restAngles.Z);

        if (Vars.Speed <= 0f)
            Vars.Speed = 40f;
    }

    [ScriptedEvent] private void OnLock() => _locked = true;
    [ScriptedEvent] private void OnUnlock() => _locked = false;

    [ScriptedEvent("Press")] private void OnPress() => Press();
    [ScriptedEvent("Use")] private void OnUsed() => Press();

    private void Press()
    {
        if (_pressed)
            return;

        if (_locked)
        {
            OnUseLocked.Fire();
            return;
        }

        _pressed = true;
        OnPressed.Fire();
        OnIn.Fire();
        RotateTo(_pressedAngles, Vars.Speed);
    }

    public override void OnMoveDone()
    {
        // Only the outward turn schedules a return; the return itself is the
        // end of the cycle.
        if (!_pressed)
            return;

        if (Vars.Wait < 0f)
            return;

        SetNextThink(Vars.Wait);
    }

    public override void OnThink()
    {
        if (!_pressed)
            return;

        _pressed = false;
        OnOut.Fire();
        RotateTo(_restAngles, Vars.Speed);
    }
}

/// <summary>
/// The map keys <see cref="FuncRotButton"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncRotButtonVars
{
    /// <summary>How far it turns, in degrees.</summary>
    public float Distance { get; set; } = 90f;

    public float Speed { get; set; } = 40f;

    /// <summary>Seconds before it returns. Negative means it stays turned.</summary>
    public float Wait { get; set; } = 1f;

    public bool StartDisabled { get; set; }
}

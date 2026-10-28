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
/// func_rot_button (CRotButton::Spawn): a lever that turns <c>distance</c>
/// degrees about the door rotate axis its spawnflags name, backwards with
/// SF_DOOR_ROTATE_BACKWARDS, not solid with SF_ROTBUTTON_NOTSOLID. Its use is
/// always set, so a +USE that reaches it presses it; it takes damage only
/// with a health.
/// </summary>
[ScriptedEntity(EntityNames.FuncRotButton, ScriptedEntityKind.Brush)]
public sealed class FuncRotButton : BaseButton
{
    public FuncRotButton(IGameClock clock, IEngineHost host, IEnginePrecache precache, IEngineSound sound,
                         IEngineEffects effects)
        : base(clock, host, precache, sound, effects)
    {
    }

    protected override SolidType BrushSolidType => SolidType.VPhysics;

    /// <summary>SF_DOOR_ROTATE_BACKWARDS.</summary>
    private const int RotateBackwardsFlag = 2;

    private RotationAxisFlag Axis => SpawnFlagsAs<RotationAxisFlag>();

    /// <summary>What a map may set on this entity. See <see cref="FuncRotButtonVars"/>.</summary>
    [ScriptVars] public FuncRotButtonVars Vars = new();

    /// <summary>m_vecAngle1 and m_vecAngle2: out and in.</summary>
    [SaveField] private Vector _out, _in;

    protected override bool UseEnabled => true;

    /// <summary>CRotButton::Spawn: DAMAGE_YES only with a health.</summary>
    public override bool TakesDamage => ButtonVars.Health > 0;

    public override void OnSpawn()
    {
        SpawnButton();

        // CBaseToggle::AxisDir: roll, then pitch, else yaw.
        Vector axis = Axis.HasFlag(RotationAxisFlag.Roll) ? new Vector(0f, 0f, 1f)
                    : Axis.HasFlag(RotationAxisFlag.Pitch) ? new Vector(1f, 0f, 0f)
                    : new Vector(0f, 1f, 0f);
        if ((SpawnFlags & RotateBackwardsFlag) != 0)
            axis = axis * -1f;

        SetSolidity(Flags.HasFlag(ButtonFlag.DontMove) ? BrushSolidity.None : BrushSolidity.Solid);
        SetVisible(true);

        _out = SpawnAngles;
        _in = _out + axis * Vars.Distance;

        // CreateVPhysics: a shadow that follows it, which props meet.
        InitShadowPhysics();
    }

    protected override void TravelIn() => RotateTo(_in, ButtonVars.Speed);

    protected override void TravelOut() => RotateTo(_out, ButtonVars.Speed);
}

/// <summary>
/// The map keys <see cref="FuncRotButton"/> declares beside a button's.
/// </summary>
[ScriptVars]
public partial class FuncRotButtonVars
{
    /// <summary>m_flMoveDistance: how far it turns, in degrees.</summary>
    public float Distance { get; set; } = 90f;
}

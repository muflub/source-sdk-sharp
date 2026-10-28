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
/// func_button (CBaseButton::Spawn): a brush that travels its own depth along
/// <c>movedir</c>, less the lip, when pressed, and back. A button too shallow
/// to travel a unit, or with SF_BUTTON_DONTMOVE, stays where it is and fires.
/// </summary>
[ScriptedEntity(EntityNames.FuncButton, ScriptedEntityKind.Brush)]
public sealed class FuncButton : BaseButton
{
    public FuncButton(IGameClock clock, IEngineHost host, IEnginePrecache precache, IEngineSound sound,
                      IEngineEffects effects)
        : base(clock, host, precache, sound, effects)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="FuncButtonVars"/>.</summary>
    [ScriptVars] public FuncButtonVars Vars = new();

    /// <summary>m_vecPosition1 and m_vecPosition2: out and in.</summary>
    [SaveField] private Vector _out, _in;

    public override void OnSpawn()
    {
        SpawnButton();

        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);

        if (Vars.Lip == 0f)
            Vars.Lip = 4f;

        // The movedir key is angles; the travel is along their forward.
        Vector direction = Angles.Forward(Vars.MoveDir);
        _out = SpawnOrigin;
        _in = _out + direction * (FuncDoor.DepthAlong(Entities.GetBoundsSize(Handle), direction) - Vars.Lip);

        if ((_in - _out).Length < 1f || Flags.HasFlag(ButtonFlag.DontMove))
            _in = _out;

        // CreateVPhysics: a shadow that follows it, which props meet.
        InitShadowPhysics();
    }

    protected override void TravelIn() => MoveTo(_in, ButtonVars.Speed);

    protected override void TravelOut() => MoveTo(_out, ButtonVars.Speed);
}

/// <summary>
/// The map keys <see cref="FuncButton"/> declares beside a button's.
/// </summary>
[ScriptVars]
public partial class FuncButtonVars
{
    /// <summary>The direction it travels in, as angles; 0 0 0 is +x.</summary>
    [VarName("movedir")]
    public Vector MoveDir { get; set; }

    /// <summary>How much of its depth is left standing out when it is in.</summary>
    public float Lip { get; set; }
}

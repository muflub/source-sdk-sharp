//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

//-----------------------------------------------------------------------------
// The brushes that do not move, or move without arriving.
//-----------------------------------------------------------------------------

/// <summary>
/// A brush the map can show, hide and make solid or passable. Scenery that
/// changes, and walls that appear.
/// </summary>
/// <summary>
/// What the map's <c>Solidity</c> key asks for. A closed set of three, not a
/// bit field -- the values are 0, 1 and 2 and a brush is in exactly one of
/// these states.
///
/// <para>
/// Shared because the key is shared: func_reflective_glass reads func_brush's
/// key and had a verbatim copy of these constants, down to naming only two of
/// the three and leaving "always solid" as an unnamed default arm in both
/// switches. Naming the third is most of the point -- a closed set with a
/// member missing reads as though the default were a fallback for bad input,
/// when it is in fact the ordinary case of a brush a mapper wanted solid.
/// </para>
/// </summary>
public enum SolidityMode
{
    /// <summary>Solid while visible, passable while hidden. The default a map gets by saying nothing.</summary>
    Toggle = 0,

    /// <summary>Never solid, however it is drawn. A brush a player walks through.</summary>
    Never = 1,

    /// <summary>Always solid, however it is drawn. An invisible wall.</summary>
    Always = 2,
}

[ScriptedEntity(EntityNames.FuncBrush, ScriptedEntityKind.Brush)]
public sealed class FuncBrush : ScriptedEntity
{
    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    /// <summary>What a map may set on this entity. See <see cref="FuncBrushVars"/>.</summary>
    [ScriptVars] public FuncBrushVars Vars = new();

    /// <summary>
    /// Which way the brush currently stands, and what <see cref="Apply"/>
    /// renders from. Decided ONCE, in <see cref="OnSpawn"/>, which a hot reload
    /// does not run again -- so it is <c>[SaveField]</c>. Uncarried, the rebuilt
    /// instance read the default and a wall a map had switched off came back
    /// solid, or one it had switched on came back gone (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private bool _on;

    public override void OnSpawn()
    {
        _on = !Vars.StartDisabled;
        Apply();
    }

    [ScriptedEvent] private void OnEnable() { _on = true; Apply(); }
    [ScriptedEvent] private void OnDisable() { _on = false; Apply(); }
    [ScriptedEvent] private void OnToggle() { _on = !_on; Apply(); }

    // TurnOn and TurnOff are the older names for the same thing, and maps use
    // both depending on when they were made.
    [ScriptedEvent("TurnOn")] private void OnTurnedOn() { _on = true; Apply(); }
    [ScriptedEvent("TurnOff")] private void OnTurnedOff() { _on = false; Apply(); }

    //-------------------------------------------------------------------------
    // Solidity does not simply follow visibility. A map can pin a brush solid
    // while hiding it — an invisible wall — or leave it drawn but passable.
    //-------------------------------------------------------------------------
    private void Apply()
    {
        SetVisible(_on);

        SetSolidity(Vars.Solidity switch
        {
            SolidityMode.Never => BrushSolidity.None,
            SolidityMode.Toggle => _on ? BrushSolidity.Solid : BrushSolidity.None,
            _ => BrushSolidity.Solid,
        });
    }
}

/// <summary>
/// The map keys <see cref="FuncBrush"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncBrushVars
{
    /// <summary>
    /// How the brush's solidity follows its visibility. The enum itself since
    /// plan Step 12 — a map still writes 0, 1 or 2, and may now write "Never"
    /// as well.
    /// </summary>
    public SolidityMode Solidity { get; set; }

    public bool StartDisabled { get; set; }
}

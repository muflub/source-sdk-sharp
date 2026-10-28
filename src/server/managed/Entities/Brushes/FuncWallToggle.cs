//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>A wall that can be switched out of existence and back.</summary>
[ScriptedEntity(EntityNames.FuncWallToggle, ScriptedEntityKind.Brush)]
public sealed class FuncWallToggle : ScriptedEntity
{
    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    private const int StartInvisible = 1;

    /// <summary>
    /// Which way the wall currently stands, and what <see cref="Apply"/> renders
    /// from. Decided ONCE, in <see cref="OnSpawn"/>, which a hot reload does not
    /// run again -- so it is <c>[SaveField]</c>. Uncarried, the rebuilt instance
    /// read the default and a wall a map had switched off came back solid, or
    /// one it had switched on came back gone (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private bool _on;

    public override void OnSpawn()
    {
        _on = (SpawnFlags & StartInvisible) == 0;
        SetSolidity(BrushSolidity.Solid);
        InitStaticPhysics();
        Apply();
    }

    [ScriptedEvent] private void OnTurnOn() { _on = true; Apply(); }
    [ScriptedEvent] private void OnTurnOff() { _on = false; Apply(); }
    [ScriptedEvent] private void OnToggle() { _on = !_on; Apply(); }

    private void Apply()
    {
        SetVisible(_on);
        SetSolidity(_on ? BrushSolidity.Solid : BrushSolidity.None);
        // TurnOn and TurnOff: props pass through a wall that is off, too.
        EnablePhysicsCollisions(_on);
    }
}

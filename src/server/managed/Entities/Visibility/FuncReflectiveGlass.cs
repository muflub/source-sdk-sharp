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
/// func_reflective_glass: a pane the client renders the world into, twice.
///
/// <para>
/// The server half is a <c>func_brush</c> and nothing more — stock's class body
/// is literally empty, and its send table declares no properties. That is the
/// whole point of the entity: it exists so that the CLIENT can find it, and what
/// it does is done by the view renderer, which asks each frame whether any pane
/// is in front of the viewer and, if one is, builds a reflection view and a
/// refraction view for it.
/// </para>
///
/// <para>
/// So the interesting half of this port is client-side and is not a class at
/// all — it is the sweep. <c>IsReflectiveGlassInView</c> walks a list a stock
/// pane joins by BEING a <c>C_FuncReflectiveGlass</c>, and nothing on the
/// parallel hierarchy is one; the sweep now asks for panes by classname as well.
/// Everything it then does to a pane — cull it, read its brush planes, backface
/// it — never needed the entity, only a renderable, which is what that function
/// now takes.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncReflectiveGlass, ScriptedEntityKind.Brush)]
public sealed class FuncReflectiveGlass : ScriptedEntity
{
    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    /// <summary>What a map may set on this entity. See <see cref="FuncReflectiveGlassVars"/>.</summary>
    [ScriptVars] public FuncReflectiveGlassVars Vars = new();

    /// <summary>
    /// Whether the pane currently reflects, and what <see cref="Apply"/> renders
    /// from. Decided ONCE, in <see cref="OnSpawn"/>, which a hot reload does not
    /// run again -- so it is <c>[SaveField]</c>. Uncarried, the rebuilt instance
    /// read the default and a pane switched off came back burning a reflection
    /// pass, or one left on came back invisible (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private bool _on;

    public override void OnSpawn()
    {
        _on = !Vars.StartDisabled;
        Apply();
    }

    // The func_brush inputs, because that is what the entity derives from in
    // stock and a mapper who has used one expects them here.
    [ScriptedEvent] private void OnEnable()  { _on = true;   Apply(); }
    [ScriptedEvent] private void OnDisable() { _on = false;  Apply(); }
    [ScriptedEvent] private void OnToggle()  { _on = !_on;   Apply(); }

    [ScriptedEvent("TurnOn")]  private void OnTurnedOn()  { _on = true;  Apply(); }
    [ScriptedEvent("TurnOff")] private void OnTurnedOff() { _on = false; Apply(); }

    //-------------------------------------------------------------------------
    // Hiding the pane is what stops it reflecting, and that is not a
    // coincidence to rely on quietly: the client's sweep skips a pane with no
    // model, and a hidden entity on this hierarchy has none. So a disabled pane
    // costs no reflection pass, which is the behaviour a mapper who switches one
    // off is asking for.
    //-------------------------------------------------------------------------
    private void Apply()
    {
        SetVisible(_on);

        // func_brush's key and func_brush's type -- see SolidityMode.
        SetSolidity(Vars.Solidity switch
        {
            SolidityMode.Never  => BrushSolidity.None,
            SolidityMode.Toggle => _on ? BrushSolidity.Solid : BrushSolidity.None,
            _                   => BrushSolidity.Solid,
        });
    }
}

/// <summary>
/// The map keys <see cref="FuncReflectiveGlass"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncReflectiveGlassVars
{
    /// <summary>func_brush's key and func_brush's type. See <see cref="SolidityMode"/>.</summary>
    public SolidityMode Solidity { get; set; }

    public bool StartDisabled { get; set; }
}

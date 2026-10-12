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
/// func_occluder: a switch for one of the map's compiled occluders.
///
/// <para>
/// The brush a mapper drew is gone by the time the game runs — vbsp turns it
/// into occlusion data and leaves behind a number, "occludernumber", the same
/// way it leaves an area portal behind a portal number. So there is no volume
/// here and nothing to draw: stock networks the brush model and then refuses to
/// draw it, with a <c>DrawModel</c> that asserts if anything ever tries.
/// </para>
///
/// <para>
/// What the entity decides is one bit, and this class is the whole of it: which
/// occluder, and whether it is on. The engine call that acts on the answer is
/// CLIENT-side, because occlusion is a rendering decision and the engine keeps
/// the state there — which is why this class networks rather than calling
/// anything, and why <see cref="SourceSharp.Client.Standalone.ClientOccluder"/>
/// is not optional.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncOccluder, ScriptedEntityKind.Brush)]
public sealed class FuncOccluder : ScriptedEntity
{
    public FuncOccluder(IEngineEntityRender render)
    {
        Render = render;
    }

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;
    /// <summary>What a map may set on this entity. See <see cref="FuncOccluderVars"/>.</summary>
    [ScriptVars] public FuncOccluderVars Vars = new();

    [NetState] public OccluderNetState State = new();

    public override void OnSpawn()
    {
        // Nothing collides with it and nothing draws it. Both are stock:
        // SOLID_NONE, MOVETYPE_NONE, and a client half that declines to draw.
        SetSolidity(BrushSolidity.None);
        SetVisible(false);

        State.Index  = Vars.OccluderNumber < 0 ? 0u : (uint)Vars.OccluderNumber;
        State.Active = Vars.StartActive;

        // ALWAYS transmitted, which is stock's FL_EDICT_ALWAYS and is the whole
        // reason it works. An occluder hides geometry that is somewhere else, so
        // the player is by definition not standing in it — PVS-check one and the
        // client is never told about the occluder for the wall it is looking
        // through, which is the case occlusion exists for.
        Render.SetTransmitAlways(Handle, true);
    }

    [ScriptedEvent] private void OnActivate()   => State.Active = true;
    [ScriptedEvent] private void OnDeactivate() => State.Active = false;
    [ScriptedEvent] private void OnToggle()     => State.Active = !State.Active;
}

/// <summary>
/// The map keys <see cref="FuncOccluder"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncOccluderVars
{
    /// <summary>vbsp computed and inserted this. A mapper never types it.</summary>
    public int OccluderNumber { get; set; }

    /// <summary>Whether it starts occluding. The one thing a mapper does set.</summary>
    public bool StartActive { get; set; } = true;
}

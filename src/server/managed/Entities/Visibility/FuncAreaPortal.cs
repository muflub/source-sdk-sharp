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
/// func_areaportal: a hole between two areas of the map, which can be shut.
///
/// <para>
/// The brush is gone before the game ever sees this entity. vbsp cuts the hole,
/// numbers it, and writes the number back as "portalnumber" — func_areaportal.cpp
/// opens by saying so, in as many words. What arrives at runtime is a number and
/// a switch, which is why a "brush entity" here declares no volume and is
/// <see cref="ScriptedEntityKind.Logical"/>.
/// </para>
///
/// <para>
/// Shutting one is what makes a closed door stop the server sending the room
/// behind it. That is the entity's whole purpose and it is decided here. What is
/// NOT here is membership of the engine's portal list: that list is walked once
/// per client per frame to build the area bits the client culls by, and a portal
/// missing from it never gets its bit set — so the client reads the area beyond
/// as closed and the room stops drawing. See shell_areaportal.cpp, which holds
/// the list entry and the two geometric tests and nothing else.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncAreaportal, ScriptedEntityKind.Logical)]
public class FuncAreaPortal : ScriptedEntity
{
    public FuncAreaPortal(IEngineVisibility visibility)
    {
        Visibility = visibility;
    }

    /// <summary>what can be seen, and how it is lit.</summary>
    protected readonly IEngineVisibility Visibility;
    /// <summary>What a map may set on this entity. See <see cref="FuncAreaPortalVars"/>.</summary>
    [ScriptVars] public FuncAreaPortalVars Vars = new();

    /// <summary>The native list entry, or 0 if this entity never got one.</summary>
    protected uint Portal;

    private bool _open = true;

    public override void OnSpawn()
    {
        Portal = Visibility.AreaPortalCreate(Vars.PortalNumber, Vars.PortalVersion);

        if (Portal == 0)
        {
            // Not an error worth stopping for, and worth saying out loud once.
            // A portal number of -1 is what an entity created with ent_create
            // has, and what a mapper's brush has if the compiler declined to
            // make a hole out of it. Either way there is nothing to open or
            // shut, and silence would look identical to a portal that works.
            Log.Msg($"[SourceSharp] {ClassName}: no portal number " +
                       $"({Vars.PortalNumber}); nothing to open or close\n");
            return;
        }

        _open = Vars.StartOpen;
        Apply();
    }

    public override void OnRemoved()
    {
        // Leaves the hole OPEN, which the native side enforces. A portal removed
        // while shut would stay shut in the engine with nobody left to reopen
        // it, and everything beyond it would stop being sent for the rest of the
        // map.
        Visibility.AreaPortalDestroy(Portal);
        Portal = 0;
    }

    [ScriptedEvent] private void OnOpen()   { _open = true;   Apply(); }
    [ScriptedEvent] private void OnClose()  { _open = false;  Apply(); }
    [ScriptedEvent] private void OnToggle() { _open = !_open; Apply(); }

    //-------------------------------------------------------------------------
    // TurnOn and TurnOff, which mean the OPPOSITE of what they look like.
    //
    // Stock maps them to InputClose and InputOpen respectively — turning the
    // portal "on" shuts the hole — and its own datadesc calls them obsolete. Any
    // map old enough to write them expects that reading, so they are carried
    // across as they are rather than as they sound.
    //-------------------------------------------------------------------------
    [ScriptedEvent("TurnOn")]  private void OnTurnedOn()  { _open = false; Apply(); }
    [ScriptedEvent("TurnOff")] private void OnTurnedOff() { _open = true;  Apply(); }

    private void Apply() => Visibility.AreaPortalSetOpen(Portal, _open);
}

/// <summary>
/// The map keys <see cref="FuncAreaPortal"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncAreaPortalVars
{
    /// <summary>vbsp's, not a mapper's. -1 means the compiler made no hole here.</summary>
    public int PortalNumber { get; set; } = -1;

    /// <summary>
    /// Which generation of area portal this is. Version 0 — shipping Half-Life 2
    /// — is always reported OPEN to the client whatever the backface test says,
    /// which the engine's own loop special-cases.
    /// </summary>
    public int PortalVersion { get; set; }

    /// <summary>The map's starting state. Stock reads it in KeyValue and defaults open.</summary>
    public bool StartOpen { get; set; } = true;
}

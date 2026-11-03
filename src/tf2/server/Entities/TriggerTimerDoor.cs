//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;

namespace TF2.Server.Entities;

/// <summary>
/// trigger_timer_door: a capture area whose door opens at the capture's pace
/// (stock's CTriggerTimerDoor, tf_triggers.cpp). The door itself is opened and
/// closed by whatever the map wires to the area's outputs; what this entity
/// owns is the pace — on the first touch it finds the door by name and times
/// its travel to <c>area_time_to_cap</c>, so the gate is fully open exactly
/// when the capture completes. A capture here never scales with mp_capstyle.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class TriggerTimerDoor(
    IGameClock clock,
    IObjectiveSettings settings,
    IObjectiveResource objectives,
    IObjectiveRoundRules roundRules,
    IControlPointRules rules,
    ICapturePlayerRules playerRules,
    ICapturePlayers players,
    IObjectiveEntities entities,
    IObjectiveEvents events,
    ITFPlayerObjectiveTimes? objectiveTimes = null,
    ITFPlayerStats? stats = null,
    IEngineGameEvents? gameEvents = null) : TriggerAreaCapture(clock, settings, objectives, roundRules, rules,
                                                              playerRules, players, entities, events,
                                                              objectiveTimes, stats, gameEvents)
{
    public new const string MapClass = "trigger_timer_door";

    /// <summary>What a map may set on this entity. See <see cref="TriggerTimerDoorVars"/>.</summary>
    [ScriptVars] public TriggerTimerDoorVars DoorVars = new();

    /// <summary>The door the first touch tuned, stock's m_hDoor. A lookup that
    /// failed is retried on the next touch, as stock's null handle is.</summary>
    private FuncDoor? _door;

    /// <summary>The cap runs at the flat time whatever mp_capstyle says (L251).</summary>
    protected override bool CaptureModeScalesWithPlayers => false;

    /// <summary>StartTouch (L280-304): the door is tuned on the same touch that
    /// starts the capture, and a touch that cannot name a door never reaches
    /// the capture at all.</summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (Disabled || !Passes(toucher))
            return;

        if (!TuneDoor())
            return;

        base.OnTouch(toucher);
    }

    private bool TuneDoor()
    {
        if (_door is not null)
            return true;

        _door = FindByName<FuncDoor>(DoorVars.DoorName);
        if (_door is not { } door)
        {
            Log.Warning($"[TF2] {MapClass} failed to find \"{DoorVars.DoorName}\" door entity\n");
            return false;
        }

        // The travel from closed to open, over the cap time: the gate's speed
        // is the distance divided by it (L298-299).
        door.Vars.Speed = door.TravelDistance / CapTime;
        return true;
    }
}

/// <summary>The map keys <see cref="TriggerTimerDoor"/> declares (tf_triggers.cpp:259-261).</summary>
[ScriptVars]
public partial class TriggerTimerDoorVars
{
    [VarName("door_name")] public string DoorName { get; set; } = string.Empty;
}

//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace TF2.Server.Entities.Rooms;

/// <summary>
/// func_respawnroomvisualizer (stock's <c>CFuncRespawnRoomVisualizer</c>,
/// func_respawnroom.cpp): the shimmering wall over a respawn room's doors,
/// which takes the room's team and on/off state.
///
/// <para>
/// Stock sends it only to the other team's players and makes it solid only
/// to them (<c>ShouldTransmit</c>, <c>ShouldCollide</c> by the team's
/// contents bit). The engine seams have neither a per-client transmit nor a
/// per-team collision, so here it is drawn for everyone while its room is
/// active and has a team, and is solid to nobody: a wall solid to all would
/// shut the room's own team in (docs/divergences.md). Its team, room and
/// solid flag are kept as stock keeps them, for the seams when they come.
/// </para>
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class FuncRespawnRoomVisualizer(IEntityDirectory directory) : ScriptedEntity
{
    public const string MapClass = "func_respawnroomvisualizer";

    [ScriptVars] public FuncRespawnRoomVisualizerVars VisualizerVars = new();

    [SaveField] private int _team;
    [SaveField] private bool _solid = true;
    [SaveField] private bool _active;

    /// <summary>The room it names, once RoundActivate found it.</summary>
    public FuncRespawnRoom? Room { get; private set; }

    /// <summary><c>GetTeamNumber</c>: the room's team.</summary>
    public int Team => _team;

    /// <summary>Stock's solid state (<c>SetActive</c>: solid to the enemy, else a trigger).</summary>
    public bool Active => _active;

    /// <summary><c>m_bSolid</c>: whether stock would make it solid to the enemy.</summary>
    public bool SolidToEnemies => _solid;

    /// <summary>
    /// Whether stock would send it to anybody (<c>ShouldTransmit</c> short of
    /// its recipient test): its room is active (or it has none) and it has a
    /// team. The win state's opening is not read here.
    /// </summary>
    public bool Shown => (Room is null || Room.Active) && _team != 0;

    public override void OnSpawn()
    {
        _solid = VisualizerVars.SolidToEnemies;
        _team = VisualizerVars.TeamNum;
        SetSolidity(BrushSolidity.None);
        SetActive(true);
    }

    /// <summary><c>InputRoundActivate</c>: find the room, join it, take its team.</summary>
    [ScriptedEvent]
    private void OnRoundActivate()
    {
        if (VisualizerVars.RespawnRoomName.Length > 0)
        {
            Room = directory.FindByName(VisualizerVars.RespawnRoomName) as FuncRespawnRoom;
            if (Room is not null)
            {
                Room.AddVisualizer(this);
                ChangeTeam(Room.Team);
            }
            else
            {
                Log.Warning($"{MapClass}({Name}) was unable to find func_respawnroomvisualizer named '{VisualizerVars.RespawnRoomName}'\n");
            }
        }

        SetActive(_solid);
    }

    /// <summary><c>InputSetSolid</c>.</summary>
    [ScriptedEvent]
    private void OnSetSolid(float solid)
    {
        _solid = solid != 0f;
        SetActive(_solid);
    }

    /// <summary><c>ChangeTeam</c>: from its room.</summary>
    public void ChangeTeam(int team)
    {
        _team = team;
        Apply();
    }

    /// <summary><c>SetActive</c>: stock's solid/trigger switch; here, what is drawn.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        Apply();
    }

    private void Apply() => SetVisible(Shown);
}

/// <summary>The map keys <see cref="FuncRespawnRoomVisualizer"/> declares (func_respawnroom.cpp's datadesc, and CBaseEntity's TeamNum).</summary>
[ScriptVars]
public partial class FuncRespawnRoomVisualizerVars
{
    [VarName("respawnroomname")] public string RespawnRoomName { get; set; } = string.Empty;

    [VarName("solid_to_enemies")] public bool SolidToEnemies { get; set; } = true;

    public int TeamNum { get; set; }
}

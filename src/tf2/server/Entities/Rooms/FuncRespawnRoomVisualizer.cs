//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared.Rooms;

namespace TF2.Server.Entities.Rooms;

/// <summary>
/// func_respawnroomvisualizer (stock's <c>CFuncRespawnRoomVisualizer</c>,
/// func_respawnroom.cpp): the shimmering wall over a respawn room's doors,
/// which takes the room's team and on/off state.
///
/// <para>
/// Stock sends it always (<c>UpdateTransmitState</c>'s FL_EDICT_ALWAYS; its
/// fuller <c>ShouldTransmit</c> sleeps behind a commented FULLCHECK) and
/// makes it solid to the enemy through the per-entity trace arm
/// (<see cref="TFRespawnRoomCollide"/>); the client hides it from its own
/// team's eyes. Its team, room and solid flag are kept as stock keeps them.
/// </para>
///
/// <para>
/// The always-send is <see cref="IEngineEntityRender.SetTransmitAlways"/>,
/// as every always-sent entity takes it, and the brush is visible to every
/// eye on the wire: the per-team hiding is the client's own answer
/// (<c>ClientRespawnRoomVisualizer</c>), asked at
/// its draw. The team and the brush's box ride the wire for that draw —
/// the team the hiding compares against, the box where the client's shimmer
/// sits.
/// </para>
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class FuncRespawnRoomVisualizer(IEntityDirectory directory, IEngineEntityRender render) : ScriptedEntity
{
    public const string MapClass = "func_respawnroomvisualizer";

    [ScriptVars] public FuncRespawnRoomVisualizerVars VisualizerVars = new();

    /// <summary>The wire state: the draw's team and the brush's box.</summary>
    [NetState] public RespawnRoomVisualizerNetState State = new();

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
    /// The brush's box as the client's draw needs it: where the shimmer's
    /// centre sits off the origin, how wide it reads to an eye looking at it
    /// (the wider of the two horizontal spans — the billboard has one width)
    /// and how tall. Never below a unit each way, so a brush authored with an
    /// empty box still reads as one. Stock's client side gets the same from its
    /// own brush model; our wire carries these numbers to ours.
    /// </summary>
    private void PublishGeometry()
    {
        (Vector mins, Vector maxs) = Entities.GetBounds(Handle);
        Vector centre = (mins + maxs) * 0.5f;

        State.CenterX = centre.X;
        State.CenterY = centre.Y;
        State.CenterZ = centre.Z;
        State.Width = Math.Max(Math.Max(maxs.X - mins.X, maxs.Y - mins.Y), 1f);
        State.Height = Math.Max(maxs.Z - mins.Z, 1f);
    }

    public override void OnSpawn()
    {
        _solid = VisualizerVars.SolidToEnemies;
        _team = VisualizerVars.TeamNum;
        State.Team = (byte)_team;
        PublishGeometry();

        // Stock's UpdateTransmitState (func_respawnroom.cpp:464-469): the FULLCHECK
        // arm is commented out, so the brush is FL_EDICT_ALWAYS and its
        // ShouldTransmit team gate never runs — everybody gets it, and each eye
        // hides it itself. The brush is drawn wherever an eye looks (the client's
        // own draw answer does the hiding); nothing on the server hides it.
        render.SetTransmitAlways(Handle, true);
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
        State.Team = (byte)team;
        Apply();
    }

    /// <summary><c>SetActive</c>: stock's solid/trigger switch; here, what is drawn.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        Apply();
    }

    /// <summary>
    /// Stock's <c>SetActive</c> (func_respawnroom.cpp:527-541) is the solid
    /// switch: on, the brush loses FSOLID_TRIGGER and FSOLID_NOT_SOLID and
    /// traces meet it (the per-entity arm,
    /// <see cref="TFRespawnRoomCollide"/>, then decides who); off, both flags
    /// return and it is a trigger again.
    /// </summary>
    private void Apply()
    {
        SetSolidity(_active ? BrushSolidity.Solid : BrushSolidity.Trigger);

        // Always drawn — stock's FL_EDICT_ALWAYS leaves the hiding to the eyes,
        // not to the send. See the client's draw arms.
        SetVisible(true);
    }
}

/// <summary>The map keys <see cref="FuncRespawnRoomVisualizer"/> declares (func_respawnroom.cpp's datadesc, and CBaseEntity's TeamNum).</summary>
[ScriptVars]
public partial class FuncRespawnRoomVisualizerVars
{
    [VarName("respawnroomname")] public string RespawnRoomName { get; set; } = string.Empty;

    [VarName("solid_to_enemies")] public bool SolidToEnemies { get; set; } = true;

    public int TeamNum { get; set; }
}

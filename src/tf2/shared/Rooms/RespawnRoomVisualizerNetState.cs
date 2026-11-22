//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Rooms;

/// <summary>
/// What func_respawnroomvisualizer tells the client. Stock networks the team
/// through the base entity's <c>m_iTeamNum</c> (<c>DT_BaseEntity</c>), which is
/// what <c>C_FuncRespawnRoomVisualizer::DrawModel</c> compares against the
/// local player (client/tf/c_func_respawnroom.cpp:48-64); the brush it draws
/// is the entity's model. <c>respawnroomname</c> and <c>solid_to_enemies</c>
/// are key data and cross no wire (server/tf/func_respawnroom.cpp:374-380).
/// </summary>
[StandaloneNetState(ClassName)]
public partial class RespawnRoomVisualizerNetState
{
    /// <summary>The entity's classname, stock's LINK_ENTITY_TO_CLASS (func_respawnroom.cpp:372).</summary>
    public const string ClassName = "func_respawnroomvisualizer";

    /// <summary>The visualizer's team, a <see cref="TF2.Shared.TFTeam"/>: whose eyes skip its draw.</summary>
    [NetField(Bits = 2)] public partial byte Team { get; set; }
}

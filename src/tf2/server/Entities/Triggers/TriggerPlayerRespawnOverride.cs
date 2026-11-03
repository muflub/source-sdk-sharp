//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace TF2.Server.Entities.Triggers;

/// <summary>
/// trigger_player_respawn_override (stock's <c>CTriggerPlayerRespawnOverride</c>,
/// tf_triggers.h:53-72, tf_triggers.cpp:505-525): a volume holding the pair of
/// <c>RespawnTime</c>/<c>RespawnName</c> that <see cref="Player.TFRespawnOverrideWalk"/>
/// hands a player who is inside it when they die — the timed hold on the wave
/// and the named spawn to come back at. The volume itself decides nothing on
/// touch: stock's <c>CTriggerMultiple</c> <c>StartTouch</c> only fires the
/// client-side outputs, and the death walk asks
/// <c>!m_bDisabled && IsTouching(pPlayer)</c> (tf_player.cpp:12890), which
/// <see cref="Overrides"/> answers.
///
/// <para>
/// Stock's <c>IsTouching</c> reads the engine's touch links; the volume's own
/// inside-set is this build's touch list, as in <see cref="TriggerStun"/> —
/// who the engine has reported entering and not yet leaving (docs/divergences.md).
/// </para>
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class TriggerPlayerRespawnOverride(IGameClock clock) : TFPlayerTrigger(clock)
{
    public const string MapClass = "trigger_player_respawn_override";

    [ScriptVars] public RespawnVars RespawnVars = new();

    /// <summary>
    /// The death walk's question (tf_player.cpp:12890): enabled, and holding
    /// this player.
    /// </summary>
    public bool Overrides(EntityHandle player) => !Disabled && Inside.Contains(player);

    /// <summary><c>InputSetRespawnTime</c> (tf_triggers.h:62): the pair's time.</summary>
    [ScriptedEvent] private void OnSetRespawnTime(float value) => RespawnVars.RespawnTime = value;

    /// <summary><c>InputSetRespawnName</c> (tf_triggers.h:63): the pair's named spot.</summary>
    [ScriptedEvent] private void OnSetRespawnName(string value) => RespawnVars.RespawnName = value;

    /// <summary>Stock's <c>CTriggerMultiple</c> touch entry has no server work; the pair's readers are the death walk's.</summary>
    protected override void StartTouch(EntityHandle toucher) { }
}

/// <summary>
/// The map keys <see cref="TriggerPlayerRespawnOverride"/> declares
/// (tf_triggers.cpp:520-521's datadesc); stock has no constructor, so the
/// members stand zeroed — the time at 0 and the name at <c>NULL_STRING</c>.
/// </summary>
[ScriptVars]
public partial class RespawnVars
{
    /// <summary><c>m_flRespawnTime</c>: the seconds the death holds the player out of the waves.</summary>
    [VarName("RespawnTime")] public float RespawnTime { get; set; }

    /// <summary><c>m_strRespawnEnt</c>: the named team spawn to come back at; "" is stock's <c>NULL_STRING</c>.</summary>
    [VarName("RespawnName")] public string RespawnName { get; set; } = string.Empty;
}

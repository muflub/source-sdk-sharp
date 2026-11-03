//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Medigun;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Server.Player;

/// <summary>
/// The class a player has asked to be (Stage 2.3's ClassComponent):
/// <c>CTFPlayer</c>'s <c>m_Shared.m_iDesiredPlayerClass</c>
/// (<c>SetDesiredPlayerClassIndex</c>), which <c>joinclass</c> sets and the
/// next spawn makes the class (<see cref="TFPlayerSpawns"/>, stock's
/// <c>ForceRespawn</c>). The class itself is the carrier's
/// (<c>m_iClass</c>). Enters <see cref="TFPlayerCarriers"/> as a part of the
/// player; its detach also drops the player's items.
/// </summary>
public sealed class TFPlayerClass(TFPlayerCarriers carriers, TFPlayerItems items) : IPlayerComponent
{
    private int _index = -1;

    /// <summary>After the carrier (0), which the class lives on.</summary>
    public int Order => 10;

    /// <summary><c>GetDesiredPlayerClassIndex</c>: <see cref="TFClass.Undefined"/> until the first <c>joinclass</c>.</summary>
    public TFClass Desired { get; set; } = TFClass.Undefined;

    /// <summary><c>m_iClassChanges</c>: how many times a spawn changed the class.</summary>
    public int ClassChanges { get; set; }

    /// <summary>
    /// <c>CTFPlayer</c>'s <c>m_bIsClassMenuOpen</c> (tf_player.h:1244): the
    /// class menu is up. The <c>menuopen</c>/<c>menuclosed</c> commands set it
    /// (<see cref="TFClassMenuCommands"/>, stock's <c>SetClassMenuOpen</c>,
    /// tf_player.cpp:8073-8076) and a class choice fakes it closed
    /// (:6781-6783); while it stands the respawn waves hold the player
    /// (<c>IsReadyToSpawn</c>, :3243) and grant the instant spawn the choice
    /// spends (:3278 through teamplayroundbased_gamerules.cpp:2661-2666).
    /// Stock never clears it at a spawn: the flag is the menu's, not the life's.
    /// </summary>
    public bool MenuOpen { get; set; }

    /// <summary>
    /// <c>CTFPlayer::m_bAllowInstantSpawn</c>: a grant to spawn the moment
    /// this player next picks a class (<c>AllowInstantSpawn()</c>,
    /// tf_player.h). <c>game_forcerespawn</c> grants it to the players its
    /// walk skips — the off-team and classless ones
    /// (<c>entity_forcerespawn.cpp:62-67</c>, <c>:82-87</c>) — the
    /// winners-round loser loses it (tf_player.cpp:6838-6842), and the spawn
    /// consumes it (tf_player.cpp:3710). Stock's other granter, the MvM
    /// between-rounds arm (tf_player.cpp:7029-7030), is not in this build.
    /// </summary>
    public bool AllowInstantSpawn { get; set; }

    public void Attach(EntityHandle player)
    {
        _index = player.EntryIndex;
        carriers.EnterPart(_index, this);
    }

    public void Tick(float now) { }

    public void Detach()
    {
        if (_index < 0)
            return;

        carriers.LeavePart(_index, this);
        items.Forget(_index);
        _index = -1;
    }
}

/// <summary>
/// The per-frame half of the player's conditions (Stage 2.3's
/// ConditionsComponent): <c>ConditionThink</c>'s ground-contact arm
/// (tf_player_shared.cpp:3124-3131) and <c>ConditionGameRulesThink</c>'s
/// countdown (tf_player_shared.cpp:2338-2375, <see cref="TFConditions.Tick"/>)
/// over the frame's time, once per player per server frame, and the removal
/// arms of what ran out (<see cref="TFPlayerConditions.Expire"/>). The healer
/// count comes from the medigun lane's healing (2.9), which
/// <c>GetNumHealers</c> answers; without it (a container with no medigun lane)
/// the count is none, as 2.3 had it.
/// </summary>
public sealed class TFPlayerConditionTicker(TFPlayerCarriers carriers, TFPlayerConditions conditions,
                                            IEngineNetworking networking, ITFPlayerHealing? healing = null)
    : IPlayerComponent
{
    private readonly TFConditionEffects _effects = new();
    private EntityHandle _player = EntityHandle.None;
    private float _last = float.NaN;

    /// <summary>After the class (10): the countdown runs on the class's carrier.</summary>
    public int Order => 20;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _last = float.NaN;
    }

    public void Tick(float now)
    {
        if (carriers.ConditionsOf(_player.EntryIndex) is not { } set)
            return;

        // ConditionThink's ground-contact arm (tf_player_shared.cpp:3124-3128):
        // the airborne conditions end on ground contact. The parachute pair
        // (:3130-3131) waits for the parachute item (docs/plans/tf2/deferred.tsv).
        // The set is read before the engine, as the flag read has no say.
        if ((set.Has(TFCond.KnockedIntoAir) || set.Has(TFCond.AirCurrent))
            && networking.EntityGetState(_player, out _).HasFlag(EntityState.OnGround))
        {
            conditions.Remove(_player, TFCond.KnockedIntoAir);
            conditions.Remove(_player, TFCond.AirCurrent);
        }

        float frametime = float.IsNaN(_last) ? 0f : now - _last;
        _last = now;
        if (frametime <= 0f)
            return;

        TFCondMask expired = set.Tick(frametime, healers: healing?.NumHealers(_player.EntryIndex) ?? 0,
                                       _effects.ExpiresFastUnderHealing);
        if (!expired.IsEmpty)
            conditions.Expire(_player, in expired);
    }

    public void Detach() => _player = EntityHandle.None;
}

/// <summary>
/// The rest of stock's <c>CTFPlayer::IsReadyToPlay</c> the round machine asks
/// (<see cref="ITeamRoundReadiness"/>): a player has chosen a class
/// (<c>GetDesiredPlayerClassIndex() &gt; TF_CLASS_UNDEFINED</c>,
/// tf_player.cpp:3219-3224).
/// </summary>
[SideService(Side.Server, As = [typeof(ITeamRoundReadiness)])]
public sealed class TFPlayerReadiness(TFPlayerCarriers carriers) : ITeamRoundReadiness
{
    public bool HasChosenClass(EntityHandle player)
        => carriers.Part<TFPlayerClass>(player.EntryIndex)?.Desired > TFClass.Undefined;
}

/// <summary>Every class's player model, precached as each level loads (<c>CTFPlayer::PrecachePlayerModels</c>, tf_player.cpp:2848).</summary>
[SideService(Side.Server)]
public sealed class TFClassModels(TFClassTable classes, IEnginePrecache precache) : IGameLifecycle
{
    /// <summary>A stage of its own in the TF player's band, after the class table's load.</summary>
    public int LifecycleOrder => TFPlayerOrders.ClassModels;

    void IGameLifecycle.OnLevelInitPreEntity(string mapName)
    {
        for (var c = TFClass.Undefined; c < TFClass.CountAll; c++)
        {
            if (classes[c].Model is { Length: > 0 } model)
                precache.PrecacheModel(model);
            if (classes[c].ModelHands is { Length: > 0 } hands)
                precache.PrecacheModel(hands);
        }
    }
}

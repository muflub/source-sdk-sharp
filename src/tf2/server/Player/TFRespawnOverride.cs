//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities.Triggers;

namespace TF2.Server.Player;

/// <summary>
/// <c>CTFPlayer</c>'s respawn override pair — <c>m_flRespawnTimeOverride</c>
/// and <c>m_strRespawnLocationOverride</c> (tf_player.h:1449-1450) — the
/// server half, as a carrier part. The death's walk
/// (<see cref="TFRespawnOverrideWalk"/>) sets it from the
/// <see cref="TriggerPlayerRespawnOverride"/> holding the dead player, exactly
/// as stock's <c>SetRespawnOverride</c> (tf_player.h:1092) is called there,
/// and the three readers ask it: the wave hold beside the round machine's
/// minimum respawn time (stock's <c>IsReadyToSpawn</c>, tf_player.cpp:3266,
/// through its one caller's gate at teamplayroundbased_gamerules.cpp:2660),
/// the forced release in the TF wave check (tf_gamerules.cpp:9502), the
/// resource row's respawn time (tf_player_resource.cpp:274-275) and the
/// named-spot arm of the spawn walk (tf_player.cpp:5745-5757).
///
/// <para>
/// Stock re-reads <c>GetDeathTime() + GetRespawnTimeOverride()</c> at every
/// check; this build fixes the same instant once, at the walk, as
/// <see cref="ReleaseAt"/>: the walk runs on the death's own frame, and every
/// reader is a later frame of the same life (the pair goes at
/// <see cref="ClearOnSpawn"/>, stock's <c>SetRespawnOverride(-1.f,
/// NULL_STRING)</c> of <c>Spawn</c>, :1089).
/// </para>
/// </summary>
public sealed class TFRespawnOverride(TFPlayerCarriers carriers, IGameClock clock) : IPlayerComponent
{
    // m_flRespawnTimeOverride, stock's CTFPlayer constructor's -1 (the pair's
    // "no override" answer), and m_strRespawnLocationOverride's NULL_STRING
    // as null.
    private float _time = -1f;
    private string? _location;
    private float _releaseAt;
    private int _index = -1;

    /// <summary>After the stun stack (30) in TF2's composite block.</summary>
    public int Order => 40;

    /// <summary><c>GetRespawnTimeOverride() != -1.f</c> — what every reader asks first.</summary>
    public bool HasOverride => _time != -1f;

    /// <summary><c>GetRespawnTimeOverride()</c>.</summary>
    public float OverrideTime => _time;

    /// <summary><c>GetRespawnLocationOverride()</c>: the named spot, or null for <c>NULL_STRING</c>.</summary>
    public string? Location => _location;

    /// <summary>The moment stock's readers would say <c>curtime</c> passed: the death's frame plus the time.</summary>
    public float ReleaseAt => _releaseAt;

    public void Attach(EntityHandle player)
    {
        _index = player.EntryIndex;
        Clear();
        carriers.EnterPart(_index, this);
    }

    public void Tick(float now) { }

    public void Detach()
    {
        if (_index >= 0)
            carriers.LeavePart(_index, this);
        _index = -1;
        Clear();
    }

    /// <summary>
    /// <c>SetRespawnOverride( time, location )</c> (tf_player.h:1092): the
    /// pair, and the run-out moment fixed off this frame — the death's own,
    /// when the walk calls it.
    /// </summary>
    public void Apply(float time, string? location)
    {
        _time = time;
        _location = location;
        _releaseAt = clock.Now + time;
    }

    /// <summary>The pair, gone — stock's <c>SetRespawnOverride( -1.f, NULL_STRING )</c>.</summary>
    public void Clear()
    {
        _time = -1f;
        _location = null;
        _releaseAt = 0f;
    }

    /// <summary>
    /// <c>CTFPlayer::Spawn</c>'s clear of the pair (tf_player.cpp:1089), run
    /// with the rest of the old life by <see cref="TFPlayerSpawns"/>.
    /// </summary>
    public void ClearOnSpawn() => Clear();
}

/// <summary>
/// The death's walk of the override triggers: stock's
/// <c>FOR_EACH_AUTO_LIST</c> over the <c>ITriggerPlayerRespawnOverride</c>
/// list in <c>CTFPlayer::OnDeath</c> (tf_player.cpp:12884-12901) — the first
/// enabled volume holding the dead player gives them its pair
/// (<c>SetRespawnOverride( GetRespawnTime(), GetRespawnName() )</c> and the
/// <c>break</c>, :12894-12896), and one that does not clears the pair
/// (:12898) — so first match wins, and no match means clear.
///
/// <para>
/// What this build leaves out of the arms around it (docs/divergences.md):
/// stock's <c>!IsMannVsMachineMode() || !IsBot()</c> gate (:12886) is deferred
/// with the mode, so every death walks; and stock's empty-autolist quirk (no
/// iteration, the pair kept) is invisible here because the pair already stands
/// cleared at every spawn (<see cref="TFRespawnOverride.ClearOnSpawn"/>).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFRespawnOverrideWalk(IEntityDirectory directory, TFPlayerCarriers carriers)
    : IPlayerDeathHook, IOrderedHook
{
    /// <summary>The walk's order in the player band, behind the death's ammo pack.</summary>
    public const int Order = TFPlayerOrders.DeathRespawnOverride;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public void OnPlayerDied(PlayerDeath death)
    {
        if (carriers.Part<TFRespawnOverride>(death.Player.EntryIndex) is not { } held)
            return;

        foreach (var trigger in directory.OfClass(TriggerPlayerRespawnOverride.MapClass)
                                         .OfType<TriggerPlayerRespawnOverride>())
        {
            // :12890: !m_bDisabled && IsTouching( pPlayer ).
            if (!trigger.Overrides(death.Player))
                continue;

            // :12894: the pair, as the volume holds it; the "" name is stock's NULL_STRING.
            held.Apply(trigger.RespawnVars.RespawnTime,
                       trigger.RespawnVars.RespawnName.Length > 0 ? trigger.RespawnVars.RespawnName : null);
            return; // :12896: the first match wins
        }

        held.Clear(); // :12898: nothing held them
    }
}

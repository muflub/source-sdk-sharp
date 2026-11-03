//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Shared.Combat;

namespace TF2.Server.Buildings;

/// <summary>
/// The buildings' kill book (<c>CTFGameRules::PlayerKilled</c>'s object arms,
/// <c>shared/tf/tf_gamerules.cpp:11315-11414</c>), as a death-notice hook
/// after the TF death notice's (<c>TF2.Server.Combat.TFDeathNotice</c>):
///
/// <para>
/// — the kills (<c>:11324-11365</c>): the inflictor that is a building — or
///   whose owner is, the sentry rocket's case, our rocket carrying its
///   <see cref="TFSentryRocket.Launcher"/> where stock reads the engine's
///   owner — takes <c>IncrementKills</c>, unless it killed its own builder.
/// </para>
/// <para>
/// — the sentry assist (<c>:11397-11412</c>): when the death has an assister
///   (<see cref="TFDamageRecords"/>' history, the same call the notice makes),
///   the assister's entry in the victim's sentry-damager history
///   (<see cref="TFSentryDamages"/>) hands <c>IncrementAssists</c> to that
///   sentry.
/// </para>
///
/// Both counters are the status panel's kills row
/// (<c>hud_obj_sentrygun.res</c>'s <c>KillsLabel</c>). The MvM miniboss's
/// extra kills and the owner achievements ride with those modes.
/// </summary>
[SideService(Side.Server)]
public sealed class TFBuildingScores(IEntityDirectory directory, IEngineEntities entities, TFDamageRecords records,
                                     TFSentryDamages sentryDamages, IGameClock clock)
    : IDeathNoticeHook, IOrderedHook
{
    /// <summary>The buildings' band (+930..+939 of TF2's thousand; 930-932 taken).</summary>
    public const int Order = 1_000_000 - 1_000 + 933;

    public int HookOrder => Order;

    public void Fill(in DeathNoticeFacts death, IDeathNoticeFields fields)
    {
        // The building behind the kill: the inflictor, else its owner — the
        // rocket's engine owner is the builder here (FlyingProjectile's
        // SetOwner), so the rocket names its launcher itself.
        TFBaseObject? killer = death.Inflictor != EntityHandle.None ? directory.Get(death.Inflictor) as TFBaseObject : null;
        killer ??= death.Inflictor != EntityHandle.None
            && directory.Get(death.Inflictor) is TFSentryRocket { Launcher: { } launcher }
            && launcher != EntityHandle.None ? directory.Get(launcher) as TFBaseObject : null;

        if (killer is not null && killer.Builder != death.Victim)
            killer.IncrementKills();

        // The assister, exactly as the notice names them, then their sentry
        // wound on this victim within the window.
        int victimIndex = death.Victim.EntryIndex;
        EntityHandle assister = death.Scorer != EntityHandle.None && death.Scorer != death.Victim
            ? records.RecentDamager(victimIndex, 1, TFCombatDefs.TimeAssistKill, clock.Now)
            : EntityHandle.None;
        if (assister == death.Scorer || (assister != EntityHandle.None && !entities.IsValid(assister)))
            return;

        EntityHandle sentry = sentryDamages.DamagedBy(victimIndex, assister, clock.Now);
        if (sentry != EntityHandle.None && directory.Get(sentry) is TFBaseObject woundBy)
            woundBy.IncrementAssists();
    }
}

//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The freeze panel's data (stock's <c>CTFFreezePanel::FireGameEvent</c> and
/// its reads, tf_hud_freezepanel.cpp): the local player's death snaps the
/// killer's row — name, team, class, health — the way stock's panel reads
/// the killer once. The killer is the death's attacker user id, found the
/// way the kill feed does (g_PR's rows); a suicide or a world death has no
/// killer row, and the panel draws nothing (stock's freeze cam never starts
/// without an observer target, tf_player.cpp:12487-12490, :13956-13968).
/// <see cref="Showing"/>
/// falls when the local player lives again, stock's <c>hide_freezepanel</c>.
///
/// <para>
/// Divergences (docs/divergences.md): the nemesis arms read the death's
/// flags, where stock reads the killer's live replicated
/// <c>m_bPlayerDominated</c> at panel time — an already-dominating killer's
/// later kills show no nemesis panel (the per-pair bitfield is not carried);
/// and the panel shows at the death, where stock shows on entering the
/// freeze cam.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFDeathScreen)])]
public sealed class TFDeathScreen : ITFDeathScreen
{
    private const int Slots = PlayerResourceNetState.Slots;

    private readonly IEngineHud _screen;
    private readonly IClientStandalones _standalones;
    private readonly IPlayerNames _names;
    private readonly TFClientPlayerResources _resources;

    // The last local death, held until the player lives again.
    private bool _dead;
    private int _attackerUserId;                     // 0: no killer row (the world, or their own hand)
    private int _flags;

    public TFDeathScreen(IEngineHud screen, IClientStandalones standalones, IPlayerNames names,
                         TFClientPlayerResources resources, EventLink events)
    {
        (_screen, _standalones, _names, _resources) = (screen, standalones, names, resources);
        events.OnDeath(OnDeath);      // stock's ListenForGameEvent in the panel's constructor
    }

    public bool Showing => _dead && _screen.LocalPlayerHealth <= 0;

    public int DeathFlags => _flags;

    public bool HasKiller => _attackerUserId > 0;

    public string KillerName
        => Row(_attackerUserId, out int slot) && _names.TryGet(slot, out ClientPlayerInfo info) ? info.Name : "";

    public int KillerTeam
        => Row(_attackerUserId, out int slot) && _resources.Live is { } live ? live.State.Team[slot] : 0;

    public string KillerClass
        => Row(_attackerUserId, out int slot) && _resources.Live is { } live
            && live.State.PlayerClass[slot] is var tfClass && tfClass != (byte)TFClass.Undefined
                ? TFClassNames.NonLocalized((TFClass)tfClass)
                : "";

    public int KillerHealth
        => Row(_attackerUserId, out int slot) && Core() is { } core
            ? (core.State.IsAlive(slot) ? Math.Max(0, (int)core.State.Health[slot]) : 0)
            : 0;

    public int KillerMaxHealth
        => Row(_attackerUserId, out int slot) && _resources.Live is { } live ? live.State.MaxHealth[slot] : 0;

    public int KillerMaxBuffedHealth
        => Row(_attackerUserId, out int slot) && _resources.Live is { } live ? live.State.MaxBuffedHealth[slot] : 0;

    public bool KillerAlive
        => Row(_attackerUserId, out int slot) && Core() is { } core && core.State.IsAlive(slot);

    /// <summary>One player_death (stock FireGameEvent): the local death is
    /// the one the panel snaps; the attacker of their own death is no
    /// killer (stock's <c>!pKiller</c> arm, tf_hud_freezepanel.cpp:568-599).</summary>
    private void OnDeath(PlayerDeathArgs death)
    {
        if (death.UserId != LocalUserId())
            return;
        _dead = true;
        _attackerUserId = death.AttackerUserId == death.UserId ? 0 : death.AttackerUserId;
        _flags = death.DeathFlags;
    }

    /// <summary>The local player's user id, from the core resource's rows.</summary>
    private int LocalUserId()
    {
        int index = _screen.LocalPlayerIndex;
        return index > 0 && index < Slots && Core() is { } core ? core.State.UserId[index] : 0;
    }

    /// <summary>The player resource's row with a user id, the way the kill
    /// feed finds one (KillFeedPlayers).</summary>
    private bool Row(int userId, out int slot)
    {
        slot = 0;
        if (userId <= 0 || Core() is not { } core)
            return false;
        for (int i = 1; i < Slots; i++)
        {
            if (core.State.IsConnected(i) && core.State.UserId[i] == userId)
            {
                slot = i;
                return true;
            }
        }
        return false;
    }

    private ClientPlayerResource? Core()
        => _standalones.OfClass(EntityNames.PlayerManager).OfType<ClientPlayerResource>().FirstOrDefault();
}

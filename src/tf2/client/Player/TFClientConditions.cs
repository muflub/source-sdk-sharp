//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Client.Player;

/// <summary>
/// The client twin of the server's <c>ITFConditionHook</c>
/// (<c>TF2.Server.Player</c>): an arm run when a player's networked condition
/// words move, as stock runs <c>OnConditionAdded</c>/<c>OnConditionRemoved</c>
/// on the client when a decode carries a changed bit
/// (<c>CTFPlayerShared::SyncConditions</c>, <c>tf_player_shared.cpp:1504-1538</c>).
/// The server's chain lives in the server assembly, so the client's arms
/// register here. Told after the decoded state holds the new words.
/// </summary>
public interface ITFClientConditionHook
{
    /// <summary>A condition's bit turned on in a decode.</summary>
    void OnAdded(EntityHandle player, TFCond cond);

    /// <summary>A condition's bit turned off in a decode.</summary>
    void OnRemoved(EntityHandle player, TFCond cond);
}

/// <summary>
/// The client condition driver: the diff stock's <c>SyncConditions</c> makes
/// between the last decoded words and the new ones, word by word, bit by bit
/// ascending, a word's adds before its removes (:1509-1535) — each to every
/// <see cref="ITFClientConditionHook"/>, for every player the client carries,
/// the local one included. Stateless: the previous words are the carrier's
/// (<see cref="ClientTFPlayerData"/> holds them, as stock's
/// <c>m_nOldConditions</c> netvars ride the entity).
///
/// <para>
/// Stock's force-word branch (:1517-1523) is not here: nothing re-fetches
/// conditions client-side (<c>docs/plans/tf2/deferred.tsv</c>, the
/// ForceRecondNextSync row).
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFClientConditionDriver(IHooks<ITFClientConditionHook> hooks)
{
    /// <summary>
    /// Every bit <paramref name="was"/> and <paramref name="now"/> disagree
    /// on, as stock's loop: per word, the added bits' arms then the removed
    /// ones, bits ascending.
    /// </summary>
    public void Sync(EntityHandle player, in TFCondMask was, in TFCondMask now)
    {
        for (int word = 0; word < TFCondMask.WordCount; word++)
        {
            int changed = was.WordAt(word) ^ now.WordAt(word);
            if (changed == 0)
                continue;

            int added = changed & now.WordAt(word);
            int removed = changed & was.WordAt(word);

            for (int bit = 0; bit < 32; bit++)
            {
                var cond = (TFCond)((word << 5) + bit);

                if ((added & (1 << bit)) != 0)
                    hooks.Each(nameof(ITFClientConditionHook.OnAdded), h => h.OnAdded(player, cond));
                else if ((removed & (1 << bit)) != 0)
                    hooks.Each(nameof(ITFClientConditionHook.OnRemoved), h => h.OnRemoved(player, cond));
            }
        }
    }
}

/// <summary>The client condition hooks' orders: the first free band after the player's (+300..+339).</summary>
public static class TFClientConditionOrders
{
    private const int Band = 1_000_000 - 1_000 + 340;

    /// <summary>The burning player's particle (the burn arm).</summary>
    public const int BurningEffect = Band + 1;

    /// <summary>The crit boost's weapon glow (the crit arm).</summary>
    public const int CritBoostEffect = Band + 2;

    /// <summary>The jarate's and the milk's drips (the drips arm).</summary>
    public const int Drips = Band + 3;

    /// <summary>The conditions' screen overlays (the overlays arm).</summary>
    public const int Overlays = Band + 4;

    /// <summary>The local player's taunt camera (stock's HandleTaunting cam arms, c_tf_player.cpp:5540-5615).</summary>
    public const int TauntCamera = Band + 5;
    /// <summary>The team-coloured teammate glows of <c>TF_COND_TEAM_GLOWS</c> (the team glow arm).</summary>
    public const int TeamGlows = Band + 6;

    /// <summary>The blast jump's feet smoke (the smoke arm).</summary>
    public const int BlastJumpSmoke = Band + 7;
}

/// <summary>The TF2 client's condition seam: the driver's chain, which only a registrar can name.</summary>
[SideService(Side.Client)]
public sealed class TFClientConditionRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<ITFClientConditionHook>();
}

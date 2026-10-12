//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Stock's UTIL_FindClientInPVS (util.cpp): one "check client" at a time,
/// moved on to the next player slot after the last one whenever a tenth of a
/// second has passed (UTIL_GetCurrentCheckClient / UTIL_GetNewCheckClient),
/// and answered only when a box is in that client's PVS from its eye. The
/// decision is here; the engine's cluster-and-PVS test is the generic
/// IEnginePlayers.BoxInPvs (the player's eyes). One per composition (statics to zero).
/// </summary>
public sealed class PvsCheckClient(IEntityDirectory directory, IEngineEntities entities, IEnginePlayers players)
{
    /// <summary>How long one check client lasts.</summary>
    public const float Interval = 0.1f;

    private int _lastCheck = 1;
    private float _lastCheckTime = -1f;

    /// <summary>The check client if <paramref name="mins"/>..<paramref name="maxs"/> may be visible to it, else null.</summary>
    public SsPlayer? FindClientInPvs(Vector mins, Vector maxs, float now)
    {
        float delta = now - _lastCheckTime;
        if (delta >= Interval || delta < 0f)
        {
            _lastCheck = NextCheck(_lastCheck);
            _lastCheckTime = now;
        }

        SsPlayer? check = PlayerAt(_lastCheck);
        if (check is null)
            return null;

        return players.BoxInPvs(check.Handle, mins, maxs) ? check : null;
    }

    // UTIL_GetNewCheckClient: the next occupied slot after `check`, wrapping;
    // `check` itself when no other is occupied.
    private int NextCheck(int check)
    {
        var slots = Players().Select(p => p.Slot).OrderBy(s => s).ToArray();
        if (slots.Length == 0)
            return check;

        foreach (int slot in slots)
        {
            if (slot > check)
                return slot;
        }

        return slots[0] == check && slots.Length == 1 ? check : slots[0];
    }

    private SsPlayer? PlayerAt(int slot)
        => Players().FirstOrDefault(p => p.Slot == slot).Player;

    private IEnumerable<(int Slot, SsPlayer Player)> Players()
    {
        foreach (var entity in directory.OfClass(EntityNames.SsPlayer))
        {
            if (entity is SsPlayer player)
                yield return (entities.GetIndex(player.Handle), player);
        }
    }
}

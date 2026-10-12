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
/// Asks, on demand, who is standing in a volume and who is not.
///
/// The odd one of the game_* family: a brush rather than a point, and the only
/// one that reads the world instead of acting on the activator. A map fires
/// CountPlayersInZone and gets four answers back — one event per player inside,
/// one per player outside, and the two totals — which is how a map gates a door
/// on "everybody is in the lift" without polling anything.
///
/// It is deliberately not a trigger. A trigger learns about occupancy from
/// crossings, so it only knows about people who moved since it spawned; this is
/// asked a question and answers it from where everyone is right now, which is
/// what a map counting heads at a moment actually wants.
/// </summary>
[ScriptedEntity(EntityNames.GameZonePlayer, ScriptedEntityKind.Brush)]
public sealed class GameZonePlayer : ScriptedEntity
{
    public GameZonePlayer(IEnginePlayers players)
    {
        Players = players;
    }

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    public readonly Output OnPlayerInZone = new();
    public readonly Output OnPlayerOutZone = new();
    public readonly Output PlayersInCount = new();
    public readonly Output PlayersOutCount = new();

    // A slot with nobody in it answers with an invalid handle, so overshooting
    // the server's real maxplayers costs a few no-ops rather than a crossing to
    // ask how many there are.
    private const int MaxPlayerSlots = 64;

    public override void OnSpawn()
    {
        // The brush is kept for its shape alone: nothing may walk into it and
        // nothing draws it. The volume is still there to be measured against,
        // because passable and absent are not the same thing.
        SetSolidity(BrushSolidity.None);
        SetVisible(false);
    }

    [ScriptedEvent("CountPlayersInZone")]
    private void OnCountPlayersInZone()
    {
        var (zoneMins, zoneMaxs) = Entities.GetBounds(Handle);
        var zoneOrigin = Origin;

        zoneMins += zoneOrigin;
        zoneMaxs += zoneOrigin;

        int inside = 0;
        int outside = 0;

        for (int slot = 1; slot <= MaxPlayerSlots; slot++)
        {
            var player = Players.GetHandle(slot);
            if (!Entities.IsValid(player))
                continue;

            var (playerMins, playerMaxs) = Entities.GetBounds(player);
            var playerOrigin = Entities.GetOrigin(player);

            if (Overlaps(zoneMins, zoneMaxs, playerMins + playerOrigin, playerMaxs + playerOrigin))
            {
                inside++;
                OnPlayerInZone.Fire();
            }
            else
            {
                outside++;
                OnPlayerOutZone.Fire();
            }
        }

        PlayersInCount.Fire(inside);
        PlayersOutCount.Fire(outside);
    }

    /// <summary>
    /// Whether two boxes share any space at all.
    ///
    /// Touching counts as overlapping, which is what stock's trace answers: a
    /// player standing exactly on the boundary of a zone is in it, and the
    /// alternative is a map where the lift doors never close because somebody's
    /// toes are on the line.
    /// </summary>
    internal static bool Overlaps(Vector aMins, Vector aMaxs, Vector bMins, Vector bMaxs)
        => aMins.X <= bMaxs.X && aMaxs.X >= bMins.X
        && aMins.Y <= bMaxs.Y && aMaxs.Y >= bMins.Y
        && aMins.Z <= bMaxs.Z && aMaxs.Z >= bMins.Z;
}

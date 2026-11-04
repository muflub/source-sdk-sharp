using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// <c>tf2_crits [player]</c>: the crit buckets of a player's weapon on the
/// server, row by row — tokens, checks, seed requests, and the first rolls
/// made and crit — with the player's class, crit multiplier and round-win
/// arm. The server log's view of the crit roll for a live check.
/// </summary>
[ConCommand("tf2_crits", Help = "A player's TF crit buckets and rolls. tf2_crits [player]")]
public sealed class TFCritReportCommand(ILog log, TFCritBuckets buckets, ITFWeaponOwners owners, WeaponCatalog weapons,
                                        IEnginePlayers players, IEngineEntities entities, ITFCarriedWeapons carried)
{
    public void Execute(int index = 1)
    {
        var player = players.GetHandle(index);
        if (!entities.IsValid(player))
        {
            log.Warning($"[TF2/Crits] no player {index}\n");
            return;
        }

        var weapon = carried.WeaponOf(player);
        int weaponIndex = weapon == EntityHandle.None ? -1 : entities.GetIndex(weapon);
        log.Msg($"[TF2/Crits] player {index} ({TFClassNames.NonLocalized(owners.ClassOf(index))}), weapon entity " +
                $"{weaponIndex}: crit mult {owners.CritMult(index):F2}, round-win crits {owners.WinsCrits(index)}\n");

        int rows = 0;
        foreach (var (typeId, state) in buckets.Of(weaponIndex))
        {
            rows++;
            string name = weapons.ByIndex(typeId)?.Name ?? $"row {typeId}";
            log.Msg($"  {name}: bucket {state.Bucket.Tokens:F1} tokens, {state.Bucket.Checks} check(s), " +
                    $"{state.Bucket.SeedRequests} seed request(s); {state.Rolls} roll(s), {state.Crits} crit(s), " +
                    $"last {(state.Last.Critical ? "CRIT" : "no crit")}{(state.Last.Random ? " (random)" : "")}\n");
        }

        if (rows == 0)
            log.Msg("  no bucket yet: the weapon has not rolled since its spawn\n");
    }
}

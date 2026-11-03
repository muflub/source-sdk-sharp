//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Buildings;

namespace TF2.Server.Combat;

/// <summary>
/// Who may hurt whom in TF (<see cref="IDamageRule"/>):
/// <c>CTFGameRules::FPlayerCanTakeDamage</c> (tf_gamerules.cpp:9808) over
/// <c>CTeamplayRules::FPlayerCanTakeDamage</c> (teamplay_gamerules.cpp:361).
/// A building hurts its own builder and the enemy, never its builder's
/// teammates; a player's teammate takes nothing from them while
/// <c>mp_friendlyfire</c> is 0, and one's own damage always lands (a rocket
/// jump). The core's own friendly-fire knob is its <c>cs_sv_friendly_fire</c>;
/// TF reads stock's. The truce arm waits for a truce.
/// </summary>
[SideService(Side.Server)]
public sealed class TFTeamDamageRule(ConVarRegistry convars, IEnginePlayers players, IEngineEntities entities,
                                     IEntityDirectory directory) : IDamageRule, IOrderedHook
{
    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 706;

    public int HookOrder => Order;

    public Decision Decide(in DamageContext hit)
    {
        if (hit.Attacker == EntityHandle.None || hit.Attacker == hit.Victim || !entities.IsValid(hit.Attacker))
            return Decision.UseStockRule;

        int victimTeam = players.GetTeam(hit.Victim);

        // An object hurts only its builder and the other team (tf_gamerules.cpp:9830-9845).
        if (directory.Get(hit.Attacker) is TFBaseObject building)
            return building.Builder == hit.Victim || victimTeam != building.Team ? Decision.UseStockRule : Decision.No;

        // A teammate's hit with friendly fire off (teamplay_gamerules.cpp:363-370); the
        // relationship is between players only (CTeamplayRules::PlayerRelationship).
        if (entities.IsPlayer(hit.Attacker) && GameRules.Teammates(victimTeam, players.GetTeam(hit.Attacker))
            && convars.Get<MpFriendlyFireConVar>() is { Value: false })
            return Decision.No;

        return Decision.UseStockRule;
    }
}

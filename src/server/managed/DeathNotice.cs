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
/// Stock's <c>CHL2MPRules::DeathNotice</c>: a kill is announced as the
/// engine's <c>player_death</c> game event, which every listener on both sides
/// -- the rules' scoreboard, logic_eventlistener, the client's kill feed --
/// hears through the engine's dispatch. Managed decides the fields and when;
/// the engine's event manager carries it (IShimEventsServer).
///
/// <para>
/// The fields, as stock derives them: <c>userid</c> the victim's;
/// <c>attacker</c> the scorer's (the attacker when it is a player, else 0);
/// <c>weapon</c> the scorer's active weapon when the scorer itself inflicted
/// the kill, else the inflictor's classname, with <c>weapon_</c>, <c>npc_</c>
/// and <c>func_</c> stripped and anything physics as <c>physics</c>;
/// <c>priority</c> 7. The inflictor is the open <see cref="DamageInflictors"/>
/// scope (a blast's grenade), else the attacker (this build's damage path
/// builds <c>CTakeDamageInfo( attacker, attacker )</c>). Nobody is "world".
/// </para>
/// </summary>
public sealed class DeathNotice(IEngineGameEvents events, IEnginePlayers players, IEngineEntities entities,
                                DamageInflictors inflictors)
{
    /// <summary>The event's name in the game's event files (modevents.res).</summary>
    public const string EventName = "player_death";

    /// <summary>Stock's HLTV priority for it.</summary>
    public const int Priority = 7;

    /// <summary>Events fired, since the module started.</summary>
    public int Fired { get; private set; }

    public void Fire(EntityHandle victim, EntityHandle attacker)
    {
        bool hasAttacker = attacker != EntityHandle.None && entities.IsValid(attacker);
        EntityHandle scorer = hasAttacker && entities.IsPlayer(attacker) ? attacker : EntityHandle.None;

        EntityHandle inflictor = inflictors.TryCurrent(out var open, out _) ? open
                               : hasAttacker ? attacker : EntityHandle.None;

        int killerId = 0;
        string weapon = "world";

        if (scorer != EntityHandle.None)
        {
            killerId = players.GetUserId(scorer);

            if (inflictor != EntityHandle.None)
            {
                if (inflictor == scorer)
                {
                    string active = players.GetActiveWeapon(scorer);
                    if (active.Length > 0)
                        weapon = active;
                }
                else
                {
                    weapon = entities.GetClassName(inflictor);
                }
            }
        }
        else if (inflictor != EntityHandle.None)
        {
            weapon = entities.GetClassName(inflictor);
        }

        weapon = WeaponName(weapon);

        IntPtr e = events.EventCreate(EventName);
        if (e == IntPtr.Zero)
            return;

        events.EventSetInt(e, "userid", players.GetUserId(victim));
        events.EventSetInt(e, "attacker", killerId);
        events.EventSetString(e, "weapon", weapon);
        events.EventSetInt(e, "priority", Priority);

        if (events.EventFire(e))
            Fired++;
    }

    /// <summary>
    /// This mod's classes that stand in for stock's, by the stock class each
    /// replaces: the name player_death carries is then what stock's would be.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> StockClassOf = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [EntityNames.CrossbowBolt] = "crossbow_bolt",
        [EntityNames.RpgMissile] = "rpg_missile",
        [EntityNames.NpcGrenadeFrag] = "npc_grenade_frag",
        [EntityNames.PropCombineBall] = "prop_combine_ball",
        [EntityNames.SsBarrel] = "prop_physics",
    };

    /// <summary>Stock's renames of the killer's weapon name, in its order, after mapping ours to stock's class.</summary>
    public static string WeaponName(string name)
    {
        if (StockClassOf.TryGetValue(name, out var stock))
            name = stock;

        if (name.StartsWith("weapon_", StringComparison.Ordinal))
            name = name[7..];
        else if (name.StartsWith("npc_", StringComparison.Ordinal))
            name = name[4..];
        else if (name.StartsWith("func_", StringComparison.Ordinal))
            name = name[5..];
        else if (name.Contains("physics", StringComparison.Ordinal))
            name = "physics";

        return name switch
        {
            "prop_combine_ball" => "combine_ball",
            "grenade_ar2" => "smg1_grenade",
            "satchel" or "tripmine" => "slam",
            _ => name,
        };
    }
}

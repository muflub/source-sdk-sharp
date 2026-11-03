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
///
/// <para>
/// <b>A MOD'S FIELDS</b> (tf2 stage 2.7): after the core's keys, every
/// <see cref="IDeathNoticeHook"/> is handed the death as the core read it and
/// the event's setters, in hook order, before the event fires. TF's event
/// carries far more than HL2MP's (<c>CTFGameRules::DeathNotice</c>: assister,
/// damagebits, customkill, crit_type, ...) and names the weapon its own way; a
/// hook may set new keys and overwrite the core's. A hook that throws is named
/// and the event still fires.
/// </para>
/// </summary>
public sealed class DeathNotice(IEngineGameEvents events, IEnginePlayers players, IEngineEntities entities,
                                DamageInflictors inflictors, IHooks<IDeathNoticeHook>? hooks = null)
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

        if (hooks is { } fill)
        {
            var death = new DeathNoticeFacts(victim, attacker, scorer, inflictor, weapon);
            var fields = new EventFields(events, e);
            fill.Each(nameof(IDeathNoticeHook.Fill), h => h.Fill(in death, fields));
        }

        if (events.EventFire(e))
            Fired++;
    }

    private sealed class EventFields(IEngineGameEvents events, IntPtr handle) : IDeathNoticeFields
    {
        public void SetInt(string key, int value) => events.EventSetInt(handle, key, value);
        public void SetBool(string key, bool value) => events.EventSetBool(handle, key, value);
        public void SetString(string key, string value) => events.EventSetString(handle, key, value);
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

/// <summary>
/// A death as <see cref="DeathNotice"/> read it, for a <see cref="IDeathNoticeHook"/>.
/// </summary>
/// <param name="Victim">The dead player.</param>
/// <param name="Attacker">The damage's attacker, or <see cref="EntityHandle.None"/>.</param>
/// <param name="Scorer">The attacker when it is a player, else <see cref="EntityHandle.None"/>.</param>
/// <param name="Inflictor">The open inflictor scope's entity, else the attacker, else none.</param>
/// <param name="Weapon">The weapon name the core wrote.</param>
public readonly record struct DeathNoticeFacts(EntityHandle Victim, EntityHandle Attacker, EntityHandle Scorer,
                                               EntityHandle Inflictor, string Weapon);

/// <summary>The setters of the <c>player_death</c> event about to fire.</summary>
public interface IDeathNoticeFields
{
    void SetInt(string key, int value);
    void SetBool(string key, bool value);
    void SetString(string key, string value);
}

/// <summary>
/// A mod's keys on the <c>player_death</c> event (tf2 stage 2.7): called by
/// <see cref="DeathNotice"/> after the core's keys and before the event fires.
/// Order among several is <see cref="IOrderedHook"/>'s.
/// </summary>
public interface IDeathNoticeHook
{
    void Fill(in DeathNoticeFacts death, IDeathNoticeFields fields);
}

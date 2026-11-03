//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Entities.Items;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Entities.Rooms;

/// <summary>
/// When each player may next be resupplied: stock's
/// <c>CTFPlayer::m_flNextRegenTime</c>, which every locker reads, kept per
/// player handle beside the player until Stage 2.3's player holds it.
/// </summary>
[SideService(Side.Server)]
public sealed class TFResupplyCooldowns
{
    private readonly Dictionary<EntityHandle, float> _next = new();

    /// <summary>
    /// <c>GetNextRegenTime() &gt; curtime</c>. A time further ahead than one
    /// cooldown is from an earlier map's clock and does not hold.
    /// </summary>
    public bool Waiting(EntityHandle player, float now)
        => _next.TryGetValue(player, out float next) && next > now && next - now <= FuncRegenerate.NextUseTime;

    /// <summary><c>SetNextRegenTime</c>.</summary>
    public void Set(EntityHandle player, float next) => _next[player] = next;
}

/// <summary>
/// func_regenerate (stock's <c>CRegenerateZone</c>, func_regenerate.cpp): the
/// resupply locker. A player of its team standing in it is fully resupplied
/// (<c>CTFPlayer::Regenerate</c>) at most every 3 s, and the locker's
/// associated prop plays "open", then "close" a second before the next use.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class FuncRegenerate(IGameClock clock, ITFMapPlayers players, ITeamRoundState round,
                                   TFResupplyCooldowns cooldowns, ITFPickupNotices notices) : TriggerVolume(clock)
{
    public const string MapClass = "func_regenerate";

    /// <summary><c>TF_REGENERATE_SOUND</c>.</summary>
    public const string RegenerateSound = "Regenerate.Touch";

    /// <summary><c>TF_REGENERATE_NEXT_USE_TIME</c>.</summary>
    public const float NextUseTime = 3.0f;

    private const string CloseThink = "RegenerateClose";

    [ScriptVars] public FuncRegenerateVars RegenerateVars = new();

    [SaveField] private EntityHandle _associatedModel;

    /// <summary>The prop the map named, once the level activated.</summary>
    public EntityHandle AssociatedModel => _associatedModel;

    /// <summary>Stock's <c>Touch</c> runs every frame a player stands inside.</summary>
    public override bool TouchEveryTick => true;

    /// <summary>Clients (a TF player); <c>InitTrigger</c> keeps the map's kinds.</summary>
    protected override TriggerSpawnflags KindFlags => TriggerSpawnflags.Clients;

    /// <summary><c>Activate</c>: find the associated model by name.</summary>
    public override void OnServerActivate()
    {
        string name = RegenerateVars.AssociatedModel;
        if (name.Length == 0)
        {
            Log.Warning($"{MapClass}({Name}) has no associated model.\n");
            return;
        }

        _associatedModel = Entities.FindByName(name);
        if (!Entities.IsValid(_associatedModel))
        {
            _associatedModel = EntityHandle.None;
            Log.Warning($"{MapClass}({Name}) unable to find associated model named '{name}'.\n");
        }
    }

    /// <summary>The entry is tracked by the volume; the work is every frame.</summary>
    protected override void Fire(EntityHandle toucher) { }

    /// <summary><c>CRegenerateZone::Touch</c>, every frame of the overlap.</summary>
    public override void OnTouch(EntityHandle toucher)
    {
        base.OnTouch(toucher);
        if (Disabled || !Passes(toucher))
            return;

        if (CanRegenerate(toucher))
            Regenerate(toucher);
    }

    /// <summary>
    /// Stock's tests in order: not cooling down, not taunting, of the zone's
    /// team (in the win state: the winners, at any zone), and not a stalemate.
    /// </summary>
    public bool CanRegenerate(EntityHandle player)
    {
        if (!players.IsAlive(player))
            return false;

        if (cooldowns.Waiting(player, Clock.Now))
            return false;

        if (players.InCond(player, TFCond.Taunting))
            return false;

        int team = RegenerateVars.TeamNum;
        if (round.State != RoundState.TeamWin)
        {
            if (team != 0 && players.Team(player) != team)
                return false;
        }
        else if ((int)round.WinningTeam != players.Team(player))
        {
            // no health for the losing team, but all zones work for the winning team
            return false;
        }

        return round.State != RoundState.Stalemate;
    }

    /// <summary><c>CRegenerateZone::Regenerate</c>.</summary>
    private void Regenerate(EntityHandle player)
    {
        players.Regenerate(player);
        cooldowns.Set(player, Clock.Now + NextUseTime);
        notices.Sound(player, RegenerateSound);

        if (_associatedModel == EntityHandle.None)
            return;

        Entities.FireInput(_associatedModel, "SetAnimation", "open", Handle);
        SetContextThink(CloseThink, NextUseTime - 1.0f);
    }

    /// <summary>The queued "close" (<c>g_EventQueue.AddEvent</c>, 2 s after the open).</summary>
    public override void OnThink(string context)
    {
        if (context == CloseThink && _associatedModel != EntityHandle.None)
            Entities.FireInput(_associatedModel, "SetAnimation", "close", Handle);
    }
}

/// <summary>The map keys <see cref="FuncRegenerate"/> declares (func_regenerate.cpp's datadesc, and CBaseEntity's TeamNum).</summary>
[ScriptVars]
public partial class FuncRegenerateVars
{
    [VarName("associatedmodel")] public string AssociatedModel { get; set; } = string.Empty;

    /// <summary>CBaseEntity::KeyValue's TeamNum: the team the locker is for, 0 for everyone.</summary>
    public int TeamNum { get; set; }
}

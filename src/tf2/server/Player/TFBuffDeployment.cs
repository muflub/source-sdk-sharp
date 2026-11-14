//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using SourceSharp.Shared;

using TF2.Server.Weapons;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// One buff pulse (<c>CTFPlayerShared::PulseRageBuff</c>,
/// <c>tf_player_shared.cpp:13923-14019</c>): every living same-team player
/// inside the buff's reach takes the buff's condition for the pulse's seconds
/// — the alive gate (:13951), the team gate (:13954), the disguised-onto-
/// another-team and stealthed skips (:13957, :13960) and the squared-distance
/// reach (:13963) are stock's, in stock's order; the deployer stands in the
/// crowd like everyone else (:13995). The pyro-rage and sniper-focus buffs
/// (types 5 and 6) reach the deployer alone (:13995-14002), and a buff type
/// with no condition answers none, stock's <c>TF_COND_LAST</c> arm (:14004).
/// </summary>
/// <remarks>
/// The dead skip rides twice by design: the carrier check here and
/// <see cref="ITFPlayerConditions.Add"/>'s own alive gate, which is stock's
/// <c>AddCond</c> :1110.
/// </remarks>
[SideService(Side.Server, As = [typeof(ITFBuffEffects)])]
public sealed class TFBuffDeployment(ITFPlayerData players, ITFPlayerConditions conditions,
                                     IEnginePlayers enginePlayers, IEngineEntities entities,
                                     IEngineSound? sound = null, IEngineGameEvents? events = null) : ITFBuffEffects
{
    public void DeployBuff(int ownerIndex, int buffType)
    {
        EntityHandle owner = ownerIndex > 0 ? enginePlayers.GetHandle(ownerIndex) : EntityHandle.None;
        if (owner == EntityHandle.None)
            return;

        TFCond cond = TFRageMeter.BuffCond(buffType);
        if (cond == TFCond.Invalid)
            return; // :14004's answer for a type with no condition.

        int team = enginePlayers.GetTeam(owner);
        Vector centre = entities.GetOrigin(owner);
        float reachSquared = TFRageMeter.BuffRadius * TFRageMeter.BuffRadius;

        for (int index = 1; index < TFPlayerResourceNetState.Slots; index++)
        {
            if (players.Networked(index) is not { } state)
                continue; // :13951: no carrier, nobody to buff.

            EntityHandle player = enginePlayers.GetHandle(index);
            if (enginePlayers.GetTeam(player) != team)
                continue; // :13954.

            if (state.InCond(TFCond.Disguised) && state.DisguiseTeam != team)
                continue; // :13957: a spy shown as another team is not this one's teammate.

            if (state.IsStealthed())
                continue; // :13960.

            if ((entities.GetOrigin(player) - centre).LengthSquared > reachSquared)
                continue; // :13963.

            // The two self-only buffs (:13995-14002).
            if ((buffType is 5 or 6) && index != ownerIndex)
                continue;

            conditions.Add(player, cond, TFRageMeter.BuffDurationSeconds, owner);
        }
    }

    /// <summary>
    /// <c>CTFBuffItem::BlowHorn</c> (<c>tf_weapon_buff_item.cpp:170-222</c>):
    /// the alive gate (:176-177), then the horn the buff type names
    /// (:186-212) — the Concheror's conch, the Buff Banner's bugle and the
    /// Battalion's horn, each Blue or Red by the owner's team — out of the
    /// owner. Buff types outside the three answer no sound (:182's 0 default).
    /// </summary>
    public void BlowBuffHorn(int ownerIndex, int buffType)
    {
        EntityHandle owner = ownerIndex > 0 ? enginePlayers.GetHandle(ownerIndex) : EntityHandle.None;
        if (owner == EntityHandle.None || !enginePlayers.IsAlive(owner))
            return;

        string? horn = buffType switch
        {
            3 => "Samurai.Conch", // :186-189
            1 => enginePlayers.GetTeam(owner) == (int)TFTeam.Blue
                     ? "Weapon_BuffBanner.HornBlue" : "Weapon_BuffBanner.HornRed", // :191-201
            2 => enginePlayers.GetTeam(owner) == (int)TFTeam.Blue
                     ? "Weapon_BattalionsBackup.HornBlue" : "Weapon_BattalionsBackup.HornRed", // :202-212
            _ => null,
        };
        if (horn is { } name)
            sound?.EntityEmitSound(owner, name);
    }

    /// <summary>
    /// <c>CTFBuffItem::RaiseFlag</c> (:227-256): the alive gate (:233-234),
    /// then the <c>deploy_buff_banner</c> event with the buff type and the
    /// owner's userid, fired before anything else raises the buff (:242-249),
    /// the flag sound over it (:251). The battle cry (:252) rides the voice
    /// lane in <see cref="TFWeaponEffects.RaiseBuffBanner"/>, the raise in
    /// <see cref="DeployBuff"/> follows.
    /// </summary>
    public void RaiseBuffBanner(int ownerIndex, int buffType)
    {
        EntityHandle owner = ownerIndex > 0 ? enginePlayers.GetHandle(ownerIndex) : EntityHandle.None;
        if (owner == EntityHandle.None || !enginePlayers.IsAlive(owner))
            return;

        if (events is { } seam)
        {
            IntPtr ev = seam.EventCreate("deploy_buff_banner"); // :243, before the ActivateRageBuff ask (:242's comment)
            if (ev != IntPtr.Zero)
            {
                seam.EventSetInt(ev, "buff_type", buffType); // :246
                seam.EventSetInt(ev, "buff_owner", enginePlayers.GetUserId(owner)); // :247
                seam.EventFire(ev); // :248
            }
        }

        sound?.EntityEmitSound(owner, "Weapon_BuffBanner.Flag"); // :251
    }
}

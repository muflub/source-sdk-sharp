//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using SourceSharp.Shared;

using TF2.Server.Voice;
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
                                     IGameClock clock, TFTauntCommand taunts,
                                     TFPlayerCarriers carriers, TFConceptSpeech speech,
                                     IEngineSound sound, IEngineGameEvents events) : ITFBuffEffects
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
            sound.EntityEmitSound(owner, name);
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

        IntPtr ev = events.EventCreate("deploy_buff_banner"); // :243, before the ActivateRageBuff ask (:242's comment)
        if (ev != IntPtr.Zero)
        {
            events.EventSetInt(ev, "buff_type", buffType); // :246
            events.EventSetInt(ev, "buff_owner", enginePlayers.GetUserId(owner)); // :247
            events.EventFire(ev); // :248
        }

        sound.EntityEmitSound(owner, "Weapon_BuffBanner.Flag"); // :251
    }

    /// <summary>
    /// The rage press (<c>CTFFlameThrower::UseRage</c> through
    /// <c>SecondaryAttack</c>'s buff arm, <c>tf_weapon_flamethrower.cpp:1196
    /// -1200</c>): the taunt, asked whole (<c>pPlayer->Taunt</c> :1136 — the
    /// speak, the condition, the timed attacks; the behaviour's meter gate and
    /// the press's clock stand behind this), and then the taunt arm itself
    /// (<c>OnTauntSucceeded</c>'s pyro branch, tf_player.cpp:17897-17921): the
    /// taunt the ask raised — the condition is the ask's own, checked false
    /// before it — whose scene is the plain taunt's
    /// (<see cref="TFTauntAttack.PyroRageScenePrefix"/>, :17901's prefix), on a
    /// pyro, lights <see cref="TFRageMeter.PyroRageBuffType"/>:
    /// </summary>
    /// <remarks>
    /// The drain's stamp is <c>ActivateRageBuff</c> through
    /// <c>SetupRageBuffTimer</c> (tf_player_shared.cpp:13794, :13743-13747) —
    /// the first pulse at once, the rest on the player's
    /// <see cref="TFBuffRageTicker"/>. The slot's <c>mod_buff_duration</c>
    /// (:13768) has no carrier yet, the ten pulses as stocked; the taunt
    /// shields are the arm's own (:17917-17918), and the battlecry is
    /// <c>ActivateRageBuff</c>'s case 5 (:13784).
    /// </remarks>
    public void UseRage(int ownerIndex, int buffType)
    {
        EntityHandle owner = ownerIndex > 0 ? enginePlayers.GetHandle(ownerIndex) : EntityHandle.None;
        if (owner == EntityHandle.None || buffType <= 0)
            return;

        var before = players.Networked(ownerIndex);
        if (before is null || before.InCond(TFCond.Taunting))
            return; // IsAllowedToTaunt's standing taunt (:682): the ask below must be the raiser.

        taunts.Handle(owner, TFTauntCommand.Command, default);

        var state = players.Networked(ownerIndex);
        if (state is null || !state.InCond(TFCond.Taunting))
            return; // :1137's arm — the taunt did not take, no drain.

        string scene = carriers.Part<TFTauntAttack>(ownerIndex)?.Scene ?? "";
        if (!scene.StartsWith(TFTauntAttack.PyroRageScenePrefix, StringComparison.OrdinalIgnoreCase))
            return; // :17901's scene gate — the special taunts do not light it.

        state.RageDraining = true;                                     // :13794
        state.RageBuffType = buffType;                                 // :13743
        state.RageBuffPulsesLeft = TFRageMeter.BuffPulses;             // :13744
        state.RageBuffNextPulseTime = clock.Now + TFBuffItemBehaviour.PulseInterval; // :13745

        speech.Speak(ownerIndex, TFConceptSpeech.PlayerBattlecry);    // :13784's case 5
        DeployBuff(ownerIndex, buffType);                              // :13747's first pulse

        // The taunt's shields (:17917-17918): invulnerability and mega-heal
        // while the pose stands, the 2.6 s of the arm.
        conditions.Add(owner, TFCond.InvulnerableUserBuff, PyroRageShieldSeconds, owner);
        conditions.Add(owner, TFCond.Megaheal, PyroRageShieldSeconds, owner);
    }

    /// <summary>The taunt shields' run (<c>tf_player.cpp:17917-17918</c>).</summary>
    public const float PyroRageShieldSeconds = 2.6f;
}

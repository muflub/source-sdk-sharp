//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

namespace TF2.Client.Player;

/// <summary>
/// The crit boost's weapon glow, stock's
/// <c>CTFPlayerShared::UpdateCritBoostEffect</c>
/// (<c>tf_player_shared.cpp:7501-7613</c>) as the condition driver's arm: the
/// crit-family bits and the demo charge's re-run it
/// (<c>OnAddCritBoost</c> and friends, :1614-1635, 1904-1925), and the
/// display gates — a crit condition or the energy/sniper rage buff, not
/// stealthed, disguised only for the local player, a blinking enemy hidden.
/// The name is <c>critgun_weaponmodel_red</c>/<c>blu</c>, the disguised
/// enemy's by the team the disguise shows (<c>GetDisguiseTeam</c>, :7576-7585).
///
/// <para>
/// <b>DIVERGENCE.</b> Stock hangs the glow on the active weapon's particle
/// prop (the viewmodel for the local player). The shell's second slot is per
/// entity and follows nothing, so the glow wears the PLAYER's extra slot at
/// the player's origin; a weapon switch retargets nothing until the next
/// condition change (stock's OnThink, :7553-7591). The weapon's
/// <c>CanBeCritBoosted</c> (<c>no_crit_boost</c>) needs the item attributes
/// no client build networks (see <c>TFClientWeaponOwners</c>) and gates
/// neither glow nor hum here; the render group waits in
/// <c>docs/plans/tf2/deferred.tsv</c>.
/// </para>
///
/// <para>
/// The hum is the same body's sound loop (:7603-7611): the local mix hears
/// <c>Weapon_General.CritPower</c> from every displayed boost, a patch on
/// the player at full volume and pitch 100 for as long as the boost
/// displays. Stock gates it on an active weapon; the same attribute-less
/// divergence above covers it.
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFCritBoostEffect(
    IEngineEffects effects,
    IEngineHud hud,
    TFClientCarriers carriers,
    TFClientPlayerResources resources,
    IEngineSound sound,
    IClientSoundController envelopes) : ITFClientConditionHook, IOrderedHook
{
    private const int ChannelStatic = 6, SoundLevelNorm = 75;

    /// <summary>The loop, :7608.</summary>
    public const string CritPowerSound = "Weapon_General.CritPower";

    private readonly TFConditionEffects _effects = new();

    /// <summary>The players whose glow is running.</summary>
    private readonly HashSet<EntityHandle> _glowing = [];

    /// <summary>The players whose hum is running, by patch.</summary>
    private readonly Dictionary<EntityHandle, int> _humming = [];

    public int HookOrder => TFClientConditionOrders.CritBoostEffect;

    public void OnAdded(EntityHandle player, TFCond cond)
    {
        if (Reevaluates(cond))
            Update(player);
    }

    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (Reevaluates(cond))
            Update(player);
    }

    /// <summary>The bits whose change re-runs the effect, stock's switch cases.</summary>
    private static bool Reevaluates(TFCond cond) => cond switch
    {
        TFCond.Critboosted or TFCond.CritboostedDemoCharge
        or TFCond.CritboostedFirstBlood or TFCond.CritboostedPumpkin
        or TFCond.CritboostedUserBuff or TFCond.CritboostedBonusTime
        or TFCond.CritboostedCtfCapture or TFCond.CritboostedOnKill
        or TFCond.CritboostedRageBuff or TFCond.SniperchargeRageBuff
        or TFCond.CritboostedCardEffect or TFCond.CritboostedRuneTemp => true,
        _ => false,
    };

    /// <summary>Stock's body: the gates decide, then the glow is worn or cleared.</summary>
    private void Update(EntityHandle player)
    {
        if (carriers.Of(player.EntryIndex) is not { } carrier)
            return;

        TFCondMask conds = carrier.State.Conditions();

        bool display = _effects.IsCritBoosted(conds, clientSide: true)
                       || conds.Has(TFCond.EnergyBuff)
                       || conds.Has(TFCond.SniperchargeRageBuff);
        display &= !_effects.IsStealthed(conds);

        int localIndex = hud.LocalPlayerIndex;
        bool local = player.EntryIndex == localIndex;
        int playerTeam = TeamAt(player.EntryIndex);
        int localTeam = TeamAt(localIndex);
        bool enemies = playerTeam != localTeam;

        display &= local || !conds.Has(TFCond.Disguised);

        // An enemy mid-blink is invisible :7561-7566; a teammate fading shows.
        if (!local && enemies)
            display &= !conds.Has(TFCond.StealthedBlink);

        if (!display)
        {
            Stop(player);

            return;
        }

        int team = conds.Has(TFCond.Disguised) && !local && enemies
            ? carrier.State.DisguiseTeam
            : playerTeam;

        string name = team == (int)TFTeam.Red ? "critgun_weaponmodel_red" : "critgun_weaponmodel_blu";

        if (effects.ParticleSystemStartExtra(player, name))
            _glowing.Add(player);

        // The hum, :7604-7610: created once per boost, on the player.
        if (!_humming.ContainsKey(player))
        {
            int patch = sound.SoundPatchCreate(player, ChannelStatic, CritPowerSound, SoundLevelNorm);

            if (patch != 0)
            {
                envelopes.Create(patch);
                envelopes.Play(patch, 1f, 100f, 0f);
                _humming[player] = patch;
            }
        }
    }

    private void Stop(EntityHandle player)
    {
        if (_glowing.Remove(player))
            effects.ParticleSystemStopExtra(player);

        if (_humming.Remove(player, out int patch))
        {
            envelopes.Destroy(patch);
            sound.SoundPatchFree(patch);
        }
    }

    /// <summary>The scoreboard's team row; 0 with no resource arrived.</summary>
    private int TeamAt(int index) => resources.Live is { } resource ? resource.State.Team[index] : 0;
}

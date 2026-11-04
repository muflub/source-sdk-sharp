//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
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
/// The glow hangs off the weapon the viewer sees, stock's two arms of :7553:
/// the local player's rides the viewmodel bearer
/// (<see cref="IWeaponViewModelBearer"/>, the attachment merged onto the hands
/// whose render pose the viewmodel pass pins to the eye), everyone else's
/// their carried gun. The frame think is stock's
/// <c>CTFCondition_CritBoost::OnThink</c> (<c>tf_condition.cpp:370-393</c>):
/// a glowing player whose bearer is no longer the one the effect sits on —
/// the gun the client carries for them is gone or replaced — re-runs the
/// whole gate set, which moves the effect.
/// </para>
///
/// <para>
/// The active weapon's <c>CanBeCritBoosted</c> gates the display too
/// (<c>tf_weaponbase.cpp:6640-6645</c>): the weapon's own
/// <c>no_crit_boost</c> (:6643 CALL_ATTRIB_HOOK_FLOAT) read through the
/// item the client holds for the carrier — the predicted working copy's row
/// for the local player, the networked defindex for anyone else
/// (<see cref="ITFWeaponOwners.ItemOf"/>).
/// </para>
///
/// <para>
/// The hum is the same body's sound loop (:7603-7611): the local mix hears
/// <c>Weapon_General.CritPower</c> from every displayed boost, a patch on
/// the player at full volume and pitch 100 for as long as the boost
/// displays and the player carries a gun.
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFCritBoostEffect(
    IEngineEffects effects,
    IEngineHud hud,
    IClientStandalones standalones,
    TFClientCarriers carriers,
    TFClientPlayerResources resources,
    IWeaponViewModelBearer bearers,
    ITFWeaponOwners owners,
    WeaponCatalog weapons,
    AttributeHooks attributes,
    IEngineSound sound,
    IClientSoundController envelopes) : ITFClientConditionHook, IClientFrameThink, IOrderedHook
{
    private const int ChannelStatic = 6, SoundLevelNorm = 75;

    /// <summary>
    /// The glow's particle slot on its bearer, beside the burn's and the
    /// first slot's tenants (the medigun's charged glow wears the extra slot
    /// on the same gun) and the drips' and feet smoke's.
    /// </summary>
    public const int GlowSlot = 6;

    /// <summary>The gate's attribute, <c>CanBeCritBoosted</c>'s read (:6643).</summary>
    public const string NoCritBoost = "no_crit_boost";

    /// <summary>The loop, :7608.</summary>
    public const string CritPowerSound = "Weapon_General.CritPower";

    /// <summary>The boost a player's glow wears: the bearer it sits on and the name it wears.</summary>
    private readonly record struct Glow(EntityHandle Bearer, string Name);

    private readonly TFConditionEffects _effects = new();

    /// <summary>The players whose glow is running, by the bearer it sits on.</summary>
    private readonly Dictionary<EntityHandle, Glow> _glowing = [];

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

    /// <summary>
    /// The frame: stock's <c>OnThink</c> (<c>tf_condition.cpp:370-393</c>). A
    /// glowing player whose carrier went loses the glow and the hum; one
    /// whose bearer changed — the gun replaced, the viewmodel attachment
    /// gone — re-runs the gates, which moves the effect.
    /// </summary>
    public void ClientThink(float frameTime)
    {
        foreach (EntityHandle player in _glowing.Keys.ToArray())
        {
            if (carriers.Of(player.EntryIndex) is null)
            {
                Stop(player);

                continue;
            }

            if (BearerOf(player) is { } bearer && bearer != EntityHandle.None
                && bearer != _glowing[player].Bearer)
            {
                Update(player);
            }
        }
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

        // The active weapon's own gate, :7510.
        display &= CritBoostable(player, local);

        if (!display)
        {
            Stop(player);

            return;
        }

        int team = conds.Has(TFCond.Disguised) && !local && enemies
            ? carrier.State.DisguiseTeam
            : playerTeam;

        string name = team == (int)TFTeam.Red ? "critgun_weaponmodel_red" : "critgun_weaponmodel_blu";

        EntityHandle bearer = BearerOf(player);

        // No carried gun yet: stock's pWeapon-null arm wears nothing and waits
        // for the next think (:7572), the hum with it (:7604).
        if (bearer == EntityHandle.None)
            return;

        // Stock creates once and otherwise re-emits (:7574-7598): a restart
        // only when the bearer or the name changed, so a re-run on an unrelated
        // bit does not reset a live system.
        if (!_glowing.TryGetValue(player, out Glow worn) || worn.Bearer != bearer || worn.Name != name)
        {
            if (worn.Bearer != EntityHandle.None)
                effects.ParticleSystemStopSlot(worn.Bearer, GlowSlot);

            if (effects.ParticleSystemStartSlot(bearer, name, GlowSlot))
            {
                // Stock pins the local player's glow into the viewmodel's
                // translucent pass in code, whatever the particle def says
                // (:7587-7593); everyone else's stays in the entity
                // translucent group the leaf system inserted it in.
                if (local)
                    effects.ParticleSystemViewModelSlot(bearer, GlowSlot, true);

                _glowing[player] = new Glow(bearer, name);
            }
        }

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

    /// <summary>
    /// The entity this player's glow hangs off, stock's two arms of :7553:
    /// the local player's the viewmodel bearer, everyone else's the gun the
    /// client carries for them — the local player's fall when no attachment
    /// is networked yet, the same fall the flame's bearer makes.
    /// </summary>
    private EntityHandle BearerOf(EntityHandle player)
    {
        int index = player.EntryIndex;

        if (index == hud.LocalPlayerIndex && bearers.ViewModelBearer() is { } attachment)
            return attachment;

        foreach (ClientStandaloneEntity entity in standalones.OfClass(EntityNames.SsWeapon))
        {
            if (entity is ClientSsWeapon weapon && weapon.State.OwnerIndex == index)
                return weapon.Handle;
        }

        return EntityHandle.None;
    }

    /// <summary>
    /// The active weapon's <c>CanBeCritBoosted</c> (:6640-6645): the weapon's
    /// own <c>no_crit_boost</c>, additive onto zero. A weapon the client holds
    /// no item for boosts, as stock's unread zero does.
    /// </summary>
    private bool CritBoostable(EntityHandle player, bool local)
    {
        int index = player.EntryIndex;

        // The local player's gate reads the predicted working copy's item;
        // everyone else's the networked defindex behind the carried gun's row.
        IAttributeProvider? item = local
            ? owners.ActiveItemOf(index)
            : owners.ItemOf(index, weapons.ByIndex(CarriedType(index))?.Name ?? "");

        return item is null || attributes.Float(0f, NoCritBoost, item) == 0f;
    }

    /// <summary>The carried gun's wire row, -1 with none.</summary>
    private int CarriedType(int index)
    {
        foreach (ClientStandaloneEntity entity in standalones.OfClass(EntityNames.SsWeapon))
        {
            if (entity is ClientSsWeapon weapon && weapon.State.OwnerIndex == index)
                return weapon.State.WeaponTypeId;
        }

        return -1;
    }

    private void Stop(EntityHandle player)
    {
        if (_glowing.Remove(player, out Glow worn) && worn.Bearer != EntityHandle.None)
            effects.ParticleSystemStopSlot(worn.Bearer, GlowSlot);

        if (_humming.Remove(player, out int patch))
        {
            envelopes.Destroy(patch);
            sound.SoundPatchFree(patch);
        }
    }

    /// <summary>The scoreboard's team row; 0 with no resource arrived.</summary>
    private int TeamAt(int index) => resources.Live is { } resource ? resource.State.Team[index] : 0;

    /// <summary>
    /// The glow's state for the live lanes: who glows, on which bearer and
    /// name, whose hum runs, and the local player's bearer and conditions now.
    /// </summary>
    internal string Report()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"glowing {_glowing.Count} humming {_humming.Count} local player {hud.LocalPlayerIndex}");

        foreach ((EntityHandle player, Glow glow) in _glowing)
        {
            _humming.TryGetValue(player, out int patch);
            text.AppendLine($"glow player {player.EntryIndex} bearer 0x{glow.Bearer.Value:X8} name {glow.Name} hum {patch}");
        }

        if (carriers.Of(hud.LocalPlayerIndex) is { } local)
            text.Append($"local bearer 0x{BearerOf(new EntityHandle((uint)hud.LocalPlayerIndex)).Value:X8} conds {local.State.Conditions()}");
        else
            text.Append("local carrier none");

        return text.ToString();
    }
}

/// <summary>The console door onto the crit glow's state, for the live lanes.</summary>
[ConCommand("tf2_cl_critglow", Help = "The crit glow's players: bearer, name and hum per glowing player, and the local player's bearer and conditions")]
public sealed class TFCritGlowReportCommand(ILog log, TFCritBoostEffect glow)
{
    public void Execute() => log.Msg(glow.Report() + "\n");
}

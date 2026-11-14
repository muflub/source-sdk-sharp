//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Combat;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Server.Voice;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;

namespace TF2.Server.Buildings;

/// <summary>
/// <c>obj_attachment_sapper</c> (tf_obj_sapper.cpp): the spy's leech. Planted
/// on an enemy building — instantly, the sapper's build time is zero — it
/// drains its parent at <c>obj_sapper_amount</c> a second through the
/// core's damage path, as <c>DMG_CRUSH</c> with the sapper itself as the
/// attacker, and dies with the building it rides.
///
/// <para>
/// <b>The clocks.</b> Stock's <c>SapperThink</c> runs every tenth: the damage
/// to give is the rate over the time since the last think, carried as a
/// fraction (<c>m_flSapperDamageAccumulator</c>), so 2.5 a tick pays 2 then 3.
/// Stock's <c>IsParentValid</c> arms differ (tf_obj_sapper.cpp:478-488): a
/// parent that is simply gone destroys the sapper; a dead or dying one does
/// not — the think stands down without rebooking, and the parent's removal
/// takes its sapper with it.
/// </para>
///
/// <para>
/// <b>The ends.</b> On any removal the parent is told
/// (<c>DetachObjectFromObject</c>); a sapper <em>killed</em> — the wrench's
/// work — also leaves the building stumbling for
/// <c>SAPPER_REMOVE_DISABLE_TIME</c> (stock's <c>Killed</c> →
/// <c>SetPlasmaDisabled</c>). What the building does while disabled is its
/// own think's business. The sapper accepts only the wrench's fix
/// (<c>OnTakeDamage</c>'s <c>TF_DMG_WRENCH_FIX</c> gate); the pair
/// propagation of the same blow is the use layer's
/// (<see cref="TFSapperUse"/>), where stock's
/// <c>DMG_FROM_OTHER_SAPPER</c> guard lives. The player-sapped event, the
/// timer sounds and the MvM arms (bots, the robo sapper, the leech
/// attributes) are deferred rows.
/// </para>
/// </summary>
[ScriptedEntity(TFSapper.MapClass, ScriptedEntityKind.Item)]
public sealed class TFSapper(ITFMapPlayers players, ITFObjectAnnouncer announcer, IEnginePrecache precache,
                             IEngineEntityRender render, IEngineDamage damage, TFDamageScopes scopes,
                             ITFAmmoPurse purse, ObjSapperAmountConVar sapperAmount,
                             TFPlayerCarriers carriers, TFConceptSpeech speech,
                             ITFWeaponOwners owners, AttributeHooks hooks,
                             ITFRevengeCrits crits, IEngineSound sound,
                             IUniformRandom rng, ServerBodygroups bodygroups,
                             IServerSkins skins)
    : TFBaseObject(players, announcer, precache, render, purse, carriers, speech, owners, hooks, scopes, crits, bodygroups, skins)
{
    public const string MapClass = "obj_attachment_sapper";

    /// <summary>SAPPER_MAX_HEALTH (tf_obj_sapper.h).</summary>
    public const int SapperMaxHealth = 100;

    /// <summary>SAPPER_REMOVE_DISABLE_TIME: the parent's stumble after a removal.</summary>
    public const float RemoveDisableTime = 0.5f;

    /// <summary>SAPPER_THINK_CONTEXT, on <c>BASE_OBJECT_THINK_DELAY</c>'s tenth.</summary>
    public const string SapperThinkContext = "SapperThink";

    private const float ThinkDelay = 0.1f;

    private readonly IEngineDamage _damage = damage;
    private readonly TFDamageScopes _scopes = scopes;
    private readonly ObjSapperAmountConVar _sapperAmount = sapperAmount;
    private readonly IEngineSound _sound = sound;
    private readonly IUniformRandom _rng = rng;

    [SaveField] private EntityHandle _builtOn;
    [SaveField] private float _accumulator, _lastThinkTime;

    private TFBaseObject? _parent;

    public override TFObjectType Type => TFObjectType.AttachmentSapper;

    /// <summary><c>GetBaseHealth</c>: SAPPER_MAX_HEALTH, no attributes yet.</summary>
    public override int MaxHealthForCurrentLevel => SapperMaxHealth;

    /// <summary>Where the sapper rides, as the placement left it.</summary>
    public EntityHandle BuiltOn => _builtOn;

    /// <summary>
    /// <c>SetBuildOnEntity</c>: the building the sapper was planted on. The
    /// link is one-way and dies with its end — the sapper's think asks
    /// <see cref="TFBaseObject.IsDead"/>, so a destroyed parent is simply no
    /// longer standing. (The handle is the durable record; the reference is
    /// what the think walks. Re-binding after a mid-sap save/load rides with
    /// save/load parity.)
    /// </summary>
    public void AttachTo(TFBaseObject builtOn)
    {
        _builtOn = builtOn.Handle;
        _parent = builtOn;
    }

    /// <summary>
    /// <c>CObjectSapper::FinishedBuilding</c>'s tail: the parent learns it is
    /// sapped — with the handle the removal cascade reaches — and the
    /// drain's clocks start.
    /// </summary>
    protected override void OnGoActive()
    {
        ResolveParent()?.OnAddSapper(Handle);
        _accumulator = 0f;
        _lastThinkTime = GameTime;
        SetContextThink(SapperThinkContext, ThinkDelay);

        // Stock's Wheatley VO for the pak sapper: IsWheatleySapper is the held builder
        // item's sapper_voice_pak == 1 (tf_weapon_builder.cpp:584-589), and the machine
        // picked at placement (:751-771: outer coin 0 asks the inner — inner 0 the PW
        // line, else the plain one; outer 1 the hacking line) speaks to its owner only
        // (WheatleyEmitSound).
        if (Builder != EntityHandle.None
            && Hooks.Float(0f, "sapper_voice_pak", Owners.ActiveItemOf(Entities.GetIndex(Builder))) == 1f)
        {
            int outer = _rng.RandomInt(0, 1);
            string line = outer != 0
                ? "PSap.Hacking"
                : _rng.RandomInt(0, 3) == 0 ? "PSap.AttachedPW" : "PSap.Attached";
            _sound.EntityEmitSound(Builder, line);
        }
    }

    /// <summary>
    /// <c>CObjectSapper::SapperThink</c>: drain the parent — the rate over the
    /// time since the last think, the fraction owed carried. Stock's
    /// IsParentValid arms (tf_obj_sapper.cpp:478-488): no parent, the sapper
    /// is destroyed; a dead or dying parent, the think stands down without
    /// rebooking — the parent's removal takes the sapper with it.
    /// </summary>
    public override void OnThink(string context)
    {
        if (context != SapperThinkContext)
            return;

        if (IsDead)
            return;

        TFBaseObject? parent = ResolveParent();
        if (parent is null)
        {
            // Stock's IsParentValid: nothing left to ride, the sapper is gone.
            DestroyObject();
            return;
        }

        if (parent.IsDead)
        {
            // Stock's !IsAlive || IsDying return: no destroy, no rebooking.
            return;
        }

        // Stock's drain (tf_obj_sapper.cpp:493-495): the rate over the time since the
        // last think, then the builder's held item scales it — the ON_OTHER walk of
        // mult_sapper_damage on pBuilder (:495).
        float drain = (GameTime - _lastThinkTime) * _sapperAmount.Value;
        if (Owners is { } holders && Hooks is { } attr && Builder != EntityHandle.None)
            drain = attr.Float(drain, "mult_sapper_damage", holders.ActiveItemOf(Entities.GetIndex(Builder)));

        _accumulator += drain;
        int hurt = (int)_accumulator;
        _accumulator -= hurt;

        if (hurt > 0)
        {
            // Stock stamps the recorder's death kind when the builder's item carries
            // sapper_degenerates_buildings (:515-519, the GetReversesBuildingConstructionSpeed
            // walk at :780-786); the kill feed reads that kind as "recorder"
            // (tf_gamerules.cpp:12333-12336). The scope carries the kind to the hit record.
            if (ReversesBuildingConstructionSpeed() != 0f)
            {
                using (_scopes.Push(TFDamageCustom.SapperRecorderDeath, TFWeaponId.None, ""))
                    _damage.TakeDamage(parent.Handle, Handle, hurt, DamageType.Crush);
            }
            else
            {
                _damage.TakeDamage(parent.Handle, Handle, hurt, DamageType.Crush);
            }
        }

        _lastThinkTime = GameTime;
        SetContextThink(SapperThinkContext, ThinkDelay);
    }

    /// <summary>
    /// Stock's <c>Killed</c>: a sapper taken out leaves its building
    /// stumbling for <c>SAPPER_REMOVE_DISABLE_TIME</c>.
    /// </summary>
    public override void OnKilled(EntityHandle attacker)
    {
        ResolveParent()?.SetPlasmaDisabled(RemoveDisableTime);
        base.OnKilled(attacker);
    }

    /// <summary>
    /// <c>CObjectSapper::GetReversesBuildingConstructionSpeed</c>
    /// (tf_obj_sapper.cpp:780-786): the builder's held item's
    /// <c>sapper_degenerates_buildings</c> — the speed the riding building
    /// unwinds its upgrade by, and the recorder stamp's gate.
    /// </summary>
    public float ReversesBuildingConstructionSpeed()
        => Owners is { } holders && Hooks is { } hooks && Builder != EntityHandle.None
               ? hooks.Float(0f, "sapper_degenerates_buildings",
                             holders.ActiveItemOf(Entities.GetIndex(Builder)))
               : 0f;

    /// <summary>
    /// <c>CObjectSapper::OnTakeDamage</c>'s gate (tf_obj_sapper.cpp:562-575):
    /// the wrench's fix (<c>TF_DMG_WRENCH_FIX</c>) always lands; anything else
    /// lands only from the weapon bearing <c>set_dmg_apply_to_sapper</c> — the
    /// Homewrecker's and the Maul's arm — asked of the hit's own launcher item
    /// off the scope, the held item only where the scope carries none, the way
    /// <c>mult_dmg_vs_buildings</c> is.
    /// </summary>
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        bool wrenchFix = _scopes.TryCurrent(out TFDamageCustom custom, out _, out _)
                      && custom == TFDamageCustom.WrenchFix;

        // The attribute arm of stock's gate: the making weapon's
        // set_dmg_apply_to_sapper over zero (info.GetWeapon(),
        // tf_obj_sapper.cpp:568-571) — the scope's launcher where the blast
        // carries one, the held item beside a hitscan's instant touch.
        if (!wrenchFix
            && !(attacker != EntityHandle.None && Hooks is { } attr && Owners is { } holders
                 && attr.Int(0, "set_dmg_apply_to_sapper",
                             _scopes.TryLauncher(out var launcher)
                                 ? launcher
                                 : holders.ActiveItemOf(Entities.GetIndex(attacker))) != 0))
        {
            return 0;
        }

        return base.OnDamaged(attacker, damage, damageType);
    }

    /// <summary>Stock's <c>DetachObjectFromObject</c>: the parent is told on any removal.</summary>
    protected override void OnDestroyed() => ResolveParent()?.OnRemoveSapper();

    /// <summary>Where the sapper's think finds its parent.</summary>
    private TFBaseObject? ResolveParent() => _parent;
}

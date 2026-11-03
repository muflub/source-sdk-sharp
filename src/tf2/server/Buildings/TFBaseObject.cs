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
using TF2.Server.Player;
using TF2.Server.Voice;
using TF2.Shared;

namespace TF2.Server.Buildings;

/// <summary>
/// What the world hears when a buildable's story turns: stock's
/// <c>CTFPlayer::OwnedObjectDestroyed</c> and the built notice (the round-end
/// announcement and the <c>player_builtobject</c> event are deferred rows).
/// </summary>
public interface ITFObjectAnnouncer
{
    /// <summary>The build finished its construction time and went active.</summary>
    void Built(EntityHandle builder, TFObjectType type, int mode);

    /// <summary>The object is gone — built or not, by hand or by damage.</summary>
    void Destroyed(EntityHandle builder, TFObjectType type, int mode);
}

/// <summary>The console voice of the buildings: one line per turn of the story.</summary>
[SideService(Side.Server, As = [typeof(ITFObjectAnnouncer)])]
public sealed class TFObjectAnnouncer(ILog log) : ITFObjectAnnouncer
{
    public void Built(EntityHandle builder, TFObjectType type, int mode)
        => log.Msg($"[TF2/Build] {TFObjectClassNames.Raw(type)} mode {mode} built\n");

    public void Destroyed(EntityHandle builder, TFObjectType type, int mode)
        => log.Msg($"[TF2/Build] {TFObjectClassNames.Raw(type)} mode {mode} destroyed\n");
}

/// <summary>
/// <c>CBaseObject</c> (tf_obj.cpp): what the sentry, the dispenser and
/// the teleporter share — the builder, the team, the mode, the build-up, the
/// metal pool, and the ways one ends.
///
/// <para>
/// <b>The flow.</b> Stock spawns the object at the map's point and the
/// builder walks it into place; here the managed build command places it
/// outright (<c>ITFObjectBuilder</c>). Either way the states run the same:
/// <see cref="StartPlacement"/> sets builder and team, <see cref="StartBuilding"/>
/// pays is not here (the command pays through <c>Arsenal.TakeAmmo</c>) and
/// drops health to the construction start, the base think constructs health up
/// over the object's build time, and <see cref="FinishedBuilding"/> opens the
/// subclass through <see cref="OnGoActive"/>. Death and
/// <see cref="DestroyObject"/> both end at removal; the builder is told
/// either way (<c>OwnedObjectDestroyed</c>'s managed half, the announcer).
/// </para>
///
/// <para>
/// <b>Health lives in the native shell.</b> Stock's <c>m_iHealth</c> is the
/// object's own field; here the core's damage path subtracts the shell's and
/// calls <see cref="OnKilled"/> at zero, exactly as <c>ss_barrel</c> proved
/// (shell_standalone_base.cpp:1199-1213) — so <see cref="Health"/> reads and
/// writes through the engine and managed keeps no second copy to go stale.
/// </para>
/// </summary>
public abstract class TFBaseObject(ITFMapPlayers players, ITFObjectAnnouncer announcer,
                                   IEnginePrecache precache, IEngineEntityRender render,
                                   ITFAmmoPurse purse, TFPlayerCarriers? carriers = null,
                                   TFConceptSpeech? speech = null) : ScriptedEntity
{
    /// <summary><c>SENTRYGUN_MAX_HEALTH</c> and its dispenser and teleporter kin (tf_obj_sentrygun.h:31 et al.): all three at 150.</summary>
    public const int ObjectMaxHealth = 150;

    /// <summary><c>UPGRADE_LEVEL_HEALTH_MULTIPLIER</c> (tf_obj.cpp:57): each level's health over the last.</summary>
    public const float UpgradeLevelHealthMultiplier = 1.2f;

    /// <summary><c>OBJ_MAX_UPGRADE_LEVEL</c> (tf_obj.h:34).</summary>
    public const int MaxUpgradeLevel = 3;

    /// <summary><c>tf_obj_upgrade_per_hit</c> (tf_obj.cpp:99): the metal a swing pours into the upgrade.</summary>
    public const int UpgradePerHit = 25;

    /// <summary><c>CTFWrench::GetRepairAmount</c> (tf_weapon_wrench.cpp:344-362) before its hooks.</summary>
    public const float WrenchRepairAmount = 100f;

    /// <summary><c>Command_Repair</c>'s <c>flRepairToMetalRatio</c> (tf_obj.cpp:2942): three health the metal.</summary>
    public const float RepairMetalRatio = 3f;

    /// <summary>
    /// <c>OBJECT_CONSTRUCTION_STARTINGHEALTH</c> (tf_obj.h:39): an absolute
    /// health, 0.1 — whose ceiling is the 1 the shell comes up holding
    /// (tf_obj.cpp:1401 → SetHealth's ceil, :1595).
    /// </summary>
    public const float ConstructionStartHealth = 1f;

    /// <summary><c>BASE_OBJECT_THINK_DELAY</c>: the base think's period.</summary>
    public const float BaseObjectThinkDelay = 0.1f;

    /// <summary>The players the object reads and feeds.</summary>
    protected readonly ITFMapPlayers Players = players;

    /// <summary>The console voice, held so a subclass can tell its own story.</summary>
    protected readonly ITFObjectAnnouncer Announcer = announcer;

    /// <summary>Where the state models are precached, at the spawn.</summary>
    protected readonly IEnginePrecache Precache = precache;

    /// <summary>Where the build animation's sequence is asked and played.</summary>
    protected readonly IEngineEntityRender Render = render;

    /// <summary>Where the wrench's metal comes from and goes (<c>CTFPlayer</c>'s ammo pool).</summary>
    protected readonly ITFAmmoPurse Purse = purse;

    /// <summary>The response rules the build, the loss and the sapping are told through.</summary>
    protected readonly TFConceptSpeech? Speech = speech;

    [SaveField] private EntityHandle _builder;
    [SaveField] private int _team;
    [SaveField] private int _mode;
    [SaveField] private bool _placing = true;
    [SaveField] private bool _building;
    [SaveField] private bool _built;
    [SaveField] private bool _dead;
    [SaveField] private float _builtStartTime = -1f;
    [SaveField] private int _constructedHealth;
    [SaveField] private int _metal;
    [SaveField] private bool _hasSapper;
    [SaveField] private EntityHandle _sapper;
    [SaveField] private float _plasmaDisableUntil;
    [SaveField] private bool _carried;
    [SaveField] private bool _carryDeploy;
    [SaveField] private int _healthOnPickup;
    [SaveField] private float _buildRate = 1f;
    [SaveField] private int _desiredBuildRotations;
    [SaveField] private int _level = 1;
    [SaveField] private int _upgradeMetal;
    [SaveField] private bool _upgrading;
    [SaveField] private float _upgradeCompleteTime;
    [SaveField] private int _kills;
    [SaveField] private int _assists;

    /// <summary>What this object is, for the object table and the refusals.</summary>
    public abstract TFObjectType Type { get; }

    public EntityHandle Builder => _builder;

    /// <summary><c>GetTeamNumber</c>: taken from the builder at placement.</summary>
    public int Team => _team;

    /// <summary><c>GetMode</c>: teleporter entrance or exit; 0 for the others.</summary>
    public int Mode => _mode;

    /// <summary><c>m_bPlacing</c>: not paid for yet, and cannot be hurt.</summary>
    public bool IsPlacing => _placing;

    /// <summary><c>m_bBuilding</c>: construction is running.</summary>
    public bool IsBuilding => _building;

    /// <summary><c>m_bBuilt</c>: construction finished.</summary>
    public bool IsBuilt => _built;

    /// <summary><c>m_bDead</c>: gone, or on the way.</summary>
    public bool IsDead => _dead;

    /// <summary><c>GetLevel</c>: the upgrade level, 1 until the wrench raises it.</summary>
    public int Level => _level;

    /// <summary><c>m_iUpgradeMetal</c>: what this level's upgrade has collected so far.</summary>
    public int UpgradeMetal => _upgradeMetal;

    /// <summary><c>IsUpgrading</c>: the level up is being played.</summary>
    public bool IsUpgrading => _upgrading;

    /// <summary><c>m_iKills</c> (tf_object.cpp): enemies this object killed —
    /// the status panel's kills column.</summary>
    public int Kills => _kills;

    /// <summary><c>m_iAssists</c>: enemies killed while this object wounded them.</summary>
    public int Assists => _assists;

    /// <summary><c>IncrementKills</c> (tf_obj.h:240), from the death rules.</summary>
    public void IncrementKills() => _kills++;

    /// <summary><c>IncrementAssists</c> (tf_obj.h:243).</summary>
    public void IncrementAssists() => _assists++;

    /// <summary>
    /// <c>GetMaxHealthForCurrentLevel</c> (tf_obj.cpp:3808-3823): the base
    /// health times <c>UPGRADE_LEVEL_HEALTH_MULTIPLIER</c> to the level
    /// beyond the first — 150, 180, 216 — truncated as stock's cast is.
    /// </summary>
    public virtual int MaxHealthForCurrentLevel
        => (int)(ObjectMaxHealth * MathF.Pow(UpgradeLevelHealthMultiplier, _level - 1));

    /// <summary>
    /// The object's health, as the native shell holds it
    /// (shell_standalone_base.cpp:1199-1213): the game's wounds subtract
    /// there and never reach managed state, so reads go back through the
    /// engine and writes go to the shell — one store, never two.
    /// </summary>
    public new int Health
    {
        get => Entities.GetHealth(Handle);
        set => Entities.SetHealth(Handle, value);
    }

    /// <summary><c>m_iAmmoMetal</c>: the dispenser's pool; the other two keep it at 0.</summary>
    public int Metal
    {
        get => _metal;
        set => _metal = value;
    }

    /// <summary>HasSapper (tf_obj.cpp): the spy's sapper is attached.</summary>
    public bool HasSapper => _hasSapper;

    /// <summary><c>m_bCarried</c> (tf_obj.cpp:165): lifted into its builder's arms.</summary>
    public bool IsCarried => _carried;

    /// <summary><c>m_iDesiredBuildRotations</c> (tf_obj.cpp:175): the quarter turns the blueprint wears.</summary>
    public int DesiredBuildRotations => _desiredBuildRotations;

    /// <summary>
    /// <c>CBaseObject::RotateBuildAngles</c> (tf_obj.cpp:3416-3421): the
    /// blueprint's build angles turn a quarter turn per press. Stock's
    /// <c>m_flCurrentBuildRotation</c> approaches the turn at
    /// <c>tf_obj_build_rotation_speed</c> a frame at a time
    /// (<c>UpdateDesiredBuildRotation</c>, baseobject_shared.cpp:744-750);
    /// ours is instant, the approach being a per-frame cosmetic (the
    /// divergence ledger's).
    /// </summary>
    public void RotateBuildAngles()
        => _desiredBuildRotations = (_desiredBuildRotations + 1) % 4;

    /// <summary>
    /// The carried branch of stock's <c>CTFWeaponBuilder::StartPlacement</c>
    /// (tf_weapon_builder.cpp:1003-1007): the building comes back out of the
    /// arms — off the builder (<c>StopFollowingEntity</c>) and visible again —
    /// and is placing. <c>m_bCarried</c> stays until the place
    /// (<c>StartBuilding</c>'s :1343-1345), as stock's does.
    /// </summary>
    public void ResumePlacement(EntityHandle builder)
    {
        ClearParent();
        SetVisible(true);
        StartPlacement(builder);
    }
    /// <summary><c>m_bCarryDeploy</c>: a lifted object building again where its builder put it down.</summary>
    protected bool IsCarryDeploy => _carryDeploy;

    /// <summary><c>m_iHealthOnPickup</c> (tf_obj.cpp:3618): the health the lift remembered.</summary>
    public int HealthOnPickup => _healthOnPickup;

    /// <summary>IsPlasmaDisabled (tf_obj.cpp): the stumble a removed sapper left, until its clock runs out.</summary>
    public bool IsPlasmaDisabled => GameTime < _plasmaDisableUntil;

    /// <summary>
    /// <c>IsDisabled</c> — <c>UpdateDisabledState</c>'s answer for what the
    /// object's function does: the two arms this milestone has (the round-won
    /// arm is game rules, deferred). Read off the fields rather than kept as
    /// stock's <c>m_bDisabled</c>, so the plasma arm needs no think to expire.
    /// </summary>
    public bool IsDisabled => _hasSapper || IsPlasmaDisabled;

    /// <summary>
    /// <c>CBaseObject::OnAddSapper</c> (tf_obj.cpp:2613-2629): the sapper is
    /// on, its handle kept for the removal cascade, and the builder is told
    /// over the response rules.
    /// </summary>
    public void OnAddSapper(EntityHandle sapper)
    {
        _hasSapper = true;
        _sapper = sapper;
        Speech?.Speak(_builder, TFConceptSpeech.SpySapper, "objtype", ResponseModifier());
    }

    /// <summary>
    /// The removal cascade: stock's removal orphans a CBaseEntity's children
    /// for delete (baseentity.cpp:2575-2585), and the sapper is the one
    /// child the buildings carry — ours to reach, since it is no engine
    /// child. A sapper that never went active is not registered here; its
    /// own think stands it down (tf_obj_sapper.cpp:486-488).
    /// </summary>
    private void RemoveAttachedSapper()
    {
        if (_sapper == EntityHandle.None)
            return;

        if (FindByHandle<TFSapper>(_sapper) is { IsDead: false } sapper)
            sapper.DestroyObject();

        _sapper = EntityHandle.None;
    }

    /// <summary><c>OnRemoveSapper</c> (tf_obj.cpp): the sapper is off.</summary>
    public void OnRemoveSapper() => _hasSapper = false;

    /// <summary><c>SetPlasmaDisabled</c> (tf_obj.cpp): the stumble's clock wound.</summary>
    public void SetPlasmaDisabled(float duration) => _plasmaDisableUntil = GameTime + duration;

    /// <summary>
    /// <c>CBaseObject::MakeCarriedObject</c> (tf_obj.cpp:3610-3636): the
    /// building leaves the world for its builder's arms — inactive
    /// (<c>m_bCarried</c>), its wounds remembered (<c>m_iHealthOnPickup</c>),
    /// mounted to the player (<c>FollowEntity</c>, :3624) and out of every
    /// think, the subclass's own through <see cref="OnCarried"/>. (The
    /// screens' destruction and the <c>player_carryobject</c> event ride
    /// with the deferred art and events.)
    /// </summary>
    /// <summary>Stock's <c>CTFPlayerShared::SetCarriedObject</c> bool half
    /// (tf_player_shared.cpp:14562): the carrier's word.</summary>
    private void SetCarriedObject(EntityHandle carrier, bool carrying)
    {
        if (carrier != EntityHandle.None)
            carriers?.Get(carrier.EntryIndex)?.CarryingObject = carrying;
    }

    public void MakeCarriedObject(EntityHandle carrier)
    {
        // Not `|| _carried`: stock's StopPlacement re-carries a carried
        // object when the builder leaves the hands (tf_weapon_builder.cpp:1045-
        // 1047), and the re-carry must re-parent and re-hide. Only the dead
        // refuse.
        if (_dead)
            return;

        _carried = true;
        _carryDeploy = false;
        SetCarriedObject(carrier, true);   // pCarrier->m_Shared.SetCarriedObject( this ) (tf_obj.cpp:3617)
        _healthOnPickup = Health;
        SetVisible(false);
        SetNotSolid(true);
        SetParent(carrier);
        StopThinking();
        OnCarried();
    }

    /// <summary>
    /// The redeploy's <c>StartBuilding</c> arm (tf_obj.cpp:1343-1345,
    /// :1375-1388): out of the arms (<c>m_bCarried = false</c>,
    /// <c>m_bCarryDeploy = true</c>), back on the ground at the new spot
    /// (<c>StopFollowingEntity</c>, tf_weapon_builder.cpp:1006) and
    /// constructing again at the redeploy rate, health from the pickup
    /// (:1377) — never above it until the deploy is done
    /// (<c>SetHealth</c>'s cap, :1595-1602).
    /// </summary>
    public void StartRedeploy(Vector origin, Vector angles, float buildRate)
    {
        _carried = false;
        _carryDeploy = true;
        SetCarriedObject(_builder, false);   // the deploy's SetCarriedObject(NULL) (tf_weapon_builder.cpp:1115)
        ClearParent();
        Entities.Teleport(Handle, origin, angles, zeroVelocity: true);
        StartBuilding(buildRate);
    }

    /// <summary>A subclass's think to stand down when it is lifted.</summary>
    protected virtual void OnCarried() { }

    /// <summary>
    /// <c>CBaseObject::Spawn</c> + <c>Precache</c> (tf_obj.cpp:356, :381): the
    /// state's models are precached and the blueprint is worn — stock spawns
    /// dressed (<c>SetModel(SENTRY_MODEL_PLACEMENT)</c>,
    /// tf_obj_sentrygun.cpp:180). The sapper, stock's
    /// <c>OF_DOESNT_HAVE_A_MODEL</c>, wears nothing and is not drawn.
    /// </summary>
    public override void OnSpawn()
    {
        base.OnSpawn();
        _placing = true;
        _desiredBuildRotations = 0;           // m_iDesiredBuildRotations' Spawn reset (tf_obj.cpp:402)
        Entities.SetHealth(Handle, 0);
        SetSolidity(BrushSolidity.None);

        if (TFObjectInfo.Of(Type) is null)
        {
            SetVisible(false);
        }
        else
        {
            PrecacheAll();
            Dress(TFObjectModels.Placement(Type, _mode));
        }

        SetNextThink(BaseObjectThinkDelay);
    }

    /// <summary>The three models the states move through, stock's <c>Precache</c> arms (tf_obj_sentrygun.cpp:465-482), every level's among them.</summary>
    private void PrecacheAll()
    {
        Precache.PrecacheModel(TFObjectModels.Placement(Type, _mode));
        Precache.PrecacheModel(TFObjectModels.Building(Type));
        Precache.PrecacheModel(TFObjectModels.Finished(Type));

        for (int level = 2; level <= MaxUpgradeLevel; level++)
        {
            if (TFObjectModels.Upgrading(Type, level) is { } upgrading)
                Precache.PrecacheModel(upgrading);
            Precache.PrecacheModel(TFObjectModels.Finished(Type, level));
        }
    }

    /// <summary><c>SetModel</c> + drawn (stock's <c>m_nRenderMode</c> stays kRenderNormal over the life).</summary>
    private void Dress(string model)
    {
        SetModel(model);
        SetVisible(true);
    }

    /// <summary>
    /// <c>CBaseObject::StartPlacement</c>: the builder's, and on the builder's
    /// team. (The build box and the NOT_SOLID switch ride with the placement
    /// trace, which is the click-to-place's to own — deferred.)
    /// </summary>
    public void StartPlacement(EntityHandle builder)
    {
        _builder = builder;
        _team = Players.Team(builder);
        _placing = true;
    }

    /// <summary>
    /// The placement ghost's visibility (<c>UpdateAttachmentPlacement</c>'s
    /// <c>EF_NODRAW</c> arm, tf_obj.cpp:1263-1277): hidden while no sappable
    /// stands under the crosshair, shown the command one does. The walk owns
    /// the answer, so the object carries it past <see cref="SetVisible"/>'s
    /// protected reach.
    /// </summary>
    public void SetPlacementVisible(bool visible) => SetVisible(visible);

    /// <summary>
    /// <c>SetObjectMode</c>: the teleporter's entrance/exit, 0 otherwise. The
    /// blueprint names the end, so a placing teleporter re-dresses it
    /// (stock's <c>SetTeleporterType</c> runs before the model's ask).
    /// </summary>
    public void SetObjectMode(int mode)
    {
        _mode = mode;

        // The model gates the dress, not the object table: the sapper has no
        // row (TFObjectDefs) and still wears its placement body.
        if (_placing && TFObjectModels.Placement(Type, mode).Length > 0)
            Dress(TFObjectModels.Placement(Type, mode));
    }

    /// <summary>
    /// <c>CBaseObject::StartBuilding</c>: construction begins, health at the
    /// starting health (1, the ceiling of stock's 0.1) — or at the pickup's
    /// for a redeploy (tf_obj.cpp:1377) — and the base think ticks it up at
    /// <paramref name="buildRate"/> (stock's build-speed multiplier,
    /// tf_player.cpp:8237: the redeploy's +2 sentry, +3 the others).
    /// </summary>
    public void StartBuilding(float buildRate = 1f)
    {
        _placing = false;
        _building = true;
        _built = false;
        _constructedHealth = 0;
        _buildRate = MathF.Max(1f, buildRate);

        // The builder's line (tf_obj.cpp:1339, :1349): the redeploy's own
        // where the object came out of the arms, else the building's — each
        // with the object type the rules' Is* criteria read.
        Speech?.Speak(_builder, _carryDeploy ? TFConceptSpeech.RedeployBuilding : TFConceptSpeech.BuildingObject,
                      "objtype", ResponseModifier());

        // SetStartBuildingModel + RemoveSolidFlags(FSOLID_NOT_SOLID)
        // (tf_obj.cpp:1406): construction is lived in the world, visible and
        // solid on the object's hull — SENTRYGUN_MINS/MAXS and kin
        // (tf_obj_sentrygun.cpp:470-482's UTIL_SetSize). Bounds before
        // solidity: the partition insert carries the box. The collision group
        // stays the default — stock's TFCOLLISION_GROUP_OBJECT_* is beyond the
        // engine's LAST_SHARED_COLLISION_GROUP, and the default blocks every
        // player as the SOLIDTOPLAYERMOVEMENT rule observably does
        // (baseobject_shared.cpp:464-496).
        if (TFObjectInfo.Of(Type) is not null)
        {
            (Vector mins, Vector maxs) = TFObjectModels.Hull(Type);
            Entities.SetBounds(Handle, mins, maxs);
            Entities.SetSolidity(Handle, SolidType.BoundingBox, SolidFlags.None);
            Dress(TFObjectModels.Building(Type));

            // DetermineAnimation's IsBuilding arm (tf_obj.cpp:3146-3149) and
            // DeterminePlaybackRate (tf_obj.cpp:745): the assembling sequence,
            // at the construction multiplier's half — the anim is authored for
            // one builder. The activity name first, the label its fallback.
            int sequence = Render.LookupSequenceByActivity(Handle, "ACT_OBJ_ASSEMBLING");
            if (sequence < 0)
                sequence = Render.LookupSequence(Handle, "build");
            if (sequence >= 0)
                Render.PlaySequence(Handle, sequence, _buildRate * 0.5f, restart: true);
        }

        Health = _carryDeploy ? _healthOnPickup : (int)ConstructionStartHealth;
        _builtStartTime = GameTime;
        SetNextThink(BaseObjectThinkDelay);
    }

    /// <summary>
    /// <c>CBaseObject::BaseObjectThink</c>: constructs while the build is up,
    /// finishes an upgrade when its clock runs out
    /// (<c>UpgradeThink</c>, tf_obj.cpp:3086-3092), and keeps the period
    /// until the object dies. Carried, nothing runs — the arms hold it still
    /// (stock's carried object has no think either).
    /// </summary>
    public override void OnThink()
    {
        if (_dead || _carried)
            return;

        if (_upgrading)
        {
            if (GameTime >= _upgradeCompleteTime)
                FinishUpgrading();

            SetNextThink(BaseObjectThinkDelay);
            return;
        }

        if (_building)
            Construct();

        UpdateMetal();
        SetNextThink(BaseObjectThinkDelay);
    }

    /// <summary><c>UpdateMetal</c>: what drains and refills the pool; the dispenser's own.</summary>
    protected virtual void UpdateMetal() { }

    /// <summary>
    /// <c>CBaseObject::Construct</c>: stock adds the tick's slice to what
    /// stands (tf_obj.cpp:1532 → :2124, <c>SetHealth(m_flHealth + flHealth)</c>)
    /// — wounds and all, never a rewrite from the clock — and at full time
    /// the object goes active. The slice is the max minus the start spread
    /// over the total time; the whole part paid so far is remembered so a
    /// wound between ticks is not paid for twice.
    /// </summary>
    private void Construct()
    {
        TFObjectInfo? info = TFObjectInfo.Of(Type);
        float buildTime = (info?.BuildTime ?? 0f) / _buildRate;
        float flPercentBuilt = buildTime > 0f ? (GameTime - _builtStartTime) / buildTime : 1f;

        if (flPercentBuilt >= 1f)
        {
            FinishedBuilding();
            return;
        }

        int constructed = (int)(((double)MaxHealthForCurrentLevel - ConstructionStartHealth)
                                * ((GameTime - _builtStartTime) / buildTime));
        int slice = constructed - _constructedHealth;
        if (slice > 0)
        {
            _constructedHealth = Math.Min(constructed, MaxHealthForCurrentLevel);
            // A redeploy never heals above what the lift remembered until
            // the deploy is done (tf_obj.cpp:1595-1602).
            int ceiling = _carryDeploy ? Math.Min(MaxHealthForCurrentLevel, _healthOnPickup)
                                       : MaxHealthForCurrentLevel;
            Health = Math.Min(ceiling, Health + slice);
        }
    }

    /// <summary>
    /// <c>CBaseObject::FinishedBuilding</c>: full health, built, and the
    /// subclass opens its business through <see cref="OnGoActive"/>.
    /// </summary>
    public void FinishedBuilding()
    {
        _building = false;
        _built = true;
        _carryDeploy = false;

        // OnGoActive's model (tf_obj_sentrygun.cpp:396, tf_obj_teleporter.cpp:425,
        // the sapper's tf_obj_sapper.cpp:265): the finished level model, set
        // before the subclass opens its business. The model gates the dress,
        // not the object table — the sapper has no row and still wears its
        // placed body.
        if (TFObjectModels.Finished(Type, _level).Length > 0)
            Dress(TFObjectModels.Finished(Type, _level));

        Health = MaxHealthForCurrentLevel;
        OnGoActive();
        Announcer.Built(_builder, Type, _mode);
    }

    /// <summary>
    /// <c>CTFBaseObject::OnGoActive</c>: the object has stood long enough to
    /// work; what working means is the subclass's.
    /// </summary>
    protected virtual void OnGoActive() { }

    /// <summary>
    /// <c>CBaseObject::InputWrenchHit</c> (tf_obj.cpp:2746-2802) without the
    /// sapper arm — that lives with the swing (<see cref="TFSapperUse"/>),
    /// which lands the blow on the attached leech. A standing object takes
    /// <see cref="OnWrenchHit"/>; one still building answers stock's
    /// <c>OnConstructionHit</c> instead, which is not this milestone's.
    /// </summary>
    public bool WrenchHit(EntityHandle player)
    {
        if (_dead || _placing || _carried)
            return false;

        if (_building && !_built)
            return false;

        return OnWrenchHit(player);
    }

    /// <summary>
    /// <c>CBaseObject::OnWrenchHit</c> (tf_obj.cpp:2807-2822): the repair
    /// first, and only a swing that repaired nothing asks the upgrade —
    /// stock's order, so a wounded building is healed before it is raised.
    /// </summary>
    protected virtual bool OnWrenchHit(EntityHandle player)
    {
        if (RepairFrom(player) > 0)
            return true;

        return CheckUpgradeOnHit(player);
    }

    /// <summary>
    /// <c>CBaseObject::Command_Repair</c> (tf_obj.cpp:2942-3005): up to
    /// <see cref="WrenchRepairAmount"/> health at the ratio's cost in metal,
    /// the cost clipped to what the player holds — what cannot be afforded
    /// repairs what it can. Returns the repaired amount, 0 for a swing that
    /// mended nothing (full health, or no metal for even one point).
    /// </summary>
    private int RepairFrom(EntityHandle player)
    {
        int repairAmount = Math.Min((int)WrenchRepairAmount, MaxHealthForCurrentLevel - Health);
        if (repairAmount <= 0)
            return 0;

        string metalName = TFAmmoNames.Name(TFAmmo.Metal);
        int cost = (int)MathF.Ceiling(repairAmount / RepairMetalRatio);
        cost = Math.Min(cost, Purse.Pools(player).GetValueOrDefault(metalName));
        if (cost <= 0)
            return 0;

        repairAmount = (int)(cost * RepairMetalRatio);
        Health = Math.Min(MaxHealthForCurrentLevel, Health + repairAmount);
        Purse.TakeAmmo(player, cost, metalName);
        return repairAmount;
    }

    /// <summary>
    /// <c>CBaseObject::CheckUpgradeOnHit</c> (tf_obj.cpp:2846-2914): the
    /// swing's metal (<see cref="UpgradePerHit"/>, clipped to the pocket and
    /// to what the level still asks) goes onto <c>m_iUpgradeMetal</c>; when
    /// the level's cost is met the upgrade starts and the pot is cleared for
    /// the next one.
    /// </summary>
    public bool CheckUpgradeOnHit(EntityHandle player)
    {
        if (!CanBeUpgraded(player))
            return false;

        string metalName = TFAmmoNames.Name(TFAmmo.Metal);
        int toAdd = Math.Min(UpgradePerHit, Purse.Pools(player).GetValueOrDefault(metalName));
        if (toAdd > RequiredUpgradeMetal - _upgradeMetal)
            toAdd = RequiredUpgradeMetal - _upgradeMetal;

        if (toAdd > 0)
            Purse.TakeAmmo(player, toAdd, metalName);

        _upgradeMetal += toAdd;
        bool didWork = toAdd > 0;

        if (_upgradeMetal >= RequiredUpgradeMetal)
        {
            StartUpgrading();
            _upgradeMetal = 0;
        }

        return didWork;
    }

    /// <summary><c>GetObjectInfo( GetType() )->m_UpgradeCost</c> (tf_obj.cpp:2639-2642).</summary>
    /// <summary>This level's upgrade price (<c>m_iUpgradeMetal</c>'s target,
    /// TFObjectInfo's <c>UpgradeCost</c>): the status panel's upgrade bar
    /// divides the collected metal by it.</summary>
    public int RequiredUpgradeMetal => TFObjectInfo.Of(Type)?.UpgradeCost ?? 0;

    /// <summary>
    /// <c>CBaseObject::CanBeUpgraded</c> (tf_obj.cpp:2919-2937): not already
    /// mid-upgrade, not on the redeploy arm, the player the trade belongs to
    /// (<c>ClassCanBuild</c> — all three buildables are the engineer's), and
    /// a level left to climb.
    /// </summary>
    public bool CanBeUpgraded(EntityHandle player)
        => !_upgrading
           && !_carryDeploy
           && Players.ClassOf(player) == TFClass.Engineer
           && _level < MaxUpgradeLevel;

    /// <summary>
    /// <c>CBaseObject::StartUpgrading</c> (tf_obj.cpp:3010-3063): the level
    /// climbs, the health jumps to the new level's table
    /// (<c>SetMaxHealth</c> + <c>SetHealth</c>, :3029-3031), the level's
    /// upgrade body is worn with <c>ACT_OBJ_UPGRADING</c> playing, and the
    /// completion clock is wound to <c>m_flUpgradeDuration</c>.
    /// </summary>
    public virtual void StartUpgrading()
    {
        OnStartUpgrade();

        _level++;
        _upgrading = true;
        _upgradeCompleteTime = GameTime + (TFObjectInfo.Of(Type)?.UpgradeDuration ?? 0f);

        Health = MaxHealthForCurrentLevel;

        if (TFObjectInfo.Of(Type) is not null)
        {
            if (TFObjectModels.Upgrading(Type, _level) is { } upgrading)
                Dress(upgrading);

            int sequence = Render.LookupSequenceByActivity(Handle, "ACT_OBJ_UPGRADING");
            if (sequence >= 0)
                Render.PlaySequence(Handle, sequence, 1f, restart: true);
        }
    }

    /// <summary>
    /// <c>CBaseObject::FinishUpgrading</c> (tf_obj.cpp:3068-3081): the
    /// upgrade is done and the level's finished model is worn.
    /// </summary>
    public virtual void FinishUpgrading()
    {
        _upgrading = false;

        if (TFObjectInfo.Of(Type) is not null)
            Dress(TFObjectModels.Finished(Type, _level));

        OnFinishUpgrade();
    }

    /// <summary>The subclass's last word on an upgrade — the sentry reloads, the state returns.</summary>
    protected virtual void OnFinishUpgrade() { }

    /// <summary>
    /// The subclass's first word, before the level climbs — stock's
    /// <c>CObjectDispenser::StartUpgrading</c> clears its healing list here
    /// (tf_obj_dispenser.cpp:328-330) so everyone re-attaches at the new rate.
    /// </summary>
    protected virtual void OnStartUpgrade() { }

    /// <summary>
    /// <c>CBaseObject::DestroyObject</c>: the builder is told and the object
    /// is gone — its thinks with it, as stock's removal kills them
    /// (SUB_Remove); the booking outlives the entity here, so the cancel is
    /// ours to make. (The gibs and the dropped metal are deferred with the
    /// effects.)
    /// </summary>
    public void DestroyObject()
    {
        if (_dead)
            return;

        _dead = true;
        _building = false;
        if (_carried)
            SetCarriedObject(_builder, false);   // the carried object dies with the life (shared.Spawn's reset, tf_player_shared.cpp:991)
        Announcer.Destroyed(_builder, Type, _mode);
        OnDestroyed();
        RemoveAttachedSapper();
        StopThinking();
        Remove();
    }

    /// <summary>A last word before removal — stopping the subclass's own thinks.</summary>
    protected virtual void OnDestroyed() { }

    /// <summary>
    /// <c>CBaseObject::GetResponseRulesModifier</c> (tf_obj.cpp:1293-1305)'s
    /// value half: the object type word the buildings' rules read. Stock's
    /// NULL default is unreachable here — <see cref="Type"/> is one of the
    /// four the switch names.
    /// </summary>
    private string ResponseModifier() => Type switch
    {
        TFObjectType.Dispenser => "dispenser",
        TFObjectType.Teleporter => "teleporter",
        TFObjectType.AttachmentSapper => "sapper",
        _ => "sentrygun",
    };

    /// <summary>
    /// <c>CBaseObject::DetonateObject</c> (tf_obj.cpp:804-809): the object
    /// killed by itself — the kill path (<see cref="OnKilled"/>), not the
    /// quiet removal of <see cref="DestroyObject"/>.
    /// </summary>
    public void DetonateObject() => OnKilled(Handle);

    /// <summary>
    /// <c>CBaseObject::OnTakeDamage</c>'s refusals, on the core's damage path:
    /// dead and placing objects take nothing, friendly fire is swallowed, and
    /// everything else is passed up so the shell subtracts and calls
    /// <see cref="OnKilled"/> at zero.
    /// </summary>
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        if (_dead || damage <= 0f)
            return 0;

        // m_bPlacing: an object still being placed has no business being hurt.
        // Carried: not solid, and the arms hold it out of the world.
        if (_placing || _carried)
            return 0;

        if (!_building && !_built)
            return 0;

        // Same-team incoming, stock's team check (the engineer's own shotgun
        // muzzle reaches this too; the sapper's blow names no player at all).
        if (attacker != EntityHandle.None && Entities.IsPlayer(attacker) && Players.Team(attacker) == _team)
            return 0;

        return (int)damage;
    }

    /// <summary>
    /// <c>CBaseObject::Killed</c>: the health ran out. (The explosion's
    /// particles, gibs and metal spray are deferred rows; the builder is told.)
    /// </summary>
    public override void OnKilled(EntityHandle attacker)
    {
        if (_dead)
            return;

        _dead = true;
        _building = false;
        _built = false;
        if (_carried)
            SetCarriedObject(_builder, false);   // the carried object dies with the life (shared.Spawn's reset, tf_player_shared.cpp:991)
        Announcer.Destroyed(_builder, Type, _mode);

        // The builder's line (tf_obj.cpp:2041-2045): the blow's death tells
        // its builder, the same object word beside it.
        Speech?.Speak(_builder, TFConceptSpeech.LostObject, "objtype", ResponseModifier());
        OnDestroyed();
        RemoveAttachedSapper();
        StopThinking();
        Remove();
    }
}

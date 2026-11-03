//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using SourceSharp.Shared;

using TF2.Server.Entities.Rooms;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Ctf;

namespace TF2.Server.Entities.Ctf;

/// <summary>
/// item_teamflag: the intelligence (stock's <c>CCaptureFlag</c>,
/// shared/tf/entity_capture_flag.cpp, server half). A point trigger the
/// enemy team picks up (<see cref="FlagTouch"/>, <see cref="PickUp"/>), that
/// rides on its carrier, drops where they die or let go of it
/// (<see cref="Drop"/>), goes home on its return timer or a team mate's touch
/// (<see cref="Think"/>), and scores when its carrier reaches their own
/// capture zone (<see cref="Capture"/>, called by <see cref="FuncCaptureZone"/>).
///
/// <para>
/// <b>Ported:</b> the CTF game type whole (the milestone mode), the type
/// checks of every other type's touch, pick-up and capture scoring, and the
/// carried-flag effects: the colour trail laid from the carrier, the paper
/// trail the client starts off <see cref="State"/>, and the return icon over
/// a dropped flag (the ring's art itself is the client's; the countdown
/// shows in the HUD's flag row).
/// <b>Not ported</b> (docs/plans/tf2/deferred.tsv): the glow's outline shader
/// (<c>CGlowObject</c>, drawn by the client registry off <see cref="GlowEnabled"/> and <see cref="TFFlagNetState.Team"/>), the stats, MvM,
/// robot/player destruction's points, powerup mode's poison, the initial
/// parent, and the <c>DT_CaptureFlag</c> fields with no client reader (the
/// HUD reads the objective resource).
/// </para>
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Item)]
public sealed class ItemTeamFlag(
    IGameClock clock,
    ICtfRules rules,
    ICtfPlayers players,
    ICtfSettings settings,
    ICtfEvents events,
    IModeObjectives objectives,
    IEngineWorldQuery world,
    IEngineEntityRender render,
    IEnginePrecache precache,
    IEngineSound sound,
    ITFAnnouncer announcer,
    TFWinPanelCappers panelCappers,
    IServerSkins skins,
    ITFRespawnRooms? respawnRooms = null,
    IFlagCarrierEffects? effects = null,
    IFlagDetectionZoneDispatch? zones = null) : ScriptedEntity
{
    public const string MapClass = "item_teamflag";

    /// <summary>TF_FLAG_THINK_TIME.</summary>
    public const float ThinkTime = 0.25f;

    /// <summary>TF_FLAG_OWNER_PICKUP_TIME: a thrower may not take it back for this long.</summary>
    public const float OwnerPickupTime = 3.0f;

    /// <summary>The <c>GetPercentInvisible() > 0.25f</c> of the stealthed pick-up refuse (entity_capture_flag.cpp:1115).</summary>
    public const float InvisiblePickupLimit = 0.25f;

    /// <summary>TF_FLAG_NUMBEROFSKINS: a carried flag wears the brighter skin this many on.</summary>
    public const int NumberOfSkins = 3;

    /// <summary>TF_FLAG_MODEL.</summary>
    public const string DefaultModel = "models/flag/briefcase.mdl";

    /// <summary>The fade think's context name (stock's "FadeFlagTrail").</summary>
    public const string FadeContext = "FadeFlagTrail";

    /// <summary>The fade's step interval (RemoveFlagTrail's think delay, entity_capture_flag.cpp:2742).</summary>
    public const float FadeStepTime = 0.05f;

    /// <summary>The fade's first life (m_flFlagTrailLife's reset, entity_capture_flag.cpp:2728).</summary>
    public const float FadeLife = 1.0f;

    /// <summary>The fade's life step (entity_capture_flag.cpp:2741).</summary>
    public const float FadeLifeStep = 0.1f;

    /// <summary>TF_CTF_FLAGSPAWN.</summary>
    public const string FlagSpawnSound = "CaptureFlag.FlagSpawn";

    /// <summary>TF_CTF_CAPTURED_TEAM_SCORE, TF_INVADE_CAPTURED_TEAM_SCORE, TF_RESOURCE_CAPTURED_TEAM_SCORE.</summary>
    public const int CapturedTeamScore = 1;

    /// <summary>INVADE_NEUTRAL_TYPE_*.</summary>
    public const int InvadeNeutralNone = 0, InvadeNeutralDefault = 1, InvadeNeutralHalf = 2;

    /// <summary>INVADE_SCORING_TEAM_CAPTURE_COUNT (the other, 0, is the team score).</summary>
    public const int InvadeScoringCaptureCount = 1;

    /// <summary>TF_INVADE_NEUTRAL_TIME.</summary>
    public const float InvadeNeutralTime = 30.0f;

    /// <summary>
    /// The collision bounds Spawn forces (<c>SetCollisionBounds</c>): the hull
    /// a dropped flag's trace sweeps.
    /// </summary>
    public static Vector Mins => new(-19.5f, -22.5f, -6.5f);

    public static Vector Maxs => new(19.5f, 22.5f, 6.5f);

    /// <summary><c>UseTriggerBounds( true, 24 )</c>: the trigger is the bounds bloated 24 out and half that up.</summary>
    public const float TriggerBloat = 24f;

    public readonly Output OnReturn = new();
    public readonly Output OnPickUp = new();
    public readonly Output OnPickup1 = new();
    public readonly Output OnPickupTeam1 = new();
    public readonly Output OnPickupTeam2 = new();
    public readonly Output OnDrop = new();
    public readonly Output OnDrop1 = new();
    public readonly Output OnCapture = new();
    public readonly Output OnCapture1 = new();
    public readonly Output OnCapTeam1 = new();
    public readonly Output OnCapTeam2 = new();
    public readonly Output OnTouchSameTeam = new();

    [ScriptVars] public ItemTeamFlagVars Vars = new();

    /// <summary>
    /// The wire of <c>DT_CaptureFlag</c>'s fields with a client reader: the
    /// status, the paper effect and the trail switch, which the mirror
    /// <c>ClientTFFlag</c> reads to run the paper trail.
    /// </summary>
    [NetState] public TFFlagNetState State = new();

    private bool _disabled;
    private TFFlagStatus _status = TFFlagStatus.Home;
    private float _resetTime;
    private float _maxResetTime;
    private float _neutralTime;
    private EntityHandle _prevOwner;
    private EntityHandle _initialPlayer;
    private bool _allowOwnerPickup = true;
    private float _ownerPickupTime;
    private bool _captured;
    private bool _touchEnabled;
    private bool _thinking;
    private float _lastPickupTime;
    private float _lastResetDuration;
    private int _returnTime;
    private TFTeam _team;
    private TFTeam _originalTeam;
    private int _skin;
    private bool _glowEnabled = true;
    private Vector _resetPos;
    private Vector _resetAng;
    private EntityHandle _trail;
    private float _trailLife = FadeLife;
    private bool _instantTrailRemove;
    private EntityHandle _returnIcon;

    // ---- what the rules, the zones and the mode read ------------------------

    public TFFlagType Type => (TFFlagType)Vars.GameType;

    /// <summary>GetTeamNumber(): the team whose flag this is (an invade or resource flag takes its carrier's).</summary>
    public TFTeam Team => _team;

    public TFFlagStatus Status => _status;

    public bool IsHome => _status == TFFlagStatus.Home;

    public bool IsStolen => _status == TFFlagStatus.Stolen;

    public bool IsDropped => _status == TFFlagStatus.Dropped;

    public bool IsDisabled => _disabled;

    /// <summary>IsCaptured: captured this think; the touch is off until the next.</summary>
    public bool IsCaptured => _captured;

    /// <summary>m_hPrevOwner: the carrier while stolen, the last one after.</summary>
    public EntityHandle PrevOwner => _prevOwner;

    /// <summary>The player carrying this flag, or <see cref="EntityHandle.None"/>.</summary>
    public EntityHandle Carrier => IsStolen ? _prevOwner : EntityHandle.None;

    /// <summary>m_flResetTime: when a dropped flag goes home; 0 with none.</summary>
    public float ResetTime => _resetTime;

    /// <summary>m_flMaxResetTime: the return timer's full length.</summary>
    public float MaxResetTime => _maxResetTime;

    /// <summary>m_flNeutralTime (invade): when a dropped flag goes back to its original team.</summary>
    public float NeutralTime => _neutralTime;

    /// <summary>m_nReturnTime: seconds a dropped flag waits (the map's <c>ReturnTime</c>, or <c>SetReturnTime</c>'s).</summary>
    public int ReturnTime => _returnTime;

    public bool AllowOwnerPickup => _allowOwnerPickup;

    public bool GlowEnabled => _glowEnabled;

    public int Skin => _skin;

    /// <summary>Whether a touch reaches <see cref="FlagTouch"/> (stock's SetTouch).</summary>
    public bool TouchEnabled => _touchEnabled;

    /// <summary>Whether the think runs (stock's SetThink).</summary>
    public bool Thinking => _thinking;

    /// <summary>Where the flag goes home to (m_vecResetPos).</summary>
    public Vector HomeOrigin => _resetPos;

    private float Now => clock.Now;

    // ---- spawn --------------------------------------------------------------

    /// <summary>GetFlagModel: the map's <c>flag_model</c>, else the briefcase.</summary>
    public string FlagModel => Vars.FlagModel.Length > 0 ? Vars.FlagModel : DefaultModel;

    /// <summary>Spawn (L592): the model, the trigger, the home position, then SetDisabled.</summary>
    public override void OnSpawn()
    {
        precache.PrecacheModel(FlagModel);
        precache.PrecacheSound(FlagSpawnSound);
        // The announcer's soundscripts, both arms (stock's Precache,
        // entity_capture_flag.cpp:342-360; the MvM variants with that mode).
        string[] announcerSounds =
        [
            TFCtfSounds.EnemyStolen, TFCtfSounds.EnemyDropped, TFCtfSounds.EnemyCaptured, TFCtfSounds.EnemyReturned,
            TFCtfSounds.TeamStolen, TFCtfSounds.TeamDropped, TFCtfSounds.TeamCaptured, TFCtfSounds.TeamReturned,
            TFAttackDefendSounds.Captured,
            TFAttackDefendSounds.EnemyStolen, TFAttackDefendSounds.EnemyDropped, TFAttackDefendSounds.EnemyCaptured, TFAttackDefendSounds.EnemyReturned,
            TFAttackDefendSounds.TeamStolen, TFAttackDefendSounds.TeamDropped, TFAttackDefendSounds.TeamCaptured, TFAttackDefendSounds.TeamReturned,
        ];
        foreach (string announce in announcerSounds)
            precache.PrecacheSound(announce);
        // The paper effect and both colour trails (stock's Precache, entity_capture_flag.cpp:403-410).
        precache.PrecacheParticleSystem(GetPaperEffect());
        precache.PrecacheModel(TFFlagTrail.Material(TFTeam.Red, Vars.FlagTrail));
        precache.PrecacheModel(TFFlagTrail.Material(TFTeam.Blue, Vars.FlagTrail));
        SetModel(FlagModel);

        // The trigger: the forced collision bounds bloated 24 sideways and 12 up
        // (WorldSpaceTriggerBounds' non-uniform bloat); the box starts at the
        // origin, not 6.5 below it (docs/divergences.md).
        MakePickup(Maxs.X + TriggerBloat, Maxs.Y + TriggerBloat, Maxs.Z + TriggerBloat * 0.5f);

        // UpdateTransmitState: ALWAYS transmit to all clients.
        render.SetTransmitAlways(Handle, true);

        _team = (TFTeam)Vars.TeamNum;
        _returnTime = Vars.ReturnTime;
        _disabled = Vars.StartDisabled;

        // The wire the mirror reads (m_szPaperEffect, m_nUseTrailEffect, and
        // the glow halves: m_bGlowEnabled and GetTeamNumber).
        State.UseTrailEffect = (byte)Vars.TrailEffect;
        State.PaperEffect = Vars.FlagPaper;
        State.GlowEnabled = (byte)(_glowEnabled ? 1 : 0);
        State.Team = (byte)_team;

        // Save the starting position, so we can reset the flag later if need be.
        _resetPos = Origin;
        _resetAng = Entities.GetAngles(Handle);

        _status = TFFlagStatus.Home;
        SetFlagStatus(TFFlagStatus.Home);
        ResetFlagReturnTime();
        ResetFlagNeutralTime();

        _initialPlayer = EntityHandle.None;
        _allowOwnerPickup = true;
        _prevOwner = EntityHandle.None;
        _captured = false;

        // Activate: the original team and the skin (0 red, 1 blue, 2 unassigned).
        _originalTeam = _team;
        SetSkin(TeamSkin());

        SetDisabled(_disabled);
    }

    /// <summary>UpdateOnRemove: the flag leaves the HUD's table with it, and
    /// the effects it owned go with it.</summary>
    public override void OnRemoved()
    {
        objectives.SetFlagPresent((int)_team, false);
        if (_trail != EntityHandle.None && Entities.IsValid(_trail))
            Entities.Remove(_trail);
        if (_returnIcon != EntityHandle.None && Entities.IsValid(_returnIcon))
            Entities.Remove(_returnIcon);
        base.OnRemoved();
    }

    private int TeamSkin() => _team == TFTeam.Unassigned ? 2 : (int)_team - 2;

    private void SetSkin(int skin)
    {
        _skin = skin;
        skins.SetSkin(Handle, skin);
    }

    // ---- enable / disable ---------------------------------------------------

    /// <summary>SetDisabled: off hides it (or dims it, VisibleWhenDisabled) and stops touch and think.</summary>
    public void SetDisabled(bool disabled)
    {
        _disabled = disabled;
        objectives.SetFlagPresent((int)_team, !disabled);   // a disabled flag is not in play for the HUD

        if (disabled)
        {
            // kRenderTransAlpha at 180 when VisibleWhenDisabled: the shell draws
            // it or not; the alpha is not set (docs/divergences.md).
            SetVisible(Vars.VisibleWhenDisabled);
            _touchEnabled = false;
            _thinking = false;
            StopThinking();
        }
        else
        {
            SetVisible(true);
            _touchEnabled = true;
            _thinking = true;
            SetNextThink(0f);
        }
    }

    [ScriptedEvent] private void OnEnable() => SetDisabled(false);

    [ScriptedEvent] private void OnDisable() => SetDisabled(true);

    // ---- status -------------------------------------------------------------

    /// <summary>SetFlagStatus: the event on a change, the animation, the wire, and the objective table.</summary>
    public void SetFlagStatus(TFFlagStatus status, EntityHandle newOwner = default)
    {
        if (_status != status)
        {
            _status = status;
            events.FlagStatusUpdate(newOwner != EntityHandle.None ? players.UserId(newOwner) : -1, Entities.GetIndex(Handle));
        }

        State.FlagStatus = (byte)status;

        // set spin animation if it's not being held; idle if it is
        PlaySequence(status == TFFlagStatus.Stolen ? "idle" : "spin");

        Publish();
    }

    /// <summary>The flag's row of the objective table, for the HUD.</summary>
    private void Publish()
    {
        int carrier = IsStolen && _prevOwner != EntityHandle.None ? players.Index(_prevOwner) : 0;
        objectives.SetFlag((int)_team, (int)_status, carrier, _resetTime);
        // The HUD's compass reads this while the flag is home or dropped;
        // stolen, the client points at the carrier's live origin.
        objectives.SetFlagOrigin((int)_team, Origin);
    }

    public void ResetFlagReturnTime()
    {
        _resetTime = 0f;
        Publish();
    }

    public void SetFlagReturnIn(float time, float maxResetTime)
    {
        _resetTime = Now + time;
        _maxResetTime = maxResetTime;
        Publish();
    }

    public void ResetFlagNeutralTime() => _neutralTime = 0f;

    public void SetFlagNeutralIn(float time)
    {
        _neutralTime = Now + time;
        _maxResetTime = time;
    }

    // ---- the carried effects ------------------------------------------------

    /// <summary>
    /// GetPaperEffect (entity_capture_flag.cpp:318-326): the map's
    /// <c>flag_paper</c>, else <see cref="TFFlagTrail.PaperEffect"/>.
    /// </summary>
    public string GetPaperEffect() => Vars.FlagPaper.Length > 0 ? Vars.FlagPaper : TFFlagTrail.PaperEffect;

    /// <summary>
    /// StartFlagTrail (entity_capture_flag.cpp:2682-2713): an env_spritetrail
    /// in the carrier team's colour, parented to the flag at its origin so
    /// the ribbon runs from the carrier. Skipped for the <see
    /// cref="TFFlagEffects.None"/> and <see cref="TFFlagEffects.PaperOnly"/>
    /// switches.
    /// </summary>
    public void StartFlagTrail()
    {
        if ((TFFlagEffects)Vars.TrailEffect is TFFlagEffects.None or TFFlagEffects.PaperOnly)
            return;

        if (_trail != EntityHandle.None && Entities.IsValid(_trail))
            return;

        if (_prevOwner == EntityHandle.None || !players.IsTFPlayer(_prevOwner))
            return;

        EntityHandle trail = Entities.Create(EntityNames.EnvSpritetrail);
        if (!Entities.IsValid(trail))
            return;

        Entities.SetOrigin(trail, Origin);
        Entities.SetKeyValue(trail, "spritename", TFFlagTrail.Material(players.Team(_prevOwner), Vars.FlagTrail));
        Entities.SetKeyValue(trail, "lifetime", TFFlagTrail.LifeTime.ToString("0.0#"));
        Entities.SetKeyValue(trail, "startwidth", TFFlagTrail.StartWidth.ToString("0.#"));
        Entities.SetKeyValue(trail, "rendermode", ((int)RenderMode.TransAlpha).ToString());
        Entities.SetKeyValue(trail, "rendercolor", "255 255 255");
        Entities.SetKeyValue(trail, "renderamt", TFFlagTrail.Alpha.ToString());
        Entities.Spawn(trail);

        // The one factory value no keyfield names (SetTextureResolution,
        // entity_capture_flag.cpp:2706).
        if (FindByHandle<EnvSpriteTrail>(trail) is { } sprite)
            sprite.Trail.TextureRes = TFFlagTrail.TextureRes;

        // FollowEntity( this ) + SetAttachment( this, 0 ).
        Entities.SetParent(trail, Handle, 0);
        _trail = trail;
    }

    /// <summary>
    /// RemoveFlagTrail (entity_capture_flag.cpp:2718-2746): the fade, a
    /// brightness of <see cref="TFFlagTrail.Alpha"/> times the life left
    /// stepping down <see cref="FadeLifeStep"/> every <see
    /// cref="FadeStepTime"/> — unless the removal is instant (a reset or a
    /// capture set <c>m_bInstantTrailRemove</c>), when it dies at once.
    /// </summary>
    public void RemoveFlagTrail()
    {
        if (_trail == EntityHandle.None || !Entities.IsValid(_trail))
            return;

        if (_trailLife <= 0f || _instantTrailRemove)
        {
            Entities.Remove(_trail);
            _trail = EntityHandle.None;
            _trailLife = FadeLife;
        }
        else
        {
            if (FindByHandle<EnvSpriteTrail>(_trail) is { } sprite)
                sprite.Trail.Brightness = (int)(TFFlagTrail.Alpha * _trailLife);

            _trailLife -= FadeLifeStep;
            SetContextThink(FadeContext, FadeStepTime);
        }

        _instantTrailRemove = false;
    }

    /// <summary>
    /// CreateReturnIcon (entity_capture_flag.cpp:2408-2419): the icon
    /// <see cref="ItemTeamFlagReturnIcon.Height"/> above the flag, parented
    /// to it — once.
    /// </summary>
    public void CreateReturnIcon()
    {
        if (_returnIcon != EntityHandle.None && Entities.IsValid(_returnIcon))
            return;

        EntityHandle icon = Entities.Create(ItemTeamFlagReturnIcon.MapClass);
        if (!Entities.IsValid(icon))
            return;

        Entities.SetOrigin(icon, Origin + new Vector(0f, 0f, ItemTeamFlagReturnIcon.Height));
        Entities.Spawn(icon);
        Entities.SetParent(icon, Handle, 0);
        _returnIcon = icon;
    }

    /// <summary>DestroyReturnIcon (entity_capture_flag.cpp:2421-2428).</summary>
    public void DestroyReturnIcon()
    {
        if (_returnIcon == EntityHandle.None || !Entities.IsValid(_returnIcon))
            return;

        Entities.Remove(_returnIcon);
        _returnIcon = EntityHandle.None;
    }

    // ---- return times -------------------------------------------------------

    /// <summary>GetMaxReturnTime: the map's return time (robot/player destruction's are not ported).</summary>
    public int MaxReturnTime() => _returnTime;

    /// <summary>
    /// GetReturnTime → GetReturnTimeShotClockMode (L740): the full time,
    /// unless the map keyed <c>ShotClockMode</c> for a CTF, robot destruction
    /// or resource flag, when the time left at the pick-up is credited
    /// <c>tf_flag_return_time_credit_factor</c> seconds for each second
    /// carried, up to the full time.
    /// </summary>
    public int GetReturnTime(int maxReturnTime)
    {
        int returnTime = maxReturnTime;

        bool shotClockPossible = Type is TFFlagType.Ctf or TFFlagType.RobotDestruction or TFFlagType.ResourceControl;
        if (shotClockPossible && Vars.ShotClockMode)
        {
            // The powerup standoff's 0 is not ported (no powerup mode).
            float creditTime = (Now - _lastPickupTime) * settings.ReturnTimeCreditFactor + _lastResetDuration;
            int possibleCreditTime = RoundFloatToInt(creditTime);
            int actualCreditTime = Math.Max(0, possibleCreditTime);
            returnTime = Math.Min(maxReturnTime, actualCreditTime);
        }

        return returnTime;
    }

    /// <summary>RoundFloatToInt: cvtss2si, which rounds to nearest, ties to even.</summary>
    private static int RoundFloatToInt(float f) => (int)MathF.Round(f, MidpointRounding.ToEven);

    /// <summary>The fade think lands here (stock's context think on RemoveFlagTrail).</summary>
    public override void OnThink(string context)
    {
        if (context == FadeContext)
            RemoveFlagTrail();
    }

    // ---- touch --------------------------------------------------------------

    public override void OnTouch(EntityHandle toucher)
    {
        if (_touchEnabled)
            FlagTouch(toucher);
    }

    /// <summary>FlagTouch (L1027).</summary>
    public void FlagTouch(EntityHandle other)
    {
        // Is the flag disabled or stolen already?
        if (IsDisabled || IsStolen)
            return;

        // The touch from a live player.
        if (!players.IsPlayer(other) || !players.IsAlive(other))
            return;

        // Don't let the person who threw this flag pick it up until it hits the ground.
        if (_prevOwner != EntityHandle.None && _prevOwner == other && !_allowOwnerPickup)
            return;

        // A player on no game team takes nothing: stock's are never alive (they
        // watch until they join), the core's spawn before they join
        // (docs/divergences.md).
        TFTeam otherTeam = players.Team(other);
        if (otherTeam < TFTeam.Red)
            return;

        if (otherTeam == _team)
        {
            OnTouchSameTeam.Fire();

            // Does my team own this flag? If so, no touch.
            if (Type is TFFlagType.Ctf or TFFlagType.RobotDestruction)
            {
                if (!settings.ReturnOnTouch)
                    return;

                if (IsHome || IsStolen)
                    return;
            }
        }

        if (Type is TFFlagType.AttackDefend or TFFlagType.TerritoryControl && otherTeam != _team)
            return;

        if (Type is TFFlagType.Invade or TFFlagType.ResourceControl && _team != TFTeam.Unassigned && otherTeam != _team)
            return;

        // Can't pickup flags during WaitingForPlayers
        if (rules.InWaitingForPlayers)
            return;

        // Get the touching player.
        if (!players.IsTFPlayer(other))
            return;

        // CTFPlayer::IsAllowedToPickUpFlag's cannot_pick_up_intelligence
        // attribute (tf_player_shared.cpp:11260-11270); MvM's bypass is not
        // ported.
        if (!players.AllowedToPickUpFlag(other))
            return;

        TFCondMask conds = players.Conditions(other);

        // Is the touching player about to teleport?
        if (conds.Has(TFCond.SelectedToTeleport))
            return;

        // Don't let invulnerable players pickup flags, except in PD
        if (IsInvulnerable(conds) && Type != TFFlagType.PlayerDestruction)
            return;

        // Don't let stealthed spies pickup the flag: the cloak, the blink,
        // and the fade (entity_capture_flag.cpp:1115, GetPercentInvisible
        // through TFInvisibility).
        if (IsStealthed(conds) || conds.Has(TFCond.StealthedBlink)
            || players.PercentInvisible(other) > InvisiblePickupLimit)
            return;

        // Don't let phased scouts pickup flags
        if (conds.Has(TFCond.Phase))
            return;

        // Don't let players carry multiple flags for user-made maps with >1
        if (rules.CarriedBy(other) is not null && Type is not (TFFlagType.RobotDestruction or TFFlagType.PlayerDestruction)
            && !settings.ReturnOnTouch)
        {
            return;
        }

        // PointInRespawnRoom( pPlayer, pPlayer->WorldSpaceCenter() ): bTeamOnly defaults false.
        if (respawnRooms?.InRespawnRoom(other, sameTeamOnly: false) == true)
            return;

        if (IsDropped && otherTeam == _team && Type == TFFlagType.Ctf && settings.ReturnOnTouch)
        {
            Reset();
            ResetMessage();
        }
        else
        {
            // Pick up the flag.
            PickUp(other, invisible: true);
        }
    }

    /// <summary><c>CTFPlayerShared::IsInvulnerable</c>.</summary>
    private static bool IsInvulnerable(in TFCondMask c)
        => c.Has(TFCond.Invulnerable) || c.Has(TFCond.InvulnerableUserBuff)
        || c.Has(TFCond.InvulnerableHideUnlessDamaged) || c.Has(TFCond.InvulnerableCardEffect);

    /// <summary><c>CTFPlayerShared::IsStealthed</c>.</summary>
    private static bool IsStealthed(in TFCondMask c)
        => c.Has(TFCond.Stealthed) || c.Has(TFCond.StealthedUserBuff) || c.Has(TFCond.StealthedUserBuffFading);

    // ---- pick up ------------------------------------------------------------

    /// <summary>PickUp (L1149).</summary>
    public void PickUp(EntityHandle player, bool invisible)
    {
        // Is the flag enabled?
        if (IsDisabled)
            return;

        if (!rules.FlagsMayBeCapped)
            return;

        if (!_allowOwnerPickup && _prevOwner != EntityHandle.None && _prevOwner == player)
            return;

        // CTFItem::PickUp: owned by and parented to the player, at their origin.
        Entities.SetOwner(Handle, player);
        SetParent(player);
        LocalOrigin = default;
        LocalAngles = default;

        // Update the parent to set the correct place on the model to attach the flag.
        int attachment = players.FlagAttachment(player);
        if (attachment > 0)
        {
            SetParent(player, attachment);
            LocalOrigin = default;
            LocalAngles = default;
        }

        // Remove the player's disguise if they're a spy, but not in PD
        if (players.Class(player) == TFClass.Spy && Type != TFFlagType.PlayerDestruction)
        {
            TFCondMask conds = players.Conditions(player);
            if (conds.Has(TFCond.Disguised) || conds.Has(TFCond.Disguising))
                effects?.RemoveDisguise(player);
        }

        // switch to brighter picked up skin
        SetSkin(_skin + NumberOfSkins);

        // Remove the touch function.
        _touchEnabled = false;

        _prevOwner = player;
        _allowOwnerPickup = true;

        if (_initialPlayer == EntityHandle.None)
            _initialPlayer = player;

        // The per-team lines (entity_capture_flag.cpp:1259-1281): the team that
        // lost the flag hears the "enemy stolen" line, the taker's team hears
        // the "team stolen" one without the picker. The attack/defend arm is
        // its sounds alone (:1282-1305).
        if (Type == TFFlagType.Ctf)
            announcer.Stolen(Handle, players.Team(player), player);
        else if (Type == TFFlagType.AttackDefend)
            announcer.AttackDefendStolen(Handle, players.Team(player), player);

        if (Type is TFFlagType.Invade or TFFlagType.ResourceControl)
        {
            // set the flag's team to match the player's team
            _team = players.Team(player);
            State.Team = (byte)_team;
            SetSkin(TeamSkin() + NumberOfSkins);
        }

        // If the flag was at home, set the initial reset time to the max allowable time, otherwise it's to whatever it was
        // right now so we can persist that until later.
        _lastResetDuration = _status == TFFlagStatus.Home ? MaxReturnTime() : _resetTime - Now;

        // Remember that this is when the item was picked up.
        _lastPickupTime = Now;

        TFFlagStatus oldStatus = _status;
        SetFlagStatus(TFFlagStatus.Stolen, player);
        ResetFlagReturnTime();
        ResetFlagNeutralTime();

        events.FlagEvent(TFFlagEvent.Pickup, players.Index(player), _team, priority: 8, home: oldStatus == TFFlagStatus.Home);

        // Output.
        OnPickUp.Fire();
        OnPickup1.Fire(player);

        switch (players.Team(player))
        {
            case TFTeam.Red: OnPickupTeam1.Fire(); break;
            case TFTeam.Blue: OnPickupTeam2.Fire(); break;
        }

        // The icon goes down and the colour trail goes up with the carry
        // (entity_capture_flag.cpp:1447-1449).
        DestroyReturnIcon();
        StartFlagTrail();

        // The detection zones hear the pick-up (entity_capture_flag.cpp:1451).
        zones?.PickedUp(player);

        rules.Report("flag_picked_up", _team, Entities.GetIndex(Handle));
        Log.Msg($"[TF2/Ctf] {Name} ({_team}) picked up by player {players.Index(player)} ({players.Team(player)}), was {oldStatus}\n");
    }

    // ---- capture ------------------------------------------------------------

    /// <summary>Capture (L1493): scored by the carrier at <paramref name="capturePoint"/>, then home.</summary>
    public void Capture(EntityHandle player, int capturePoint)
    {
        // Is the flag enabled?
        if (IsDisabled)
            return;

        TFTeam playerTeam = players.Team(player);

        if (Type == TFFlagType.Ctf)
        {
            bool notify = true;

            // don't play any sounds if this is going to win the round for one of the teams (victory sound will be played instead)
            if (settings.CapsPerRound > 0)
            {
                int caps = rules.FlagCaptures(playerTeam);
                if (caps >= 0 && settings.CapsPerRound - caps <= 1)
                {
                    // this cap is going to win, so don't play a sound
                    notify = false;
                }
            }

            // The per-team lines (entity_capture_flag.cpp:1517-1549); the bonus is the effects seam's.
            if (notify)
            {
                announcer.Captured(Handle, playerTeam, player);
                effects?.CaptureBonus(playerTeam);
            }

            // Reward the team
            rules.RewardTeam(playerTeam, asCapture: settings.CapsPerRound > 0, CapturedTeamScore);
        }
        else if (Type == TFFlagType.Invade)
        {
            // Reward the team
            bool asCapture = Vars.ScoringType == InvadeScoringCaptureCount && settings.CapsPerRound > 0;
            rules.RewardTeam(playerTeam, asCapture, CapturedTeamScore);
        }
        else if (Type == TFFlagType.ResourceControl)
        {
            // Reward the team
            rules.RewardTeam(playerTeam, asCapture: settings.CapsPerRound > 0, CapturedTeamScore);
        }
        else if (Type == TFFlagType.AttackDefend)
        {
            // The per-team lines and the broadcast sting; no caps-per-round
            // mute and no bonus (entity_capture_flag.cpp:1574-1609).
            announcer.AttackDefendCaptured(Handle, playerTeam, player);
        }
        panelCappers.NoteCapper(players.Index(player));

        events.FlagEvent(TFFlagEvent.Capture, players.Index(player), _team, priority: 9);

        SetFlagStatus(TFFlagStatus.Home);
        ResetFlagReturnTime();
        ResetFlagNeutralTime();

        // The trail dies at once on a capture (entity_capture_flag.cpp:1734-1735).
        _instantTrailRemove = true;
        RemoveFlagTrail();

        SetSkin(TeamSkin());

        // The zones hear the capture's drop half; the capture arm is the
        // doomsday achievement, deferred (entity_capture_flag.cpp:1738-1739).
        zones?.Dropped(player);

        // Reset the flag (CTFItem::Drop, then Reset).
        Unparent();
        Reset();

        // Outputs
        OnCapture.Fire();
        OnCapture1.Fire(player);

        switch (playerTeam)
        {
            case TFTeam.Red: OnCapTeam1.Fire(); break;
            case TFTeam.Blue: OnCapTeam2.Fire(); break;
        }

        _captured = true;
        _thinking = true;
        SetNextThink(ThinkTime);

        rules.Report("flag_captured", playerTeam, Entities.GetIndex(Handle));
        Log.Msg($"[TF2/Ctf] {Name} ({_team}) captured by player {players.Index(player)} ({playerTeam}) at point {capturePoint}: "
                + $"{playerTeam} flag captures {rules.FlagCaptures(playerTeam)}\n");

        if (rules.InStalemate)
        {
            // whoever capped the flag is the winner, give them enough caps to win
            if (rules.FlagCaptures(playerTeam) < settings.CapsPerRound)
                rules.SetFlagCaptures(playerTeam, settings.CapsPerRound);
        }
    }

    // ---- drop ---------------------------------------------------------------

    /// <summary>
    /// Drop (L1792): off the player, onto the ground below their centre,
    /// with the return timer running. <paramref name="thrown"/> keeps the
    /// thrower from taking it back for <see cref="OwnerPickupTime"/>.
    /// </summary>
    public void Drop(EntityHandle player, bool visible, bool thrown = false, bool message = true)
    {
        // Is the flag enabled?
        if (IsDisabled)
            return;

        // CTFItem::Drop: visible, unparented, unowned.
        if (visible)
            SetVisible(true);

        // Drop from the player's center so we can guarantee that it is in a valid
        // spot (a player already gone leaves it where it is).
        bool present = players.IsPlayer(player);
        Vector center = present ? players.WorldSpaceCenter(player) : Origin;

        Unparent();

        if (thrown)
        {
            _allowOwnerPickup = false;
            _ownerPickupTime = Now + OwnerPickupTime;
        }

        Origin = DropPoint(center, present ? player : Handle);

        // The drop lines for a CTF (entity_capture_flag.cpp:1947-1968); the
        // attack/defend arm is its sounds alone under bMessage (:2002-2028);
        // invade's neutral timer is.
        if (Type == TFFlagType.Ctf && message)
            announcer.Dropped(Handle, players.Team(player), player);
        else if (Type == TFFlagType.AttackDefend && message)
            announcer.AttackDefendDropped(Handle, players.Team(player), player);

        if (Type == TFFlagType.Invade)
        {
            if (Vars.NeutralType == InvadeNeutralHalf)
            {
                SetFlagNeutralIn(MaxReturnTime() / 2.0f);
            }
            else if (Vars.NeutralType == InvadeNeutralDefault)
            {
                // if our return time is less than the neutral time, we don't need a neutral time
                if (InvadeNeutralTime < MaxReturnTime())
                    SetFlagNeutralIn(InvadeNeutralTime);
            }
        }

        SetSkin(_skin - NumberOfSkins);

        // The colour trail fades out (entity_capture_flag.cpp:2089).
        RemoveFlagTrail();

        int maxReturnTime = MaxReturnTime();
        SetFlagReturnIn(GetReturnTime(maxReturnTime), maxReturnTime);

        // Reset the flag's angles.
        SetAngles(_resetAng);

        // Reset the touch function.
        _touchEnabled = true;

        SetFlagStatus(TFFlagStatus.Dropped);

        // Output.
        OnDrop.Fire();
        OnDrop1.Fire(player);

        // The return icon hangs over the dropped flag (entity_capture_flag.cpp:2106-2109;
        // MvM's max-return gate is not ported).
        CreateReturnIcon();

        rules.Report("flag_dropped", _team, Entities.GetIndex(Handle));
        Log.Msg($"[TF2/Ctf] {Name} ({_team}) dropped at {Origin}, returns in {_resetTime - Now:0.##} s\n");

        // did we get dropped in a func_respawnflag zone?
        if (rules.PointInRespawnFlagZone(Origin))
        {
            Reset();
            ResetMessage();
        }

        // The zones hear the drop, after that reset, as stock's order runs
        // (entity_capture_flag.cpp:2112-2118).
        zones?.Dropped(player);
    }

    /// <summary>
    /// The trace of Drop: the flag's hull straight down 8000 units from
    /// <paramref name="center"/>; one that starts in solid wiggles 10 units
    /// along each axis in turn, and failing every wiggle it stays at the centre.
    /// The elevator arm (parent to a MOVETYPE_PUSH ground) is not ported:
    /// the hull trace answers no entity. The trace passes through the
    /// carrier, where stock's passes through the flag and leaves the carrier
    /// to <c>COLLISION_GROUP_DEBRIS</c>, which the core's trace filter does
    /// not keep off players (docs/divergences.md).
    /// </summary>
    private Vector DropPoint(Vector center, EntityHandle ignore)
    {
        var trace = Trace(center, ignore);

        if (trace.StartSolid)
        {
            Log.Warning("Dropped flag trace started solid!\nWiggle around each axis to find a safer fit!\n");

            ReadOnlySpan<float> multipliers = [0.0f, 1.0f, -1.0f];

            // Wiggle it around on each axis to find a safe place
            for (int z = 0; z < multipliers.Length && trace.StartSolid; z++)
            {
                for (int y = 0; y < multipliers.Length && trace.StartSolid; y++)
                {
                    for (int x = 0; x < multipliers.Length && trace.StartSolid; x++)
                    {
                        var start = center + new Vector(multipliers[x] * 10.0f, multipliers[y] * 10.0f, multipliers[z] * 10.0f);
                        trace = Trace(start, ignore);
                    }
                }
            }
        }

        if (trace.StartSolid)
        {
            // Couldn't find a good spot... just leave it in the center of where the player died
            Log.Warning("Couldn't find a safe place to drop the flag!\nDropping at the player's center!\n");
            return center;
        }

        // Found a good spot for it
        return trace.EndPos;
    }

    private EntityHullTrace Trace(Vector start, EntityHandle ignore)
        => world.EntityTraceHull(ignore, start, start - new Vector(0f, 0f, 8000.0f), Mins, Maxs,
                                 EnginePushWorld.MaskSolid, (int)EngineCollisionGroup.Debris);

    /// <summary>CTFItem::Drop's SetParent( NULL ) and SetOwnerEntity( NULL ).</summary>
    private void Unparent()
    {
        ClearParent();
        Entities.SetOwner(Handle, EntityHandle.None);
    }

    // ---- the carrier's drops ------------------------------------------------

    /// <summary>
    /// <c>CTFPlayer::DropFlag</c> (tf_player.cpp:8550) for this flag's
    /// carrier: a thrown drop, then the dropped event (the <c>dropitem</c>
    /// command, a respawn-flag zone, <c>ForceDrop</c>).
    /// </summary>
    public void DropFlag(EntityHandle player, bool silent = false)
    {
        TFTeam flagTeam = _team;
        Drop(player, visible: true, thrown: true, message: !silent);
        events.FlagEvent(TFFlagEvent.Dropped, players.Index(player), flagTeam, priority: 8);
    }

    /// <summary>
    /// <c>CTFPlayer::RemoveAllItems</c>' flag arm (tf_player.cpp:13236-13250):
    /// a drop that is not a throw, then the dropped event (a death, a
    /// disconnect).
    /// </summary>
    public void RemoveFromCarrier(EntityHandle player)
    {
        TFTeam flagTeam = _team;
        Drop(player, visible: true);
        events.FlagEvent(TFFlagEvent.Dropped, players.Index(player), flagTeam, priority: 8);
    }

    // ---- reset --------------------------------------------------------------

    /// <summary>Reset (L831): home, idle, nobody's.</summary>
    public void Reset()
    {
        // The trail dies at once on a reset (entity_capture_flag.cpp:834-835).
        _instantTrailRemove = true;
        RemoveFlagTrail();

        // Set the flag position.
        Unparent();
        Origin = _resetPos;
        SetAngles(_resetAng);

        // No longer dropped, if it was.
        SetFlagStatus(TFFlagStatus.Home);
        ResetFlagReturnTime();
        ResetFlagNeutralTime();

        _initialPlayer = EntityHandle.None;
        _allowOwnerPickup = true;
        _prevOwner = EntityHandle.None;

        if (Type is TFFlagType.Invade or TFFlagType.ResourceControl)
        {
            _team = _originalTeam;
            State.Team = (byte)_team;
            SetSkin(TeamSkin());
        }
    }

    /// <summary>ResetMessage (L909), the CTF arm: the returned event, the
    /// per-team lines, and the flag's spawn sound.</summary>
    public void ResetMessage()
    {
        if (Type == TFFlagType.Ctf)
        {
            events.FlagEvent(TFFlagEvent.Returned, -1, _team, priority: 8);

            // The per-team lines (entity_capture_flag.cpp:912-930).
            announcer.Returned(Handle, _team);

            // Returned sound
            sound.EntityEmitSound(Handle, FlagSpawnSound);
        }
        else if (Type == TFFlagType.AttackDefend)
        {
            // The attack/defend return has neither the event nor the spawn
            // sound, only the per-team lines (entity_capture_flag.cpp:945-967).
            announcer.AttackDefendReturned(Handle, _team);
        }

        // Output.
        OnReturn.Fire();

        // The icon comes down with the return (entity_capture_flag.cpp:1020).
        DestroyReturnIcon();

        rules.Report("flag_returned", _team, Entities.GetIndex(Handle));
        Log.Msg($"[TF2/Ctf] {Name} ({_team}) returned home\n");
    }

    /// <summary>ResetFlag: Reset then ResetMessage.</summary>
    public void ResetFlag()
    {
        Reset();
        ResetMessage();
    }

    // ---- think --------------------------------------------------------------

    public override void OnThink()
    {
        if (_thinking)
            Think();
    }

    /// <summary>Think (L2274): the return timer, the thrower's lockout, every quarter second.</summary>
    public void Think()
    {
        // Is the flag enabled?
        if (IsDisabled)
        {
            _thinking = false;
            return;
        }

        if (!rules.FlagsMayBeCapped)
        {
            SetNextThink(ThinkTime);
            return;
        }

        if (_captured)
        {
            _captured = false;
            _touchEnabled = true;
        }

        // The icon's backstop (entity_capture_flag.cpp:2350-2353): the return
        // timer's end takes it with it.
        if (_resetTime != 0f && Now > _resetTime)
            DestroyReturnIcon();

        // The colour trail rides the carrier's speed (entity_capture_flag.cpp:2355-2378):
        // running at a tenth of max speed or better keeps it, a stopped
        // carrier loses it, and the quarter-second think re-decides either way.
        if (_prevOwner != EntityHandle.None && players.IsTFPlayer(_prevOwner))
        {
            float maxSpeed = players.MaxSpeed(_prevOwner);
            bool running = players.Velocity(_prevOwner).LengthSquared >= maxSpeed * maxSpeed * 0.1f;
            if (!running && _trail != EntityHandle.None && Entities.IsValid(_trail))
                RemoveFlagTrail();
            else if (running && (_trail == EntityHandle.None || !Entities.IsValid(_trail)))
                StartFlagTrail();
        }

        if (IsDropped)
        {
            if (!_allowOwnerPickup)
            {
                if (_ownerPickupTime != 0f && Now > _ownerPickupTime)
                    _allowOwnerPickup = true;
            }

            if (Type == TFFlagType.Invade)
            {
                if (_resetTime != 0f && Now > _resetTime)
                {
                    Reset();
                    ResetMessage();
                }
                else if (_neutralTime != 0f && Now > _neutralTime)
                {
                    // reset the team to the original team setting (when it spawned)
                    _team = _originalTeam;
                    State.Team = (byte)_team;
                    SetSkin(TeamSkin());
                    ResetFlagNeutralTime();
                }
            }
            else
            {
                if (_resetTime != 0f && Now > _resetTime)
                {
                    Reset();
                    ResetMessage();
                }
            }
        }

        SetNextThink(ThinkTime);
    }

    // ---- inputs -------------------------------------------------------------

    /// <summary>InputRoundActivate: a carried flag drops silently, then home.</summary>
    [ScriptedEvent]
    private void OnRoundActivate()
    {
        // If the player has a capture flag, drop it.
        if (IsStolen && _prevOwner != EntityHandle.None)
            Drop(_prevOwner, visible: true, thrown: false, message: false);

        Reset();
    }

    [ScriptedEvent]
    private void OnForceDrop()
    {
        // If the player has a capture flag, drop it.
        if (IsStolen && _prevOwner != EntityHandle.None)
            DropFlag(_prevOwner);
    }

    /// <summary>InternalForceReset.</summary>
    public void ForceReset(bool silent = false)
    {
        if (IsHome)
            return;

        // If the player has a capture flag, drop it.
        if (IsStolen && _prevOwner != EntityHandle.None)
            DropFlag(_prevOwner, silent);

        if (!silent)
            ResetFlag();
        else
            Reset();
    }

    [ScriptedEvent] private void OnForceReset() => ForceReset();

    [ScriptedEvent] private void OnForceResetSilent() => ForceReset(silent: true);

    [ScriptedEvent]
    private void OnForceResetAndDisableSilent()
    {
        ForceReset(silent: true);
        SetDisabled(true);
    }

    /// <summary>InputSetReturnTime: a new return time, and a dropped flag's timer restarted on it.</summary>
    [ScriptedEvent]
    private void OnSetReturnTime(float time)
    {
        int returnTime = (int)time;
        _returnTime = returnTime >= 0 ? returnTime : 0;

        if (IsDropped)
        {
            // do we currently have a neutral time?
            if (_neutralTime > 0f)
            {
                if (Vars.NeutralType == InvadeNeutralHalf)
                {
                    SetFlagNeutralIn(_returnTime / 2.0f);
                }
                else if (Vars.NeutralType == InvadeNeutralDefault)
                {
                    // if our return time is less than the neutral time, we don't need a neutral time
                    if (InvadeNeutralTime < _returnTime)
                        SetFlagNeutralIn(InvadeNeutralTime);
                }
            }

            SetFlagReturnIn(_returnTime, _returnTime);
        }
    }

    /// <summary>InputShowTimer: the return timer restarted at the given time, with the icon over it (entity_capture_flag.cpp:2555-2563).</summary>
    [ScriptedEvent]
    private void OnShowTimer(float time)
    {
        int returnTime = (int)time;
        _returnTime = returnTime >= 0 ? returnTime : 0;
        SetFlagReturnIn(_returnTime, _returnTime);
        CreateReturnIcon();
    }

    /// <summary>InputForceGlowDisabled: off takes the glow off the wire with it.</summary>
    [ScriptedEvent]
    private void OnForceGlowDisabled(float state)
    {
        _glowEnabled = (int)state == 0;
        State.GlowEnabled = (byte)(_glowEnabled ? 1 : 0);
    }
}

/// <summary>The map keys <see cref="ItemTeamFlag"/> declares (entity_capture_flag.cpp:171-189, CBaseEntity's TeamNum, the prop's StartDisabled).</summary>
[ScriptVars]
public partial class ItemTeamFlagVars
{
    [VarName("GameType")] public int GameType { get; set; }
    [VarName("ReturnTime")] public int ReturnTime { get; set; } = 60;
    [VarName("trail_effect")] public int TrailEffect { get; set; } = 1;
    [VarName("NeutralType")] public int NeutralType { get; set; } = ItemTeamFlag.InvadeNeutralDefault;
    [VarName("ScoringType")] public int ScoringType { get; set; }
    [VarName("ReturnBetweenWaves")] public bool ReturnBetweenWaves { get; set; } = true;
    [VarName("VisibleWhenDisabled")] public bool VisibleWhenDisabled { get; set; }
    [VarName("ShotClockMode")] public bool ShotClockMode { get; set; }
    [VarName("PointValue")] public int PointValue { get; set; }
    [VarName("flag_model")] public string FlagModel { get; set; } = string.Empty;
    [VarName("flag_icon")] public string FlagIcon { get; set; } = string.Empty;
    [VarName("flag_paper")] public string FlagPaper { get; set; } = string.Empty;
    [VarName("flag_trail")] public string FlagTrail { get; set; } = string.Empty;
    [VarName("tags")] public string Tags { get; set; } = string.Empty;
    [VarName("StartDisabled")] public bool StartDisabled { get; set; }
    [VarName("TeamNum")] public int TeamNum { get; set; }
}

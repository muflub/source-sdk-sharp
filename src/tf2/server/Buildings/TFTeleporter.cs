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
using TF2.Server.Econ;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Server.Voice;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Shared.Movement;

namespace TF2.Server.Buildings;

/// <summary><c>CTFTeleporter::EState</c>: the pair's shared life, one end at a time.</summary>
public enum TFTeleporterState
{
    Building = 0,
    Idle,
    Ready,
    Sending,
    Receiving,
    ReceivingRelease,
    Recharging,
}

/// <summary>
/// <c>obj_teleporter</c> at level 1 (tf_obj_teleporter.cpp): one end of the
/// pair that carries a standing friendly from the entrance to the exit.
///
/// <para>
/// <b>The trip.</b> Each end, once built, waits for its partner (same
/// builder, the other mode — the pair, not the team, because stock counts
/// objects per <c>(type, mode)</c> and so admits one of each). Ready and
/// matched, the entrance books whoever steps onto it; when that player stops
/// moving the entrance sends: the exit fades in, the arrival fades the player
/// out of the entrance and back in on the exit — the arrival lands on the
/// exit's deck with no velocity — and both ends recharge for the stock level
/// 1 ten seconds before the pair is ready again.
/// </para>
///
/// <para>
/// <b>The show.</b> The send marks itself at the pad — the team's
/// <c>teleported</c> portal on the spot and the sparkles on the traveller —
/// and the exit answers with the <c>teleportedin</c> portal alone; each end
/// sounds its half (<c>TeleporterSend</c> :98-124, <c>TeleporterReceive</c>
/// :171-200), the arrival flashes the traveller's screen white
/// (<c>RecieveTeleportingPlayer</c> :1024-1025) after taking a zoomed
/// sniper's scopes away and easing the view back from the scoped width
/// (<c>InstantlySniperUnzoom</c> :1020, <c>SetFOV</c> :1022), and the release
/// lights the twelve-second <c>TF_COND_TELEPORTED</c> glow
/// (<c>TeleportEffect</c>, :1139). The colour is the rider's team: stock's
/// disguised-spy arm
/// (:102-108, :174-181) re-colours by the builder only for a rider the
/// booking here does not yet take (see <see cref="CanBeTeleported"/>), where
/// it is the identity.
/// </para>
///
/// <para>
/// <b>What this milestone is not.</b> The cloak and spy arms and the
/// per-state sounds are deferred rows; the exit is reached at deck height
/// because the models are deferred with it. The arrival's box sweeps for
/// enemies but not for solid blockers — the abort arm needs solidity and
/// collision-group reads our seams do not have. The direction arrow is
/// narrowed: stock shows it with <c>SetBodygroup</c> (:1215-1241) and this
/// tree has no bodygroup seam, and the yaw it sets is the
/// <c>m_flYawToExit</c> SendPropFloat (:54), a client-visible word the
/// buildings do not yet carry (the 2.14 row of deferred.tsv).
/// </para>
/// </summary>
[ScriptedEntity(TFTeleporter.MapClass, ScriptedEntityKind.Brush)]
[Precache(PrecacheKind.ParticleSystem, "teleported_red")]
[Precache(PrecacheKind.ParticleSystem, "player_sparkles_red")]
[Precache(PrecacheKind.ParticleSystem, "teleported_blue")]
[Precache(PrecacheKind.ParticleSystem, "player_sparkles_blue")]
[Precache(PrecacheKind.ParticleSystem, "teleportedin_red")]
[Precache(PrecacheKind.ParticleSystem, "teleportedin_blue")]
[Precache(PrecacheKind.Sound, "Building_Teleporter.Send")]
[Precache(PrecacheKind.Sound, "Building_Teleporter.Receive")]
public sealed class TFTeleporter(ITFMapPlayers players, ITFObjectAnnouncer announcer, IEnginePrecache precache,
                                 IEngineEntityRender render, ITFBuildings buildings, ITFAmmoPurse purse,
                                 IEngineWorldQuery world, IEngineDamage damage, TFDamageScopes scopes,
                                 DamageHitGroups hitGroups, ITFWeaponOwners owners, AttributeHooks hooks,
                                 ITFPlayerConditions conditions, IUniformRandom random, ITFPlayerClasses classes,
                                 IEngineEntities entities, IEntityDirectory directory, ILog log,
                                 ITempEnts temps, IEngineSound sound, IScreenEffects screen,
                                 IEnginePlayers enginePlayers, TFTeleporterFovStartConVar fovStart,
                                 TFTeleporterFovTimeConVar fovTime,
                                 TFPlayerCarriers? carriers = null, TFConceptSpeech? speech = null)
    : TFBaseObject(players, announcer, precache, render, purse, carriers, speech)
{
    public const string MapClass = "obj_teleporter";

    private readonly IUniformRandom _random = random;
    private readonly ITFPlayerClasses _classes = classes;

    /// <summary>
    /// Throwing the bread. Built with <c>new</c> rather than injected, the way
    /// <c>EnvShooter</c> builds its <see cref="Gibs"/>: a helper over the four
    /// engine areas and the said-this-once set, so a per-teleporter instance is
    /// the right lifetime and nothing but the arrival ever asks it for a loaf.
    /// The log is the injected one, as the other buildings' parts take it.
    /// </summary>
    private readonly Gibs _gibs = new(log, entities, render, precache, directory);

    /// <summary><c>TELEPORTER_NEXT_THINK</c>.</summary>
    public const float TeleporterThinkDelay = 0.05f;

    /// <summary>
    /// The fades and <c>TELEPORTER_SEND_TIME</c>: the send's own beat before
    /// the exit is told (tf_obj_teleporter.cpp:126-127).
    /// </summary>
    public const float FadeTime = 0.25f, SendDelay = 0.1f;

    /// <summary>
    /// <c>g_iTeleporterRechargeTimes</c> (tf_obj_teleporter.cpp:41-47): the
    /// pair recharges for 10, 5 and 3 seconds at levels 1, 2 and 3 —
    /// <c>m_flCurrentRechargeDuration</c> is the level's
    /// (<c>GetUpgradeLevel()</c> indexed, :1109, :1168).
    /// </summary>
    public static float RechargeTimeForLevel(int level) => level switch
    {
        2 => 5f,
        >= 3 => 3f,
        _ => 10f,
    };

    /// <summary>A reserved player has stopped moving below this squared speed (<c>&lt; 25</c>).</summary>
    public const float StandingSpeedSquared = 25f;

    /// <summary>The arrival deck: the exit's top (maxs.z) plus a step.</summary>
    public const float ArrivalHeight = 13f;

    /// <summary>
    /// <c>Vector expand(4, 4, 4)</c> (:930): the slack the telefrag box gets
    /// around the arrival's hull.
    /// </summary>
    public const float TelefragSlack = 4f;

    /// <summary><c>FIRST_GAME_TEAM</c> (shareddefs.h:286, <c>LAST_SHARED_TEAM+1</c>): teams below it are unassigned and spectator.</summary>
    private const int FirstGameTeam = (int)TFTeam.Red;

    /// <summary>The send's word, spoken by the entrance (<c>TeleporterSend</c>, tf_obj_teleporter.cpp:124).</summary>
    public const string SendSound = "Building_Teleporter.Send";

    /// <summary>The arrival's word, spoken by the exit (<c>TeleporterReceive</c>, tf_obj_teleporter.cpp:200).</summary>
    public const string ReceiveSound = "Building_Teleporter.Receive";

    /// <summary><c>PARTICLE_DISPATCH_FROM_ENTITY</c> (particle_parse.h:37): the record names an entity.</summary>
    private const int FromEntity = 1 << 0;

    /// <summary><c>PATTACH_ABSORIGIN</c>, what the sparkles ride (tf_obj_teleporter.cpp:114).</summary>
    private const int PattachAbsorigin = 0;

    /// <summary>
    /// <c>TeleportEffect</c>'s glow (tf_player.cpp:15558): the released
    /// traveller burns <c>TF_COND_TELEPORTED</c> this long. (MvM bots'
    /// 30 seconds is that mode's, and there are no bots here.)
    /// </summary>
    public const float TeleportedGlowSeconds = 12f;

    /// <summary>
    /// <c>EndTouch</c>'s window (tf_obj_teleporter.cpp:567, <c>curtime + 2.0</c>):
    /// a stepped-off rider keeps the booking this long — players can push the
    /// rider off the pad.
    /// </summary>
    public const float BookingGrace = 2f;

    /// <summary>
    /// <c>g_pszBreadModels</c> (shared/tf/tf_shareddefs.cpp:184-195): the nine
    /// lunch models. The stock draw is the normal-class range
    /// (<c>RandomInt(0, TF_LAST_NORMAL_CLASS - TF_FIRST_NORMAL_CLASS - 1)</c>,
    /// :1465), which is 0 to 8 and reaches every entry. An instance field:
    /// SS0010 keeps the mods' tables off the statics, and a teleporter is
    /// where the table is read.
    /// </summary>
    private readonly string[] _breadModels =
    [
        "models/weapons/c_models/c_bread/c_bread_baguette.mdl",
        "models/weapons/c_models/c_bread/c_bread_burnt.mdl",
        "models/weapons/c_models/c_bread/c_bread_cinnamon.mdl",
        "models/weapons/c_models/c_bread/c_bread_cornbread.mdl",
        "models/weapons/c_models/c_bread/c_bread_crumpet.mdl",
        "models/weapons/c_models/c_bread/c_bread_plainloaf.mdl",
        "models/weapons/c_models/c_bread/c_bread_pretzel.mdl",
        "models/weapons/c_models/c_bread/c_bread_ration.mdl",
        "models/weapons/c_models/c_bread/c_bread_russianblack.mdl",
    ];

    private const string TeleporterThink = "TeleporterThink";

    private readonly ITFBuildings _buildings = buildings;
    private readonly IEngineWorldQuery _world = world;
    private readonly IEngineDamage _damage = damage;
    private readonly TFDamageScopes _scopes = scopes;
    private readonly DamageHitGroups _hitGroups = hitGroups;
    private readonly ITFWeaponOwners _owners = owners;
    private readonly AttributeHooks _hooks = hooks;
    private readonly ITFPlayerConditions _conditions = conditions;
    private readonly ITempEnts _temps = temps;
    private readonly IEngineSound _sound = sound;
    private readonly IScreenEffects _screen = screen;
    private readonly IEnginePlayers _enginePlayers = enginePlayers;
    private readonly TFTeleporterFovStartConVar _fovStart = fovStart;
    private readonly TFTeleporterFovTimeConVar _fovTime = fovTime;

    [SaveField] private TFTeleporterState _state = TFTeleporterState.Building;
    [SaveField] private float _stateEndsAt;
    [SaveField] private EntityHandle _teleportee;
    [SaveField] private EntityHandle _reserved;
    [SaveField] private float _reserveUntil;
    [SaveField] private EntityHandle _matchHandle;
    [SaveField] private int _timesUsed;

    // Stock sends from the touch callback itself (:639-648); this port's send
    // is think-driven, so the pad carries whether the booked rider is on it —
    // a walked-off rider waits out the grace on the pad's edge, unsent.
    private bool _onPad;

    public override TFObjectType Type => TFObjectType.Teleporter;

    /// <summary><c>m_eTeleporterState</c>.</summary>
    public TFTeleporterState State => _state;

    /// <summary>When the current state ends (<c>m_flCurrentRechargeDuration</c>'s
    /// horizon while recharging): the status panel's recharge fraction reads it
    /// against <see cref="RechargeTimeForLevel"/>.</summary>
    public float StateEndsAt => _stateEndsAt;

    /// <summary><c>m_hTeleportee</c>: the player mid-trip, either end's view of it.</summary>
    public EntityHandle Teleportee => _teleportee;

    /// <summary>The player the entrance has booked to send.</summary>
    public EntityHandle Reserved => _reserved;

    /// <summary>
    /// <c>m_hTeleporter</c>: the partner — held once found, and re-found when
    /// what was held has gone. Stock's <c>FindMatch</c> gates on nothing
    /// built (tf_obj_teleporter.cpp:1282-1326): a still-building end holds
    /// its half, the state asked at use
    /// (<see cref="MatchReady"/>, IsMatchingTeleporterReady :735-749).
    /// </summary>
    public TFTeleporter? Match => ResolveMatch();

    private TFTeleporter? ResolveMatch()
    {
        if (_matchHandle != EntityHandle.None)
        {
            if (FindByHandle<TFTeleporter>(_matchHandle) is { } held)
                return held;
        }

        int otherMode = Mode == 0 ? 1 : 0;
        var candidates = _buildings.OwnedBy(Builder, TFObjectType.Teleporter, otherMode)
                                    .OfType<TFTeleporter>()
                                    .Where(other => other != this)
                                    .ToArray();

        // Stock's order: a partner that has already claimed this end wins;
        // else the first unclaimed one. The raw handles, not the Match
        // property — the pair reading each other's Match would recurse.
        foreach (TFTeleporter other in candidates)
            if (other._matchHandle == Handle)
                return Keep(other);

        foreach (TFTeleporter other in candidates)
            if (other._matchHandle == EntityHandle.None)
                return Keep(other);

        _matchHandle = EntityHandle.None;
        return null;
    }

    private TFTeleporter Keep(TFTeleporter other)
    {
        _matchHandle = other.Handle;
        return other;
    }

    /// <summary>
    /// <c>CTFTeleporter::OnGoActive</c>: the end stands straight up in IDLE
    /// (tf_obj_teleporter.cpp:431) — the building state belongs to the
    /// construction, not to the activation — and the think is booked.
    /// </summary>
    protected override void OnGoActive()
    {
        _state = TFTeleporterState.Idle;
        SetContextThink(TeleporterThink, TeleporterThinkDelay);
    }

    protected override void OnDestroyed()
    {
        StopThinking(TeleporterThink);
        _teleportee = EntityHandle.None;
        _reserved = EntityHandle.None;
    }

    protected override void OnCarried() => StopThinking(TeleporterThink);

    /// <summary><c>CTFTeleporter::TeleporterThink</c>: the state machine's tick.</summary>
    public override void OnThink(string context)
    {
        if (context != TeleporterThink || IsDead || !IsBuilt)
            return;

        // Sapper, etc. (TeleporterThink's refusal, tf_obj_teleporter.cpp
        // :1051-1060): at any point, a disabled end — or an end whose partner
        // is not ready, a sapped or building one among them
        // (IsMatchingTeleporterReady's !IsDisabled, :735-749) — stands down
        // to idle. The entrance of a sapped exit sends nobody.
        TFTeleporter? match = Match;
        if (IsDisabled || !MatchReady(match))
        {
            if (_state != TFTeleporterState.Idle)
                GoIdle();
            SetContextThink(TeleporterThink, TeleporterThinkDelay);
            return;
        }

        switch (_state)
        {
            case TFTeleporterState.Idle:
                _state = TFTeleporterState.Ready;
                break;

            case TFTeleporterState.Ready:
                TrySend(match);
                break;

            case TFTeleporterState.Sending:
                if (GameTime >= _stateEndsAt)
                    FinishSend(match);
                break;

            case TFTeleporterState.Receiving:
                if (GameTime >= _stateEndsAt)
                    ReleaseTeleportee();
                break;

            case TFTeleporterState.ReceivingRelease:
                if (GameTime >= _stateEndsAt)
                    FinishTrip();
                break;

            case TFTeleporterState.Recharging:
                if (GameTime >= _stateEndsAt)
                    GoIdle();
                break;
        }

        SetContextThink(TeleporterThink, TeleporterThinkDelay);
    }

    /// <summary>
    /// <c>CTFTeleporter::StartTouch</c> and <c>TeleporterTouch</c>'s booking
    /// (tf_obj_teleporter.cpp :546-553, :617-633): an entrance takes the first
    /// friendly who can ride, in any state — the send is what waits for READY
    /// (:639). A booking is replaced only when its rider can no longer ride or
    /// its grace has run out (:625-626); a rider stepping back onto the pad
    /// closes its own window (:550-553), and an unbookable toucher who holds a
    /// booking loses it (:602-605).
    /// </summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (IsDead || !IsBuilt || IsDisabled || Mode != 0)
            return;

        if (!CanBeTeleported(toucher))
        {
            // :602-605: the reserved rider who can no longer ride — dead or a
            // newly-spied enemy — is unbooked.
            if (_reserved == toucher)
                _reserved = EntityHandle.None;
            return;
        }

        // :550-553: the booked rider back on the pad — its grace window closes
        // and the send can have it again.
        if (_reserved == toucher)
        {
            _reserveUntil = 0;
            _onPad = true;
            return;
        }

        // :622-632: the first who can ride books; a booking is replaced only
        // when its rider can no longer ride or its grace has run out.
        bool rebook = _reserved == EntityHandle.None
                      || !CanBeTeleported(_reserved)
                      || (_reserveUntil != 0 && _reserveUntil < GameTime);
        if (rebook)
        {
            _reserved = toucher;
            _reserveUntil = 0;
        }

        // The pad belongs to the rider it books; a toucher who is not the
        // booked rider leaves the booked rider's pad state alone.
        if (_reserved == toucher)
            _onPad = true;
    }

    /// <summary>
    /// <c>CTFTeleporter::EndTouch</c> (tf_obj_teleporter.cpp :559-569): the
    /// stepped-off rider is pushed off the pad, so it keeps the booking for
    /// <see cref="BookingGrace"/> seconds — but is not sent while off it.
    /// </summary>
    public override void OnEndTouch(EntityHandle toucher)
    {
        if (_reserved == toucher)
        {
            _reserveUntil = GameTime + BookingGrace;
            _onPad = false;
        }
    }

    /// <summary>
    /// <c>CTFTeleporter::PlayerCanBeTeleported</c>: a live friendly. (The
    /// flag and passtime arms, and the spy's, are deferred.)
    /// </summary>
    private bool CanBeTeleported(EntityHandle candidate)
        => candidate != EntityHandle.None
           && Entities.IsPlayer(candidate)
           && Players.IsAlive(candidate)
           && Players.Team(candidate) == Team;

    /// <summary>
    /// <c>CTFTeleporter::TeleporterSend</c>'s start: the entrance books the
    /// standing player and holds the send for its own beat
    /// (<c>TELEPORTER_SEND_TIME</c>, tf_obj_teleporter.cpp:126-127).
    /// </summary>
    private void TrySend(TFTeleporter? match)
    {
        if (Mode != 0 || match is null || _reserved == EntityHandle.None)
            return;

        if (!CanBeTeleported(_reserved))
        {
            _reserved = EntityHandle.None;
            return;
        }

        // Stock sends only from the rider's own touch callback (:639-648):
        // a booked rider standing off the pad — pushed off by the crowd —
        // keeps its booking through the grace but rides nobody.
        if (!_onPad)
            return;

        if (Entities.GetVelocity(_reserved).LengthSquared >= StandingSpeedSquared)
            return;

        EntityHandle traveller = _reserved;
        _reserved = EntityHandle.None;
        _teleportee = traveller;
        _state = TFTeleporterState.Sending;
        _stateEndsAt = GameTime + SendDelay;

        // TeleporterSend's show (tf_obj_teleporter.cpp:98-124): the portal on
        // the pad, the sparkles on the rider, then the send.
        TeleportShow(traveller, departure: true);
        _sound.EntityEmitSound(Handle, SendSound);

        // TeleporterSend's m_iTimesUsed++ (tf_obj_teleporter.cpp:129): the
        // status panel's "Times Used" counts a departure here and the
        // exit's arrival in its own BeginReceive.
        _timesUsed++;

        // TeleporterSend's boost arm (tf_obj_teleporter.cpp:153-158): the
        // builder's mod_teleporter_speed_boost, read
        // CALL_ATTRIB_HOOK_INT_ON_OTHER-style on the builder's own manager,
        // buys the traveller four seconds of the stock speed boost.
        if (_hooks.Int(0, "mod_teleporter_speed_boost", _owners.ManagerOf(Entities.GetIndex(Builder))) != 0)
            _conditions.Add(traveller, TFCond.SpeedBoost, 4f);
    }

    /// <summary>
    /// <c>TeleporterThink</c>'s SENDING arm (tf_obj_teleporter.cpp
    /// :1105-1120): the exit is told — its <c>TeleporterReceive</c> starts
    /// the fade-in — and the entrance's long recharge counts from here:
    /// <c>FADEOUT + FADEIN + duration</c>.
    /// </summary>
    private void FinishSend(TFTeleporter? match)
    {
        if (match is not null && _teleportee != EntityHandle.None)
            match.BeginReceive(_teleportee);

        _state = TFTeleporterState.Recharging;
        _stateEndsAt = GameTime + FadeTime + FadeTime + RechargeTimeForLevel(Level);
    }

    /// <summary>
    /// The exit's side of the send: fade the arrival in.
    /// </summary>
    internal void BeginReceive(EntityHandle traveller)
    {
        _teleportee = traveller;
        _state = TFTeleporterState.Receiving;
        _stateEndsAt = GameTime + FadeTime;

        // TeleporterReceive's show (tf_obj_teleporter.cpp:171-200): the
        // arrival portal, then the receive.
        TeleportShow(traveller, departure: false);
        _sound.EntityEmitSound(Handle, ReceiveSound);

        // TeleporterReceive's m_iTimesUsed++ (tf_obj_teleporter.cpp:205).
        _timesUsed++;
    }

    /// <summary>
    /// The pair's half of the show, by the rider's team: the departure fires
    /// the <c>teleported</c> portal at the pad and the sparkles on the
    /// traveller (<c>PATTACH_ABSORIGIN</c>); the arrival fires the
    /// <c>teleportedin</c> portal alone (tf_obj_teleporter.cpp:110-122,
    /// :188-198). Every record rides <c>CPVSFilter</c> at the origin
    /// (:99, :172) — the PVS of the point, over the world-position
    /// <c>ParticleEffect</c> dispatch whose Start stays at the zero default
    /// (tf_fx.cpp:320 → :342-348, <c>vecStart</c> = <c>vec3_origin</c>).
    /// </summary>
    private void TeleportShow(EntityHandle traveller, bool departure)
    {
        Vector origin = Origin;

        (string portal, string? sparkles) = Players.Team(traveller) switch
        {
            (int)TFTeam.Red => departure ? ("teleported_red", "player_sparkles_red") : ("teleportedin_red", null),
            (int)TFTeam.Blue => departure ? ("teleported_blue", "player_sparkles_blue") : ("teleportedin_blue", null),
            _ => ("", null),                     // stock's default arm sends nothing
        };

        if (portal.Length > 0)
            DispatchPortal(portal, origin);

        if (sparkles is not null)
        {
            // The rider's record: the entity, the attach type where the
            // client reads it, and no attachment point (the name overload
            // looks one up only when given a name to look for).
            TeEffectData worn = new()
            {
                Scale = 1f,
                HitBox = Precache.ParticleSystemIndex(sparkles),
                Flags = FromEntity,
                EntIndex = Entities.GetIndex(traveller),
                DamageType = PattachAbsorigin,
            };
            _temps.DispatchEffect(TeFilters.Pvs(origin), 0f, "ParticleEffect", in worn);
        }
    }

    /// <summary>The world-position portal record: the system, the spot, nothing else.</summary>
    private void DispatchPortal(string name, Vector origin)
    {
        TeEffectData record = new()
        {
            Scale = 1f,
            HitBox = Precache.ParticleSystemIndex(name),
            Origin = origin,
        };

        _temps.DispatchEffect(TeFilters.Pvs(origin), 0f, "ParticleEffect", in record);
    }

    /// <summary><c>m_iTimesUsed</c>: trips counted, once at each end —
    /// hud_obj_tele_entrance.res's <c>TimesUsedLabel</c>.</summary>
    public int TimesUsed => _timesUsed;

    /// <summary>
    /// <c>TeleporterThink</c>'s RECEIVING arm (tf_obj_teleporter.cpp
    /// :1123-1131): the trip ends — <c>RecieveTeleportingPlayer</c>, the
    /// traveller on the deck with no velocity (:1125) — and the fade-in
    /// (<c>RECEIVING_RELEASE</c>) follows.
    /// </summary>
    private void ReleaseTeleportee()
    {
        _state = TFTeleporterState.ReceivingRelease;
        _stateEndsAt = GameTime + FadeTime;

        if (_teleportee != EntityHandle.None)
        {
            // Stock kills before the move (:1012-1017): the frag is aimed at
            // the box the arrival is about to occupy.
            Telefrag(_teleportee);
            Entities.Teleport(_teleportee, Origin + new Vector(0f, 0f, ArrivalHeight), null, zeroVelocity: true);

            // InstantlySniperUnzoom (tf_player_shared.cpp:1387-1406): the
            // zoomed sniper — here the zoomed condition, which only the
            // rifles set — loses it and the aiming with it, and the slam
            // takes the view back now (:1402, a zero rate). The rifle's
            // pending clocks are the behaviour's own word, not the arrival's.
            if (_conditions.InCond(_teleportee, TFCond.Zoomed))
            {
                _conditions.Remove(_teleportee, TFCond.Zoomed);
                _conditions.Remove(_teleportee, TFCond.Aiming);
                _enginePlayers.SetFov(_teleportee, _teleportee, 0, 0f);
            }

            // The unzoom's tail (:1022): the view eases back from the scoped
            // width, tf_teleporter_fov_start over tf_teleporter_fov_time.
            _enginePlayers.SetFov(_teleportee, _teleportee, 0, _fovTime.Value, _fovStart.Value);

            // The arrival's flash (:1024-1025): white at 100, a 0.25 s fade
            // in over a 0.4 s hold, FFADE_IN.
            _screen.Fade(_teleportee, 255, 255, 255, 100, 0.25f, 0.4f, FadeFlags.In);

            // The bread roll (:1027-1032): every arrival rolls for lunch —
            // the soldier one time in three, everyone else one in twenty.
            int nMax = _classes.ClassOf(_teleportee) == TFClass.Soldier ? 2 : 19;
            if (_random.RandomInt(0, nMax) == 0)
                SpawnBread();
        }
    }

    /// <summary>
    /// <c>SpawnBread</c> (tf_obj_teleporter.cpp:1460-1520): a physics prop of
    /// lunch launched off the exit's crown — the exit's origin with
    /// <c>TELEPORTER_MAXS.z + 50</c> of headroom (:35, :1479-1480), thrown
    /// forward at 100, up at 200 and 50 units of jitter on each axis, with a
    /// ±100 tumble, and gone after ten seconds (:1506-1514).
    ///
    /// <para>
    /// The tree has no <c>prop_physics_override</c> to create, so the loaf is
    /// thrown as debris through the gib seam (<see cref="Gibs"/>), which
    /// carries the three things the prop is for: a solver object in the
    /// debris collision group (:1482), the instantaneous launch (:1510) and
    /// the timed removal (:1514). What it does not carry: the 5000-health
    /// DAMAGE_YES that keeps lunch from blocking trains (:1500-1501) and the
    /// FL_GRENADE flag that makes it airblastable (:1484) — a gib takes no
    /// damage and carries no grenade flag, so a loaf here is scenery a train
    /// passes through rather than a prop it pushes.
    /// </para>
    /// </summary>
    private void SpawnBread()
    {
        // :1465: the class-range draw over the nine models. The comment rows
        // of g_pszBreadModels name the classes loosely; the draw ignores who
        // rides, stock included.
        string model = _breadModels[_random.RandomInt(0, _breadModels.Length - 1)];

        // :1479-1481 and :1506-1510: the exit's crown and its facing, and
        // the stock launch — forward 100, up 200, 50 of jitter per axis.
        Vector angles = Entities.GetAngles(Handle);
        Vector velocity = Angles.Forward(angles) * 100f
                          + new Vector(0f, 0f, 200f)
                          + new Vector(_random.RandomFloat(-50f, 50f),
                                       _random.RandomFloat(-50f, 50f),
                                       _random.RandomFloat(-50f, 50f));

        // The gib's own arms carry the rest the prop asked for: the debris
        // collision group, friction and the ten-second removal
        // (SsGib.Throw, from the physics branch); AngularVelocity is the
        // ±100 impulse's scale, and the gib's tumble keeps stock's zero on
        // the spin axis. Silent and bloodless: it is a prop, not offal.
        _gibs.Shoot(model, new GibShot
        {
            Origin = Origin + new Vector(0f, 0f, 62f),   // TELEPORTER_MAXS.z (12) + 50
            Angles = angles,
            Direction = velocity.Normalized,
            Speed = velocity.Length,
            Simulation = (int)GibSimulation.Physics,
            Lifetime = 10f,                              // :1514 SUB_Remove at curtime + 10
            AngularVelocity = 100f,                      // :1506 RandomFloat(-100, 100) per axis
            Scale = 1f,
            Material = (int)SurfaceMaterial.Silent,
            BloodColor = (int)BloodColor.None,
        });
    }

    /// <summary>
    /// <c>RecieveTeleportingPlayer</c>'s telefrag arm (tf_obj_teleporter.cpp
    /// :927-933, :942-961, :1012-1015): sweep the box the arrival will occupy
    /// — the deck position, the player hull and <see cref="TelefragSlack"/>
    /// of slack on each side — and every enemy player standing in it takes
    /// 1000 <c>DMG_CRUSH</c> with <c>TF_DMG_CUSTOM_TELEFRAG</c> from the
    /// traveller, attacker and infliction both. A traveller off the teams
    /// kills nobody (:957), and the traveller escapes its own arrival by the
    /// same-team test. (Stock's solid-abort arm, :967-1005, reads solidity
    /// and collision groups the seams do not have; a blocked arrival that
    /// stock detonates the exit over teleports here.)
    /// </summary>
    private void Telefrag(EntityHandle traveller)
    {
        Vector expand = new(TelefragSlack, TelefragSlack, TelefragSlack);
        Vector deck = Origin + new Vector(0f, 0f, ArrivalHeight);
        Vector mins = deck + TFViewVectors.Params.HullMin - expand;
        Vector maxs = deck + TFViewVectors.Params.HullMax + expand;

        // UTIL_EntitiesInBox on a sphere superset, the blast applier's
        // pattern: the engine sweep is wide, the exact box retest is ours.
        Span<EntityHandle> found = stackalloc EntityHandle[256];
        int count = _world.EntitiesInSphere((mins + maxs) * 0.5f, (maxs - mins).Length * 0.5f, found);

        int travellerTeam = Players.Team(traveller);
        if (travellerTeam < FirstGameTeam)   // :957 — the arrival's own team test
            return;

        List<EntityHandle>? toKill = null;
        for (int i = 0; i < count; i++)
        {
            EntityHandle candidate = found[i];
            if (candidate == EntityHandle.None || candidate == Handle)   // :948-952
                continue;

            if (!Entities.IsPlayer(candidate))
                continue;

            int team = Players.Team(candidate);
            if (team < FirstGameTeam || team == travellerTeam)           // :955-957
                continue;

            (Vector hullMins, Vector hullMaxs) = Entities.GetBounds(candidate);
            Vector at = Entities.GetOrigin(candidate);
            if (at.X + hullMins.X > maxs.X || mins.X > at.X + hullMaxs.X
                || at.Y + hullMins.Y > maxs.Y || mins.Y > at.Y + hullMaxs.Y
                || at.Z + hullMins.Z > maxs.Z || mins.Z > at.Z + hullMaxs.Z)
                continue;                                                 // the box, exactly

            (toKill ??= new List<EntityHandle>()).Add(candidate);
        }

        if (toKill is null)
            return;

        foreach (EntityHandle victim in toKill)
        {
            // TakeDamageInfo(pTeleportee, pTeleportee, 1000, DMG_CRUSH,
            // TF_DMG_CUSTOM_TELEFRAG) — no weapon, never a crit.
            using (_hitGroups.Push(HitGroup.Generic, critical: false))
            using (_scopes.Push(TFDamageCustom.Telefrag, TFWeaponId.None, ""))
                _damage.TakeDamageAnswered(victim, traveller, 1000f, DamageType.Crush);
        }
    }

    /// <summary>
    /// The release's end: the traveller forgotten, the exit recharging. The
    /// farewell is the glow the release arm lights first
    /// (<c>TeleportEffect</c>, called at tf_obj_teleporter.cpp:1139) —
    /// <c>TF_COND_TELEPORTED</c> for <see cref="TeleportedGlowSeconds"/>.
    /// </summary>
    private void FinishTrip()
    {
        if (_teleportee != EntityHandle.None)
            _conditions.Add(_teleportee, TFCond.Teleported, TeleportedGlowSeconds);

        _teleportee = EntityHandle.None;
        _state = TFTeleporterState.Recharging;
        _stateEndsAt = GameTime + RechargeTimeForLevel(Level);
    }

    private void GoIdle()
    {
        // The reservation is not stock's to clear here — it goes when the
        // send happens (tf_obj_teleporter.cpp:131) or the touch unbooks.
        _state = TFTeleporterState.Idle;
    }

    /// <summary>
    /// <c>IsMatchingTeleporterReady</c>: the partner stands built, alive and
    /// unsapped — the readiness the think's refusal and the state machine
    /// both ask (tf_obj_teleporter.cpp:735-749).
    /// </summary>
    private static bool MatchReady(TFTeleporter? match) => match is { IsBuilt: true, IsDead: false, IsDisabled: false };
}

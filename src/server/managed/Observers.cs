//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>What the server remembers about one player's watching, beyond the networked mode and target.</summary>
public sealed class ObserverRecord
{
    /// <summary>m_iObserverLastMode: the mode a death or a death cam goes back to.</summary>
    public ObserverMode LastMode { get; set; } = ObserverMode.Roaming;

    /// <summary>m_bForcedObserverMode: watching a fallback until a target turns up.</summary>
    public bool Forced { get; set; }

    /// <summary>m_flDeathTime.</summary>
    public float DeathTime { get; set; } = float.NegativeInfinity;

    /// <summary>
    /// CHL2MP_Player's m_bEnterObserver: the player asked to watch (spectate).
    /// HL2MP observes only then, never on a death timeout.
    /// </summary>
    public bool EnterObserver { get; set; }

    /// <summary>The dead body came to rest (PlayerDeathThink's MOVETYPE_NONE on the ground).</summary>
    public bool Landed { get; set; }
}

/// <summary>What a <c>jointeam N</c> did (CHL2MP_Player::HandleCommand_JoinTeam).</summary>
public enum JoinTeamResult
{
    /// <summary>No such team, or team 0: refused with stock's warning.</summary>
    Invalid,
    /// <summary>Team 1 with mp_allowspectators 0: #Cannot_Be_Spectator.</summary>
    SpectatorsNotAllowed,
    /// <summary>Team 1: on the spectator team (<see cref="Teams.Join"/> stores it) and watching.</summary>
    Spectating,
    /// <summary>A playing team: observing stopped, and the team stored by <see cref="Teams.Join"/>.</summary>
    Playing,
    /// <summary>A playing team the <see cref="IJoinTeamPolicy"/> said no to: nothing changed.</summary>
    Refused,
}

/// <summary>
/// Stock's observer mode, the dead CBasePlayer arm, on the managed player
/// (player.cpp: StartObserverMode, StopObserverMode, SetObserverMode,
/// ForceObserverMode, CheckObserverSettings, ValidateCurrentObserverTarget,
/// SetObserverTarget, IsValidObserverTarget, FindNextObserverTarget, and
/// ClientCommand's spec_mode, spec_next, spec_prev and spec_player).
///
/// <para>
/// The mode and target are the player's networked state
/// (<see cref="PlayerNetState.ObserverMode"/>, <see cref="PlayerNetState.ObserverTarget"/>,
/// the invalid handle for nobody). The native player carries out what this
/// decides: the observer flag and solidity (IEnginePlayers.SetObserverMode)
/// and stock's movetype per mode (NONE for death cam and fixed, OBSERVER
/// otherwise). The server's movement follows the target in in-eye and chase
/// (MovementRunner.ObserverTargetPose).
/// </para>
///
/// <para>
/// HL2MP's rule over all of it (CHL2MP_Player::StartObserverMode): a player
/// observes only after asking (<c>spectate</c>, <c>jointeam 1</c>), never on a
/// death timeout. A dead player keeps mode NONE until respawn and the client
/// draws its death cam.
/// </para>
///
/// <para>
/// Not ported, each named: the team change's rate limit (the change itself is
/// Teams.Join's, which fires player_team); the HUD's HIDEHUD_HEALTH and stock's health of 1
/// and LIFE_DEAD while observing (the managed player's life is its own);
/// mp_fadetoblack; the fog controller copy in CheckObserverSettings; the
/// SteamID forms of a spec_player argument; spec_goto; the spectator GUI
/// panels (the UI lane's).
/// </para>
/// </summary>
public sealed class Observers(ILog log, IEntityDirectory directory, IEngineEntities entities, IEnginePlayers players,
                              IEnginePlayerMovement movement, IEngineEntityRender render, IEngineDamage damage,
                              IEngineWorldQuery world, IEngineEntityPhysics physics, ConVarRegistry convars,
                              IConnectedClients? clients = null)
{
    /// <summary>DEATH_ANIMATION_TIME: a dead player may be watched this long.</summary>
    public const float DeathAnimationTime = 3f;

    /// <summary>TEAM_SPECTATOR.</summary>
    public const int TeamSpectator = 1;

    // Looked up when first asked: the module builds this before its convars are registered.
    private ForceCameraConVar? _forceCamera;

    private int ForceCamera
    {
        get
        {
            _forceCamera ??= convars.Get<ForceCameraConVar>();
            int v = _forceCamera?.Value ?? ForceCameraConVar.AllowTeam;
            return v is >= ForceCameraConVar.AllowAll and <= ForceCameraConVar.AllowNone ? v : ForceCameraConVar.AllowTeam;
        }
    }

    private static ObserverMode ModeOf(SsPlayer p) => (ObserverMode)p.State.ObserverMode;

    /// <summary>IsObserver(): watching rather than playing.</summary>
    public static bool IsObserver(SsPlayer p) => p.State.ObserverMode != 0;

    /// <summary>The watched player, or null.</summary>
    public SsPlayer? TargetOf(SsPlayer player)
    {
        var handle = new EntityHandle(player.State.ObserverTarget);
        return handle == EntityHandle.None ? null : directory.Get(handle) as SsPlayer;
    }

    //-- StartObserverMode / StopObserverMode -----------------------------------

    //-- CHL2MP_Player: spectate / jointeam (HandleCommand_JoinTeam, ChangeTeam,
    //   State_Enter_OBSERVER_MODE) --------------------------------------------

    // Looked up when first asked, like mp_forcecamera.
    private AllowSpectatorsConVar? _allowSpectators;

    /// <summary>
    /// <c>spectate</c>'s observer half, which is HandleCommand_JoinTeam(TEAM_SPECTATOR)
    /// past its gate: refused without mp_allowspectators
    /// (<see cref="AllowsSpectators"/>); ChangeTeam's RemoveAllItems(true);
    /// State_Enter_OBSERVER_MODE's mode, cl_spec_mode when it is in-eye to
    /// roaming and the last mode otherwise; m_bEnterObserver; StartObserverMode.
    /// The team itself is <see cref="Teams.Join"/>'s to store (it stores team 1
    /// BEFORE calling this, stock's ChangeTeam order). Stock's suicide for a
    /// living teamed player is not made (the <c>ITeams.Join</c> divergence row).
    /// </summary>
    public bool Spectate(SsPlayer player, float now)
    {
        if (!AllowsSpectators())
            return false;

        players.RemoveAllItems(player.Handle, stripSuit: true);

        var mode = player.Observer.LastMode;
        if (int.TryParse(clients?.ClientConVar(entities.GetIndex(player.Handle), "cl_spec_mode"),
                         NumberStyles.Integer, CultureInfo.InvariantCulture, out int ideal)
            && ideal > (int)ObserverMode.Fixed && ideal <= (int)ObserverMode.Roaming)
            mode = (ObserverMode)ideal;

        player.Observer.EnterObserver = true;
        return Start(player, mode, now);
    }

    /// <summary>
    /// HandleCommand_JoinTeam(TEAM_SPECTATOR)'s gate: mp_allowspectators, and
    /// stock's <c>#Cannot_Be_Spectator</c> when it is off. Asked by
    /// <see cref="Teams.Join"/> before it stores team 1, so a refusal changes
    /// nothing.
    /// </summary>
    public bool AllowsSpectators()
    {
        _allowSpectators ??= convars.Get<AllowSpectatorsConVar>();
        if (_allowSpectators is { Value: false })
        {
            log.Msg("#Cannot_Be_Spectator\n");
            return false;
        }

        return true;
    }

    /// <summary>
    /// <c>jointeam N</c>'s observer half: HandleCommand_JoinTeam. No such team,
    /// or team 0, is refused (stock's warning); team 1 is <see cref="Spectate"/>;
    /// a playing team stops observing (StopObserverMode, State_Enter_ACTIVE).
    /// DIVERGENCE: stock's spectator is LIFE_DEAD and comes back on a button
    /// (or mp_forcerespawn); this one never died, so it is respawned here. The
    /// team store is <see cref="Teams.Join"/>'s.
    /// </summary>
    public JoinTeamResult JoinTeam(SsPlayer player, string? arg, float now)
    {
        int team = Atoi(arg);
        if (!IsValidTeamNumber(team))
        {
            log.Warning($"HandleCommand_JoinTeam( {team} ) - invalid team index.\n");
            return JoinTeamResult.Invalid;
        }

        if (team == TeamSpectator)
            return Spectate(player, now) ? JoinTeamResult.Spectating : JoinTeamResult.SpectatorsNotAllowed;

        bool was = IsObserver(player);
        Stop(player);
        if (was)
            players.Respawn(player.Handle);
        return JoinTeamResult.Playing;
    }

    /// <summary>The last of HL2MP's four teams (unassigned, spectator, combine, rebels).</summary>
    public const int LastTeam = 3;

    /// <summary>
    /// Whether a <c>jointeam</c> argument names one of HL2MP's joinable teams.
    /// The same gate <see cref="JoinTeam"/> applies — one spelling, so the
    /// route that asks first (<see cref="Teams.Join"/>) and the arm that
    /// refuses cannot drift apart.
    /// </summary>
    public static bool IsValidTeamNumber(int team) => team is >= TeamSpectator and <= LastTeam;

    /// <summary>
    /// C's <c>atoi</c>, as stock's command handlers parse a team number: the
    /// leading digits after optional whitespace and sign, 0 for none
    /// (<c>"2x"</c> is 2, <c>""</c> is 0). One spelling for <c>jointeam</c>'s
    /// two readers (the command and this class's own <see cref="JoinTeam"/>).
    /// </summary>
    internal static int Atoi(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        int i = 0, sign = 1, value = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        if (i < text.Length && (text[i] == '-' || text[i] == '+'))
            sign = text[i++] == '-' ? -1 : 1;
        while (i < text.Length && text[i] is >= '0' and <= '9' && value < 100000)
            value = value * 10 + (text[i++] - '0');
        return sign * value;
    }

    //-- The dead player (CBasePlayer::Event_Killed's body, PlayerDeathThink) ---

    private const uint FlOnGround = 1u << 0;

    /// <summary>
    /// Event_Killed's body: not solid, MOVETYPE_FLYGRAVITY until it lands.
    /// The mode stays NONE: the client's view is its death cam.
    /// </summary>
    public void Killed(SsPlayer player, float now)
    {
        player.Observer.DeathTime = now;
        player.Observer.Landed = false;
        physics.SetNotSolid(player.Handle, true);
        movement.SetMoveType(player.Handle, MoveType.FlyGravity);
    }

    /// <summary>
    /// PlayerDeathThink's observer half, as CHL2MP_Player runs it (not for an
    /// observer): the body comes to rest (MOVETYPE_NONE) on the ground, and
    /// DEATH_ANIMATION_TIME later StartObserverMode(m_iObserverLastMode),
    /// which HL2MP refuses unless the player asked to watch.
    /// </summary>
    public void DeathThink(SsPlayer player, float now)
    {
        if (IsObserver(player))
            return;

        if (!player.Observer.Landed && (player.State.Flags & FlOnGround) != 0)
        {
            player.Observer.Landed = true;
            movement.SetMoveType(player.Handle, MoveType.None);
        }

        if (now - player.Observer.DeathTime > DeathAnimationTime)
            Start(player, player.Observer.LastMode, now);
    }

    /// <summary>CHL2MP_Player::PlayerDeathThink runs (and so respawns) only a player who is not observing.</summary>
    public static bool MayRespawn(SsPlayer player) => !IsObserver(player);

    public bool Start(SsPlayer player, ObserverMode mode, float now)
    {
        // CHL2MP_Player::StartObserverMode: "we only want to go into observer
        // mode if the player asked to, not on a death timeout".
        if (!player.Observer.EnterObserver)
            return false;

        if (mode == ObserverMode.None)
            return false;

        players.HolsterActiveWeapon(player.Handle);
        render.SetVisible(player.Handle, false);          // EF_NODRAW
        damage.SetTakeDamageMode(player.Handle, TakeDamageMode.No);
        players.SetObserverMode(player.Handle, mode);     // PFLAG_OBSERVER, not solid

        SetMode(player, mode, now);
        log.Msg($"[SourceSharp/Observer] {player.Handle} watching as {ModeOf(player)}\n");
        return true;
    }

    public void Stop(SsPlayer player)
    {
        player.Observer.EnterObserver = false;   // CHL2MP_Player::StopObserverMode
        player.Observer.Forced = false;
        if (ModeOf(player) == ObserverMode.None)
            return;

        if (ModeOf(player) > ObserverMode.DeathCam)
            player.Observer.LastMode = ModeOf(player);

        player.State.ObserverMode = 0;
        players.SetObserverMode(player.Handle, ObserverMode.None);
        damage.SetTakeDamageMode(player.Handle, TakeDamageMode.Yes);
    }

    //-- SetObserverMode / ForceObserverMode ------------------------------------

    public bool SetMode(SsPlayer player, ObserverMode mode, float now)
    {
        if (mode < ObserverMode.None || mode > ObserverMode.Roaming)
            return false;

        // mp_forcecamera restricts a player on a team (team > TEAM_SPECTATOR).
        if (mode > ObserverMode.Fixed && players.GetTeam(player.Handle) > TeamSpectator)
        {
            mode = ForceCamera switch
            {
                ForceCameraConVar.AllowTeam => ObserverMode.InEye,
                ForceCameraConVar.AllowNone => ObserverMode.Fixed,
                _ => mode,
            };
        }

        if (ModeOf(player) > ObserverMode.DeathCam)
            player.Observer.LastMode = ModeOf(player);

        player.State.ObserverMode = (int)mode;

        // And to the native player, whose mode is the one the movement loop
        // reads (MvGetObserverMode): FullObserverMove follows the target only
        // in in-eye, chase and POI. Networked alone, a spec_mode to in-eye left
        // the observer roaming where SetObserverTarget's jump put it.
        if (mode != ObserverMode.None)
            players.SetObserverMode(player.Handle, mode);

        switch (mode)
        {
            case ObserverMode.None:
            case ObserverMode.Fixed:
            case ObserverMode.DeathCam:
                movement.SetMoveType(player.Handle, MoveType.None);
                break;
            default:
                SetTarget(player, TargetOf(player), now);
                movement.SetMoveType(player.Handle, MoveType.Observer);
                break;
        }

        CheckSettings(player, now);
        return true;
    }

    private void Force(SsPlayer player, ObserverMode mode, float now)
    {
        var temp = ObserverMode.Roaming;
        if (ModeOf(player) == mode)
            return;

        if (player.Observer.Forced)
            temp = player.Observer.LastMode;

        SetMode(player, mode, now);

        if (player.Observer.Forced)
            player.Observer.LastMode = temp;

        player.Observer.Forced = true;
    }

    //-- CheckObserverSettings / ValidateCurrentObserverTarget ------------------

    /// <summary>Every frame while watching, and after a mode change.</summary>
    public void CheckSettings(SsPlayer player, float now)
    {
        if (player.Observer.Forced)
        {
            var target = TargetOf(player);
            if (!IsValidTarget(player, target, now))
                target = FindNext(player, reverse: false, now);

            if (target is not null)
            {
                player.Observer.Forced = false;
                SetMode(player, player.Observer.LastMode, now);
                SetTarget(player, target, now);
            }

            return;
        }

        if (player.Observer.LastMode < ObserverMode.Fixed)
            player.Observer.LastMode = ObserverMode.Roaming;

        if (ModeOf(player) is ObserverMode.InEye or ObserverMode.Chase or ObserverMode.Fixed or ObserverMode.PointOfInterest)
        {
            if (!IsValidTarget(player, TargetOf(player), now))
            {
                var next = FindNext(player, reverse: false, now);
                if (next is not null)
                {
                    SetTarget(player, next, now);
                }
                else if (ForceCamera == ForceCameraConVar.AllowAll)
                {
                    Force(player, ObserverMode.Roaming, now);
                }
                else
                {
                    Force(player, ObserverMode.Fixed, now);
                    player.State.ObserverTarget = EntityHandle.None.Value;
                }
            }

            // In-eye takes the target's view offset (stock also copies its
            // ground and duck flags and fog controller: not ported).
            if (TargetOf(player) is { } t && ModeOf(player) == ObserverMode.InEye)
                player.State.ViewOffset = t.State.ViewOffset;
        }
    }

    //-- SetObserverTarget / IsValidObserverTarget / FindNextObserverTarget ----

    public bool SetTarget(SsPlayer player, SsPlayer? target, float now)
    {
        if (!IsValidTarget(player, target, now))
            return false;

        player.State.ObserverTarget = target!.Handle.Value;

        // Roaming starts 64 units behind the target's eyes, looking where it looks.
        if (ModeOf(player) == ObserverMode.Roaming)
        {
            Vector start = target.ViewOrigin;
            Vector angles = entities.GetEyeAngles(target.Handle);
            angles = new Vector(angles.X, angles.Y, 0f);
            Vector end = start - Angles.Forward(angles) * 64f;
            var c = MoveConstantDefaults.Stock;
            var tr = world.EntityTraceHull(target.Handle, start, end, c.DuckHullMin, c.DuckHullMax,
                                           GameMovement.MaskPlayerSolid, GameMovement.CollisionGroupPlayerMovement);
            entities.Teleport(player.Handle, tr.EndPos, angles, zeroVelocity: true);
            movement.SnapEyeAngles(player.Handle, angles);
        }

        return true;
    }

    public bool IsValidTarget(SsPlayer player, SsPlayer? target, float now)
    {
        if (target is null || ReferenceEquals(target, player))
            return false;

        // EF_NODRAW: stock's observers are not drawn.
        if (IsObserver(target))
            return false;

        if (!target.State.Alive && now > target.Observer.DeathTime + DeathAnimationTime)
            return false;

        if (players.GetTeam(player.Handle) != TeamSpectator)
        {
            switch (ForceCamera)
            {
                case ForceCameraConVar.AllowTeam:
                    if (players.GetTeam(player.Handle) != players.GetTeam(target.Handle))
                        return false;
                    break;
                case ForceCameraConVar.AllowNone:
                    return false;
            }
        }

        return true;
    }

    public SsPlayer? FindNext(SsPlayer player, bool reverse, float now)
    {
        var bySlot = Slots();
        if (bySlot.Count == 0)
            return null;

        int max = Math.Max(bySlot.Keys.Max(), EngineLimits.MaxPlayerSlots);
        int dir = reverse ? -1 : 1;
        var current = TargetOf(player);
        int start = (current is not null ? entities.GetIndex(current.Handle) : entities.GetIndex(player.Handle)) + dir;
        start = Wrap(start, max);

        int index = start;
        do
        {
            if (bySlot.TryGetValue(index, out var candidate) && IsValidTarget(player, candidate, now))
                return candidate;

            index = Wrap(index + dir, max);
        }
        while (index != start);

        return null;
    }

    private static int Wrap(int index, int max) => index > max ? 1 : index < 1 ? max : index;

    private Dictionary<int, SsPlayer> Slots()
    {
        var slots = new Dictionary<int, SsPlayer>();
        foreach (var entity in directory.OfClass(EntityNames.Player))
        {
            if (entity is SsPlayer p)
                slots[entities.GetIndex(p.Handle)] = p;
        }
        return slots;
    }

    //-- the spec_* commands (CBasePlayer::ClientCommand) ------------------------

    /// <summary>spec_mode [mode]: the next mode, or the one named; only remembered in the death cam.</summary>
    public void SpecMode(SsPlayer player, string? arg, float now)
    {
        if (ModeOf(player) == ObserverMode.FreezeCam)
        {
            Start(player, ObserverMode.DeathCam, now);    // AttemptToExitFreezeCam
            return;
        }

        int mode;
        if (arg is not null)
        {
            mode = int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) ? m : 0;
            if (mode < (int)ObserverMode.InEye || mode > (int)ObserverMode.Roaming)
                mode = (int)ObserverMode.InEye;
        }
        else
        {
            mode = (int)ModeOf(player) + 1;
            if (mode > (int)ObserverMode.Roaming)
                mode = (int)ObserverMode.InEye;
            else if (mode < (int)ObserverMode.InEye)
                mode = (int)ObserverMode.Roaming;
        }

        if (ModeOf(player) > ObserverMode.DeathCam)
        {
            if (!SetMode(player, (ObserverMode)mode, now))
                log.Msg("#Spectator_Mode_Unknown\n");
        }
        else
        {
            player.Observer.LastMode = (ObserverMode)mode;
        }
    }

    /// <summary>spec_next / spec_prev.</summary>
    public void SpecNext(SsPlayer player, bool reverse, float now)
    {
        if (ModeOf(player) > ObserverMode.Fixed)
        {
            if (FindNext(player, reverse, now) is { } target)
                SetTarget(player, target, now);
        }
        else if (ModeOf(player) == ObserverMode.FreezeCam)
        {
            Start(player, ObserverMode.DeathCam, now);
        }
    }

    /// <summary>spec_player &lt;#userid | name | userid&gt;.</summary>
    public void SpecPlayer(SsPlayer player, string arg, float now)
    {
        if (ModeOf(player) <= ObserverMode.Fixed)
            return;

        if (ByCommandArg(arg) is { } target && IsValidTarget(player, target, now))
            SetTarget(player, target, now);
    }

    /// <summary>UTIL_PlayerByCommandArg without the SteamID forms: #userid, a unique partial name, then a bare userid.</summary>
    public SsPlayer? ByCommandArg(string arg)
    {
        if (arg.Length == 0 || clients is null)
            return null;

        var slots = Slots();
        bool allButFirstDigits = arg.Skip(1).All(char.IsAsciiDigit);
        bool allDigits = char.IsAsciiDigit(arg[0]) && allButFirstDigits;

        SsPlayer? found = null;
        if (arg.Length > 1 && arg[0] == '#' && allButFirstDigits)
            found = ByUserId(slots, int.Parse(arg.AsSpan(1), CultureInfo.InvariantCulture));

        var named = slots.Where(p => (clients.ClientConVar(p.Key, "name") ?? "")
                                         .Contains(arg, StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Count == 1)
        {
            if (found is not null && !ReferenceEquals(found, named[0].Value))
                return null;
            found = named[0].Value;
        }

        if (allDigits && found is null)
            found = ByUserId(slots, int.Parse(arg, CultureInfo.InvariantCulture));

        return found;
    }

    private SsPlayer? ByUserId(Dictionary<int, SsPlayer> slots, int userId)
        => slots.FirstOrDefault(p => clients!.UserId(p.Key) == userId).Value;
}

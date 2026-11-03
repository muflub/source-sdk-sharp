namespace TF2.Client.Ui;

/// <summary>
/// The TF2 client HUD's data contract (docs/plans/tf2/stage3_client.md
/// Stage 3): what TF2's screens draw, beside the core's own UI sources
/// (<see cref="SourceSharp.Client.Ui.IPlayerHealth"/> and its kin,
/// src/client/interface/Ui/UiSources.cs), in TF2's own contract assembly
/// rather than beside the screens, because the UI loader holds the screen
/// assembly in its own collectible context while the adapters live on the
/// game side, and the service container resolves by interface type
/// identity: both halves must name the one type the parent context holds. The TF2 client assembly answers
/// these with <c>[SideService]</c> adapters over its netstate mirrors
/// (src/tf2/client/Ui).
/// </summary>
public interface ITFLocalPlayer
{
    /// <summary>The local player's class name ("Soldier"), or the empty
    /// string before one is chosen:
    /// <c>g_aPlayerClassNames_NonLocalized</c> (tf_shareddefs.cpp), the
    /// table <c>TFClassNames.NonLocalized</c> carries.</summary>
    string ClassName { get; }

    /// <summary>Health points, what stock's player status HUD paints
    /// (<c>health</c> in <c>CTFPlayerStatusHUD::SetHealth</c>,
    /// tf_hud_playerstatus.cpp).</summary>
    int Health { get; }

    /// <summary>The class's health without overheal: the cross's
    /// <c>m_iMaxHealth</c> column of <c>tf_player_manager</c>, the value
    /// the server writes from <c>GetMaxHealth</c>.</summary>
    int MaxHealth { get; }

    /// <summary>How high overheal may go: the <c>m_iMaxBuffedHealth</c>
    /// column, what the server writes from
    /// <c>CTFPlayer::GetMaxHealthForBuffing</c> — the class's health
    /// times <c>tf_max_health_boost</c> (1.5, tf_player_shared.cpp:120),
    /// floored to a multiple of 5 (tf_player_shared.cpp:2253).</summary>
    int MaxBuffedHealth { get; }

    /// <summary>The health above the class's max, drawn as the bonus
    /// band (stock's bonus image scaled by
    /// <c>(health − max) / (maxbuffed − max)</c>,
    /// tf_hud_playerstatus.cpp:769); never below zero.</summary>
    int Overheal { get; }

    /// <summary>Whether the active weapon has an ammo readout at all —
    /// stock's <c>CTFHudWeaponAmmo::ShouldDraw</c>
    /// (tf_hud_ammostatus.cpp:120-163): false for empty hands, for the
    /// medigun (:137-140, whose charge the UberChargeAmmoPerShot test
    /// :150 also catches), for a weapon that spends no primary
    /// ammunition (:143-144, the scripts' <c>"None"</c>), and for one
    /// whose pool is metal (:147-148).</summary>
    bool ShowsAmmo { get; }

    /// <summary>Rounds in the clip (<c>Clip1()</c>, the
    /// <c>nAmmo1</c> of <c>OnThink</c>, tf_hud_ammostatus.cpp:276);
    /// <c>-1</c> for a weapon with no clip, whose row shows the pool
    /// total alone (:279-282).</summary>
    int Clip { get; }

    /// <summary>The local player's team, the raw TF number (2 RED, 3 BLU):
    /// <c>m_iTeam</c> of the <c>tf_player_manager</c> row, which stock's
    /// player status reads for the class image's team arm
    /// (<c>CTFClassImage::ApplySchemeSettings</c>'s teambg images,
    /// hudplayerclass.res).</summary>
    int Team { get; }

    /// <summary>The pool behind the clip (<c>GetAmmoCount</c> of the
    /// primary type, the <c>nAmmo2</c> of the clip arm,
    /// tf_hud_ammostatus.cpp:284-287) — and on a clip-less weapon the
    /// whole number there is to show (:279-282).</summary>
    int Reserve { get; }

    /// <summary>What the weapon's primary ammunition counts up to — the
    /// <c>nMaxTotalAmmo</c> of the low-ammo warning
    /// (tf_hud_ammostatus.cpp:313-319): the class's <c>m_aAmmoMax</c> row
    /// for the weapon's ammo (zero before a class is chosen, as stock's
    /// undefined row is) plus the script's <c>MaxClip1</c> when it has
    /// one. 0 with no weapon to count.</summary>
    int MaxTotalAmmo { get; }

    /// <summary>The engineer's metal stock: the local player's
    /// <c>TF_AMMO_METAL</c> pool, the account the metal panel counts
    /// (stock's changed-metal event payload,
    /// c_tf_player.cpp:4799-4813) and the build menu's purse.</summary>
    int Metal { get; }
}

/// <summary>
/// The capture points the HUD's icon panel draws (stock's
/// <c>CHudControlPointIcons</c>, hud_controlpointicons.cpp): the map's
/// point table — owners, caps and the layout string — plus the cap
/// progress stock advanced in the client's own think. Teams are the raw
/// TF team numbers (2 RED, 3 BLU); the screens' <c>TFHudTeams.ToneOf</c>
/// maps them to tones.
/// </summary>
public interface ITFCapturePoints
{
    /// <summary><c>m_iNumControlPoints</c>.</summary>
    int NumPoints { get; }

    /// <summary><c>m_pszCapLayoutInHUD</c>: the rows of point indices the
    /// map names — a comma starts a new line, a space separates the points
    /// on one line, so "3,1 2" is the pyramid. An empty layout is every
    /// point on one line (stock's default, hud_controlpointicons.cpp).</summary>
    string CapLayout { get; }

    /// <summary><c>m_bCPIsVisible</c>.</summary>
    bool Visible(int point);

    /// <summary><c>m_bInMiniRound</c>.</summary>
    bool InMiniRound(int point);

    /// <summary><c>m_iOwner</c>.</summary>
    int OwningTeam(int point);

    /// <summary><c>m_iCappingTeam</c>.</summary>
    int CappingTeam(int point);

    /// <summary>Stock <c>GetCPCapPercentage</c>, advanced to now — the cap
    /// time is the client's own model (see
    /// <c>ClientTFObjectiveResource</c>'s lazy think).</summary>
    float CapProgress(int point);

    /// <summary>Stock <c>CapIsBlocked</c>: a cap has time left and nobody
    /// on the point.</summary>
    bool Blocked(int point);

    /// <summary>Stock <c>IsPointLocked</c> (hud_controlpointicons.cpp:414-433):
    /// no game team may cap the point, its owner ignored.</summary>
    bool Locked(int point);

    /// <summary>Stock <c>GetNumPlayersInArea</c>: one team's cappers on the
    /// point.</summary>
    int Cappers(int point, int team);

    /// <summary>Stock <c>m_flCPTimerTimes[point]</c> against now (the icon's
    /// countdown counts to it, <c>CControlPointIcon::OnTick</c>,
    /// hud_controlpointicons.cpp:587-591): seconds left, 0 or less for
    /// none — the server clears the time as stock's reset does.</summary>
    float TimerSeconds(int point);

    /// <summary>Stock <c>m_flUnlockTimes[point]</c> against now (the
    /// per-icon countdown shows the last five seconds,
    /// <c>CControlPointCountdown::OnTick</c>, :1851-1876).</summary>
    float UnlockSeconds(int point);
}

/// <summary>
/// The cap teardrop's reads (stock's <c>CControlPointProgressBar</c>,
/// hud_controlpointicons.cpp:1499-1646): which point the local player stands
/// on, whether the sweep or the blocked text draws there, the sweep's fill
/// and the <c>#Team_Capture_*</c> token the ladder names.
/// </summary>
public interface ITFCapProgress
{
    /// <summary>The point the local player's <c>controlpoint_starttouch</c>
    /// named, -1 once their <c>controlpoint_endtouch</c> lands or before any
    /// touch (the <c>m_iCurrentCP</c> the event handler keeps,
    /// hud_controlpointicons.cpp:874-891).</summary>
    int Touched { get; }

    /// <summary>SetupForPoint's bar arm (:1519): a cap runs for the point,
    /// nobody blocked it, and it is the local team doing the capping — the
    /// sweep arm. False draws the blocked mark and the ladder's text.</summary>
    bool ShowsBar(int point);

    /// <summary>Stock <c>GetCPCapPercentage</c> (0..1): the sweep's fill.</summary>
    float Progress(int point);

    /// <summary>UpdateBarText's ladder (:1546-1646): the
    /// <c>#Team_Capture_*</c>/<c>#Cant_cap_*</c> token to show when
    /// <see cref="ShowsBar"/> is false.</summary>
    string Text(int point);
}

/// <summary>
/// The payload cart's reads for the escort HUD (tf_hud_escort.cpp): the
/// cart's progress along the track, the cappers pushing it, the recede
/// deadline and the track alarm, and the checkpoints and hills along the
/// bar. The single-train escort follows stock's blue bar (m_nTeam,
/// tf_hud_escort.cpp:445), so every per-team read is BLU's.
/// </summary>
public interface ITFEscort
{
    /// <summary>Stock's m_bHaveValidPointPositions
    /// (tf_hud_escort.cpp:1102-1114): at least one on-track point has a
    /// path distance — the escort map's bar has somewhere to stand.</summary>
    bool Present { get; }

    /// <summary>Stock's m_flProgress (the escort_progress event's value,
    /// carried here by the objective mirror's m_flTrainProgress): 0 at the
    /// first checkpoint, 1 at the last.</summary>
    float Progress { get; }

    /// <summary>Stock's m_iNumPlayers (the escort_speed event's "players"):
    /// how many cappers push the cart.</summary>
    int Cappers { get; }

    /// <summary>Stock's m_iSpeedLevel (the escort_speed event's "speed"):
    /// -1 rolling backwards, 0 stalled, 1..3 forward.</summary>
    int SpeedLevel { get; }

    /// <summary>Stock's m_flRecedeTime against the frame's now
    /// (:830): seconds until the cart starts receding, 0 when not
    /// receding.</summary>
    float RecedeSeconds { get; }

    /// <summary>Stock's m_bAlarm (GetTrackAlarm, :737): the cart's team is
    /// losing ground and the alarm pulses.</summary>
    bool Alarm { get; }

    /// <summary><c>m_iNumControlPoints</c>.</summary>
    int Points { get; }

    /// <summary>Stock's m_bMultipleTrains (HasMultipleTrains): the map fields
    /// more than one train — the group test and the alarm's gate
    /// (tf_hud_escort.cpp:1011-1024, :656-691).</summary>
    bool MultipleTrains { get; }

    /// <summary>The point stands on this track: stock's icon validity arm
    /// (tf_hud_escort.cpp:1075-1080) — on a multiple-train map only the
    /// local team's group shows.</summary>
    bool PointOnTrack(int point);

    /// <summary><c>m_iPathDistance</c>: the point's share of the bar,
    /// 0..1.</summary>
    float PathDistance(int point);

    /// <summary>Stock <c>CapIsBlocked</c>: a cap has time left and nobody
    /// on the point (read by controlpoint_updateimages, :947).</summary>
    bool Blocked(int point);

    /// <summary><c>m_iOwner</c>: the checkpoint square's colour.</summary>
    int OwningTeam(int point);

    /// <summary>Stock's m_nNumHills (GetNumNodeHillData).</summary>
    int NumHills { get; }

    /// <summary>The hill's span along the bar (GetHillData's start and
    /// end); false when the hill is out of range.</summary>
    bool Hill(int hill, out float start, out float end);

    /// <summary>Stock <c>IsHillDownhill</c>.</summary>
    bool HillDownhill(int hill);
}

/// <summary>
/// The CTF flags the HUD's status row draws (stock's
/// <c>CTFHudFlagObjectives</c> with its per-team <c>CTFFlagStatus</c>
/// panels, tf_hud_flagstatus.cpp). Teams are the raw TF team numbers
/// (2 RED, 3 BLU).
/// </summary>
public interface ITFFlagStatus
{
    /// <summary>Stock's <c>nNumValidFlags</c> count of the tick
    /// (tf_hud_flagstatus.cpp:539): at least one team fields a flag —
    /// with none the row draws nothing.</summary>
    bool Present { get; }

    /// <summary>Whether the team fields a flag: stock's
    /// <c>m_pRedFlag</c>/<c>m_pBlueFlag</c> holding a live, enabled
    /// flag entity (OnTick, :541-559).</summary>
    bool Has(int team);

    /// <summary><c>ETFFlagStatus</c> of the team's flag: 0 home,
    /// 1 stolen, 2 dropped (<c>CTFFlagStatus::UpdateStatus</c>'s
    /// IsDropped/IsStolen arms, :338-346).</summary>
    int Status(int team);

    /// <summary>Seconds until a dropped flag returns — the mirror's
    /// <c>m_flFlagResetTime</c> against the frame's now, 0 when not
    /// dropped.</summary>
    float ResetSeconds(int team);

    /// <summary>Stock's dialog variables
    /// <c>bluescore</c>/<c>redscore</c> (OnTick, :643-679): the team's
    /// flag captures while <c>tf_flag_caps_per_round</c> is up, else
    /// the team's plain score.</summary>
    int Captures(int team);

    /// <summary>Stock's <c>rounds</c> dialog variable
    /// (<c>tf_flag_caps_per_round</c>, :661): the caps a round is
    /// played to; 0 is straight score and hides the target.</summary>
    int CapsPerRound { get; }

    /// <summary>Stock's <c>CTFArrowPanel::GetAngleRotation</c>
    /// (:167-207): the team's flag's bearing from the local view, in
    /// degrees, 0 straight ahead, positive to the right. A stolen flag
    /// reads its carrier's live origin; no view, no angle (0).</summary>
    float Bearing(int team);

    /// <summary>Whether the local player carries the team's flag —
    /// stock's <c>pLocalPlayer->HasItem()</c> arm of
    /// <c>UpdateStatus</c> (:779-860), which hides both squares for
    /// the carried image.</summary>
    bool CarriedByLocal(int team);

    /// <summary>Stock's capture-point arrow while carrying
    /// (<c>m_pCapturePoint</c>, :840-858): the local team's zone's
    /// bearing from the local view, same units as
    /// <see cref="Bearing"/>.</summary>
    float ZoneBearing { get; }

    /// <summary>Stock's no-arrow compass arm (<c>CTFArrowPanel::OnTick</c>'s
    /// observer check, tf_hud_flagstatus.cpp:127-159): the local player's
    /// observer mode is <c>OBS_MODE_IN_EYE</c> and the player watched is
    /// this team's flag's carrier — the compass then draws the disc without
    /// the needle, the seen carrier's own hands being the pointer. False
    /// while playing, in the other modes, or watching anyone else.</summary>
    bool SpectatingCarrier(int team);
}

/// <summary>
/// The notification the HUD's panel shows (stock's
/// <c>CHudNotificationPanel</c>, tf_hud_notification_panel.cpp): the
/// HudNotify user message now on show. One at a time — stock's panel
/// reloads its control settings per message, replacing the last
/// (MsgFunc_HudNotify, :96-135).
/// </summary>
public interface ITFNotifications
{
    /// <summary>Whether a notification is on show — stock's
    /// <c>m_flFadeTime</c> still ahead of now (OnTick's fade,
    /// :96-135).</summary>
    bool Present { get; }

    /// <summary>Stock's <c>HudNotification_t</c> byte
    /// (tf_shareddefs.h:1731-1744).</summary>
    byte Type { get; }

    /// <summary>Seconds before the notification fades
    /// (<c>tf_hud_notification_duration</c>).</summary>
    float SecondsLeft { get; }

    /// <summary>The local player's raw team (2 RED, 3 BLU): stock picks
    /// the notification's team variant with it
    /// (<c>GetNotificationByType</c>'s <c>bOnBlueTeam</c>, :331-338).</summary>
    int LocalTeam { get; }

    /// <summary>The copy to draw: stock's
    /// <c>resource/UI/notifications/notify_*.res</c> per type and team
    /// (:342-434) as this port's token.</summary>
    string Token { get; }
}


/// <summary>What the team menu can ask for (stock's CTeamMenu buttons,
/// <c>COMMAND_MENU_BLUE..COMMAND_MENU_CANCEL_SPEC</c>,
/// game/client/tf/vgui/tf_teammenu.cpp): join a team, be assigned to the
/// smaller one, or spectate.</summary>
public enum TFTeamChoice
{
    Red,
    Blue,
    Auto,
    Spectate,
}

/// <summary>
/// The TF2 team menu's data (stock's CTeamMenu,
/// game/client/tf/vgui/tf_teammenu.cpp): the two teams' head counts
/// (stock's <c>bluecount</c>/<c>redcount</c> dialog variables,
/// updated from <c>Get_Number_Players</c>, :210-218), the local
/// player's team, and whether spectating is open at all
/// (<c>mp_allowspectators</c>). Teams are the raw TF team numbers
/// (2 RED, 3 BLU); the screen names them itself.
/// </summary>
public interface ITFTeamMenuSource
{
    /// <summary>Players on RED — the count RED's card shows.</summary>
    int RedPlayers { get; }

    /// <summary>Players on BLU — the count BLU's card shows.</summary>
    int BluePlayers { get; }

    /// <summary>The local player's team as the raw number (2 RED, 3 BLU);
    /// 1 or below is unassigned or spectator, and the menu then marks
    /// nobody and hides Cancel (stock shows it only once a team is
    /// chosen, tf_teammenu.cpp:165).</summary>
    int CurrentTeam { get; }

    /// <summary><c>mp_allowspectators</c>: stock adds the Spectate
    /// button only when it is set (tf_teammenu.cpp:127-130).</summary>
    bool AllowsSpectators { get; }
}

/// <summary>What the team menu's clicks do (stock's CTeamMenu
/// <c>OnCommandSelected</c>): the adapter sends the named
/// <c>jointeam</c> and closes the menu. The class panel afterwards is
/// the server's to show (tf_player.cpp:6450-6455) — a client chain
/// could not see a refused join.</summary>
public interface ITFTeamMenuActions
{
    /// <summary>The card or button was clicked. Stock sends nothing for
    /// the team you are already on (the same-team arm); either way the
    /// menu closes, and the next panel — class or team again — is the
    /// server's push.</summary>
    void Choose(TFTeamChoice choice);

    /// <summary>Cancel: hides the menu without choosing (stock's
    /// Cancel only ever being visible with a team already chosen).</summary>
    void Cancel();
}

/// <summary>What TF2's text window's clicks do (stock's
/// <c>CTFTextWindow::OnCommand</c>, tf_textwindow.cpp:220-229): the OK
/// button.</summary>
public interface ITFTextWindowActions
{
    /// <summary>OK: stock hides the window and raises the map info panel
    /// (<c>PANEL_MAPINFO</c>, tf_shareddefs.h:164), by the panel's name
    /// as stock's viewport shows it.</summary>
    void Okay();
}

/// <summary>
/// The TF2 map info menu's data (stock's <c>CTFMapInfoMenu</c>,
/// game/client/tf/vgui/tf_mapinfomenu.cpp): the map's display name and
/// game type (the <c>%mapname%</c>/<c>%gamemode%</c> dialog variables,
/// :125, :580), its briefing text (<c>LoadMapPage</c>'s description
/// arms, :340-563), the menu-photo material the .res's MapImage shows
/// (<c>VGUI/maps/menu_photos_&lt;map&gt;</c>, :349-368), and the local
/// team the Back/Continue arms key on (<c>CheckBackContinueButtons</c>,
/// :230-250).
/// </summary>
public interface ITFMapInfoSource
{
    /// <summary>The map's display name (stock's <c>%mapname%</c> —
    /// <c>GetMapDisplayName</c>, tf_gamerules.cpp:18974: the prefix
    /// stripped, underscores for spaces, TF2's LOUD name).</summary>
    string MapName { get; }

    /// <summary>The game type's display text (stock's <c>%gamemode%</c> —
    /// the localised <c>GetMapType</c>, tf_gamerules.cpp:19111); the empty
    /// string when the map names none.</summary>
    string GameMode { get; }

    /// <summary>The map's briefing text (stock's <c>LoadMapPage</c>
    /// description); the empty string when the map has none.</summary>
    string Briefing { get; }

    /// <summary>The menu-photo material to ask the library for (stock's
    /// <c>LoadMapPage</c> image, vgui/maps/menu_photos_&lt;map&gt;); null
    /// when the map has none.</summary>
    string? Photo { get; }

    /// <summary>The local player's team as the raw number (2 RED, 3 BLU);
    /// below 2 is unassigned or spectator — stock's Back shows and the
    /// CONTINUE runs the team setup only for
    /// <c>TEAM_UNASSIGNED</c> (:230-250, :288-298).</summary>
    int Team { get; }
}

/// <summary>What the map info menu's clicks do (stock's
/// <c>CTFMapInfoMenu::OnCommand</c>, tf_mapinfomenu.cpp:257-327):
/// CONTINUE and BACK.</summary>
public interface ITFMapInfoActions
{
    /// <summary>CONTINUE: stock hides the menu and — with no team yet —
    /// runs the team setup (<c>team_ui_setup</c>, :288-298, the panel
    /// the text window's OK used to go straight to). The intro-movie arm
    /// (:274-279) is the video system's, deferred.</summary>
    void Continue();

    /// <summary>BACK: stock hides the menu and raises the text window
    /// again (<c>PANEL_INFO</c>, :261-269) — only ever reachable without
    /// a team, which is the screen's visibility gate too.</summary>
    void Back();
}

/// <summary>
/// What the class menu shows for one class (stock's per-class tiles,
/// <c>m_pClassButtons</c> and their count labels,
/// game/client/tf/vgui/tf_classmenu.cpp): the raw TF_CLASS number
/// (1 Scout .. 9 Engineer — the number a <c>joinclass</c> names by its
/// raw script name), the display name, how many of the team already
/// play it (stock's per-class counts, which exclude the local player,
/// :1492-1498), whether it is the class the local player is on, and
/// whether it is full — the count at the limit, the state stock's
/// <c>Go</c> refuses to send for (:1216-1275).
/// </summary>
public readonly record struct TFClassChoice(int Number, string Name, int Players, bool Current, bool LimitHit);

/// <summary>
/// The TF2 class menu's data (stock's CTFClassMenu,
/// game/client/tf/vgui/tf_classmenu.cpp): the local player's team, the
/// nine classes as the tiles read them, and whether the player has a
/// class at all — stock shows Cancel only once one is chosen
/// (:1044-1081, a forced first pick cannot be cancelled).
/// </summary>
public interface ITFClassMenuSource
{
    /// <summary>The local player's team as the raw number (2 RED, 3 BLU);
    /// below 2 is unassigned or spectator, and the menu then blanks the
    /// counts (stock's spectator arm, :1446-1449) and hides Cancel.</summary>
    int Team { get; }

    /// <summary>What one class tile shows, by its raw TF_CLASS number
    /// (1 Scout .. 9 Engineer).</summary>
    TFClassChoice Choice(int classNumber);

    /// <summary>Whether the local player is on a class: the gate on
    /// Cancel, and on the <c>changeclass</c> command's show of the
    /// panel (stock's viewport opens it for a player on a game team,
    /// tf_viewport.cpp:193-211).</summary>
    bool HasClass { get; }
}

/// <summary>What the class menu's clicks do (stock's CTFClassMenu
/// <c>Go</c> and <c>Join_Class</c>, :1216-1275, :1330-1339): the named
/// <c>joinclass</c>, the random arm, and the closing clicks — and the
/// visibility the menu tells the server of (<c>SetVisible</c>'s
/// <c>menuopen</c>/<c>menuclosed</c>, :1174-1211).</summary>
public interface ITFClassMenuActions
{
    /// <summary>The menu was shown (any of its panel names): stock's
    /// show arm tells the server <c>menuopen</c> (:1182).</summary>
    void Opened();

    /// <summary>The menu was closed, whichever way — the host's word for
    /// stock's hide arm (<c>menuclosed</c>, :1195-1199): said once per
    /// close, and only after a show said <c>menuopen</c>.</summary>
    void Closed();

    /// <summary>A class tile was clicked, by its raw TF_CLASS number.
    /// Stock's <c>Go</c> sends nothing when the class is at its limit
    /// (the <c>CanPlayerChooseClass</c> arm, :1223) and the named
    /// <c>joinclass</c> otherwise, closing the menu.</summary>
    void Choose(int classNumber);

    /// <summary>The random button: the named <c>joinclass random</c> —
    /// the server picks the class, the menu only names the arm.</summary>
    void ChooseRandom();

    /// <summary>Cancel: hides the menu without choosing (stock's Cancel
    /// only ever being visible with a class already chosen).</summary>
    void Cancel();

    /// <summary>Edit Loadout (stock's <c>openloadout</c>,
    /// tf_classmenu.cpp:1307-1320): raises the loadout screen on the class
    /// the menu is on and asks for that class's reply.</summary>
    void EditLoadout();

    /// <summary>Reset (stock's <c>resetclass</c> arm,
    /// tf_classmenu.cpp:1300-1306): clears the class's every slot — the
    /// <c>tf2_equip &lt;class&gt; all none</c> line. Stock's arm is
    /// highlander-only and its button ships hidden; ours works in every
    /// mode (docs/divergences.md).</summary>
    void ResetLoadout();
}

/// <summary>
/// The round timer for the HUD's timer panel: stock's
/// <c>CTFHudTimeStatus</c> reads the active <c>team_round_timer</c>
/// (tf_time_panel.cpp). Everything reads false or zero while no timer
/// is up to show.
/// </summary>
public interface ITFRoundTimer
{
    /// <summary>Whether an active <c>team_round_timer</c> exists to show —
    /// the one the client's election picks the way stock's
    /// <c>CTFGameRules::GetActiveRoundTimer</c> does.</summary>
    bool Present { get; }

    /// <summary>Pre-round setup: the timer's state is
    /// <c>RT_STATE_SETUP</c> (teamplay_round_timer.cpp).</summary>
    bool InSetup { get; }

    /// <summary>Overtime: the round's clock has run out. Stock asks
    /// <c>TFGameRules()-&gt;InOvertime()</c> (tf_time_panel.cpp:512), a
    /// flag not replicated to the client, so the adapter derives it;
    /// the docs/divergences.md row names the reason.</summary>
    bool Overtime { get; }

    /// <summary>Whether the timer is paused (<c>m_bTimerPaused</c>).</summary>
    bool IsPaused { get; }

    /// <summary>Whether the clock counts down
    /// (<c>m_bShowTimeRemaining</c>, teamplay_round_timer.cpp) —
    /// otherwise it counts up from zero (the choice at
    /// tf_time_panel.cpp:696).</summary>
    bool ShowTimeRemaining { get; }

    /// <summary>The seconds on the clock, floored
    /// (<c>GetTimeRemaining</c>, teamplay_round_timer.cpp:377-430);
    /// may run negative once the round has overrun, and is 0 with
    /// nothing to show.</summary>
    int SecondsLeft { get; }

    /// <summary>The round's whole-second length as the timer states it
    /// (<c>GetTimerMaxLength</c>, same file); 0 when it states none.</summary>
    int TotalSeconds { get; }

    /// <summary>The local player's team number — what stock's
    /// <c>SetTeamBackground</c> picks the panel's art by
    /// (tf_time_panel.cpp:327-356, red for TF_TEAM_RED, blue otherwise);
    /// 0 with no local player.</summary>
    int Team { get; }

    /// <summary>Stock's <c>TFGameRules()-&gt;InStalemate()</c>: the round
    /// ended with no winner (the sudden-death band, SetExtraTimePanels
    /// tf_time_panel.cpp:489-508). Arena mode is stock's one exclusion and
    /// this build has none.</summary>
    bool Stalemate { get; }

    /// <summary>Stock's <c>TFGameRules()-&gt;IsInWaitingForPlayers()</c>:
    /// the pre-game wait whose band hides the setup one
    /// (tf_time_panel.cpp:573-589).</summary>
    bool WaitingForPlayers { get; }

    /// <summary>Whether the active timer is a stop-watch
    /// (<c>m_bStopWatchTimer</c>): stock hides the progress bar over one
    /// (tf_time_panel.cpp:447-452).</summary>
    bool StopWatch { get; }

    /// <summary>Stock's <c>TFGameRules()-&gt;GetTimeLeft()</c>
    /// (teamplayroundbased_gamerules.cpp:1226-1239): the seconds of the
    /// server's own <c>mp_timelimit</c> clock left, floored, never below
    /// zero — what the server-time band counts down
    /// (tf_time_panel.cpp:741-808); 0 also names its change-on-round-end
    /// arm.</summary>
    int ServerTimeLeft { get; }

    /// <summary>Stock's <c>nServerTimeLimit = mp_timelimit * 60</c>
    /// (tf_time_panel.cpp:744): the server limit in seconds, 0 while the
    /// map has none — the band's gate.</summary>
    int ServerTimeLimit { get; }

    /// <summary>Stock's <c>tf_hud_show_servertimelimit</c>
    /// (tf_hud_objectivestatus.cpp:42): whether the player asked the
    /// server-time band to show at all.</summary>
    bool ShowServerLimit { get; }
}

/// <summary>
/// The medic's charge meter (tf_hud_mediccharge.cpp): the ÜberCharge meter
/// stock reads off the medigun — the level (GetChargeLevel, OnTick :226), the
/// release (IsReleasingCharge, :272) and the beam's patient (m_hHealTarget).
/// Stock's ShouldDraw (:147-170) is a live medic whose active weapon is the
/// medigun, the bonesaw, or a syringe gun with an ÜberCharge ammo spend; the
/// bonesaw and the syringe then read the medigun's own fields (OnTick
/// :190-197), so the meter's reads are the medic's, not the hands'.
/// </summary>
public interface ITFMedicCharge
{
    /// <summary>Stock's ShouldDraw: the meter has a live medic with a
    /// medigun-shaped weapon to read.</summary>
    bool Present { get; }

    /// <summary>CWeaponMedigun's m_flChargeLevel, 0..1 (the percent dialog
    /// variable is the value times 100, OnTick :258).</summary>
    float Charge { get; }

    /// <summary><c>IsReleasingCharge</c>: the meter is draining — the ready
    /// pulse stops while it runs (:269-277).</summary>
    bool Releasing { get; }

    /// <summary>The beam's patient's shown name (m_hHealTarget through
    /// g_PR's GetPlayerName), or the empty string while the beam is down.</summary>
    string HealTarget { get; }
}

/// <summary>
/// The spy's meters (tf_hud_itemeffectmeter.cpp, tf_hud_disguise_status.cpp):
/// the cloak watch's charge (the item-effect meter over m_flCloakMeter,
/// GetProgress :513-519) and the disguise status (CDisguiseStatus::ShouldDraw
/// :131-157, InCond TF_COND_DISGUISED). The disguise's fields stay stock's
/// words (m_nDisguiseTeam) and the non-localized class name — this contract
/// assembly compiles no TF2.Shared source of its own.
/// </summary>
public interface ITFSpy
{
    /// <summary>The cloak meter draws: a live spy (the watch is the class's
    /// own; stock's IsEnabled gates on owning it, CTFWeaponInvis).</summary>
    bool CloakPresent { get; }

    /// <summary>The cloak charge, 0..1 (m_flCloakMeter / 100, :516).</summary>
    float Cloak { get; }

    /// <summary>The disguise status draws (m_Shared.InCond(TF_COND_DISGUISED),
    /// tf_hud_disguise_status.cpp:138-145).</summary>
    bool Disguised { get; }

    /// <summary>The class the disguise shows (m_nDisguiseClass), the
    /// non-localized name; the empty string when the disguise names none.</summary>
    string DisguiseClassName { get; }

    /// <summary>The team the disguise shows (m_nDisguiseTeam): 2 RED,
    /// 3 BLU (tf_shareddefs.h), the panel maps the tone.</summary>
    int DisguiseTeam { get; }
}

/// <summary>
/// A player's class as the scoreboard shows it (stock's class column,
/// tf_clientscoreboard.cpp:1467-1490, read off <c>tf_player_manager</c>'s
/// <c>m_iClass</c>). The non-localized name, the empty string when the
/// player has none yet; whether a row may show it at all is the screen's
/// stock gate (the local team's, tf_clientscoreboard.cpp:1467-1468), not
/// the data's.
/// </summary>
public interface ITFScoreboardClasses
{
    /// <summary>The class of the carrier owned by <paramref name="playerIndex"/>
    /// (<c>g_TF_PR-&gt;GetPlayerClass</c>), or the empty string when there is
    /// no carrier or no class chosen.</summary>
    string ClassNameOf(int playerIndex);
}

/// <summary>
/// How many players a row is dominating (stock's domination-count column,
/// <c>g_TF_PR-&gt;GetActiveDominations</c>, tf_clientscoreboard.cpp:1335,
/// read off <c>tf_player_manager</c>'s <c>m_iActiveDominations</c>).
/// </summary>
public interface ITFScoreboardDominations
{
    /// <summary>The active domination count of <paramref name="playerIndex"/>;
    /// 0 when there is no carrier or the count is none. The screen clamps to
    /// the icon table's span as stock does (SCOREBOARD_DOMINATION_ICONS - 1,
    /// tf_shareddefs.h:638).</summary>
    int DominationsOf(int playerIndex);

    /// <summary>Whether <paramref name="playerIndex"/> dominates the local
    /// player — the row's nemesis icon (tf_clientscoreboard.cpp:1507-1513,
    /// through stock's client arm over the local player's own data,
    /// tf_player_shared.cpp:7748-7751). False with no local carrier.</summary>
    bool DominatesLocal(int playerIndex);

    /// <summary>Whether the local player dominates <paramref name="playerIndex"/>
    /// — the row's dominated icon (tf_clientscoreboard.cpp:1514-1518).
    /// False with no local carrier.</summary>
    bool DominatedByLocal(int playerIndex);
}

/// <summary>
/// One player's details grid, stock's LocalPlayerStatsPanel values
/// (UpdatePlayerDetails, tf_clientscoreboard.cpp:1880-1964) minus the deaths
/// — stock reads those off <c>g_TF_PR</c> too, and this screen takes them
/// from the row it already shows. Every count is the round's.
/// </summary>
/// <param name="Kills">The score the list's column shows
/// (<c>g_TF_PR-&gt;GetPlayerScore</c>, :1883).</param>
/// <param name="Assists"><c>m_Shared.GetKillAssists</c>, :1893.</param>
/// <param name="Destruction"><c>GetBuildingsDestroyed</c>, :1898.</param>
/// <param name="Captures"><c>GetCaptures</c>, :1903.</param>
/// <param name="Defenses"><c>GetDefenses</c>, :1908.</param>
/// <param name="Dominations"><c>GetDominations</c>, :1913.</param>
/// <param name="Revenge"><c>GetRevenge</c>, :1918.</param>
/// <param name="Healing"><c>GetHealPoints</c>, :1923.</param>
/// <param name="Invulns"><c>GetInvulns</c>, :1928.</param>
/// <param name="Teleports"><c>GetTeleports</c>, :1933.</param>
/// <param name="Headshots"><c>GetHeadshots</c>, :1938.</param>
/// <param name="Backstabs"><c>GetBackstabs</c>, :1943.</param>
/// <param name="Bonus"><c>GetBonusPoints</c>, :1948.</param>
/// <param name="Support"><c>CalcPlayerSupportScore</c>'s client arm — damage
/// assist + healing assist + damage blocked + bonus × 25
/// (tf_gamerules.cpp:17072-17075), :1954.</param>
/// <param name="Damage"><c>g_TF_PR-&gt;GetDamage</c>, :1960.</param>
public readonly record struct TFScoreboardPlayerDetails(
    int Kills, int Assists, int Destruction, int Captures, int Defenses, int Dominations,
    int Revenge, int Healing, int Invulns, int Teleports, int Headshots, int Backstabs,
    int Bonus, int Support, int Damage);

/// <summary>
/// The details grid's counts for a player, off the live
/// <c>tf_player_manager</c>; all zeros when it has not arrived.
/// </summary>
public interface ITFScoreboardDetails
{
    TFScoreboardPlayerDetails Of(int playerIndex);
}

/// <summary>
/// The death screen's reads (stock's <c>CTFFreezePanel</c>,
/// tf_hud_freezepanel.cpp): the local player's last death — its
/// <c>TF_DEATH_*</c> flags and the killer's row, snapshotted as the death
/// arrived the way stock's panel reads the killer once
/// (<c>SetKiller</c>, :353-404). <see cref="Showing"/> falls when the local
/// player lives again, stock's <c>hide_freezepanel</c> (the respawn, a
/// divergence: stock shows on entering the freeze cam, this build at the
/// death).
/// </summary>
public interface ITFDeathScreen
{
    /// <summary>A death of the local player is up: the panel's gate.</summary>
    bool Showing { get; }

    /// <summary>The death's <c>TF_DEATH_*</c> flags (the player_death
    /// event's <c>death_flags</c>): domination 0x1, revenge 0x4 — the bits
    /// that arm the nemesis panel (tf_hud_freezepanel.cpp:234-246).</summary>
    int DeathFlags { get; }

    /// <summary>Whether a player killed us: the panel's gate, since a
    /// suicide's or the world's death never enters the freeze cam
    /// (m_hObserverTarget NULL, tf_player.cpp:12487-12490, :13956-13968) and
    /// draws nothing.</summary>
    bool HasKiller { get; }

    /// <summary>The killer's shown name (<c>g_PR-&gt;GetPlayerName</c>, :404).</summary>
    string KillerName { get; }

    /// <summary>The killer's team, the raw TF number (2 RED, 3 BLU): the
    /// background's arm (:580-584) and the class image's.</summary>
    int KillerTeam { get; }

    /// <summary>The killer's class, non-localized (the class image's key).</summary>
    string KillerClass { get; }

    /// <summary>The killer's health at the death (<c>pKiller-&gt;GetHealth()</c>,
    /// :362) — 0 when they are dead (:363-366).</summary>
    int KillerHealth { get; }

    /// <summary>The killer's max health (<c>pKiller-&gt;GetMaxHealth()</c>, :369).</summary>
    int KillerMaxHealth { get; }

    /// <summary>The killer's buffed max (<c>GetMaxBuffedHealth</c>, :355-360).</summary>
    int KillerMaxBuffedHealth { get; }

    /// <summary>Whether the killer still lives (<c>pKiller-&gt;IsAlive()</c>):
    /// the <c>_Dead</c> label arm (:383-401).</summary>
    bool KillerAlive { get; }
}

/// <summary>
/// The respawn countdown (stock's <c>UpdateReinforcements</c>,
/// tf_spectatorgui.cpp:343-447): when the local player's next wave lets them
/// out, and the round's two arms that name it. The stalemate and team-win
/// states are the gamerules round state this build carries on the player
/// resource (the <c>m_iRoundState</c> the gamerules proxy would).
/// </summary>
public interface ITFRespawnCountdown
{
    /// <summary>A wave is up: <c>m_flNextRespawnTime</c> non-zero (:393-398).</summary>
    bool HasWave { get; }

    /// <summary>Whole seconds until it (<c>flNextRespawn - gpGlobals-&gt;curtime</c>, :400-401).</summary>
    int WaitSeconds { get; }

    /// <summary><c>InStalemate()</c>: no respawning in sudden death (:363-372).</summary>
    bool Stalemate { get; }

    /// <summary><c>GR_STATE_TEAM_WIN</c>: wait for the next round (:374-378).</summary>
    bool TeamWin { get; }
}

/// <summary>
/// The engineer's build menu data (stock's CHudMenuEngyBuild,
/// game/client/tf/tf_hud_menu_engy_build.cpp): whether the build PDA is
/// deployed — stock's show arm, the PDA's OnDataChanged setting
/// <c>m_bBuilderEquipped</c> once it is WEAPON_IS_ACTIVE
/// (tf_weapon_pda.cpp:157-180) — the player's metal
/// (OnTick's <c>GetAmmoCount(TF_AMMO_METAL)</c>, :533), the
/// <c>lastinv</c> binding the Cancel label names (SetVisible's dialog
/// variable, :612-619), and the per-building reads the OnTick state
/// machine asks (:535-581).
/// </summary>
public interface ITFBuildMenuSource
{
    /// <summary>Whether the build PDA is the active weapon: stock's
    /// m_bBuilderEquipped, set by the PDA's OnDataChanged and the gate
    /// CHudMenuEngyBuild shows under (tf_weapon_pda.cpp:157-180).</summary>
    bool BuilderEquipped { get; }

    /// <summary>The local player's metal
    /// (<c>GetAmmoCount(TF_AMMO_METAL)</c>,
    /// tf_hud_menu_engy_build.cpp:533).</summary>
    int Metal { get; }

    /// <summary>The Cancel sentence stock's CancelLabel shows — the
    /// #Hud_Menu_Build_Cancel token with <c>%lastinv%</c> replaced by the
    /// key the command is bound to (stock's SetDialogVariable("lastinv"),
    /// tf_hud_menu_engy_build.cpp:612-619; the resolve is the adapter's,
    /// once, because a describe method allocates nothing).</summary>
    string CancelLabel { get; }

    /// <summary>The slot's building price as stock's
    /// <c>CalculateObjectCost</c> answers it (OnTick,
    /// tf_hud_menu_engy_build.cpp:558), by the raw OBJ_* number
    /// (OBJ_DISPENSER 0, OBJ_TELEPORTER 1, OBJ_SENTRYGUN 2,
    /// tf_shareddefs.h:1368-1370) and its mode (MODE_TELEPORTER_ENTRANCE 0,
    /// MODE_TELEPORTER_EXIT 1, tf_shareddefs.h:1389-1390).</summary>
    int Cost(int building, int mode);

    /// <summary>Whether stock's <c>CanBuild(building, mode)</c> answers
    /// CB_CAN_BUILD (tf_hud_menu_engy_build.cpp:559, :562).</summary>
    bool CanBuild(int building, int mode);

    /// <summary>Whether the player's own object of (building, mode)
    /// exists and is not placing — the <c>GetObjectOfType</c> and
    /// <c>IsPlacing</c> pair of the already-built arm (:545, :562).</summary>
    bool Built(int building, int mode);
}

/// <summary>
/// The engineer's destroy menu data (stock's CHudMenuEngyDestroy,
/// game/client/tf/tf_hud_menu_engy_destroy.cpp): whether the destroy
/// PDA is deployed — the same show arm as the build menu's, the PDA's
/// OnDataChanged setting <c>m_bBuilderEquipped</c>
/// (tf_weapon_pda.cpp:157-180) — the <c>lastinv</c> binding the Cancel
/// label names (SetVisible's dialog variable, :360-367), and whether
/// the player's own object of a kind stands (OnTick's state machine,
/// :316-339).
/// </summary>
public interface ITFDestroyMenuSource
{
    /// <summary>Whether the destroy PDA is the active weapon: the
    /// m_bBuilderEquipped the menu shows under
    /// (tf_weapon_pda.cpp:157-180).</summary>
    bool DestroyerEquipped { get; }

    /// <summary>The Cancel sentence stock's CancelLabel shows — the
    /// build menu's token with <c>%lastinv%</c> replaced by the key the
    /// command is bound to (stock's SetDialogVariable,
    /// tf_hud_menu_engy_destroy.cpp:360-367; the resolve is the
    /// adapter's, once, because a describe method allocates nothing).</summary>
    string CancelLabel { get; }

    /// <summary>Whether the player's own object of (building, mode)
    /// exists and is not placing — the <c>pObj != NULL &amp;&amp;
    /// !pObj-&gt;IsPlacing()</c> arm of OnTick
    /// (tf_hud_menu_engy_destroy.cpp:316-339), by the raw OBJ_* number
    /// and its mode (tf_shareddefs.h:1368-1370, :1389-1390).</summary>
    bool Built(int building, int mode);
}

/// <summary>
/// What the destroy menu's choices do (stock's send arm and cancel
/// keys, tf_hud_menu_engy_destroy.cpp): a choice is the slot's
/// <c>destroy %d %d; lastinv</c> (:264). Stock's further gate — the
/// object without a sapper and not plasma-disabled (:259-266) — is the
/// server's to hold here; the screen only sends from a built slot.
/// </summary>
public interface ITFDestroyMenuActions
{
    /// <summary>A slot was chosen, by its raw OBJ_* number and mode —
    /// stock's <c>destroy %d %d; lastinv</c> and
    /// <c>engine-&gt;ClientCmd</c> (:259-266, :264).</summary>
    void Destroy(int building, int mode);

    /// <summary>Stock's cancel: lastinv (the build menu's own keys,
    /// tf_hud_menu_engy_build.cpp:425-430).</summary>
    void Cancel();
}

/// <summary>
/// The spy's disguise menu data (stock's CHudMenuSpyDisguise,
/// game/client/tf/tf_hud_menu_spy_disguise.cpp): whether the disguise
/// kit is deployed — the same show arm as the build menu's
/// (tf_weapon_pda.cpp:157-180) — the local player's team the menu arms
/// its shown team from (the spy_pda_reset event's arm, :607-629), and
/// the resolved hint sentences (FindToggleBinding's three dialog
/// variables, :453-476).
/// </summary>
public interface ITFDisguiseMenuSource
{
    /// <summary>Whether the disguise kit is the active weapon: the
    /// m_bBuilderEquipped the menu shows under
    /// (tf_weapon_pda.cpp:157-180).</summary>
    bool DisguiseKitEquipped { get; }

    /// <summary>The local player's team, the raw TF number (2 RED, 3
    /// BLU): what stock's spy_pda_reset arm shows the enemy of
    /// (:613-624). Below 2 is unassigned — the menu falls to stock's
    /// constructor default RED.</summary>
    int Team { get; }

    /// <summary>The toggle sentence — the #Hud_Menu_Spy_Minus_Toggle
    /// token with <c>%disguiseteam%</c> and <c>%reload%</c> replaced by
    /// the keys they are bound to (stock's SetDialogVariable,
    /// tf_hud_menu_spy_disguise.cpp:463-475; the resolve is the
    /// adapter's, once, because a describe method allocates nothing).</summary>
    string ToggleLabel { get; }

    /// <summary>The Cancel sentence — the build menu's token with
    /// <c>%lastinv%</c> (stock's CancelLabel names it,
    /// hudmenuspydisguise.res:122).</summary>
    string CancelLabel { get; }
}

/// <summary>
/// What the disguise menu's choices do (stock's SelectDisguise and the
/// cancel keys, tf_hud_menu_spy_disguise.cpp): a choice is the class
/// and the shown team in the interception's own naming — the enemy team
/// -1, the player's own -2 — because the menu's numbers do not match
/// the defines (c_tf_player.cpp:9637-9679).
/// </summary>
public interface ITFDisguiseMenuActions
{
    /// <summary>A tile was chosen, by its raw TF_CLASS number (1 Scout
    /// .. 9 Spy) and the shown team as the interception names it
    /// (stock's <c>disguise 2%d%d</c> retokenised,
    /// tf_hud_menu_spy_disguise.cpp:543-564).</summary>
    void Disguise(int classNumber, int team);

    /// <summary>Stock's cancel: lastinv (slot10 and KEY_0, :294-298,
    /// :395-398).</summary>
    void Cancel();
}

/// <summary>
/// What the build menu's choices do (stock's SendBuildMessage and the
/// cancel keys, tf_hud_menu_engy_build.cpp): a choice is the slot's
/// <c>build %d %d</c> (:475-476) and a cancel is <c>lastinv</c>
/// (:425-430, :389-394). Whether a choice sends at all is the screen's
/// CanBuild gate (:457-459), not the actions'.
/// </summary>
public interface ITFBuildMenuActions
{
    /// <summary>A slot was chosen, by its raw OBJ_* number and mode —
    /// stock's <c>Q_snprintf(szCmd, "build %d %d", iBuilding, iMode)</c>
    /// and <c>engine->ClientCmd</c> (:475-476).</summary>
    void Build(int building, int mode);

    /// <summary>Stock's cancel: KEY_0 and slot10 run <c>lastinv</c>
    /// (:425-430, :389-394).</summary>
    void Cancel();
}

/// <summary>
/// One line of the death notices, as stock's <c>DeathNoticeItem</c>
/// (hud_basedeathnotice.h:27-80) carries it after the TF decode: the names
/// and their raw teams (2 RED, 3 BLU), the icon resolved through the
/// custom-kill overrides (<c>OnGameEvent</c>, tf_hud_deathnotice.cpp:975-1039)
/// onto the <c>d_</c> + weapon name (<c>FireGameEvent</c>,
/// hud_basedeathnotice.cpp:513-516), whether the kill was a crit
/// (<c>damagebits</c> &amp; DMG_CRITICAL — <c>d_crit</c> behind the icon,
/// hud_basedeathnotice.cpp:497-501), whether it was a suicide (no killer
/// name, :517-520), and whether the local player was involved (the lighter
/// background and the inverted icon, :486-490). <paramref name="LocalIcon"/>
/// is that inverted art's name — <c>dneg_</c> + the suffix, stock's
/// <c>GetIcon</c> kDeathNoticeIcon_Inverted arm (:894-913), built once per
/// death so the screen asks the library by name and never builds one. The
/// empty killer means a suicide; the empty icon means stock's skull
/// fallback.
///
/// <para>
/// <paramref name="Info"/> is the additional text drawn after the victim
/// (stock's <c>wzInfoText</c>, hud_basedeathnotice.cpp:303-314): on a
/// self-inflicted death the phrase the localiser answers for the fall
/// (:527-531) or the suicide custom kind (tf_hud_deathnotice.cpp:1041-1051),
/// as a <c>#Token</c> the frame resolves. Empty means stock's no text.
/// </para>
/// </summary>
public readonly record struct TFKillFeedLine(
    string Killer, int KillerTeam, string Victim, int VictimTeam,
    string Icon, string LocalIcon, bool Crit, bool Suicide, bool LocalInvolved,
    string Info = "");

/// <summary>
/// The death notices the kill feed screen draws (stock's
/// <c>CTFHudDeathNotice</c>, tf_hud_deathnotice.cpp over
/// hud_basedeathnotice.cpp), from the server's player_death events. The
/// queue's rules — hud_deathnotice_time, MaxDeathNotices, the local
/// player's lines kept twice as long and culled last — are the adapter's;
/// the screen only draws what the count answers now.
/// </summary>
public interface ITFKillFeed
{
    /// <summary>The lines as of now, oldest first (stock's
    /// <c>RetireExpiredDeathNotices</c> before the read,
    /// hud_basedeathnotice.cpp:129-132).</summary>
    int LineCount { get; }

    /// <summary>Line <paramref name="i"/>, 0 the oldest.</summary>
    TFKillFeedLine Line(int i);
}

/// <summary>What the target ID's data line holds (stock's
/// <c>GetTargetIDDataString</c> arms, c_tf_player.cpp:9748-9829): the
/// disguise reveal — a disguised spy the local player can see through — or
/// the medic's charge read. The plain-player arm is the empty one.</summary>
public enum TFTargetIdLineKind
{
    /// <summary>No data line (the label hides, :1089-1101).</summary>
    None,

    /// <summary>The disguise line (<c>#TF_playerid_friendlyspy_disguise</c>,
    /// :9768-9779): "Disguised as" + the alignment word + the class.</summary>
    Disguise,

    /// <summary>The medic's charge (<c>#TF_playerid_mediccharge</c>,
    /// :9794-9804): "ÜberCharge:" + the percent.</summary>
    MedicCharge,
}

/// <summary>
/// The data line's pieces, as the screen lays them out: the kind, the
/// disguise's class name (<c>g_aPlayerClassNames_NonLocalized</c>) and
/// whether the alignment word is "Enemy" (the line's %s1 — the disguise's
/// team is not the spy's own, :9761-9762), or the charge percent (the line's
/// %s1, the level times 100, :9789).
/// </summary>
public readonly record struct TFTargetIdLine(TFTargetIdLineKind Kind, string ClassName, bool Enemy, int Charge);

/// <summary>
/// One frame of the target ID (stock's <c>CTargetID</c>,
/// tf_hud_target_id.cpp): who the view's centre ray stops on. Stock's
/// validity gate (<c>IsValidIDTarget</c> :340-456) names only the players
/// the ID may show — the same real or displayed team, or a local spy (the
/// medic and heavy content arms of :806-810 sit behind a validity gate that
/// never lets them through for an enemy, so the arms stay with the code) —
/// and a cloaked target is none (<c>IsStealthed</c> :414-419, :771). The
/// name (<c>#TF_playerid_sameteam</c>'s %s1) and the health cross
/// (<c>SpectatorGUIHealth</c>, targetid.res:121-138 over
/// spectatorguihealth.res) show by the :806-810 gate; the data line is
/// <see cref="TFTargetIdLine"/>'s. The team the panel names is the
/// disguise's for a spy whose real team is not the local's (UpdateID
/// :776-779) and the real one otherwise. Stock's disguised-enemy arm — the
/// disguise TARGET's name and health (m_iDisguiseHealth,
/// tf_player_shared.h:956) — has no networked fields to read; the carrier
/// networks the class and team only (the divergence TFHudSpy records), so
/// the panel shows the spy's own name and health until that lane lands.
/// </summary>
public interface ITFTargetId
{
    /// <summary>Whether the panel draws: the ray stopped on a valid ID
    /// target and something would show in it (an empty name with no data
    /// line and no health is stock's hidden labels, :1073-1101).</summary>
    bool Present { get; }

    /// <summary>The name shown (the engine's player info, stock's
    /// <c>GetPlayerName</c>); the empty string when the gate hides it.</summary>
    string Name { get; }

    /// <summary>The team the panel names: 2 RED, 3 BLU, stock's
    /// <c>teambg_2</c>/<c>teambg_3</c> selectors (targetid.res:19-22).</summary>
    int Team { get; }

    /// <summary>Stock's bShowHealth: the health cross draws. Max health
    /// rides with it (the g_TF_PR arm, :818-838 — without a resource row
    /// stock hides the cross, :835-838).</summary>
    bool ShowsHealth { get; }

    /// <summary>The aimed player's health, as the player networks it.</summary>
    int Health { get; }

    /// <summary>The aimed player's maximum (<c>g_TF_PR-&gt;GetMaxHealth</c>,
    /// :831).</summary>
    int MaxHealth { get; }

    /// <summary>The data line under the name (stock's %targetdata%).</summary>
    TFTargetIdLine Line { get; }
}

/// <summary>The building a status line names, the hud_obj_*.res items'
/// four kinds (the teleporter's two rows part by <c>GetMode</c>).</summary>
public enum TFBuildingLineKind
{
    /// <summary><c>obj_sentrygun</c>, the tall 60-unit item.</summary>
    Sentry,

    /// <summary><c>obj_dispenser</c>.</summary>
    Dispenser,

    /// <summary><c>obj_teleporter</c> mode 0, the entrance's recharge row.</summary>
    TeleporterEntrance,

    /// <summary><c>obj_teleporter</c> mode 1.</summary>
    TeleporterExit,
}

/// <summary>One building of the status panel, as stock's
/// <c>CBuildingStatusItem</c> reads its object: the .res item's switches and
/// bar fills, all from the carrier's row. <see cref="Placing"/> is
/// <c>m_bPlacing</c> (the not-built label), <see cref="Building"/>
/// construction (<c>BuildingPanel</c> plus its progress), <see cref="Sapped"/>
/// the tray's sapper icon. The bars read the pairs: health over max health,
/// shells over max shells (the sentry), metal over the dispenser's pool.
/// The per-object rows: <see cref="Kills"/> and <see cref="Assists"/> the
/// sentry's score label, <see cref="Recharge"/> the entrance's charge
/// percentage while it recharges or -1 (stock's ChargingPanel against
/// FullyChargedPanel), <see cref="TimesUsed"/> the trips both teleporter
/// ends count, <see cref="Rockets"/> the level 3 sentry's pod against
/// <see cref="MaxRockets"/>.</summary>
public readonly record struct TFBuildingStatusLine(
    TFBuildingLineKind Kind, int Level, int Team, bool Placing, bool Building, bool Sapped,
    int BuildProgress, int Health, int MaxHealth, int Shells, int MaxShells, int Ammo,
    int Kills, int Assists, int Recharge, int TimesUsed, int Rockets, int MaxRockets);

/// <summary>
/// The engineer's building status (stock's <c>CBuildingStatusContainer</c>
/// over its per-object <c>CBuildingStatusItem</c>s,
/// tf_hud_building_status.cpp): the local player's own buildings, stacked
/// from the container's top-left. The spy's arm — stock's
/// <c>BuildingStatus_Spy</c> shows the buildings carrying a spy's sapper —
/// is the same list read for sapped rows whose team is not the local's
/// (the container's OnTick arm, :319-426).
/// </summary>
public interface ITFBuildingStatus
{
    /// <summary>How many buildings the panel draws, oldest placed first.</summary>
    int Count { get; }

    /// <summary>Building <paramref name="i"/>, 0 the first.</summary>
    TFBuildingStatusLine Building(int i);
}

/// <summary>One hit the local player took, as stock's
/// <c>CHudDamageIndicator</c> keeps them (tf_hud_damageindicator.cpp):
/// <see cref="Amount"/> the health taken (stock's <c>iScale</c>, capped at
/// its MaximumDamage 100 — the quad's size and the show time both remap off
/// it), <see cref="Yaw"/> where it came from at the frame's view (degrees
/// clockwise from straight ahead, the damage panel's convention),
/// <see cref="At"/> when on the game clock and <see cref="Life"/> how long
/// it shows.</summary>
public readonly record struct TFDamageTaken(int Amount, float Yaw, float At, float Life);

/// <summary>The local player's hits still showing, stock's
/// <c>m_vecDamages</c>: oldest first, the expired ones already gone, a new
/// level (the clock going back) purging the rest.</summary>
public interface ITFDamageHistory
{
    /// <summary>How many hits are alive.</summary>
    int Count { get; }

    /// <summary>Hit <paramref name="i"/>, 0 the oldest.</summary>
    TFDamageTaken Damage(int i);
}

/// <summary>One dealt-damage number, stock's account_delta_t of
/// ACCOUNT_DELTA_DAMAGE (tf_hud_account.cpp): <see cref="Amount"/> the
/// post-armour damage the local player dealt (player_hurt's
/// <c>damageamount</c>), <see cref="Crit"/> whether the hit was a critical
/// (the large font), <see cref="At"/> when on the game clock. Stock's item
/// also carries the victim's world position (m_bWorldSpace, :630-639); the
/// read holds it beside the number and answers the paint's projection
/// (<see cref="ITFCombatText.Screen"/>).</summary>
public readonly record struct TFCombatNumber(int Amount, bool Crit, float At)
{
    /// <summary>Stock's CPanelAnimationVar m_flDeltaLifetime
    /// (tf_hud_account.cpp:225): how long one number shows.</summary>
    public const float DeltaLifetime = 2f;

    /// <summary>How far the number floats up over its life, m_nHEnd minus
    /// m_nHStart (tf_hud_account.cpp:638).</summary>
    public const float Rise = 32f;
}

/// <summary>The local player's dealt damage still showing, stock's
/// <c>CAccountPanel</c>'s delta items (tf_hud_account.cpp): oldest first,
/// the expired ones already gone, a new level (the clock going back)
/// purging the rest.</summary>
public interface ITFCombatText
{
    /// <summary>How many numbers are alive.</summary>
    int Count { get; }

    /// <summary>Number <paramref name="i"/>, 0 the oldest.</summary>
    TFCombatNumber Number(int i);

    /// <summary>Number <paramref name="i"/>'s hud position in pixels, stock's
    /// paint (tf_hud_account.cpp:1120-1131): the world anchor risen by
    /// <paramref name="lifetimePercent"/> of <see cref="TFCombatNumber.Rise"/>,
    /// through the engine's projection. False when the number carries no
    /// anchor or the projection refuses — GetVectorInHudSpace's arm, the
    /// draw's skip.</summary>
    bool Screen(int i, float lifetimePercent, out System.Numerics.Vector2 hud);
}

/// <summary>The weapon in hand's crosshair, stock's
/// <c>CTFWeaponBase::ShouldDrawCrosshair</c> (tf_weaponbase.cpp:3638-3648)
/// and <c>GetWeaponCrosshairScale</c>
/// (basecombatweapon_shared.h:531) as <c>CHudTFCrosshair::Paint</c> applies
/// them (tf_hud_crosshair.cpp:189-208): whether the weapon draws one — a
/// custom <c>cl_crosshair_file</c> always draws (ShouldDrawCrosshair's
/// "return true", :3646-3647), the empty-file default crosshair follows the
/// weapon's script <c>DrawCrosshair</c> word (tf_weapon_parse.cpp:78,
/// :3641-3644) — and the scale on the 32-pixel texture, the weapon's
/// (base 1.0; the revolver's headshot bloom,
/// tf_weapon_revolver.cpp:207-223) times the player's
/// <c>cl_crosshair_scale</c>/32 (its default 32, tf_hud_crosshair.cpp:201).
/// The colour and the custom material are the player's own: stock's Paint
/// binds <c>vgui/crosshairs/&lt;file&gt;</c> (:150-158) and modulates by
/// <c>cl_crosshair_red/green/blue</c> at alpha 255 (:200, and the base arm's
/// same read at hud_crosshair.cpp:263).</summary>
public interface ITFCrosshair
{
    /// <summary>Whether the crosshair draws this frame.</summary>
    bool Draw { get; }

    /// <summary>The weapon's scale on the crosshair's 32-pixel shape, the
    /// weapon's times <c>cl_crosshair_scale</c>/32.</summary>
    float Scale { get; }

    /// <summary>The custom crosshair's material — <c>vgui/crosshairs/</c>
    /// plus the <c>cl_crosshair_file</c> word, empty for the default
    /// crosshair (stock's Paint path, tf_hud_crosshair.cpp:150-154).</summary>
    string Material { get; }

    /// <summary>
    /// The held weapon's script <c>TextureData</c> <c>crosshair</c> entry —
    /// stock's <c>iconCrosshair</c> (<c>weapons_resource.cpp:123-127</c>),
    /// which <c>ItemPreFrame</c>'s <c>SetCrosshair</c> hands the hud element
    /// (<c>c_basecombatweapon.cpp:280-284</c>) and the empty-file
    /// <c>BaseClass::Paint</c> arm then draws cropped at the centre
    /// (<c>hud_crosshair.cpp:233-281</c>): the file and its pixel rect, the
    /// rect's size being the texture Paint scales (:253-254), its position
    /// the crop's origin in the file (<c>hud.cpp:653-656</c>). Flat as
    /// <see cref="TFSelectionWeapon"/>'s icon: this contract assembly
    /// compiles no TF2.Shared source of its own. Empty file and zero rect
    /// when the script names no crosshair entry — there the arm draws
    /// nothing (<c>hud_crosshair.cpp:235-236</c>).
    /// </summary>
    string ScriptFile { get; }

    /// <summary>Left of the script rect on the crosshair file.</summary>
    int ScriptX { get; }

    /// <summary>Top of the script rect on the crosshair file.</summary>
    int ScriptY { get; }

    /// <summary>Width of the script rect, stock's <c>iTextureW</c> (:253).</summary>
    int ScriptWide { get; }

    /// <summary>Height of the script rect, stock's <c>iTextureH</c> (:254).</summary>
    int ScriptTall { get; }

    /// <summary>The colour the paint modulates by, stock's three
    /// <c>cl_crosshair_*</c> ints (tf_hud_crosshair.cpp:200), alpha 255.</summary>
    int Red { get; }

    /// <summary>Green channel of <see cref="Red"/>.</summary>
    int Green { get; }

    /// <summary>Blue channel of <see cref="Red"/>.</summary>
    int Blue { get; }
}

/// <summary>
/// What one equipped slot shows on the loadout screen (stock's
/// <c>CItemModelPanel</c> rows, class_loadout_panel.cpp): the raw
/// <c>TFLoadoutSlot</c> number, the panel's label token (the
/// <c>#LoadoutSlot_*</c> strings tf_item_schema.cpp:1543-1569 maps slot
/// words to), the equipped item's name token (the schema's
/// <c>item_name</c>, empty for a slot with nothing), and the schema's
/// <c>image_inventory</c> material (empty likewise). Tokens, not text: the
/// screens localise through <c>IUiLocalisation</c>.
/// </summary>
public readonly record struct TFLoadoutSlotView(int Slot, string Label, string ItemName, string Icon);

/// <summary>One owned item on the loadout screen: its defindex (what an
/// equip names), its slot for the screen's filter, its name and inventory
/// image tokens.</summary>
public readonly record struct TFLoadoutOwnedItem(int Defindex, int Slot, string Name, string Icon);

/// <summary>
/// The TF2 loadout screen's data (stock's <c>CClassLoadoutPanel</c>,
/// game/client/tf/vgui/class_loadout_panel.cpp): the class the last
/// <c>TF2:loadout</c> reply named, its visible slots in panel order, and
/// the owned items. Everything is empty before a reply arrives.
/// </summary>
public interface ITFLoadoutSource
{
    /// <summary>The reply's class, the raw TF_CLASS number; 0 before one arrives.</summary>
    int Class { get; }

    /// <summary>How many visible slots the reply carries.</summary>
    int Slots { get; }

    /// <summary>What the slot at <paramref name="index"/> shows, in panel order.</summary>
    TFLoadoutSlotView Slot(int index);

    /// <summary>How many owned items the reply carries.</summary>
    int OwnedCount { get; }

    /// <summary>The owned item at <paramref name="index"/>.</summary>
    TFLoadoutOwnedItem Owned(int index);
}

/// <summary>What the loadout screen's clicks do: ask the server for a
/// class's loadout, equip an owned item (a negative defindex clears the
/// slot, stock's reset arm), and close.</summary>
public interface ITFLoadoutActions
{
    /// <summary>The class strip's tile was clicked, by its raw TF_CLASS
    /// number: the one-byte ask.</summary>
    void Select(int classNumber);

    /// <summary>An item panel or the reset arm: the named
    /// <c>tf2_equip</c> console line.</summary>
    void Equip(int classNumber, int slot, int defindex);

    /// <summary>Back/cancel: closes the screen.</summary>
    void Close();
}

/// <summary>One row of the win panel's top players (stock's
/// <c>Player</c> rows, tf_hud_winpanel.cpp:491-585): the name and class
/// as the scoreboard names them (g_PR / g_aPlayerClassNames), the round
/// points, and the row's team — the raw TF number (2 RED, 3 BLU) the
/// screen's tone comes from. A zero-points row is not shown, so the
/// fields are only read while <see cref="ITFWinPanelSource.Showing"/>.</summary>
public readonly record struct TFWinPanelPlayerView(string Name, string Class, int Points, int Team);

/// <summary>
/// The win panel's data (stock's <c>CTFWinPanel</c>, tf_hud_winpanel.cpp):
/// the <c>teamplay_win_panel</c> event snapshotted as it arrived
/// (<c>FireGameEvent</c>, :127-665), hidden again by
/// <c>teamplay_round_start</c> and the other hide events (:119-126). The
/// sentences are resolved once, at the event, the way stock's
/// ConstructString calls run in FireGameEvent. <see cref="ScoresTick"/> is
/// stock's 2-second score-tick arm — the scores the panel shows are the
/// previous ones until it lands (OnThink :714-737, m_flTimeUpdateTeamScore).
/// </summary>
public interface ITFWinPanelSource
{
    /// <summary>A win panel is up.</summary>
    bool Showing { get; }

    /// <summary>The banner sentence (stock's WinningTeamLabel on a
    /// complete round, AdvancingTeamLabel on a mini-round): the team's
    /// wins/advances/defends line, or #Winpanel_Stalemate on a stalemate.</summary>
    string Banner { get; }

    /// <summary>The win reason sentence (stock's WinReasonLabel).</summary>
    string WinReason { get; }

    /// <summary>The details sentence (stock's DetailsLabel): the
    /// mini-round's capture points left, or the capture's cappers.</summary>
    string Details { get; }

    /// <summary>The top players heading (stock's TopPlayersLabel): the
    /// winning team's MVPs, or #Winpanel_TopPlayers on a stalemate.</summary>
    string TopPlayersLabel { get; }

    /// <summary><c>winning_team</c>: 2 RED, 3 BLU, 0 a stalemate — the
    /// banner's tone and the border's.</summary>
    int WinningTeam { get; }

    /// <summary><c>round_complete</c>: a whole round rather than a
    /// mini-round — the team-scores panel shows only on one
    /// (stock's TeamScoresPanel SetVisible).</summary>
    bool RoundComplete { get; }

    /// <summary>The blue score the panel shows now: <c>blue_score_prev</c>
    /// until the 2-second tick, then <c>blue_score</c>.</summary>
    int BlueScore { get; }

    /// <summary>The red score the panel shows now, on the same tick.</summary>
    int RedScore { get; }

    /// <summary>Whether the scores have ticked up to the final ones —
    /// false in the two seconds after a complete round's panel, when
    /// they differ (the tick is a no-op without it).</summary>
    bool ScoresTick { get; }

    /// <summary>The top player of <paramref name="position"/> (0..2, the
    /// <c>player_1..3</c> rows); a zero <see cref="TFWinPanelPlayerView.Points"/>
    /// is stock's hidden row.</summary>
    TFWinPanelPlayerView Player(int position);

    /// <summary>The round's top killstreak row (<c>killstreak_player_1</c>);
    /// a zero count is stock's hidden row.</summary>
    TFWinPanelPlayerView KillStreak { get; }
}

/// <summary>
/// The sniper scope's reads (tf_hud_scope.cpp): whether the local player is
/// zoomed (<c>CHudScope::ShouldDraw</c>'s <c>TF_COND_ZOOMED</c>, :373-390)
/// and the carried rifle's charge for the meter (<c>CHudScopeCharge::Paint</c>,
/// :162-226 — stock's charge value reaches the texture through a material
/// proxy over the weapon's <c>m_flChargedDamage</c>; ours derives it on the
/// client from the carried state's charge-begin clock).
/// </summary>
public interface ITFScope
{
    /// <summary>Stock's ShouldDraw: the local player
    /// <c>InCond(TF_COND_ZOOMED)</c> (tf_hud_scope.cpp:381).</summary>
    bool Zoomed { get; }

    /// <summary>Stock's charge-meter weapon arm: the active weapon is a
    /// sniper rifle (<c>assert_cast&lt;CTFSniperRifle*&gt;</c>, tf_hud_scope.cpp:178).</summary>
    bool MeterPresent { get; }

    /// <summary>The charge as the meter shows it, 0..1 —
    /// <c>m_flChargedDamage</c> over <c>TF_WEAPON_SNIPERRIFLE_DAMAGE_MAX</c>
    /// 150. 0 while no rifle is up or the charge has not begun.</summary>
    float Charge { get; }
}

/// <summary>
/// The top team-status bar's players, stock's <c>CTFTeamStatus</c> walk
/// (tf_teamstatus.cpp <c>RecalculatePlayerPanels</c> :577-614 over
/// <c>g_TF_PR</c>): every connected player on a game team in entity-index
/// order, the reads <c>CTFTeamStatusPlayerPanel::Update</c> makes
/// (:87-140). The panel orders them about the local player and hides enemy
/// classes; this only carries the numbers.
/// </summary>
public interface ITFTeamStatus
{
    /// <summary>ShouldDraw's team arm: the local player stands on a game
    /// team (tf_teamstatus.cpp:520-522); the match-HUD arm is default-on
    /// and the summary arm is the round end's own screen.</summary>
    bool Present { get; }

    /// <summary>Connected players on a game team — the walk's count.</summary>
    int Count { get; }

    /// <summary>The walk's <paramref name="index"/>th entry.</summary>
    TFTeamStatusPlayer Player(int index);

    /// <summary>The local player's entity index — the walk's
    /// <c>entindex()</c>, the panel's innermost slot.</summary>
    int LocalPlayerIndex { get; }
}

/// <summary>One team-status row: <c>Update</c>'s reads before its
/// same-team shaping — the raw class, the -1 no-health of the unranked
/// rows, and the respawn wait once its seconds are spent
/// (tf_teamstatus.cpp:82-133).</summary>
public readonly record struct TFTeamStatusPlayer(
    int EntityIndex,
    int Team,
    int PlayerClass,
    bool Alive,
    int Health,
    int MaxHealth,
    int MaxBuffedHealth,
    int RespawnSeconds);

/// <summary>
/// The TF2 main menu's art (stock's <c>CHudMainMenuOverride</c>,
/// game/client/tf/tf_hud_mainmenuoverride.cpp): the holiday background
/// mainmenuoverride.res names under the active holiday's condition
/// (<c>ApplySchemeSettings</c>, :463-519) and the character the menu picks
/// from scripts/CharacterBackgrounds.txt (<c>LoadCharacterImageFile</c>,
/// :657-773). Both are material names under materials/, picked once, as
/// stock picks them when the panel is built.
/// </summary>
public interface ITFMainMenuArt
{
    /// <summary>The holiday background (e.g.
    /// <c>console/title_team_halloween2015_widescreen</c>), or null when the
    /// holiday names none — the GameUI's own background shows then.</summary>
    string? Background { get; }

    /// <summary>The character art (e.g. <c>console/characters/heavy</c>),
    /// or null when the background takes none (two of Halloween's) or the
    /// script names nothing usable.</summary>
    string? Character { get; }
}

/// <summary>
/// What the TF2 main menu's buttons do (stock's
/// <c>CTFMatchmakingDashboard::OnCommand</c> for the top bar and
/// <c>CHudMainMenuOverride::OnCommand</c> for the rest): most are the GameUI's
/// own dialogs by menu command, as stock's
/// <c>SendMainMenuCommand</c>/<c>gamemenucommand</c> asks for them. Resume
/// and Quit are the core's (<see cref="SourceSharp.Client.Ui.IMainMenuSource"/>).
/// </summary>
public interface ITFMainMenuActions
{
    /// <summary>FIND A GAME: <c>gamemenucommand openserverbrowser</c>
    /// (<c>OnPlayCommunity</c>, tf_matchmaking_dashboard.cpp:917-922).</summary>
    void FindAGame();

    /// <summary>HOST A GAME: <c>gamemenucommand
    /// OpenCreateMultiplayerGameDialog</c> (<c>OnCreateServer</c>, :924-929).</summary>
    void HostAGame();

    /// <summary>The first gear: the GameUI's options dialog
    /// (SettingsButtonSDK's <c>OpenOptionsDialog</c>).</summary>
    void Options();

    /// <summary>The second gear: <c>opentf2options</c>, the TF2 advanced
    /// options dialog (TF2SettingsButtonSDK).</summary>
    void AdvancedOptions();

    /// <summary>ITEMS: stock's <c>engine open_charinfo</c>.</summary>
    void Items();

    /// <summary>In game, Disconnect's confirmed leave: <c>disconnect</c>
    /// (<c>PromptOrFireCommand</c>, tf_hud_mainmenuoverride.cpp:123-138).</summary>
    void Disconnect();

    /// <summary>In game: GameMenu.res's CallVoteButton, <c>callvote</c> and
    /// the GameUI's ResumeGame (:1923-1931).</summary>
    void CallVote();

    /// <summary>In game: MutePlayersButton, the GameUI's
    /// <c>OpenMutePlayerDialog</c>.</summary>
    void MutePlayers();

    /// <summary>In game: RequestCoachButton, <c>engine
    /// cl_coach_find_coach</c>.</summary>
    void RequestCoach();

    /// <summary>In game: ReportPlayerButton, the GameUI's
    /// <c>OpenReportPlayerDialog</c>.</summary>
    void ReportPlayer();
}

/// <summary>
/// The demoman's sticky-bomb counter (tf_hud_demomanpipes.cpp): the pipes
/// panel's count, stock's <c>CHudDemomanPipes::OnTick</c>
/// <c>GetNumActivePipebombs</c> read (tf_player_shared.cpp:13065). ShouldDraw's
/// remaining arms — the Halloween ghost, the minigame and the match summary
/// (:112-119) — have no port to gate on yet.
/// </summary>
public interface ITFDemomanPipes
{
    /// <summary>ShouldDraw's arms the port holds: a live demoman
    /// (tf_hud_demomanpipes.cpp:104-110).</summary>
    bool Present { get; }

    /// <summary>The sticky bombs out — the launcher's
    /// <c>m_iPipebombCount</c> through <c>GetNumActivePipebombs</c>.</summary>
    int Count { get; }
}

/// <summary>
/// The map-goal hint panel's reads, stock's <c>CHudTeamGoal</c>
/// (tf_hud_teamgoal.cpp): the panel the map's goal string shows shortly
/// after spawn. <see cref="Visible"/> is <c>ShouldDraw</c>'s rule
/// (:102-158) — the round visible, the local player alive on a game team
/// and a goal string up, for the 15 seconds after it first shows; the
/// other arms (tournament, the team-switch alert) have no port to gate on.
/// <see cref="Text"/> is the localised goal line, <see cref="Image"/> the
/// role icon the panel's corner carries, <see cref="Team"/> the local
/// player's team for the background art.
/// </summary>
public interface ITFTeamGoal
{
    /// <summary>Stock's <c>ShouldDraw</c> where it returns true.</summary>
    bool Visible { get; }

    /// <summary>The goal line, localised (<c>SetupGoalPanel</c>'s
    /// <c>g_pVGuiLocalize->Find</c> with the raw string as fallback).</summary>
    string Text { get; }

    /// <summary>The role icon material (hud_icon_capture/defend/attack), as
    /// <c>SetupGoalPanel</c>'s table picks it; empty is the res default.</summary>
    string Image { get; }

    /// <summary>The local player's team (the res's teambg_2/3 key).</summary>
    int Team { get; }
}

/// <summary>
/// The hud's render groups, stock's <c>gHUD</c> group lock
/// (hud.cpp:288-303): a <c>CHudElement</c> registered for a group stands
/// down while the group is locked. The TF scoreboard locks the hud's
/// <c>global</c> group for exactly the while it shows
/// (tf_clientscoreboard.cpp:433-441 locked, :469 freed), and every hud
/// element joins the group in the base constructor (hud.cpp:244-246) — the
/// health cross, the ammo, the clock, the goal hint, the kill feed, the
/// chat, all of them. The TF screens that are stock hud elements answer
/// this from the live UI and stand down with it; the scope overlay
/// unregisters the group in stock (tf_hud_scope.cpp:336) and the spectator
/// bars are no hud elements, so neither stands down.
/// </summary>
public interface ITFRenderGroups
{
    /// <summary>Whether the global group is locked now — the TF scoreboard
    /// shows, so the hud's elements stand down (<c>ShouldDraw</c>'s locked
    /// arm, hud.cpp:288-303).</summary>
    bool GlobalLocked { get; }
}

/// <summary>
/// One card of the weapon selection: the weapon a slot's box shows, as
/// stock's item panel is set (<c>SetItem</c>, tf_hud_weaponselection.cpp:560)
/// and the draw reads it. The icon is the script's own art for the local
/// player's team (<c>GetSpriteInactive</c>, or <c>GetSpriteActive</c> on
/// BLU, :806-813) as one file-and-rect — this contract assembly compiles no
/// TF2.Shared source of its own, so the parsed pair rides flat. The name is
/// the printname localised, the card's label; <see cref="CanSelect"/> is
/// <c>CanBeSelected</c>, whose false draws the out-of-ammo note (:745-752).
/// Every string is empty and every count 0 when the slot shows no weapon.
/// </summary>
public readonly record struct TFSelectionWeapon(
    string IconFile,
    int IconX,
    int IconY,
    int IconWide,
    int IconTall,
    string Name,
    bool CanSelect);

/// <summary>
/// The weapon selection buckets, stock's <c>CHudWeaponSelection</c>
/// (tf_hud_weaponselection.cpp) over the base's state
/// (<c>CBaseHudWeaponSelection</c>, weapon_selection.cpp): which slots show,
/// which is the selection, and what each card carries. The timeouts are the
/// hud's own (<c>OnThink</c>, :259-278) and live on the selection state, not
/// here; the screen draws while <see cref="Showing"/> holds.
/// </summary>
public interface ITFWeaponSelect
{
    /// <summary>Whether the buckets draw: <c>ShouldDrawInternal</c>'s
    /// <c>m_bSelectionVisible</c> arm behind its player arms (:315-349) —
    /// a living, armed player with the selection open under
    /// hud_fastswitch 0.</summary>
    bool Showing { get; }

    /// <summary>The selected weapon's slot: the large box, whose card carries
    /// the number and the out-of-ammo note (<c>iActiveSlot</c>, :707); -1
    /// while nothing is selected.</summary>
    int SelectedSlot { get; }

    /// <summary>Whether the slot shows a card at all — <c>GetFirstPos</c>'s
    /// find (:395-409, the slot holds a visible weapon).</summary>
    bool SlotHasWeapon(int slot);

    /// <summary>The card a slot shows: its held weapons as the panel takes
    /// them, the last one set winning (<c>SetItem</c> per position,
    /// :551-576) — read <see cref="SourceSharp.Client.Ui.IWeaponSelectionSource"/>
    /// for the positions themselves.</summary>
    TFSelectionWeapon WeaponInSlot(int slot);

    /// <summary>The local player's team, 2 RED 3 BLU: the card art's
    /// active/inactive pick (:806-813) and the border's colour (:565-572).</summary>
    int Team { get; }
}

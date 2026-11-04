namespace SourceSharp.Client.Ui;

// THE DATA INTERFACES SCREENS READ (plan_client_ui §4, decision 2): a screen
// never sees the engine. What it shows comes in by constructor injection as one
// of these; the core implements them over the game, the screens' samples
// implement them with fixed data, and that is what makes every screen
// previewable. Index-and-count shapes rather than lists, so reading them each
// frame allocates nothing.

/// <summary>
/// One player, as the scoreboard shows them. <see cref="Tag"/> is a short
/// label a mod gives the player (a bot's skill, user ruling 2026-11-01),
/// shown beside the name as a secondary tag; empty for most players.
/// </summary>
public readonly record struct ScoreboardPlayer(
    int Index, string Name, int Team, int Frags, int Deaths, int Ping,
    bool Alive, bool Bot, bool Local, string Tag = "");

/// <summary>One team. <see cref="Number"/> is the game's team number.</summary>
public readonly record struct ScoreboardTeam(int Number, string Name, int Score);

/// <summary>The scoreboard's data: the server, its teams and its players.</summary>
public interface IScoreboardSource
{
    string ServerName { get; }

    string MapName { get; }

    int MaxPlayers { get; }

    /// <summary>The playing teams, in display order (spectators and unassigned are not teams here).</summary>
    int TeamCount { get; }

    ScoreboardTeam Team(int i);

    int PlayerCount { get; }

    ScoreboardPlayer Player(int i);
}

// THE LOCAL PLAYER, one interface per concern (user ruling 2026-10-05): a
// panel takes only what it reads, so a panel's constructor says what it
// depends on and nothing more. There is no aggregate.

/// <summary>Whether there is a local player in play.</summary>
public interface IPlayerLife
{
    /// <summary>False in the menu and between levels: the HUD draws nothing then.</summary>
    bool Alive { get; }
}

/// <summary>The local player's health.</summary>
public interface IPlayerHealth
{
    int Health { get; }

    int MaxHealth { get; }
}

/// <summary>The local player's suit armour.</summary>
public interface IPlayerArmour
{
    int Armour { get; }

    int MaxArmour { get; }
}

/// <summary>The local player's suit power, HL2's AUX POWER.</summary>
public interface IPlayerSuitPower
{
    /// <summary>0 to 100.</summary>
    float SuitPower { get; }

    /// <summary>The devices drawing on it (m_bitsActiveDevices): 1 sprint, 2 flashlight, 4 breather.</summary>
    int SuitDevices { get; }
}

/// <summary>The weapon in the local player's hands.</summary>
public interface IPlayerWeapon
{
    /// <summary>Whether a weapon is in hand.</summary>
    bool HasWeapon { get; }

    /// <summary>The weapon's display name (#Token or text).</summary>
    string WeaponName { get; }
}

/// <summary>
/// The weapon selection (stock HL2's CHudWeaponSelection, hud_fastswitch 0):
/// the buckets of the weapons carried, the one holding the selected weapon
/// open into its positions.
/// </summary>
public interface IWeaponSelectionSource
{
    /// <summary>Whether it is drawn: open, in the bucket style, with a player to select for.</summary>
    bool Showing { get; }

    /// <summary>Its fade, 0 to 1: up as it opens, down as it times out.</summary>
    float Opacity { get; }

    /// <summary>The bucket the selected weapon is in, 0 to 5; -1 for none.</summary>
    int OpenSlot { get; }

    /// <summary>Whether a weapon is carried in the bucket.</summary>
    bool SlotHasWeapon(int slot);

    /// <summary>The furthest position carried in the bucket; -1 for none.</summary>
    int LastPosition(int slot);

    /// <summary>The weapon at a bucket's position; false for none there.</summary>
    bool Weapon(int slot, int position, out WeaponSelectionBox box);
}

/// <summary>
/// The plus weapon selection (stock HL2's CHudWeaponSelection, hud_fastswitch
/// 2): the buckets as arms round the crosshair, up, right, down, left and the
/// two lower diagonals, each place out from the centre a weapon's position.
/// </summary>
public interface IWeaponPlusSource
{
    /// <summary>Whether it is drawn: a key switched in the last few seconds, with a weapon selected.</summary>
    bool Showing { get; }

    /// <summary>Its fade, 0 to 1.</summary>
    float Opacity { get; }

    /// <summary>The selected weapon's glow, 1 as it is reached and down to 0.</summary>
    float Glow { get; }

    /// <summary>The selected arm (its bucket, 0 to 5); -1 for none.</summary>
    int SelectedSlot { get; }

    /// <summary>The selected place along the arm, from 0 by the centre.</summary>
    int SelectedPosition { get; }

    /// <summary>The furthest position carried in the bucket; -1 for none.</summary>
    int LastPosition(int slot);

    /// <summary>The weapon at an arm's place; false for none there (an empty place, still drawn).</summary>
    bool Weapon(int slot, int position, out WeaponSelectionBox box);
}

/// <summary>
/// The carousel weapon selection (stock HL2's CHudWeaponSelection,
/// hud_fastswitch 3): every weapon carried, in bucket order, along the top;
/// the selected one in the middle once the strip has slid to it.
/// </summary>
public interface IWeaponCarouselSource
{
    /// <summary>Whether it is drawn: a wheel step in the last few seconds, more than one weapon carried.</summary>
    bool Showing { get; }

    /// <summary>Its fade, 0 to 1.</summary>
    float Opacity { get; }

    /// <summary>The selected weapon's glow, 1 as the strip arrives and down to 0.</summary>
    float Glow { get; }

    /// <summary>The boxes in the strip.</summary>
    int Count { get; }

    /// <summary>The selected box.</summary>
    int Selected { get; }

    /// <summary>How far the strip has still to slide, in boxes: positive while it comes in from the right.</summary>
    float Offset { get; }

    /// <summary>The weapon in a box; false when it is no longer carried.</summary>
    bool Weapon(int box, out WeaponSelectionBox weapon);
}

/// <summary>One weapon in the open bucket.</summary>
/// <param name="Icon">Its icon in the UI's icon set (<c>smg1</c>, <c>357</c>).</param>
/// <param name="Name">Its display name, localised.</param>
/// <param name="Selectable">Whether it has anything to fire (stock's CanBeSelected): drawn red when not.</param>
/// <param name="Selected">The weapon the selection is on.</param>
public readonly record struct WeaponSelectionBox(string Icon, string Name, bool Selectable, bool Selected);

/// <summary>
/// The pickup history (stock CHudHistoryResource): what was just picked up, a
/// row per pickup by slot from the bottom; a slot may be empty.
/// </summary>
public interface IPickupHistorySource
{
    /// <summary>Whether any row waits to be drawn.</summary>
    bool Showing { get; }

    /// <summary>How many slots the history holds, empty ones included.</summary>
    int SlotCount { get; }

    /// <summary>The row in a slot; false for an empty one, or one with nothing to draw.</summary>
    bool Row(int slot, out PickupRow row);
}

/// <summary>
/// One history row: its icon, the count an ammo pickup gave (0 for none),
/// whether it is a refusal ("FULL"), whether it is drawn in caution (a weapon
/// with nothing to fire, a refusal), and its fade, 0 to 1.
/// </summary>
public readonly record struct PickupRow(string Icon, int Count, bool Full, bool Caution, float Opacity);

/// <summary>The ammunition of the weapon in hand.</summary>
public interface IPlayerAmmo
{
    /// <summary>Rounds in the clip, -1 for a weapon without one.</summary>
    int Clip { get; }

    /// <summary>Rounds in reserve, -1 for a weapon that uses none.</summary>
    int Reserve { get; }

    /// <summary>Secondary ammunition carried (the SMG's grenades, the AR2's energy balls), -1 for a weapon without one.</summary>
    int Secondary { get; }

    /// <summary>
    /// The most of the secondary ammunition that can be carried (the ammo type's
    /// max carry: 3 SMG grenades, 3 AR2 balls in stock HL2), -1 when the weapon
    /// has no secondary or the maximum is not known.
    /// </summary>
    int SecondaryMax { get; }
}

/// <summary>
/// One line of the kill feed (stock CHudDeathNotice's DeathNoticeItem): who
/// killed whom with what. A suicide or a world death has no killer drawn.
/// </summary>
/// <param name="Killer">The killer's name; empty for a suicide or the world.</param>
/// <param name="Weapon">The weapon as it is shown: the event's name in capitals, words apart (<c>SMG1</c>, <c>CROSSBOW BOLT</c>); empty for none.</param>
/// <param name="Icon">The icon of the UI's icon set the kill is drawn with (<c>smg1</c>, <c>skull</c>, <c>generic</c>).</param>
/// <param name="Local">The local player killed or was killed.</param>
public readonly record struct KillFeedLine(
    string Killer, int KillerTeam, string Victim, int VictimTeam, string Weapon, string Icon, bool Suicide, bool Headshot,
    bool Local);

/// <summary>The kill feed's lines, oldest first, the expired ones already gone.</summary>
public interface IKillFeedSource
{
    int LineCount { get; }

    KillFeedLine Line(int i);
}

/// <summary>
/// One number of damage the local player dealt: how much, and how far through its
/// short life it is, 0 fresh to 1 gone.
/// </summary>
public readonly record struct DamageNumber(int Amount, float Age);

/// <summary>The damage the local player just dealt, oldest first.</summary>
public interface IDamageDealtSource
{
    int NumberCount { get; }

    DamageNumber Number(int i);
}

/// <summary>One thing on the minimap: where it falls on the square, 0..1, east and south.</summary>
public readonly record struct MinimapDot(float U, float V);

/// <summary>The map as seen from above: its outline, the local player, and the others they can see.</summary>
public interface IMinimapSource
{
    /// <summary>x0, y0, x1, y1 for each line, 0..1 across the square; empty when the map has no outline.</summary>
    float[] Outline { get; }

    MinimapDot Self { get; }

    /// <summary>Which way the local player faces, radians clockwise from north.</summary>
    float SelfFacing { get; }

    /// <summary>The other players on the local player's screen right now.</summary>
    int DotCount { get; }

    MinimapDot Dot(int i);
}

/// <summary>A marker on the minimap: where (0..1 across the square), its radius as a share of the square (0 for a ping), and how far through its life it is, 0..1.</summary>
public readonly record struct MinimapPing(float U, float V, float Radius, float Age);

/// <summary>What a server has asked this player's minimap to show them (a generic marker, see <c>IPlayerMarkers</c>).</summary>
public interface IMinimapPingSource
{
    int PingCount { get; }

    MinimapPing Ping(int i);
}

/// <summary>A marker in the world, drawn through walls: its place on the screen as fractions, what it is, how far, and how far through its life, 0..1.</summary>
public readonly record struct WorldMarker(float X, float Y, string Label, int Metres, float Age);

/// <summary>The places a server has asked this player's screen to point at.</summary>
public interface IWorldMarkerSource
{
    int MarkerCount { get; }

    WorldMarker Marker(int i);
}

/// <summary>The last damage the local player took.</summary>
public interface IPlayerDamage
{
    /// <summary>When, in the clock the layer is driven with; negative infinity for never.</summary>
    double LastDamageAt { get; }

    /// <summary>From where, degrees clockwise from straight ahead (-90 left, 90 right, 180 behind); NaN when not known.</summary>
    float LastDamageYaw { get; }
}

/// <summary>
/// The player a spectator is watching (in-eye or chase cam): who, which side,
/// how hurt, and what they hold. Read by the spectator target panel.
/// </summary>
public interface ISpectatorTargetSource
{
    /// <summary>False when not spectating, or spectating no one (free roam): the panel draws nothing then.</summary>
    bool HasTarget { get; }

    /// <summary>The target's player name.</summary>
    string Name { get; }

    /// <summary>The target's game team number (2 Combine, 3 Rebels); 0 or 1 for no team (deathmatch).</summary>
    int Team { get; }

    int Health { get; }

    int MaxHealth { get; }

    /// <summary>The weapon in the target's hands, display name (#Token or text); empty for none.</summary>
    string WeaponName { get; }
}

/// <summary>How the local player is watching: stock's OBS_MODE_*, by value.</summary>
public enum SpectatorMode
{
    None = 0,
    DeathCam = 1,
    FreezeCam = 2,
    Fixed = 3,
    InEye = 4,
    Chase = 5,
    PointOfInterest = 6,
    Roaming = 7,
}

/// <summary>
/// Whether the local player is observing, and how (stock CSpectatorGUI's top
/// bar): the mode and the map. The player watched is <see cref="ISpectatorTargetSource"/>.
/// </summary>
public interface IObserverSource
{
    /// <summary><see cref="SpectatorMode.None"/> when playing (and through the death cam's first seconds).</summary>
    SpectatorMode Mode { get; }

    /// <summary>The map's name, without its folder or extension; empty when not known.</summary>
    string MapName { get; }
}

/// <summary>One line of a map's on-screen text (game_text, env_message), placed as the map asked.</summary>
public readonly record struct HudTextLine(
    string Text, float X, float Y, UiColor Color, float Alpha);

/// <summary>The centre print (the server's one message to the player).</summary>
public interface ICentrePrintSource
{
    /// <summary>The message, empty for none.</summary>
    string CenterPrint { get; }

    /// <summary>0..1: how visible it is now.</summary>
    float CenterPrintAlpha { get; }
}

/// <summary>A map's on-screen text (game_text, env_message) showing now.</summary>
public interface IMapTextSource
{
    int MessageCount { get; }

    HudTextLine Message(int i);
}

/// <summary>Key hints (env_hudhint), with %command% already resolved.</summary>
public interface IKeyHintSource
{
    int HintCount { get; }

    string Hint(int i);

    /// <summary>The bound key hint <paramref name="i"/> refers to, empty for none: drawn as a keycap.</summary>
    string HintKey(int i);
}

/// <summary>
/// One chat line: a speaker (empty for a server line), their team, and what they
/// said; said to the team only, or by the dead; how visible it is now.
/// </summary>
public readonly record struct ChatLine(string Speaker, int Team, string Text, bool TeamOnly, float Alpha, bool Dead = false);

/// <summary>The line being typed into the chat (stock messagemode / messagemode2).</summary>
public interface IChatInputSource
{
    /// <summary>messagemode2: the line goes to the team only.</summary>
    bool TeamOnly { get; }

    string Input { get; }

    /// <summary>The caret, a char index into <see cref="Input"/>.</summary>
    int Caret { get; }
}

/// <summary>The engine's loading and paused plaques: whether each is up, and its text.</summary>
public interface ILoadingDiscSource
{
    bool Loading { get; }

    bool Paused { get; }

    string LoadingText { get; }

    string PausedText { get; }
}

/// <summary>One line of the engine's debug-overlay text, where the engine placed it (pixels) and in its colour.</summary>
public readonly record struct DebugOverlayLine(float X, float Y, UiColor Colour, string Text);

/// <summary>The engine's debug-overlay text this frame (stock CDebugOverlay's list).</summary>
public interface IDebugOverlaySource
{
    int Count { get; }

    DebugOverlayLine Line(int i);
}

/// <summary>One player speaking on voice: their name, team and whether they are alive.</summary>
public readonly record struct VoiceSpeaker(string Name, int Team, bool Alive);

/// <summary>Who is speaking on voice (stock CHudVoiceStatus) and whether the local player is (CHudVoiceSelfStatus).</summary>
public interface IVoiceSource
{
    bool LocalSpeaking { get; }

    /// <summary>The speakers, in the order they started.</summary>
    int Count { get; }

    VoiceSpeaker Speaker(int i);
}

/// <summary>The chat feed, newest last.</summary>
public interface IChatSource
{
    int Count { get; }

    ChatLine Line(int i);
}

/// <summary>Which build is running: the corner stamp.</summary>
public interface IBuildStampSource
{
    bool Visible { get; }

    /// <summary>The stamp line, e.g. <c>n 19:04:12 1a0e · m 20:31:40 4337 · main@3b34ce20</c>.</summary>
    string Line { get; }

    /// <summary>Something newer was built since this loaded.</summary>
    bool Stale { get; }

    /// <summary>When and where the view is, for screenshots; empty for none.</summary>
    string DebugLine { get; }
}

/// <summary>One choice on the team menu.</summary>
public readonly record struct TeamChoice(int Number, string Name, int Players, int Score, bool Full);

/// <summary>The team menu's data.</summary>
public interface ITeamMenuSource
{
    string MapName { get; }

    int ChoiceCount { get; }

    TeamChoice Choice(int i);

    /// <summary>The local player's team number, 0 for none yet.</summary>
    int CurrentTeam { get; }
}

// ---- the console, one interface per concern ----

/// <summary>How a console line was printed: Msg, Warning, Error, DevMsg, or a command the player entered.</summary>
public enum ConsoleSeverity : byte
{
    Message,
    Warning,
    Error,
    Developer,
    /// <summary>A command the player entered, echoed ("] sv_cheats 1").</summary>
    Echo,
}

/// <summary>One line of console output.</summary>
public readonly record struct ConsoleLine(string Text, ConsoleSeverity Severity);

/// <summary>The console's output, oldest first.</summary>
public interface IConsoleLogSource
{
    int LineCount { get; }

    ConsoleLine Line(int i);
}

/// <summary>The console's input line: what is typed, the caret and the selection, and history recall.</summary>
public interface IConsoleInputSource
{
    string Input { get; }

    /// <summary>The caret, a char index into <see cref="Input"/>.</summary>
    int Caret { get; }

    int SelectionStart { get; }

    int SelectionLength { get; }

    /// <summary>Commands entered this session, oldest first.</summary>
    int HistoryCount { get; }

    /// <summary>Which history entry the input was recalled from (Up/Down), -1 when it was typed.</summary>
    int HistoryIndex { get; }
}

/// <summary>One autocomplete suggestion: a convar (with its value) or a command, and its help.</summary>
public readonly record struct ConsoleCompletion(string Name, string Value, string Help, bool IsCommand);

/// <summary>The suggestions for what is typed, best first.</summary>
public interface IConsoleCompletionSource
{
    int CompletionCount { get; }

    ConsoleCompletion Completion(int i);

    /// <summary>The suggestion the line holds once Tab or an arrow took one; before that the one Tab would take; -1 for none.</summary>
    int SelectedCompletion { get; }
}

// ---- the server browser, one interface per concern ----

/// <summary>One server, as the browser lists it.</summary>
public readonly record struct ServerEntry(
    string Name, string Game, string Map, int Players, int MaxPlayers, int Bots, int Ping,
    bool Password, bool Secure);

/// <summary>The servers of the current tab, as they have answered.</summary>
public interface IServerListSource
{
    int ServerCount { get; }

    ServerEntry Server(int i);

    /// <summary>Changes whenever the list does: what a view re-sorts and re-filters on.</summary>
    int Version { get; }
}

/// <summary>Which list is shown, and whether it is being refreshed.</summary>
public enum ServerTab : byte
{
    Internet,
    Favourites,
    Lan,
    History,
}

/// <summary>The columns the server table sorts by.</summary>
public enum ServerSort : byte
{
    Name,
    Game,
    Map,
    Players,
    Ping,
}

/// <summary>How the server table is ordered: by a column, up or down.</summary>
public interface IServerSortSource
{
    ServerSort Sort { get; }

    bool Descending { get; }
}

/// <summary>
/// The browser beyond its lists: whether Steam answers (the internet list's
/// source), whether the selected server is a favourite, and the password a
/// passworded server's join is waiting for.
/// </summary>
public interface IServerBrowserState
{
    bool SteamAvailable { get; }

    bool SelectedIsFavourite { get; }

    bool AskingPassword { get; }

    /// <summary>The name of the server the password is for.</summary>
    string PasswordServer { get; }
}

/// <summary>What the browser's controls do (stock's ServerBrowser.so dialogs).</summary>
public interface IServerBrowserActions
{
    /// <summary>Once a frame while the browser is open: the queries' answers come in.</summary>
    void Poll();

    void SelectTab(ServerTab tab);

    void SetMapContains(string text);

    void SetNotFull(bool on);

    void SetNotEmpty(bool on);

    void SetNoPassword(bool on);

    void SetMaxPing(int ms);

    /// <summary>Sorts by a column; the column already sorted by turns over.</summary>
    void SortBy(ServerSort column);

    /// <summary>Selects a server of the current list by its index there.</summary>
    void Select(int index);

    void Refresh();

    /// <summary>Joins the selected server; a passworded one asks for the password first.</summary>
    void Connect();

    /// <summary>Adds the selected server to the favourites, or on the favourites tab removes it.</summary>
    void ToggleFavourite();

    /// <summary>Adds a server by its address (a.b.c.d[:port]) to the favourites; false for an address that is not one.</summary>
    bool AddServer(string address);

    void SubmitPassword(string password);

    void CancelPassword();
}

/// <summary>The browser's query: the tab, and a refresh in progress.</summary>
public interface IServerQuerySource
{
    ServerTab Tab { get; }

    bool Refreshing { get; }

    /// <summary>0..1: how far the refresh has got.</summary>
    float RefreshProgress { get; }
}

/// <summary>The browser's filters.</summary>
public interface IServerFilterSource
{
    /// <summary>Shown servers' maps contain this (case-insensitive); empty for any.</summary>
    string MapContains { get; }

    bool NotFull { get; }

    bool NotEmpty { get; }

    bool NoPassword { get; }

    /// <summary>The highest ping shown, ms; 0 for any.</summary>
    int MaxPing { get; }
}

/// <summary>One player on the selected server.</summary>
public readonly record struct ServerPlayer(string Name, int Score, int Minutes);

/// <summary>The selected server and who is on it.</summary>
public interface IServerDetailsSource
{
    /// <summary>The selected server's index in the list, -1 for none.</summary>
    int Selected { get; }

    int PlayerCount { get; }

    ServerPlayer Player(int i);
}

// ---- windows ----

/// <summary>
/// Where a window is: its top-left and size in design units, from the screen's
/// top-left. Clamped back onto the screen whenever it is used.
/// </summary>
public readonly record struct UiWindowPlacement(float X, float Y, float W, float H);

/// <summary>
/// Where windows were left, between sessions (the core keeps it in the mod's
/// config). Read when a window first opens, written when a move or a resize ends.
/// </summary>
public interface IUiWindowStore
{
    bool TryLoad(string windowId, out UiWindowPlacement placement);

    void Save(string windowId, UiWindowPlacement placement);
}

// ---- what a menu does (plan_client_ui U3): the core carries it out ----

/// <summary>The team menu's choice: 0 auto-assign, 1 spectate, 2 and up a team; or nothing.</summary>
public interface ITeamMenuActions
{
    void Choose(int team);

    void Cancel();
}

/// <summary>The message of the day (stock: the "info" panel): a title and plain text.</summary>
public interface IMotdSource
{
    string Title { get; }

    string Text { get; }
}

/// <summary>The message of the day, read and dismissed.</summary>
public interface IMotdActions
{
    void Close();
}

/// <summary>The console's Submit button (Enter does the same through the core's key handling).</summary>
public interface IConsoleActions
{
    void Submit();

    /// <summary>A suggestion was clicked: it goes into the line.</summary>
    void TakeCompletion(int i);
}

/// <summary>The main menu's buttons (the launch menu and the pause menu, stock's ESC
/// menu): Play loads the map the mod's maplist names, Resume hides the menu, Options
/// shows the options window, Quit ends the game. Resume means something only with a
/// level up, Play only when the mod named a map.</summary>
public interface IMainMenuSource
{
    /// <summary>A level is up and named: the engine's level, Resume's premise.</summary>
    bool InLevel { get; }

    /// <summary>The mod's maplist named a map: Play's premise.</summary>
    bool CanPlay { get; }

    void Play();

    void Resume();

    void OpenOptions();

    void Quit();
}

/// <summary>
/// The menu background (plan_gameui_managed.md G1): what the stock GameUI
/// painted full screen while no level was up, the material
/// <c>console/&lt;name&gt;</c>, <c>&lt;name&gt;</c> the engine's main-menu
/// background, with <c>_widescreen</c> on screens of aspect 1.6 or wider
/// (docs/gameui-census.md §5). Both are null while a level is up or when the
/// engine names no background.
/// </summary>
public interface IMenuBackgroundSource
{
    /// <summary>The material for screens narrower than 1.6.</summary>
    string? Material { get; }

    /// <summary>The material for screens of aspect 1.6 or wider.</summary>
    string? WidescreenMaterial { get; }
}

/// <summary>
/// The level load's progress dialog (stock GameUI's loading dialog,
/// plan_gameui_managed.md G1): up only for a load the engine started with
/// its progress dialog asked for; the fraction loaded and the engine's
/// current step, localised (empty before the first step names itself).
/// </summary>
public interface ILoadingProgressSource
{
    bool Showing { get; }

    /// <summary>The fraction loaded, 0 to 1.</summary>
    float Progress { get; }

    /// <summary>The engine's current step, localised, or empty.</summary>
    string Status { get; }
}

/// <summary>
/// A disconnect or failed load the player must read (stock GameUI's loading
/// dialog in its error form, titled "Disconnected"): the reason, localised.
/// </summary>
public interface IDisconnectMessageSource
{
    bool Showing { get; }

    /// <summary>The reason, localised (an extended reason already set into it).</summary>
    string Body { get; }

    /// <summary>The Close button.</summary>
    void Close();
}

/// <summary>
/// Quit's question (stock GameUI's quit confirmation, the engine's
/// OnConfirmQuit and the menu's Quit): up while asked; Quit ends the game,
/// Cancel withdraws the question.
/// </summary>
public interface IConfirmQuitSource
{
    bool Asking { get; }

    void Quit();

    void Cancel();
}

// ---- forms (plan_client_ui §6) ----

/// <summary>Key bindings, as a form's key fields read and write them: the engine's binds in game, a dictionary in a sample.</summary>
public interface IUiKeyBindingStore
{
    /// <summary>The key bound to <paramref name="command"/> (the engine's name, upper case), or null.</summary>
    string? KeyFor(string command);

    /// <summary>Binds <paramref name="key"/> to <paramref name="command"/>, replacing what it held.</summary>
    void Bind(string key, string command);

    void Unbind(string key);

    /// <summary>The engine's name for a button code (inputsystem's ButtonCode_t), upper case, or null.</summary>
    string? KeyName(int buttonCode);
}

/// <summary>
/// One row of the keyboard page: the console line a key runs, what the row says
/// (a #Token or the file's own words) and the heading it sits under (the same).
/// </summary>
public readonly record struct UiKeyAction(string Command, string Label, string? Section);

/// <summary>This player's admin key, for the advanced options: what it is, and making or setting one.</summary>
public interface IAdminKeySource
{
    bool HasKey { get; }

    /// <summary>The public half, base64, to put in a server's cfg/admins.yaml; empty with no usable key.</summary>
    string PublicKey { get; }

    /// <summary>The file holding the public half.</summary>
    string PublicKeyFile { get; }

    /// <summary>What went wrong with the last request, a token; null when nothing did.</summary>
    string? Problem { get; }

    /// <summary>Makes a key when there is none; never replaces one.</summary>
    void Generate();

    /// <summary>Takes a private key from a file's path, or as base64; one that does not read leaves the key there alone.</summary>
    void Set(string pathOrKey);
}

/// <summary>The vote on now, as the panel shows it: live from the server.</summary>
public interface IVoteSource
{
    /// <summary>Whether there is anything to show: a ballot open, or its result still up.</summary>
    bool Showing { get; }

    /// <summary>The ballot closed one way or the other.</summary>
    bool Decided { get; }

    bool Passed { get; }

    /// <summary>What it would do, a token.</summary>
    string Title { get; }

    /// <summary>Whom it would kick or what it would change to, empty when it says nothing more; and who called it, by name.</summary>
    string Target { get; }

    string Caller { get; }

    int Yes { get; }

    int No { get; }

    /// <summary>How many can vote on it.</summary>
    int Voters { get; }

    int SecondsLeft { get; }

    /// <summary>This player has voted: the panel stops asking.</summary>
    bool Voted { get; }

    /// <summary>The key that opens the ballot, by the engine's name; empty when unbound.</summary>
    string Key { get; }

    /// <summary>Why this player's last call was refused, a #Token; null for none now.</summary>
    string? Refusal { get; }
}

/// <summary>One choice of a radial menu: what it says (a #Token or data), a second line, and whether it can be taken.</summary>
public readonly record struct UiRadialItem(string Label, string? Detail, bool Enabled);

/// <summary>The radial menu up now, if any: voice commands, a vote to call, the admin's choices.</summary>
public interface IRadialMenuSource
{
    bool Showing { get; }

    /// <summary>What the hub says (a #Token or data).</summary>
    string Title { get; }

    int ItemCount { get; }

    UiRadialItem Item(int i);
}

/// <summary>What the radial menu's input does.</summary>
public interface IRadialMenuActions
{
    void Choose(int i);

    /// <summary>The place under the pointer this frame, -1 for none: what letting go of a held key takes.</summary>
    void Hover(int i);

    /// <summary>A button pressed while the menu holds the keyboard, which so never reached its bind: true when it is the one that opened the menu, which then closes.</summary>
    bool Pressed(int buttonCode);

    /// <summary>A step back up the menus, closing the first; Escape and the hub.</summary>
    void Back();
}

/// <summary>The keyboard page's rows, the running mod's.</summary>
public interface IUiKeyActions
{
    IReadOnlyList<UiKeyAction> Actions();

    /// <summary>The keys a fresh player gets, in the actions' order; a command may have several.</summary>
    IReadOnlyList<(string Key, string Command)> DefaultBinds { get; }
}

/// <summary>Convars by name, as a form's bindings read and write them: the engine's in game, a dictionary in a sample.</summary>
public interface IUiConVarStore
{
    /// <summary>The value as text, or null when there is no such convar.</summary>
    string? Get(string name);

    void Set(string name, string value);
}

/// <summary>A console line an options page submits where a setting is a command rather than a convar (closecaption, mat_setvideomode, setinfo).</summary>
public interface IUiCommandLine
{
    void Submit(string line);
}

/// <summary>The microphone's volume (0 to 1) and boost, through the engine's voice tweak controls (stock's voice options).</summary>
public interface IMicrophoneSettings
{
    /// <summary>Whether the engine has the controls; without them the voice page's controls are off.</summary>
    bool Available { get; }

    float Volume { get; set; }

    bool Boost { get; set; }
}

/// <summary>A video mode as the options list it, in pixels.</summary>
public readonly record struct UiVideoMode(int Width, int Height);

/// <summary>
/// What the video options list and start from (stock's video options): the
/// engine's modes, the desktop's size, the current mode and whether it is
/// windowed, and which antialiasing modes the card does. Read when the page
/// loads, not each frame.
/// </summary>
public interface IVideoModeSource
{
    IReadOnlyList<UiVideoMode> Modes { get; }

    UiVideoMode Desktop { get; }

    UiVideoMode Current { get; }

    bool Windowed { get; }

    bool SupportsMsaa(int samples);

    bool SupportsCsaa(int samples, int quality);
}

/// <summary>The game's files by wildcard (the player models, the spray logos): names without their directory. Read when a page loads.</summary>
public interface IUiFileList
{
    IReadOnlyList<string> Find(string wildcard);

    /// <summary>The directories a wildcard finds, never <c>.</c> or <c>..</c>.</summary>
    IReadOnlyList<string> Directories(string wildcard);
}

/// <summary>What an options script entry is drawn as (stock's script object types; a button runs its value).</summary>
public enum UiScriptKind
{
    Bool,
    Number,
    List,
    String,
    Slider,
    Category,
    Button,
}

/// <summary>One choice of a list entry: its words (a token or text) and the value it sets.</summary>
public readonly record struct UiScriptItem(string Label, string Value);

/// <summary>
/// One entry of an options script (cfg/user_default.scr's "cvar" { "prompt"
/// { type } { default } }): the convar, its prompt and tooltip, how it is
/// edited, a number's range (-1 for none), a list's choices, its current
/// value, and whether it is sent with setinfo.
/// </summary>
public readonly record struct UiScriptEntry(
    string Name, string Prompt, string? Tooltip, UiScriptKind Kind, float Min, float Max,
    IReadOnlyList<UiScriptItem> Items, string Value, bool SetInfo);

/// <summary>
/// The multiplayer options' advanced entries (stock: the advanced multiplayer
/// dialog over cfg/user_default.scr and cfg/user.scr): read when the page
/// loads; saved, each value goes to its convar and the script to
/// cfg/user.scr.
/// </summary>
public interface IOptionsScriptSource
{
    IReadOnlyList<UiScriptEntry> Load();

    void Save(IReadOnlyList<UiScriptEntry> entries, IReadOnlyList<string> values);

    /// <summary>A button entry pressed: its value, run as a console line.</summary>
    void Press(UiScriptEntry entry);
}

/// <summary>
/// The create-server game page's script (stock: cfg/settings_default.scr,
/// then cfg/settings.scr; SERVER_OPTIONS), as <see cref="IOptionsScriptSource"/>
/// answers the advanced multiplayer one.
/// </summary>
public interface IServerSettingsSource
{
    IReadOnlyList<UiScriptEntry> Load();

    void Save(IReadOnlyList<UiScriptEntry> entries, IReadOnlyList<string> values);

    void Press(UiScriptEntry entry);
}

/// <summary>
/// The maps a server can start on (stock's create-server map list): the
/// maps on the GAME paths less the mod's hidden maps, by name; and the
/// game's name, the server name a script does not give one. Read when the
/// window opens.
/// </summary>
public interface IMapListSource
{
    IReadOnlyList<string> Maps();

    string GameName { get; }
}

/// <summary>
/// Starts a listen server as stock's create-server OK did, with the mod pages'
/// console lines (<paramref name="beforeMap"/> of <c>Start</c>) after the
/// server's size and name and before the map.
/// </summary>
public interface ICreateServerActions
{
    void Start(string map, int maxPlayers, string hostName, string password, IReadOnlyList<string> beforeMap);
}

/// <summary>One player as the player list shows them: their name, and whether muted, a bot, or this client.</summary>
public readonly record struct PlayerListEntry(string Name, bool Muted, bool Bot, bool Local);

/// <summary>
/// The players in the game and this client's mutes of them (stock's player
/// list dialog): refreshed when the window opens and after a mute, read by
/// index each frame. A bot or this client cannot be muted.
/// </summary>
public interface IPlayerListSource
{
    void Refresh();

    int Count { get; }

    PlayerListEntry Player(int i);

    void ToggleMute(int i);
}

/// <summary>One button of the taskbar: an open window, its title token, whether it is on top or minimised.</summary>
public readonly record struct UiTaskbarWindow(string Screen, string TitleToken, bool Top, bool Minimised);

/// <summary>
/// The taskbar's windows: every open window of the menu layer, in the order
/// they were first shown (a button keeps its place when its window is raised).
/// </summary>
public interface ITaskbarWindows
{
    int WindowCount { get; }

    UiTaskbarWindow Window(int i);

    /// <summary>A button clicked: a window under others (or minimised) comes to the top; the top one minimises.</summary>
    void Activate(int i);
}

/// <summary>The taskbar's tray: the settings quick-open and the local time.</summary>
public interface ITaskbarTray
{
    /// <summary>Whether the options window is up (its button shows it).</summary>
    bool SettingsOpen { get; }

    /// <summary>Opens the options window, on top.</summary>
    void OpenSettings();

    /// <summary>The local time as H:mm.</summary>
    string Clock { get; }
}

/// <summary>
/// The game menu's commands by stock's names (OpenServerBrowser,
/// OpenCreateMultiplayerGameDialog, OpenPlayerListDialog, Disconnect, Quit, ...),
/// as a menu's buttons send them; the GameUI's table answers each.
/// </summary>
public interface IMainMenuCommands
{
    void Run(string command);
}

/// <summary>A button a mod puts on the taskbar (TF2's ITEMS and gears): its label token, and whether it stands in the tray beside Settings rather than at the bar's left.</summary>
public readonly record struct UiTaskbarLauncher(string Token, bool Tray);

/// <summary>
/// The buttons a mod puts on the taskbar while its menu is up, in order
/// (index-and-count, read each frame): a mod's menu moves its bottom row
/// here rather than draw it under the bar.
/// </summary>
public interface ITaskbarLaunchers
{
    int LauncherCount { get; }

    UiTaskbarLauncher Launcher(int i);

    void Launch(int i);
}

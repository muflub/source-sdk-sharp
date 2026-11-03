namespace TF2.Server.Player;

/// <summary>
/// The TF player's stages (Stage 2.3's band, +300..+399 of TF2's thousand,
/// below the mods' <c>ModStages.First</c>): every ordered hook and lifecycle
/// participant of this lane takes one of these, so none ties another
/// (TF2LifecycleOrderTests).
/// </summary>
public static class TFPlayerOrders
{
    private const int Band = 1_000_000 - 1_000 + 300;

    /// <summary>The spawn arm: the class, the health, the model (<c>TFPlayerSpawns</c>).</summary>
    public const int Spawn = Band + 0;

    /// <summary>The class menu's server commands (<see cref="TFClassMenuCommands"/>), on +75 between the composite and the death cam; +10..+19 are the anim hooks' band (TF2LifecycleOrderTests).</summary>
    public const int ClassMenu = Band + 75;

    /// <summary>
    /// The round listener: the winners' crit boost (<c>TFRoundWinCrits</c>).
    /// It shipped at <c>+10</c>; that slot and <c>+11..+19</c> belong to the
    /// anim hook band — the <c>ITFAnimHook</c> chain's orders (plan D10, held
    /// by <c>TF2LifecycleOrderTests</c>) — so it took the free <c>+40</c>.
    /// </summary>
    public const int RoundWinCrits = Band + 40;

    /// <summary>The class models' precache at level init (<c>TFClassModels</c>).</summary>
    public const int ClassModels = Band + 20;

    /// <summary>The player resource's creation at level init.</summary>
    public const int PlayerResource = Band + 30;

    /// <summary>The building status carrier's creation at level init (<see cref="TFBuildingResourceSpawner"/>).</summary>
    public const int BuildingResource = Band + 80;


    /// <summary>The items behind the weapons, dropped at level shutdown (<c>TFPlayerItems</c>).</summary>
    public const int PlayerItems = Band + 50;

    /// <summary>The players' stats: their round reset, their level end, their departure (<c>TFPlayerStats</c>).</summary>
    public const int Stats = Band + 60;

    /// <summary>The win panel's most-recent-cappers, a round listener like the stats (<c>TFWinPanelCappers</c>).</summary>
    public const int WinPanel = Band + 65;

    /// <summary><see cref="TFPlayerComponents"/>: the carrier, the class and the condition countdown, in the composite's source chain.</summary>
    public const int Components = Band + 70;

    /// <summary>The death cam: State_Think_DYING's StartObserverMode( OBS_MODE_DEATHCAM ) (<c>TFDeathCam</c>).</summary>
    public const int DeathCam = Band + 80;
    /// <summary>The kit gate: the unassigned player watches with nothing (<see cref="TFKitGate"/>).</summary>
    public const int KitGate = Band + 85;

    /// <summary>
    /// Death's ammo pack (<see cref="TFDeathDrops"/>): the pack a death
    /// leaves behind, and the cleanup that keeps one owner to three.
    /// </summary>
    public const int DeathDrops = Band + 90;

    /// <summary>The players' last-objective times, dropped at the level's end and the player's departure (<c>TFPlayerObjectiveTimes</c>).</summary>
    public const int ObjectiveTimes = Band + 95;

    /// <summary>Whether the death's body arrived burning (<see cref="TFBurnRagdoll"/>).</summary>
    public const int BurnRagdoll = Band + 96;

    /// <summary>
    /// Death's walk of the respawn-override volumes (<see cref="TFRespawnOverrideWalk"/>):
    /// the pair the death takes from the volume holding the player.
    /// </summary>
    public const int DeathRespawnOverride = Band + 97;
    /// <summary>The condition effect arms: the add and removal arms of stock's <c>OnConditionAdded</c>/<c>OnConditionRemoved</c> (<see cref="TFPlayerConditionEffectArms"/>). It runs in its own chain, so nothing orders it against the death walk; it took <c>+98</c>, the first free value, after <c>DeathRespawnOverride</c> landed on <c>+97</c>.</summary>
    public const int ConditionEffectArms = Band + 98;

    /// <summary>
    /// The dropped weapon's pickup, on the use key's action-slot verbs
    /// (<c>TFDroppedWeaponPickup</c>): after the death walk whose lies it
    /// picks up, on <c>+99</c>.
    /// </summary>
    public const int UseActionSlot = Band + 99;

    /// <summary>The condition cheat commands <c>addcond</c>/<c>removecond</c> (<see cref="TFConditionCommands"/>).</summary>
    public const int ConditionCommands = Band + 89;

    /// <summary>
    /// The <c>taunt</c> command pair (<see cref="TFTauntCommand"/>): on +100,
    /// past the +92 the gib and the settings share (they tie each other in
    /// their own chains); every ordered hook needs its own value here.
    /// </summary>
    public const int TauntCommand = Band + 100;

    /// <summary>
    /// Whether the death's body arrived in pieces (<see cref="TFPlayerGib"/>):
    /// it reads the killing blow before the notice clears the records.
    /// </summary>
    public const int PlayerGib = Band + 92;

    /// <summary>
    /// The spawn's escort stop (<see cref="TFEscortSpawnStop"/>): stock's
    /// <c>CBaseMultiplayerPlayer::Spawn</c> unschedules the escort scoring
    /// before the base Spawn (basemultiplayerplayer.cpp:234). On +91, the
    /// first free value past the death walks: the spawn prologue's arms stand
    /// beside them, not inside them.
    /// </summary>
    public const int EscortSpawnStop = Band + 91;

    /// <summary>The per-player settings (<see cref="TFPlayerSettings"/>): the userinfo hook's cached convars, the first free value past the escort.</summary>
    public const int Settings = Band + 92;

    /// <summary>
    /// The spawn's choice of held weapon (<see cref="TFSpawnWeaponWalk"/>):
    /// stock's primary/secondary/melee fallback after the kit
    /// (tf_player.cpp:4712-4726), on +93 past the settings.
    /// </summary>
    public const int SpawnWeaponWalk = Band + 93;

    /// <summary>
    /// The response rules' level precache (<see cref="TF2.Server.Voice.TFResponsePrecache"/>):
    /// the level's scenes and speak entries sounded before anything can ask for
    /// them, on +94 — the last free value in the band, after the spawn walks.
    /// </summary>
    public const int ResponsePrecache = Band + 94;
}

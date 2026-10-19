using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// One game mode's half of the rules: a mode composes these, and the core asks
/// <see cref="IGameMode"/> rather than a rules class it can neither replace nor
/// subclass (the ruling U1 this machinery exists for).
///
/// <para>
/// Each concern is its OWN interface for one reason: a mode that owns rounds
/// but not scoring must be able to say so, and <c>null</c> from
/// <see cref="IGameMode.Respawn"/> — or any other optional concern — is the
/// mode saying "the core's own rule, unchanged". The core default is never a
/// guess at what the mode might have meant; it is the behaviour the game had
/// before any mode existed, and every no-owner fact in the modes' tests is the
/// proof that it still runs.
/// </para>
/// </summary>
public interface IGameMode
{
    /// <summary>
    /// The name <see cref="GameModes.SwitchTo"/>, <c>sv_mode</c> and the
    /// <c>game_mode_switch</c> entity's <c>SwitchTo</c> input ask for.
    /// Lower-case, stable across builds: a map author types it.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The round machine: when it thinks, when it is over, what a reset
    /// clears. Required — a mode with no round machine is a mode whose round
    /// never ends and never resets, which is a claim to make by implementing
    /// this, not by leaving it out.
    /// </summary>
    IRoundFlow Rounds { get; }

    /// <summary>The respawn wait and release; null is the core's 3-second deathmatch answer.</summary>
    IRespawnPolicy? Respawn => null;

    /// <summary>
    /// The active mode's spawn points: asked by <see cref="Teams.SelectSpawn"/>
    /// BEFORE the registered <see cref="ISpawnSelector"/> chain (the round's
    /// owner first). Null, or a None answer, leaves it to the chain and then
    /// the core's walk.
    /// </summary>
    ISpawnSelector? Spawns => null;

    /// <summary>The extra score writes a mode keeps beside the core's scoreboard; null writes none.</summary>
    IScoring? Scoring => null;

    /// <summary>What a round needs prepared; null prepares nothing.</summary>
    IModeSetup? Setup => null;
}

/// <summary>
/// The round machine of the active mode.
///
/// <para>
/// The moves of today's <c>GameRules</c> frag/time-limit machine (its Think,
/// the round-end inside it, and ResetRound), put behind the interface so a
/// mode owns them. With no mode but <see cref="NullMode"/> they run exactly
/// that machine — the proxy is never allowed to change deathmatch by existing.
/// </para>
/// </summary>
public interface IRoundFlow
{
    /// <summary>The frame pass: the win check, and whatever else ticks per frame.</summary>
    void Think(float now);

    /// <summary>Whether the round has ended (and so ends once, and scores nothing more).</summary>
    bool RoundOver { get; }

    /// <summary>
    /// Back to a running round, players kept. The flush a mode switch runs
    /// first, and the reset a level init runs; the entity walk
    /// <see cref="GameModes.ResetRoundEntities"/> does beside it is the
    /// in-place answer to stock's <c>CleanUpMap</c> (a divergence row).
    /// </summary>
    void ResetRound();

    /// <summary>
    /// A player joined the living, called after the core has given them
    /// their loadout and started their spawn protection. <see cref="NullMode"/>
    /// adds nothing — deathmatch's spawn bookkeeping IS the core's — and a
    /// mode that readies a player per class owns it here.
    /// </summary>
    void OnPlayerSpawned(EntityHandle player, float now);

    /// <summary>
    /// What one death does beyond the core's scoreboard write, called after
    /// that write with the engine's own account of the kill. The plan named
    /// this <c>OnPlayerKilled(PlayerDeath)</c>; the caller
    /// (<c>GameRules.ScoreDeath</c>, on the engine's <c>player_death</c>
    /// event) holds the event's <see cref="PlayerDeathArgs"/> — userids and
    /// weapon — and nothing else, so the contract carries what exists rather
    /// than a handle-shaped guess (reported as a corrected plan citation).
    /// </summary>
    void OnPlayerKilled(PlayerDeathArgs death);
}

/// <summary>
/// When the dead come back, for the active mode.
///
/// <para>
/// Stock HL2MP answers both halves of this from <c>CGameRules</c> and
/// <c>CHL2MP_Player::PlayerDeathThink</c>; this build's answer was
/// <c>Players.RespawnDelay</c> (3 s, one button press) for everyone. A mode
/// that wants team waves or a class-scaled wait owns both halves: a long
/// delay plus a <c>false</c> wave release keeps a player dead until the mode
/// says otherwise.
/// </para>
/// </summary>
public interface IRespawnPolicy
{
    /// <summary>
    /// Seconds this player waits, measured from their death. The core's
    /// default is 3 seconds; a mode returning more delays the button, less
    /// hastens it.
    /// </summary>
    float Delay(EntityHandle player);

    /// <summary>
    /// Whether the mode lets them out now — a wave's release, or the plain
    /// "yes, their wait is over". The core's default answers true; a mode
    /// that waves answers false until the wave fires.
    /// </summary>
    bool WaveRelease(EntityHandle player, float now);
}

/// <summary>
/// The score writes of the active mode, beside the core's scoreboard.
///
/// <para>
/// The core's own scoring (points on <see cref="IScoreboard"/> for the frag
/// limit, the engine's own frag counters for the roster) is not replaceable
/// and is not what this is: this is the writes a mode ADDS — the team score
/// fields HL2MP has no equivalent of because it has no teams. A mode with no
/// team score registers nothing here, and the scoreboard alone answers.
/// </para>
/// </summary>
public interface IScoring
{
    /// <summary>One death's extra writes, after the core's own have been made.</summary>
    void OnDeath(PlayerDeathArgs death);

    /// <summary>A team's current score, for the scoreboard; negative for "no such team".</summary>
    int TeamScore(int team);

    /// <summary>Per-round state: the mode's own score totals go back to zero.</summary>
    void ResetRound();
}

/// <summary>
/// What the active mode prepares before the round is allowed to run —
/// teams assigned, loadouts decided, the map's mode-owned entities told.
///
/// <para>
/// Runs on a switch, after the flush and before warmup, and at level init for
/// the picked mode. The one place a mode arranges the world before a player
/// walks into it, so that a failed setup is a round that never starts rather
/// than a broken round.
/// </para>
/// </summary>
public interface IModeSetup
{
    /// <summary>Arrange the world for this mode, once per switch and level init.</summary>
    void SetupRound(float now);
}

/// <summary>
/// Who is playing: the mode-selector chain, asked at level init.
///
/// <para>
/// A CHAIN: every registered selector is asked in
/// <see cref="IOrderedHook.HookOrder"/>'s order and the first non-null answer
/// names the mode — a mod that pins its own mode, a map script that asks for
/// the round to be different, the core's default last. Returning <c>null</c>
/// is "no opinion", and a selector that throws is logged and skipped: a
/// broken mod must never strand a server with no mode, because no mode means
/// no round machine means the game does not start.
/// </para>
/// </summary>
public interface IGameModeSelector
{
    /// <summary>
    /// The mode's <see cref="IGameMode.Id"/>, or null for "ask the next one".
    /// An id nothing registered is an unknown id: <see cref="GameModes"/>
    /// logs it and the game keeps the rules it had.
    /// </summary>
    string? PickMode();
}

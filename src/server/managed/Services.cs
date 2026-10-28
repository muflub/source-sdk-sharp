using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The rules of the mode, as anything that has to obey them sees them.
///
/// <para>
/// What is NOT here is as deliberate as what is. <c>Think</c> and
/// <c>ResetRound</c> are lifecycle — the module drives them, once per tick and
/// once per level — and an entity able to call either from the middle of a tick
/// could end a round halfway through a frame, or reset one from inside a touch.
/// Anything on this interface can be asked at any moment without surprising
/// what is running.
/// </para>
/// </summary>
public interface IGameRules
{
    /// <summary>
    /// Whether an attack lands. <see cref="Decision.UseStockRule"/> defers.
    /// <paramref name="hitGroup"/> is the hit's studio hitbox group (a traced
    /// shot's; Generic otherwise), handed on to the mods' damage rules.
    /// </summary>
    Decision CanTakeDamage(EntityHandle player, EntityHandle attacker,
                           float damage, int damageType, float now,
                           HitGroup hitGroup = HitGroup.Generic);

    /// <summary>Whether a dead player may come back yet.</summary>
    Decision CanRespawn(EntityHandle player);

    /// <summary>How long they wait first. Negative means the mod has no opinion.</summary>
    float GetRespawnDelay(EntityHandle player);

    /// <summary>Whether spawn protection is still holding for a player.</summary>
    bool IsProtected(EntityHandle player, float now);

    /// <summary>Whether the round has already been ended.</summary>
    bool RoundOver { get; }

    /// <summary>Starts a player's spawn protection.</summary>
    void OnPlayerSpawned(EntityHandle player, float now);

    /// <summary>Drops what is remembered about a player who has left.</summary>
    void Forget(EntityHandle player);
}

/// <summary>
/// The players, and what the server remembers about each of them.
///
/// <para>
/// The settings on it — how long the wait is, what you watch while you wait,
/// whether you leave your weapon behind — are readable and writable rather than
/// fixed, because they are exactly what a map entity legitimately tunes. A
/// mapper writing a mode where you keep your weapon is doing a normal thing,
/// and the alternative is another convar for every one of them.
/// </para>
///
/// <para>
/// <c>Think</c> and <c>Reset</c> are off it for the same reason they are off
/// <see cref="IGameRules"/>: they are the module's, once a tick and once a
/// level.
/// </para>
/// </summary>
public interface IPlayerDirectory
{
    /// <summary>The record for a player, created on first use.</summary>
    PlayerRecord this[EntityHandle player] { get; }

    /// <summary>Whether anything is known about a player, without inventing it.</summary>
    bool Knows(EntityHandle player);

    /// <summary>Everyone the server has a record for.</summary>
    IEnumerable<(EntityHandle Player, PlayerRecord Record)> All { get; }

    /// <summary>How much longer a dead player has to watch. Zero once they may come back.</summary>
    float WaitRemaining(EntityHandle player, float now);

    /// <summary>How long a dead player watches before they may come back.</summary>
    float RespawnDelay { get; set; }

    /// <summary>What a dead player watches.</summary>
    ObserverMode DeathView { get; set; }

    /// <summary>Whether a dead player leaves their weapon behind.</summary>
    bool DropWeaponOnDeath { get; set; }

    /// <summary>How hard that weapon is thrown, along the victim's aim.</summary>
    float DropSpeed { get; set; }

    /// <summary>Records a spawn.</summary>
    void OnSpawned(EntityHandle player, float now);

    /// <summary>Records a death and credits the kill.</summary>
    void OnKilled(EntityHandle player, EntityHandle attacker, float now);

    /// <summary>Drops a player who has left.</summary>
    void Forget(EntityHandle player);
}

/// <summary>
/// The pickups in the world and the ones waiting to come back.
///
/// <para>
/// Wanted by more than the console command that spawns one: an item entity
/// asking whether the thing it just touched is a live pickup, a rule handing
/// out a pickup where somebody died, a trigger that clears them. All of those
/// need the same bookkeeping, and none of them should reach it through a static
/// field somebody has to remember to assign.
/// </para>
/// </summary>
public interface IPickupDirectory
{
    /// <summary>How many pickups are in the world.</summary>
    int LiveCount { get; }

    /// <summary>How many are waiting to come back.</summary>
    int PendingCount { get; }

    /// <summary>Spawns one. <see cref="EntityHandle.None"/> if the entity could not be created.</summary>
    EntityHandle Spawn(PickupKind kind, Vector origin);

    /// <summary>Whether an entity is one of the live pickups.</summary>
    bool IsLive(EntityHandle entity);

    /// <summary>A player walked into one. True if it was collected.</summary>
    bool OnTouched(EntityHandle entity, EntityHandle other, float now);

}

/// <summary>
/// Which buttons a player is holding down.
///
/// <para>
/// A one-method service, and it replaces a static delegate the module had to
/// remember to assign — <c>GameUi.ReadButtons</c>, which was null in every test
/// and in any build where that assignment was missed, and which reported
/// "nothing held" either way. A missing service is a message naming the entity
/// that wanted it; a null delegate is a player who never presses anything.
/// </para>
///
/// <para>
/// It is a service rather than an engine call because the buttons already cross
/// the boundary once per command for the movement rules, and the module keeps
/// the last set. Asking the engine again per entity per tick would pay for the
/// same answer twice.
/// </para>
/// </summary>
public interface IPlayerButtons
{
    /// <summary>The button mask a player is holding, or zero if nothing is known.</summary>
    int Held(EntityHandle player);
}

/// <summary>
/// The last button mask the server saw from each player.
///
/// <para>
/// Kept rather than acted on. The buttons already cross the boundary once per
/// player per tick for the movement rules, and several unrelated things want to
/// read them afterwards — the death cycle asking whether a dead player is
/// trying to come back, and <c>game_ui</c> reporting which keys the player it
/// captured is holding. One store answering both is cheaper than a second ABI
/// call, and it is the only place a scripted entity needs them.
/// </para>
/// </summary>
public sealed class PlayerButtons : IPlayerButtons, IGameLifecycle
{
    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PlayerInput;

    void IGameLifecycle.OnLevelInit(string mapName) => Reset();

    private readonly Dictionary<uint, int> _held = new();

    /// <summary>Takes a tick's worth of input for one player.</summary>
    public void Record(EntityHandle player, int buttons) => _held[player.Value] = buttons;

    /// <inheritdoc/>
    public int Held(EntityHandle player) => _held.TryGetValue(player.Value, out int b) ? b : 0;

    /// <summary>
    /// Drops everything, at a level change.
    ///
    /// Handles do not survive one, so a kept entry would report the buttons of
    /// a player who no longer exists — under a handle the engine may since have
    /// reused for something else entirely.
    /// </summary>
    public void Reset() => _held.Clear();
}

/// <summary>
/// Which soundscape each player is hearing.
///
/// <para>
/// A service both ways round, which is why it earns an interface more clearly
/// than most: an <c>env_soundscape</c> puts ITSELF in it at spawn to get its
/// identity token, and a <c>trigger_soundscape</c> QUERIES it to say that a
/// player has walked into a volume. Neither can do its job from its own state
/// alone — the choice is made across every soundscape in the map and every
/// player on the server.
/// </para>
///
/// <para>
/// <c>Frame</c> and <c>Reset</c> are off it, matching every other service here:
/// the per-tick pass is load-balanced across players and holds a round-robin
/// cursor, and an entity able to run it from inside its own touch would move
/// that cursor under the module.
/// </para>
/// </summary>
public interface ISoundscapes
{
    /// <summary>Every registered soundscape, in spawn order.</summary>
    IReadOnlyList<EnvSoundscape> All { get; }

    /// <summary>Adds a soundscape and returns its identity token, which is one-based.</summary>
    int Register(EnvSoundscape soundscape);

    /// <summary>A player entered a volume naming a soundscape.</summary>
    void EnterTrigger(EntityHandle player, EnvSoundscape soundscape);

    /// <summary>A player left one. Whatever they were in before takes over again.</summary>
    void LeaveTrigger(EntityHandle player, EnvSoundscape soundscape);

    /// <summary>
    /// The index both sides of the wire share for a soundscape script name, or
    /// -1 when the level's library has none by that name.
    /// </summary>
    int IndexOf(string name);

    /// <summary>The level's soundscape library; null before the first level.</summary>
    SourceSharp.Shared.Soundscapes.SoundscapeLibrary? Library { get; }

    /// <summary>How many trigger volumes a player is inside.</summary>
    int TriggerDepth(EntityHandle player);

    /// <summary>The soundscape a player is hearing, by id. Zero for none.</summary>
    int CurrentId(EntityHandle player);

    /// <summary>A player left the game.</summary>
    void Forget(EntityHandle player);
}

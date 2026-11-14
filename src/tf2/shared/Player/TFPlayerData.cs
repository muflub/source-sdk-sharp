using SourceSharp.Abi;

using TF2.Shared.Conditions;

namespace TF2.Shared.Player;

/// <summary>
/// The per-player TF state a predicted rule reads and writes, on either side:
/// how a movement rule (1.8) or a weapon (1.9) reaches the player's
/// <c>tf_player_data</c> carrier by the entity index movement hands it.
/// One owner per side: the server's carriers and the client's prediction
/// participant each register it with <c>As = [typeof(ITFPlayerData)]</c>.
/// </summary>
public interface ITFPlayerData
{
    /// <summary>
    /// The state a rule running for <paramref name="playerIndex"/>'s command
    /// reads and writes, or null when there is none.
    ///
    /// <para>
    /// SERVER: the carrier's networked state itself. CLIENT: the local
    /// player's WORKING COPY, which the replay resets to the decoded state
    /// once a frame and saves after each replayed command; null for every
    /// other player (nobody else is predicted) and before the local carrier
    /// has arrived. A client write therefore never reaches the decoded state
    /// and reaches the prediction ring only through a replayed command.
    /// </para>
    /// </summary>
    TFPlayerNetState? Predicted(int playerIndex);

    /// <summary>
    /// The state as the server last said it, for reading (the HUD, effects,
    /// another player's class or conditions): the server's live state, the
    /// client's decoded copy. Null when the player has no carrier.
    /// </summary>
    TFPlayerNetState? Networked(int playerIndex);

    /// <summary>
    /// The rise arm's enemy-blast read (<c>CTFGameMovement::CategorizePosition</c>'s
    /// GAME_DLL latch arm, tf_gamemovement.cpp:2360-2364): with the player's
    /// blast latch up the rise names the enemy-blasted jump. Stock's client
    /// build compiles the arm away; the client's answer is the default nothing.
    /// </summary>
    void EnemyBlasted(int playerIndex) { }

    /// <summary>
    /// The end of one command (tf_gamemovement.cpp:334-336): the server drops
    /// what its blast latch kept for the command's rise. Client side: nothing,
    /// as stock's GAME_DLL arm.
    /// </summary>
    void EndCommand(int playerIndex) { }

    /// <summary>
    /// <c>CTFPlayerShared::EndCharge</c> (tf_player_shared.cpp:6070-6095),
    /// the charge's end behind the walk's two asks: in the charge, and under
    /// 90 meter, the shield's bash, the next swing's crit and the duration
    /// zero that removes the condition on the next condition think. Server
    /// side: the whole body is stock's <c>GAME_DLL</c>; the client's answer is
    /// the default nothing.
    /// </summary>
    void EndCharge(int playerIndex) { }

    /// <summary>
    /// Whether the entity is one of the mod's players: the bump arm's
    /// <c>ToTFPlayer</c> over a blocked walk's trace hit
    /// (tf_gamemovement.cpp:1925) — the world and every non-player standing
    /// ends no charge. The server's carriers answer from the entity's kind;
    /// the client inherits the default no, its half of the arm being stock's
    /// server-only one.
    /// </summary>
    bool IsPlayerEntity(EntityHandle entity) => false;

    /// <summary>
    /// The Force-a-Nature's shove, its real half (<c>CTFScatterGun::FireBullet</c>'s
    /// <c>SetAbsVelocity</c> and <c>StunPlayer</c>, tf_weapon_shotgun.cpp:353, :335):
    /// <paramref name="velocity"/> becomes the player's own, the ground flag and
    /// entity go with it, and the 0.3 s forward-only movement stun lands. The
    /// server's carriers answer; the client's replay inherits the default
    /// nothing, the predicted velocity above being its whole half.
    /// </summary>
    void ScattergunKnock(int playerIndex, Vector velocity) { }

    /// <summary>
    /// The Thermal Thruster's launch, its real half (<c>CTFRocketPack::RocketLaunchPlayer</c>'s
    /// <c>SetAbsVelocity</c> and <c>ApplyAbsVelocityImpulse</c> under the preserve-convars'
    /// zeroing, tf_weapon_rocketpack.cpp:465-477, with the half-second movement stun :461):
    /// <paramref name="velocity"/> becomes the player's own and the stun lands. The server's
    /// carriers answer; the client's replay inherits the default nothing, the predicted
    /// velocity above being its whole half. The pop and the flight each call it once.
    /// </summary>
    void ThrusterLaunch(int playerIndex, Vector velocity) { }
}

/// <summary>Typed views over <see cref="TFPlayerNetState"/>'s words.</summary>
public static class TFPlayerNetStateViews
{
    /// <summary>The player's class.</summary>
    public static TFClass Class(this TFPlayerNetState state) => (TFClass)state.PlayerClass;

    /// <summary>Sets the player's class.</summary>
    public static void SetClass(this TFPlayerNetState state, TFClass playerClass) => state.PlayerClass = (int)playerClass;

    /// <summary>The class a disguised spy shows.</summary>
    public static TFClass DisguiseAs(this TFPlayerNetState state) => (TFClass)state.DisguiseClass;

    /// <summary>The five condition words as one mask.</summary>
    public static TFCondMask Conditions(this TFPlayerNetState state)
        => new(state.PlayerCond, state.PlayerCondEx, state.PlayerCondEx2, state.PlayerCondEx3, state.PlayerCondEx4);

    /// <summary>Writes the five condition words from a mask.</summary>
    public static void SetConditions(this TFPlayerNetState state, in TFCondMask bits)
    {
        state.PlayerCond = bits.Word0;
        state.PlayerCondEx = bits.Word1;
        state.PlayerCondEx2 = bits.Word2;
        state.PlayerCondEx3 = bits.Word3;
        state.PlayerCondEx4 = bits.Word4;
    }

    /// <summary>
    /// <c>CTFPlayerShared::IsInvulnerable</c> (src/game/shared/tf/tf_player_shared.cpp:8099):
    /// any of the four invulnerable conditions.
    /// </summary>
    public static bool IsInvulnerable(this TFPlayerNetState state)
    {
        TFCondMask c = state.Conditions();
        return c.Has(TFCond.Invulnerable) || c.Has(TFCond.InvulnerableUserBuff)
            || c.Has(TFCond.InvulnerableHideUnlessDamaged) || c.Has(TFCond.InvulnerableCardEffect);
    }

    /// <summary><c>CTFPlayerShared::IsStealthed</c> (tf_player_shared.cpp:8112).</summary>
    public static bool IsStealthed(this TFPlayerNetState state)
    {
        TFCondMask c = state.Conditions();
        return Movement.TFMaxSpeed.IsStealthed(in c);
    }

    /// <summary>
    /// <c>CTFPlayerShared::GetCritMult</c> (tf_player_shared.cpp:9504-9514):
    /// <see cref="TFPlayerNetState.CritMult"/>'s 0..255 as 1x..4x.
    /// </summary>
    public static float CritMultiplier(this TFPlayerNetState state)
        => CritBucket.RemapValClamped(state.CritMult, 0f, 255f, 1f, 4f);

    /// <summary><c>InCond</c> over the networked words.</summary>
    public static bool InCond(this TFPlayerNetState state, TFCond cond) => state.Conditions().Has(cond);

    /// <summary>
    /// <c>CTFPlayerShared::IsPlayerDominated</c> (tf_player_shared.cpp:
    /// 7737-7756) over the networked words: does the player dominate
    /// <paramref name="playerIndex"/>? Stock's client arm answers only the
    /// local player's data (:7739-7751), which is what the scoreboard's
    /// dominated arm reads (tf_clientscoreboard.cpp:1514).
    /// </summary>
    public static bool IsPlayerDominated(this TFPlayerNetState state, int playerIndex)
        => (uint)playerIndex < TFPlayerResourceNetState.Slots && state.PlayerDominated[playerIndex];

    /// <summary>
    /// <c>CTFPlayerShared::IsPlayerDominatingMe</c> (tf_player_shared.cpp:
    /// 7761-7764) over the networked words: does <paramref name="playerIndex"/>
    /// dominate the player? The scoreboard's nemesis arm
    /// (tf_clientscoreboard.cpp:1509 through the client's fallthrough :7751).
    /// </summary>
    public static bool IsPlayerDominatingMe(this TFPlayerNetState state, int playerIndex)
        => (uint)playerIndex < TFPlayerResourceNetState.Slots && state.PlayerDominatingMe[playerIndex];
}

/// <summary>
/// A carrier's five condition words as a <see cref="TFConditions"/>' active
/// set: what binds a player's condition set to the state the network and the
/// prediction carry.
/// </summary>
public sealed class TFCarrierCondWords(TFPlayerNetState state) : ITFCondWords
{
    /// <summary>The state whose words these are.</summary>
    public TFPlayerNetState State { get; } = state ?? throw new ArgumentNullException(nameof(state));

    public TFCondMask Read() => State.Conditions();

    public void Write(in TFCondMask bits) => State.SetConditions(in bits);
}

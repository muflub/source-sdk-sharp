//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities.Items;
using TF2.Server.Voice;

using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// The taunt's server half: the <c>taunt</c>/<c>weapon_taunt</c> command pair,
/// the <c>stop_taunt</c> and <c>-taunt</c> consumed arms of
/// <c>CTFPlayer::ClientCommand</c> (tf_player.cpp:7507-7533), the gates of
/// <c>CTFPlayer::IsAllowedToTaunt</c> (tf_player_shared.cpp:676-766), and what
/// <c>OnTauntSucceeded</c> leaves behind for the base weapon taunt
/// (tf_player.cpp:17732-17768): <c>TF_COND_TAUNTING</c> with a counted
/// duration — stock adds the condition permanent and sets the remove time to
/// <c>curtime + flDuration</c> with <c>m_bAllowedToRemoveTaunt</c> (the
/// ConditionThink expiry, tf_player_shared.cpp:2761-2766, is exactly our
/// timed-condition countdown here).
///
/// <para>
/// The taunt itself is its scene's speak (<c>Taunt</c>, tf_player.cpp:17862):
/// <c>MP_CONCEPT_PLAYER_TAUNT</c> asked with the held weapon's word, the
/// per-class rules answering the taunt scene — and a search with no answer is
/// no taunt (<c>m_bInitTaunt</c> back down, no condition, no window). The
/// scene sounds through the speech as the concepts do.
/// </para>
///
/// <para>
/// <c>flDuration</c> is the scene length plus the fixed 0.2 tail, divided by
/// the <c>mult_gesture_time</c> hook on the player's own attribute manager
/// (:17734-17738). The rate-limit clock (<c>m_flTauntNextStartTime</c>, asked
/// at :7511 and stamped at :17744) is this object's own, per player, as the
/// scene system that measures a real scene length is not in this build: the
/// scene length is the documented fallback constant until the gesture's own
/// sequence length arrives with the deferred row.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFTauntCommand(ITFMapPlayers players, IEngineNetworking networking, IGameClock clock,
                                   IEnginePlayers engine, TFPlayerCarriers carriers, TFConceptSpeech speech,
                                   ITFWeaponOwners owners, AttributeHooks hooks,
                                   Voice.TFSceneImage scenes)
    : IClientCommandHook, IOrderedHook
{
    /// <summary><c>taunt</c> / <c>weapon_taunt</c> (:7507).</summary>
    public const string Command = "taunt";

    /// <summary>The fixed tail behind the scene (:17734).</summary>
    public const float TauntTailSeconds = 0.2f;

    /// <summary>
    /// The scene length for a side with no scene image: stock's half of the
    /// duration is <c>GetSceneDuration</c>, which <see cref="Voice.TFSceneImage"/>
    /// answers from the game's own <c>scenes/scenes.image</c> where it stands.
    /// </summary>
    public const float BaseTauntSceneSeconds = 2.8f;

    private readonly Dictionary<int, float> _nextStart = [];

    public int HookOrder => TFPlayerOrders.TauntCommand;

    /// <summary>The keying's home on this player, if its composite carries one.</summary>
    private TFTauntAttack? Attack(EntityHandle player) => carriers?.Part<TFTauntAttack>(player.EntryIndex);

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        // The release arm does nothing at all (:7528-7533), and the stop's
        // long-taunt arm (:7519-7527) rides the long-taunt index, which the
        // base-only port leaves in the deferred row: both consume.
        if (string.Equals(command, "-taunt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "stop_taunt", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.Equals(command, Command, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(command, "weapon_taunt", StringComparison.OrdinalIgnoreCase))
            return false;

        float now = clock.Now;

        // The rate-limit read (:7511): a press before the last taunt's window
        // ended is consumed with nothing done; never having taunted passes.
        if (_nextStart.TryGetValue(player.EntryIndex, out float next) && next >= now)
            return true;

        if (!IsAllowedToTaunt(player))
            return true;

        Begin(player, TFConceptSpeech.PlayerTaunt);
        return true;
    }

    /// <summary>The gates and the taunt in one ask — the special taunts' entry, stock's
    /// <c>Taunt</c> running its own <c>CanStartTaunt</c> checks: the caller
    /// that has not gated asks here (the Eureka Effect's does).</summary>
    internal bool TauntNow(EntityHandle player, string concept)
        => IsAllowedToTaunt(player) && Begin(player, concept);

    /// <summary>
    /// The taunt itself, stock's <c>CTFPlayer::Taunt</c> past its gates
    /// (:17732-17768): the speak — <paramref name="concept"/> with the held
    /// weapon's word, no answer no taunt — the matched scene keyed into the
    /// timed attack (:17979), the duration with its tail over
    /// <c>mult_gesture_time</c>, the condition, and the next-start stamp
    /// (:17744). The command's <c>TLK_PLAYER_TAUNT</c> and the special
    /// taunts — the Eureka Effect's teleport's
    /// MP_CONCEPT_TAUNT_EUREKA_EFFECT_TELEPORT (tf_player.cpp:7734) — meet here.
    /// </summary>
    internal bool Begin(EntityHandle player, string concept)
    {
        // The speak the taunt is (tf_player.cpp:17858-17870): the matched
        // scene comes back with the yes (:17862's buffer); a failed speak
        // zeroes any pending one (:17874-17875).
        if (!speech.Speak(player, concept, engine.GetActiveWeapon(player), out string scene))
        {
            Attack(player)?.Clear();
            return false;
        }

        Attack(player)?.Arm(scene, clock.Now);

        // OnTauntSucceeded (:17732-17768): the duration with its tail, the
        // condition, and the next-start stamp at start + duration (:17744).
        float duration = TauntDuration(player, scene);
        players.AddCond(player, TFCond.Taunting, duration);
        _nextStart[player.EntryIndex] = clock.Now + duration;
        return true;
    }

    /// <summary>
    /// The base weapon taunt's window: the scene length plus the tail
    /// (:17734), divided by the <c>mult_gesture_time</c> hook on the player's
    /// own manager (:17736-17738) — the player manager roots the hook, so a
    /// side with none divides by one.
    /// </summary>
    private float TauntDuration(EntityHandle player, string scene)
    {
        float mod = hooks.Float(1f, "mult_gesture_time", owners.ManagerOf(player.EntryIndex));
        float length = scenes.SceneDuration(scene);
        return (length + TauntTailSeconds) / mod;
    }

    /// <summary>
    /// <c>CTFPlayer::IsAllowedToTaunt</c> (tf_player_shared.cpp:676-766) as
    /// this build can ask it: alive (:678-679), not already taunting (:682-683),
    /// the four condition gates (:685-695), the ground gate with the kart's
    /// exemption (:706), and the spy's cloak and disguise arms (:756-763). The
    /// FOV lerp (:698), the water and CYOA stop (:702, :13140-1350), the
    /// active weapon's own arms (:709-719), carrying an object (:722), the
    /// grapple (:726) and the scout's lunchbox arms (:729-754) wait in
    /// <c>docs/plans/tf2/deferred.tsv</c>.
    /// </summary>
    private bool IsAllowedToTaunt(EntityHandle player)
    {
        if (!players.IsAlive(player))
            return false;

        if (players.InCond(player, TFCond.Taunting))
            return false;

        if (players.InCond(player, TFCond.HalloweenKart)
            || players.InCond(player, TFCond.HalloweenGhostMode)
            || players.InCond(player, TFCond.ShieldCharge)
            || players.InCond(player, TFCond.CompetitiveLoser))
            return false;

        // On the ground, save the kart riding a vehicle move (:706).
        if (!networking.EntityGetState(player, out _).HasFlag(EntityState.OnGround)
            && !players.InCond(player, TFCond.HalloweenKart))
            return false;

        // The spy's own arms: cloaked or mid-disguise holds the taunt (:756-763).
        return players.ClassOf(player) != TFClass.Spy
               || !(players.InCond(player, TFCond.Stealthed) || players.InCond(player, TFCond.StealthedBlink)
                    || players.InCond(player, TFCond.Disguised) || players.InCond(player, TFCond.Disguising));
    }
}

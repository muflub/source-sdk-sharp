//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Voice;

namespace TF2.Server.Voice;

/// <summary>
/// The <c>voicemenu &lt;menu&gt; &lt;item&gt;</c> client command, through its
/// three stock layers:
///
/// <list type="bullet">
/// <item><c>CMultiplayRules::ClientCommand</c>'s arm
/// (shared/multiplay_gamerules.cpp:1672-1698): fewer than two arguments is
/// silently handled, and the rest rides the per-player, per-command rate
/// limit (<c>CBaseMultiplayerPlayer::ShouldRunRateLimitedCommand</c>,
/// game/server/basemultiplayerplayer.cpp:152-172 —
/// <c>COMMAND_MAX_RATE</c> 0.3 s, the first word free, a word inside the
/// window refused and silence kept).</item>
/// <item><c>CTFGameRules::VoiceCommand</c>
/// (shared/tf/tf_gamerules.cpp:10166-10210): a Halloween ghost's voice is the
/// engine command <c>boo</c> and nothing else.</item>
/// <item><c>CMultiplayRules::VoiceCommand</c>
/// (shared/multiplay_gamerules.cpp:1760-1864) over
/// <c>CTFPlayer::CanSpeakVoiceCommand</c>/<c>NoteSpokeVoiceCommand</c>
/// (server/tf/tf_player.cpp:20028-20072): the item bounds, the admission
/// gates, the concept through the response rules, the matched scene's sounds
/// from the scene image, the subtitle to its audience, and the next-speak
/// time the scene's length and the spam counter set.</item>
/// </list>
///
/// <para>
/// The criteria the concept is asked with are
/// <c>CTFPlayer::ModifyOrAppendCriteria</c>'s
/// (server/tf/tf_player.cpp:19212-19274) as far as this build's carrier reads
/// them: the class — the disguise's, stock's override through the
/// <c>disguiseclass</c> modifier (:19219-19231) —, <c>OnRedTeam</c> decided
/// by the disguise too (:19233-19238), and the condition words. The stats,
/// weapon-role and context criteria and the disguised spy's second, enemy
/// half of <c>CTFPlayer::SpeakConceptIfAllowed</c> (:19897-19939) are
/// divergences (docs/divergences.md, the voice-menu row).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFVoiceMenuCommand(
    TFVoiceCommandMenus menus,
    TFSceneImage scenes,
    IResponseRules responseRules,
    TFPlayerCarriers carriers,
    IPlayerDirectory records,
    ITeams teams,
    IEngineEntities entities,
    IEngineSound sound,
    IEngineConsole console,
    IUserMessageSender messages,
    IGameClock clock,
    TFVoiceCommandSuspensionModeConVar suspensionMode,
    TFMaxVoiceSpeakDelayConVar speakDelay,
    ITeamRoundState? round = null,
    ILog? log = null)
    : IClientCommandHook, IOrderedHook
{
    /// <summary>The verb, as <see cref="TFJoinClassCommand.Verb"/> is for its own.</summary>
    public const string Verb = "voicemenu";

    /// <summary><c>COMMAND_MAX_RATE</c> (basemultiplayerplayer.cpp:13): the
    /// per-command window a repeated voice menu waits out.</summary>
    public const float RateLimitSeconds = 0.3f;

    /// <summary>VOICE_COMMAND_MAX_SUBTITLE_DIST
    /// (shared/multiplay_gamerules.h:37): how far a distance-cut subtitle
    /// reaches, flat over the PAS cut that trims the candidate set first.</summary>
    public const float MaxSubtitleDistance = 1900f;

    /// <summary>CanSpeakVoiceCommand's spam window and grace
    /// (server/tf/tf_player.cpp:20053-20064): past five seconds the counter
    /// resets, inside one the repeat is a spam and adds half a second.</summary>
    public const float SpamResetSeconds = 5f;

    public const float SpamGraceSeconds = 1f;

    public const float SpamSecondsEach = 0.5f;

    /// <summary>After the class commands' band, before nothing: a voice menu
    /// is its own word, ordered only against its peers.</summary>
    public const int Order = 999_081;

    public int HookOrder => Order;

    /// <summary>What a <c>voicemenu</c> did, for the facts and the log.</summary>
    public enum Outcome
    {
        /// <summary>Not this hook's verb.</summary>
        NotOurs,

        /// <summary>Handled with no menu and item worth the name (too few arguments, out of bounds).</summary>
        Empty,

        /// <summary>The per-command rate limit held the word.</summary>
        RateLimited,

        /// <summary>The admission gates held it: the suspension mode or the next-speak time.</summary>
        Refused,

        /// <summary>A ghost's <c>boo</c>, and no menu.</summary>
        Ghost,

        /// <summary>The concept asked, and the response rules matched nothing playable.</summary>
        Unmatched,

        /// <summary>The line played, subtitle and all.</summary>
        Spoke,
    }

    /// <summary>One player's voice state: the rate limit's last word and
    /// <c>m_flNextVoiceCommandTime</c>/<c>m_iVoiceSpamCounter</c>.</summary>
    private sealed class VoiceState
    {
        public float LastCommand;

        public float NextVoiceCommand;

        public int SpamCounter;

        public bool RateLimited;
    }

    private readonly Dictionary<int, VoiceState> _state = [];

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (!string.Equals(command, Verb, StringComparison.OrdinalIgnoreCase))
            return false;

        if (args.Length < 2)
            return true;

        VoiceMenu(player, Atoi(args[0]), Atoi(args[1]));
        return true;
    }

    /// <summary>The whole chain; the outcome names what held or what played.</summary>
    public Outcome VoiceMenu(EntityHandle player, int menu, int item)
    {
        int index = player.EntryIndex;
        var state = StateOf(index);
        float now = clock.Now;

        // ShouldRunRateLimitedCommand: the first word is free, a word inside
        // the window refused and silent, one past it stamped and taken.
        if (!state.RateLimited)
        {
            state.RateLimited = true;
        }
        else if (now - state.LastCommand < RateLimitSeconds)
        {
            return Outcome.RateLimited;
        }

        state.LastCommand = now;

        // The ghost's voice: the engine command and no menu (:10179-10183).
        // The arm's own suspension gate (:10171-10177) rides
        // BHaveChatSuspensionInCurrentMatch, false without a GC match; what
        // survives here is CanSpeakVoiceCommand's own mode check, stock's
        // unconditional one, below with its next-speak gate.
        var carrierState = carriers.Get(index);
        if (carrierState is not null && carrierState.InCond(TFCond.HalloweenGhostMode))
        {
            console.SendClientCommand(player, "boo");
            return Outcome.Ghost;
        }

        if (menus.Item(menu, item) is not { } entry)
            return Outcome.Empty;

        // CanSpeakVoiceCommand (:20028-20044): the mode-1 refusal, then the
        // next-speak gate. The mode-2 token bucket behind them rides the
        // chat suspension too, and is a divergence with it.
        if (suspensionMode.Value == 1)
            return Outcome.Refused;

        if (now <= state.NextVoiceCommand)
            return Outcome.Refused;

        // SpeakConceptIfAllowed's criteria set, stock's two appends first
        // (ai_speech.cpp: the concept, then the player's words).
        var criteria = new ResponseCriteriaSet();
        criteria.Append("concept", entry.Concept);
        AppendPlayerCriteria(player, carrierState, round, criteria);

        if (!responseRules.FindBestResponse(criteria, out var response))
            return Outcome.Unmatched;

        bool spoke;
        float seconds;
        switch (response.Kind)
        {
            // The scene's speak events, sounded one after another; the scene's
            // own length is the image's word on it. A scene the image does not
            // carry sounds nothing, as stock's missing scene does not play.
            case ResponseKind.Scene:
                IReadOnlyList<string> sceneSounds = scenes.SceneSounds(response.Name);
                foreach (string sceneSound in sceneSounds)
                    sound.EntityEmitSound(player, sceneSound);

                seconds = scenes.SceneDuration(response.Name);
                spoke = sceneSounds.Count > 0;
                break;

            // RT_SOUND: the sound itself, no scene around it.
            case ResponseKind.Speak:
                sound.EntityEmitSound(player, response.Name);
                seconds = scenes.SceneDuration(response.Name);
                spoke = true;
                break;

            default:
                seconds = 0f;
                spoke = false;
                break;
        }

        // A response chosen but not played is stock's pItem=NULL: no
        // subtitle, no note (:1853-1856).
        if (!spoke)
            return Outcome.Unmatched;

        if (entry.ShowSubtitle)
            SendSubtitle(player, carrierState, menu, item, entry.DistanceBasedSubtitle);

        // NoteSpokeVoiceCommand (:20049-20072): the spam counter over the
        // gap since the last allowance, then the next time — the scene's
        // length under the convar's cap, plus half a second per spam.
        float since = now - state.NextVoiceCommand;
        if (since > SpamResetSeconds)
            state.SpamCounter = 0;
        else if (since < SpamGraceSeconds)
            state.SpamCounter++;

        state.NextVoiceCommand = now + MathF.Min(seconds, speakDelay.Value);
        if (state.SpamCounter > 0)
            state.NextVoiceCommand += state.SpamCounter * SpamSecondsEach;

        log?.Msg($"[TF2/Voice] player {index} spoke {response.Kind} {response.Name} (menu {menu} item {item})\n");
        return Outcome.Spoke;
    }

    /// <summary>
    /// <c>CTFPlayer::ModifyOrAppendCriteria</c>'s words
    /// (server/tf/tf_player.cpp:19212-19274) over the carrier's state, and
    /// <c>CBasePlayer::ModifyOrAppendPlayerCriteria</c>'s class word before
    /// them. The disguise's class is stock's override through the
    /// <c>disguiseclass</c> modifier: appended last, it wins the lookup
    /// (:19219-19224), so a disguised spy asks as what they show.
    /// </summary>
    private void AppendPlayerCriteria(EntityHandle speaker, TFPlayerNetState? state, ITeamRoundState? round, ResponseCriteriaSet criteria)
    {
        if (state is null)
            return;

        // The class, the disguise's where there is one (stock's else arm
        // names it from g_aPlayerClassNames_NonLocalized).
        TFClass shown = state.InCond(TFCond.Disguised) && state.DisguiseAs() != TFClass.Undefined
            ? state.DisguiseAs()
            : state.Class();
        if (shown != TFClass.Undefined)
            criteria.Append("playerclass", TFClassNames.NonLocalized(shown));

        // OnRedTeam, the disguise's team where there is one (:19233-19238).
        int team = state.InCond(TFCond.Disguised) ? state.DisguiseTeam : teams.Team(speaker);
        criteria.Append("OnRedTeam", (TFTeam)team == TFTeam.Red ? "1" : "0");

        bool disguised = state.InCond(TFCond.Disguised);
        criteria.Append("disguised", disguised ? "1" : "0");
        criteria.Append("cloaked", state.IsStealthed() || state.InCond(TFCond.StealthedBlink) ? "1" : "0");
        criteria.Append("invulnerable", state.IsInvulnerable() ? "1" : "0");
        criteria.Append("beinghealed", state.InCond(TFCond.HealthBuff) ? "1" : "0");
        criteria.Append("waitingforplayers", round is { } r && r.InWaitingForPlayers ? "1" : "0");

        // IsControlStunned / IsSnared over the carried stun entry, the same
        // reading TFMovementRules makes (TFMovementRules.cs, the stock gates
        // at tf_player_shared.cpp:9952-9986).
        bool stunned = state.StunIndex >= 0 && state.InCond(TFCond.Stunned);
        var stunFlags = stunned ? (TFStunFlags)state.StunFlags : TFStunFlags.None;
        bool controlStunned = stunned && (stunFlags & TFStunFlags.Controls) != 0;
        criteria.Append("stunned", controlStunned ? "1" : "0");
        criteria.Append("snared", stunned && !controlStunned ? "1" : "0");
        criteria.Append("dodging", state.InCond(TFCond.Phase) || state.InCond(TFCond.PasstimeInterception) ? "1" : "0");
        criteria.Append("doublejumping", state.AirDash > 0 ? "1" : "0");
    }

    /// <summary>
    /// The subtitle's audience: the distance cut first where the item asks it
    /// (:1796-1816, the speaker always kept), then the team cut unless the
    /// speaker is a disguised spy showing the enemy their voice
    /// (<c>ShouldShowVoiceSubtitleToEnemy</c>, tf_player.cpp:20020-20023).
    /// </summary>
    private void SendSubtitle(EntityHandle speaker, TFPlayerNetState? state, int menu, int item, bool distanceBased)
    {
        int speakerTeam = teams.Team(speaker);
        bool showToEnemy = state is not null
            && state.InCond(TFCond.Disguised)
            && state.DisguiseTeam != speakerTeam;

        foreach (var (listener, _) in records.All)
        {
            if (distanceBased && listener != speaker
                &&Squared(entities.GetOrigin(listener), entities.GetOrigin(speaker)) > MaxSubtitleDistance * MaxSubtitleDistance)
            {
                continue;
            }

            if (!showToEnemy && teams.Team(listener) != speakerTeam)
                continue;

            messages.Send(new TFVoiceSubtitle
            {
                Speaker = (byte)speaker.EntryIndex,
                Menu = (byte)menu,
                Item = (byte)item,
            }, listener.EntryIndex, reliable: true);
        }
    }

    /// <summary>The flat distance stock cuts by, squared (the PAS pre-filter is a divergence).</summary>
    private static float Squared(Vector a, Vector b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private VoiceState StateOf(int index)
    {
        if (!_state.TryGetValue(index, out var state))
        {
            state = new VoiceState();
            _state[index] = state;
        }

        return state;
    }

    private static int Atoi(string text)
    {
        int at = 0;
        while (at < text.Length && !char.IsWhiteSpace(text[at]))
            at++;

        return int.TryParse(text.AsSpan(0, at), out var value) ? value : 0;
    }
}

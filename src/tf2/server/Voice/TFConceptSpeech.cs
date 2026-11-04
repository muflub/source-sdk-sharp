//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;

namespace TF2.Server.Voice;

/// <summary>
/// A player speaking a concept through the response rules, outside the voice
/// menu: <c>CBaseMultiplayerPlayer::SpeakConceptIfAllowed</c>
/// (game/server/basemultiplayerplayer.cpp:103-108) — the alive gate of
/// <c>SpeakIfAllowed</c> (:71-78), then the concept and the player's words
/// (<see cref="TFPlayerCriteriaWords"/>) into the search, and the matched
/// response played as the voice menu plays its own: the scene's speak events
/// one after another, or the RT_SOUND itself.
///
/// <para>The concepts are stock's MP-concept strings
/// (shared/mp_shareddefs.cpp:45-95); a name the rules do not answer is
/// silence, as stock's unmatched search is.</para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFConceptSpeech(
    IResponseRules responseRules,
    TFPlayerCarriers carriers,
    ITeams teams,
    IEnginePlayers players,
    TFSceneImage scenes,
    IEngineSound sound,
    IUniformRandom rng,
    ITeamRoundState? round = null,
    ILog? log = null)
{
    /// <summary>MP_CONCEPT_BUILDING_OBJECT (mp_shareddefs.cpp:57) — the builder's line as construction starts.</summary>
    public const string BuildingObject = "TLK_BUILDING_OBJECT";

    /// <summary>MP_CONCEPT_LOST_OBJECT (mp_shareddefs.cpp:58) — the builder's line as the blow kills it.</summary>
    public const string LostObject = "TLK_LOST_OBJECT";

    /// <summary>MP_CONCEPT_PLAYER_TAUNT (mp_shareddefs.cpp:23) — the base weapon taunt's line, whose per-class rules are the taunt scenes (talker/tf.txt).</summary>
    public const string PlayerTaunt = "TLK_PLAYER_TAUNT";

    /// <summary>MP_CONCEPT_SPY_SAPPER (mp_shareddefs.cpp:59) — the builder's line as the sapper lands.</summary>
    public const string SpySapper = "TLK_SPY_SAPPER";

    /// <summary>MP_CONCEPT_REDEPLOY_BUILDING (mp_shareddefs.h:104) — the redeploy's line, the carried object put down.</summary>
    public const string RedeployBuilding = "TLK_REDEPLOY_BUILDING";

    /// <summary>MP_CONCEPT_CART_MOVING_FORWARD (mp_shareddefs.cpp:71) — the escort line, under the push.</summary>
    public const string CartMovingForward = "TLK_CART_MOVING_FORWARD";

    /// <summary>MP_CONCEPT_CART_MOVING_BACKWARD (mp_shareddefs.cpp:72) — the escort line, the cart receding.</summary>
    public const string CartMovingBackward = "TLK_CART_MOVING_BACKWARD";

    /// <summary>MP_CONCEPT_CART_STOP (mp_shareddefs.cpp:73) — the escort line, the cart halted.</summary>
    public const string CartStop = "TLK_CART_STOP";

    /// <summary>MP_CONCEPT_LOST_CONTROL_POINT (mp_shareddefs.cpp:61).</summary>
    public const string LostControlPoint = "TLK_LOST_CONTROL_POINT";

    /// <summary>MP_CONCEPT_CAPTURED_POINT (mp_shareddefs.cpp:62).</summary>
    public const string CapturedPoint = "TLK_CAPTURED_POINT";

    /// <summary>MP_CONCEPT_CAPTURE_BLOCKED (mp_shareddefs.cpp:63).</summary>
    public const string CaptureBlocked = "TLK_CAPTURE_BLOCKED";

    /// <summary>MP_CONCEPT_FIREWEAPON, the plain fire concept behind <see cref="SpeakWeaponFire"/>'s own clock.</summary>
    public const string FireWeapon = "TLK_FIREWEAPON";

    /// <summary>MP_CONCEPT_MINIGUN_WIND, the wind-up line a spun gun speaks.</summary>
    public const string WindMinigun = "TLK_WINDMINIGUN";

    /// <summary>MP_CONCEPT_MINIGUN_FIRE, the line for handing a spun gun to its fire loop.</summary>
    public const string FireMinigun = "TLK_FIREMINIGUN";

    /// <summary>MP_CONCEPT_MINIGUN_FIREWEAPON, the firing concept behind its five-second gate.</summary>
    public const string MinigunFireWeapon = "TLK_MINIGUN_FIREWEAPON";

    /// <summary>The player speaks the concept; false when dead, uncarried or unmatched.</summary>
    public bool Speak(EntityHandle speaker, string concept) => Ask(speaker, concept, null).Spoke;

    /// <summary>
    /// The player speaks the concept with one extra criterion word — stock's
    /// single <c>key:value</c> modifier (<c>CBaseObject::GetResponseRulesModifier</c>'s
    /// <c>objtype:sentrygun</c> shape, tf_obj.cpp:1293-1305, into
    /// <c>SpeakIfAllowed</c>'s criteria).
    /// </summary>
    public bool Speak(EntityHandle speaker, string concept, string key, string value)
        => Ask(speaker, concept, criteria => criteria.Append(key, value)).Spoke;

    /// <summary>The player speaks with its held weapon's classname as the word — the shared criteria's <c>playerweapon</c> (<c>ModifyOrAppendCriteria</c>, tf_player.cpp:19212+), which the per-class rules' <c>WeaponIs*</c> criteria read.</summary>
    public bool Speak(EntityHandle speaker, string concept, string weaponName)
        => Ask(speaker, concept, criteria => criteria.Append("playerweapon", weaponName)).Spoke;

    /// <summary>
    /// The same ask, answering the matched response's name — stock's
    /// <c>SpeakConceptIfAllowed</c> filling the caller's buffer
    /// (basemultiplayerplayer.cpp:103's <c>pszResponse</c>): the taunt's
    /// scene, which <c>CTFPlayer::Taunt</c> keys its timed attack on
    /// (tf_player.cpp:17862, :17979). Empty when nothing was matched.
    /// </summary>
    public bool Speak(EntityHandle speaker, string concept, string weaponName, out string responseName)
    {
        (bool spoke, responseName) = Ask(speaker, concept, criteria => criteria.Append("playerweapon", weaponName));
        return spoke;
    }

    /// <summary>
    /// The player speaks with its held weapon's words beside the player's own
    /// (<c>CTFPlayer::ModifyOrAppendCriteria</c>, tf_player.cpp:19212+: the classname
    /// of the weapon in hand, and its firing duration to one decimal), and with the
    /// roll the percent-chance criteria read.
    /// </summary>
    public bool Speak(EntityHandle speaker, string concept, string weaponName, float firingDuration)
        => Ask(speaker, concept, criteria =>
        {
            criteria.Append("playerweapon", weaponName);
            criteria.Append("minigunfiretime", firingDuration.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        }).Spoke;

    /// <summary>The ask itself: the alive gate, the player's words, the roll, the caller's extra, the match — and the matched response's name beside whether anything played.</summary>
    private (bool Spoke, string Name) Ask(EntityHandle speaker, string concept, Action<ResponseCriteriaSet>? appendExtra)
    {
        // SpeakIfAllowed's gate (basemultiplayerplayer.cpp:71-78).
        if (!players.IsAlive(speaker))
            return (false, "");

        var criteria = new ResponseCriteriaSet();
        criteria.Append("concept", concept);
        TFPlayerCriteriaWords.Append(carriers.Get(speaker.EntryIndex), teams.Team(speaker), round, criteria);

        // The roll behind the percent-chance criteria (SpeakInternal): a missing key
        // fails them all, so it rides every ask.
        criteria.Append("randomnum", rng.RandomInt(0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture));

        appendExtra?.Invoke(criteria);

        if (!responseRules.FindBestResponse(criteria, out var response))
            return (false, "");

        bool spoke;
        switch (response.Kind)
        {
            // The scene's speak events, sounded one after another; a scene the
            // image does not carry sounds nothing, as stock's missing scene
            // does not play.
            case ResponseKind.Scene:
                IReadOnlyList<string> sceneSounds = scenes.SceneSounds(response.Name);
                foreach (string sceneSound in sceneSounds)
                    sound.EntityEmitSound(speaker, sceneSound);

                spoke = sceneSounds.Count > 0;
                break;

            // RT_SOUND: the sound itself, no scene around it.
            case ResponseKind.Speak:
                sound.EntityEmitSound(speaker, response.Name);
                spoke = true;
                break;

            default:
                spoke = false;
                break;
        }

        if (spoke)
            log?.Msg($"[TF2/Voice] player {speaker.EntryIndex} spoke concept {concept} ({response.Kind} {response.Name})\n");

        return (spoke, response.Name);
    }

    /// <summary>
    /// The player at an entry index speaks, as the capture area's capper and
    /// blocker lists name them: silence with no such player (stock's
    /// <c>ToBaseMultiplayerPlayer(UTIL_PlayerByIndex(i))</c> null).
    /// </summary>
    public bool Speak(int playerIndex, string concept)
    {
        EntityHandle speaker = players.GetHandle(playerIndex);
        return speaker != EntityHandle.None && Speak(speaker, concept);
    }

    /// <summary>The player at an entry index speaks with its held weapon's words beside.</summary>
    public bool Speak(int playerIndex, string concept, string weaponName, float firingDuration)
    {
        EntityHandle speaker = players.GetHandle(playerIndex);
        return speaker != EntityHandle.None && Speak(speaker, concept, weaponName, firingDuration);
    }

    /// <summary>
    /// Every member of a team speaks, once each: stock's old-team loop over
    /// the clients (team_control_point.cpp:638-647) with the team filter on
    /// the roster, whose order the walk does not keep.
    /// </summary>
    public void SpeakTeam(int team, string concept)
    {
        foreach (EntityHandle member in teams.Players(team))
            Speak(member, concept);
    }

    /// <summary>
    /// Every client speaks, once each:
    /// <c>CMultiplayRules::HaveAllPlayersSpeakConceptIfAllowed</c>
    /// (multiplay_gamerules.cpp:1817-1835) with no team filter — the walk over
    /// the player table, the dead refused by the same gate as one speaker's.
    /// </summary>
    public void SpeakAll(string concept)
    {
        for (int slot = 1; slot <= EngineLimits.MaxPlayerSlots; slot++)
        {
            EntityHandle speaker = players.GetHandle(slot);
            if (speaker != EntityHandle.None)
                Speak(speaker, concept);
        }
    }
}

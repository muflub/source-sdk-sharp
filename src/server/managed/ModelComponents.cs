//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// What precaching a model brings with it — plan_server_novalve.md P2.4/P3, out
/// of Precache_ModelComponents (precache_model.cpp) and the model sounds cache
/// it read (CModelSoundsCache::BuildAnimationEventSoundList). Pure: the model's
/// sequences and events come in, the names to precache go out, in stock's order.
/// </summary>
public static class ModelComponents
{
    // eventlist.h, scriptevent.h and anim_event_names.h's numbers.
    public const int TypeNewEventSystem = 1 << 10;   // AE_TYPE_NEWEVENTSYSTEM
    public const int TypeClient = 1 << 4;            // AE_TYPE_CLIENT
    public const int ClEventSound = 5004;            // CL_EVENT_SOUND
    public const int ClEventFootstepLeft = 6004;     // CL_EVENT_FOOTSTEP_LEFT
    public const int ClEventFootstepRight = 6005;    // CL_EVENT_FOOTSTEP_RIGHT
    public const int ScriptEventSound = 1004;        // SCRIPT_EVENT_SOUND
    public const int ScriptEventSoundVoice = 1008;   // SCRIPT_EVENT_SOUND_VOICE

    // THE ANIMEVENT ENUM ORDINAL, NOT A FILE NUMBER: stock compares an
    // old-system event's raw id with AE_CL_CREATE_PARTICLE_EFFECT, eventlist.h's
    // 33rd enumerator (32, checked by compiling the header). Reproduced as is.
    public const int AeClCreateParticleEffectOrdinal = 32;

    private static bool Is(string name, string wanted) => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase);

    private static bool IsClientName(string name) => (AnimationEventCatalog.SideOf(name) & AnimationEventSide.Client) != 0;

    /// <summary>
    /// The script sound names the model's animation events name, in the order
    /// BuildAnimationEventSoundList met them (duplicates and names the sound
    /// table does not hold are the caller's to drop, as FindOrAddScriptSound did).
    /// </summary>
    public static List<string> SoundNames(IReadOnlyList<ModelSequence> sequences, string modelName, Action<string>? warn = null)
    {
        var names = new List<string>();

        foreach (var seq in sequences)
        {
            for (int i = 0; i < seq.Events.Count; i++)
            {
                ModelSequenceEvent e = seq.Events[i];

                if ((e.Type & TypeNewEventSystem) != 0)
                {
                    if (Is(e.Name, "AE_SV_PLAYSOUND") || Is(e.Name, "AE_SV_EXCLUDE_PLAYER_SOUND"))
                    {
                        names.Add(e.Options);
                    }
                    else if (Is(e.Name, "AE_CL_PLAYSOUND") || Is(e.Name, "AE_CL_EXCLUDE_PLAYER_SOUND"))
                    {
                        if ((e.Type & TypeClient) != 0 || IsClientName(e.Name))
                        {
                            if (e.Options.Length > 0)
                                names.Add(e.Options);
                            else
                                warn?.Invoke($"-- Error --:  empty soundname, .qc error on AE_CL_PLAYSOUND in model {modelName}, " +
                                             $"sequence {seq.Label}, animevent # {i + 1}");
                        }
                    }

                    continue;
                }

                switch (e.Id)
                {
                    case ClEventSound:
                    case ScriptEventSound:
                    case ScriptEventSoundVoice:
                        names.Add(e.Options);
                        break;

                    case ClEventFootstepLeft:
                    case ClEventFootstepRight:
                        string prefix = e.Options.Length > 0 ? e.Options : "NPC_CombineS";
                        names.Add(prefix + ".RunFootstepLeft");
                        names.Add(prefix + ".RunFootstepRight");
                        names.Add(prefix + ".FootstepLeft");
                        names.Add(prefix + ".FootstepRight");
                        break;
                }
            }
        }

        return names;
    }

    /// <summary>
    /// The particle systems the model's animation events create: a client
    /// AE_CL_CREATE_PARTICLE_EFFECT (or an old-system event with that enum's
    /// ordinal), the first space-separated token of its options.
    /// </summary>
    public static List<string> EventParticles(IReadOnlyList<ModelSequence> sequences)
    {
        var names = new List<string>();

        foreach (var seq in sequences)
        {
            foreach (var e in seq.Events)
            {
                bool newSystem = (e.Type & TypeNewEventSystem) != 0;
                bool client = (e.Type & TypeClient) != 0 || (newSystem && IsClientName(e.Name));

                if (newSystem && !client)
                    continue;

                bool isEffect = newSystem ? Is(e.Name, "AE_CL_CREATE_PARTICLE_EFFECT")
                                          : e.Id == AeClCreateParticleEffectOrdinal;
                if (!isEffect)
                    continue;

                string token = FirstToken(e.Options);
                if (token.Length > 0)
                    names.Add(token);
            }
        }

        return names;
    }

    // nexttoken( token, 256, options, ' ' ): everything up to the first
    // separator, from the very start (a leading space is an empty token), at
    // most 255 characters.
    internal static string FirstToken(string s)
    {
        int i = 0;
        while (i < s.Length && s[i] != ' ' && i < 255)
            i++;
        return s.Substring(0, i);
    }
}

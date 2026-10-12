//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The close caption a sound carries, decided and sent here: stock's
/// <c>CSoundEmitterSystem::EmitCloseCaption</c> (soundemittersystem.cpp) past
/// the facts only native code can look up. sound_emitter.cpp forwards the
/// recipients, the token, the duration (filled from the wave when it was
/// negative) and the speaker's gender, once per caption, through
/// <c>IManagedUserMessages::EmitCloseCaption</c>.
/// </summary>
public sealed class CloseCaptions(IUserMessageSender messages, IEngineEnvironment environment,
                                  IEngineConsole console) : ICloseCaptions
{
    /// <summary>CLOSE_CAPTION_WARNIFMISSING and its siblings, shareddefs.h's values.</summary>
    [Flags]
    public enum CaptionFlags : byte
    {
        None          = 0,
        WarnIfMissing = 1 << 0,
        FromPlayer    = 1 << 1,
        GenderMale    = 1 << 2,
        GenderFemale  = 1 << 3,
    }

    /// <summary>gender_t: GENDER_NONE, GENDER_MALE, GENDER_FEMALE.</summary>
    public const int GenderMale = 1;
    public const int GenderFemale = 2;

    /// <summary>
    /// The longest token sent: stock's buffer held 255, and 251 leaves the
    /// duration and flags room in the engine's 255 bytes, so a send cannot fail.
    /// </summary>
    public const int MaxToken = 251;

    /// <inheritdoc/>
    /// <remarks>
    /// No close captions in multiplayer, and none in single player with the
    /// closecaption convar off: stock's first line, in both of its overloads.
    /// </remarks>
    public bool Wanted
    {
        get
        {
            var globals = environment.Globals;
            if (!globals.IsAvailable)
                return false;

            int maxClients = globals.MaxClients;
            return !(maxClients > 1 || (maxClients == 1 && !IsOn(console.FindConVarValue("closecaption"))));
        }
    }

    /// <inheritdoc/>
    public int Emit(ReadOnlySpan<int> recipients, bool reliable, string token, float duration,
                    bool fromPlayer, bool warnIfMissing, int gender)
    {
        if (!Wanted)
            return 0;

        if (recipients.IsEmpty)
            return 0;

        var caption = PayloadOf(token, duration, fromPlayer, warnIfMissing, gender);

        int sent = 0;
        foreach (int slot in recipients)
        {
            if (slot > 0 && messages.Send(caption, slot, reliable))
                sent++;
        }

        return sent;
    }

    /// <summary>
    /// The payload: the token lowercased with its <c>\n</c> escapes made real
    /// (Hack_FixEscapeChars), the duration in tenths of a second capped at 255,
    /// and the flags.
    /// </summary>
    public static CloseCaption PayloadOf(string token, float duration, bool fromPlayer, bool warnIfMissing, int gender)
    {
        var flags = CaptionFlags.None;
        if (warnIfMissing)
            flags |= CaptionFlags.WarnIfMissing;
        if (fromPlayer)
            flags |= CaptionFlags.FromPlayer;
        if (gender == GenderMale)
            flags |= CaptionFlags.GenderMale;
        else if (gender == GenderFemale)
            flags |= CaptionFlags.GenderFemale;

        string lower = token.Length > MaxToken ? token[..MaxToken] : token;
        lower = FixEscapes(lower.ToLowerInvariant());

        return new CloseCaption
        {
            Token    = lower,
            Duration = (short)Math.Min(255, (int)(duration * 10.0f)),
            Flags    = (byte)flags,
        };
    }

    /// <summary>
    /// Hack_FixEscapeChars: a backslash followed by <c>n</c> becomes a
    /// newline; any other backslash stays as it is.
    /// </summary>
    public static string FixEscapes(string text)
    {
        if (!text.Contains('\\'))
            return text;

        var fixedText = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == 'n')
            {
                fixedText.Append('\n');
                i++;
            }
            else
            {
                fixedText.Append(text[i]);
            }
        }

        return fixedText.ToString();
    }

    /// <summary>A convar's value read as a bool, as ConVar::GetBool does: non-zero is on.</summary>
    private static bool IsOn(string? value)
        => value is not null && (int.TryParse(value, out int n) ? n != 0
                                 : float.TryParse(value, System.Globalization.NumberStyles.Float,
                                                  System.Globalization.CultureInfo.InvariantCulture, out float f) && f != 0f);
}

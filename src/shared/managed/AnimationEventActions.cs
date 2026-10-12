//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared;

/// <summary>
/// What a fired animation event asks for.
/// </summary>
public enum AnimationEventKind
{
    /// <summary>
    /// Nothing this shell knows how to do. A real answer — most of the event
    /// list is the server's, and a few of the client's are obsolete in stock
    /// too — and deliberately not an error.
    /// </summary>
    None = 0,

    /// <summary>A muzzle flash at an attachment.</summary>
    MuzzleFlash,

    /// <summary>A soundscript, played by the client.</summary>
    Sound,

    /// <summary>A brass shell thrown from an attachment.</summary>
    EjectBrass,
}

/// <summary>
/// One decoded event: what to do, where, with what.
/// </summary>
/// <param name="Kind">What is being asked for.</param>
/// <param name="AttachmentName">
/// The attachment by NAME, for the events that name one. Empty when the event
/// names its attachment by index instead.
/// </param>
/// <param name="AttachmentIndex">
/// The attachment by index, ONE-BASED as this shell's attachment API is. Zero
/// when the event names its attachment by name instead.
/// </param>
/// <param name="Payload">
/// Whatever else the event carried: the muzzle-flash type, the soundscript
/// name, the brass type.
/// </param>
public readonly record struct AnimationEventAction(AnimationEventKind Kind,
                                                   string AttachmentName,
                                                   int AttachmentIndex,
                                                   string Payload)
{
    /// <summary>
    /// A muzzle flash's person: the view model's events (<c>AE_MUZZLEFLASH</c>,
    /// <c>CL_EVENT_MUZZLEFLASH*</c>) are first person, the third-person twins
    /// (<c>AE_NPC_MUZZLEFLASH</c>, <c>CL_EVENT_NPC_MUZZLEFLASH*</c>) are not.
    /// </summary>
    public bool FirstPerson { get; init; } = true;

    /// <summary>Nothing to do.</summary>
    public static AnimationEventAction Nothing
        => new(AnimationEventKind.None, string.Empty, 0, string.Empty);
}

/// <summary>
/// WHAT a fired animation event asks the client to do, decoded from the event
/// and its options string.
///
/// <para>
/// Pure, and here rather than in the client, because it is the part that can
/// be silently wrong. Two of the three decodings are arithmetic on a magic
/// number — <c>5011</c> is a muzzle flash on attachment 1 and <c>5021</c> on
/// attachment 2, ten apart, from <c>cl_animevent.h</c> — and the third is a
/// space-separated options string whose two tokens mean different things.
/// Getting the arithmetic wrong puts the flash on the wrong end of the gun and
/// nothing anywhere reports it.
/// </para>
///
/// <para>
/// <b>The one-based conversion is the trap.</b> The event numbers count
/// attachments from ZERO and this shell's attachment API counts from ONE —
/// stock's own <c>CL_EVENT_EJECTBRASS1</c> handler is written
/// <c>GetAttachment( 2, ... )</c> for what its name calls attachment 1, which
/// is the same conversion done by hand and is why it is worth doing in one
/// place with a test on it.
/// </para>
///
/// <para>
/// SIDE-NESS: bucket 5, a shared declaration. The numbers are the event list's
/// and the options grammar is studiomdl's; neither belongs to a side. Nothing
/// here acts — acting is bucket 3 and lives on the client.
/// </para>
/// </summary>
public static class AnimationEventActions
{
    /// <summary>
    /// <c>CL_EVENT_MUZZLEFLASH0</c>. First-person muzzle flash on attachment
    /// 0; 1, 2 and 3 follow at <see cref="AttachmentStride"/> apart.
    /// </summary>
    public const int MuzzleFlashFirst = 5001;

    /// <summary>
    /// <c>CL_EVENT_NPC_MUZZLEFLASH0</c>. The third-person twin, same stride.
    /// </summary>
    public const int NpcMuzzleFlashFirst = 5003;

    /// <summary>How far apart the per-attachment variants are numbered.</summary>
    public const int AttachmentStride = 10;

    /// <summary>How many attachments the numbered variants cover: 0 to 3.</summary>
    public const int NumberedAttachments = 4;

    /// <summary><c>CL_EVENT_SOUND</c>: emit the soundscript in the options.</summary>
    public const int SoundEvent = 5004;

    /// <summary><c>CL_EVENT_EJECTBRASS1</c>: brass from attachment 1.</summary>
    public const int EjectBrassEvent = 6001;

    /// <summary>The named muzzle flash a first-person view model carries.</summary>
    public const string MuzzleFlashName = "AE_MUZZLEFLASH";

    /// <summary>The named muzzle flash a third-person model carries.</summary>
    public const string NpcMuzzleFlashName = "AE_NPC_MUZZLEFLASH";

    /// <summary>The named "play this soundscript" event.</summary>
    public const string PlaySoundName = "AE_CL_PLAYSOUND";

    /// <summary>
    /// Decode one event of a model's table.
    ///
    /// <para>
    /// Only ever asked about events the catalog has already agreed are the
    /// client's — this decides WHAT, not WHETHER.
    /// </para>
    /// </summary>
    public static AnimationEventAction Decide(in ModelAnimationEvent carried)
    {
        if (carried.IsNamed)
            return DecideNamed(carried.Name, carried.Options);

        return DecideNumbered(carried.EventId, carried.Options);
    }

    private static AnimationEventAction DecideNamed(string name, string options)
    {
        if (string.Equals(name, PlaySoundName, StringComparison.OrdinalIgnoreCase))
        {
            // The whole options string, not the first token: a soundscript
            // name has no spaces in practice but splitting one that did would
            // silently emit a sound nobody authored.
            string script = options.Trim();

            return script.Length > 0
                ? new AnimationEventAction(AnimationEventKind.Sound, string.Empty, 0, script)
                : AnimationEventAction.Nothing;
        }

        bool muzzle = string.Equals(name, MuzzleFlashName, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(name, NpcMuzzleFlashName, StringComparison.OrdinalIgnoreCase);

        if (!muzzle)
            return AnimationEventAction.Nothing;

        // "SMG1 muzzle" — the flash TYPE then the attachment NAME. Two tokens,
        // and stock refuses the event outright when either is missing rather
        // than guessing. Refused here too: a flash at a guessed attachment is
        // a flash coming out of the stock of the gun, which reads as a broken
        // model rather than as a broken options string.
        string[] tokens = options.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2)
            return AnimationEventAction.Nothing;

        return new AnimationEventAction(AnimationEventKind.MuzzleFlash, tokens[1], 0, tokens[0])
        {
            FirstPerson = string.Equals(name, MuzzleFlashName, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static AnimationEventAction DecideNumbered(int eventId, string options)
    {
        if (eventId == SoundEvent)
        {
            string script = options.Trim();

            return script.Length > 0
                ? new AnimationEventAction(AnimationEventKind.Sound, string.Empty, 0, script)
                : AnimationEventAction.Nothing;
        }

        if (eventId == EjectBrassEvent)
        {
            // Attachment 1 in the event's own zero-based counting, which is
            // index 2 here. Stock's handler spells that conversion out as the
            // literal 2, which is exactly the kind of arithmetic that is right
            // once and wrong the second time somebody writes it.
            return new AnimationEventAction(AnimationEventKind.EjectBrass, string.Empty,
                                            1 + 1, options.Trim());
        }

        int attachment = NumberedAttachmentFor(eventId, MuzzleFlashFirst);
        bool firstPerson = attachment >= 0;

        if (attachment < 0)
            attachment = NumberedAttachmentFor(eventId, NpcMuzzleFlashFirst);

        if (attachment < 0)
            return AnimationEventAction.Nothing;

        // Zero-based in the event's numbering, one-based in this shell's.
        return new AnimationEventAction(AnimationEventKind.MuzzleFlash, string.Empty,
                                        attachment + 1, options.Trim())
        {
            FirstPerson = firstPerson,
        };
    }

    /// <summary>
    /// <c>DispatchMuzzleEffect</c>'s type token (c_baseanimating.cpp), case
    /// blind: <c>MUZZLEFLASH_COMBINE</c> 5, <c>SMG1</c> 2, <c>PISTOL</c> 4,
    /// <c>SHOTGUN</c> 1, <c>357</c> 6, <c>RPG</c> 7. Anything else is stock's
    /// Assert and a type of 0 (the AR2, which no path draws).
    /// </summary>
    public static int MuzzleFlashType(string token) => token.ToUpperInvariant() switch
    {
        "COMBINE" => 5,
        "SMG1" => 2,
        "PISTOL" => 4,
        "SHOTGUN" => 1,
        "357" => 6,
        "RPG" => 7,
        _ => 0,
    };

    /// <summary>
    /// <c>atoi( options )</c>, the numbered events' flash type: the leading
    /// optionally-signed digits, 0 when there are none.
    /// </summary>
    public static int Atoi(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
        int sign = 1;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
            sign = s[i++] == '-' ? -1 : 1;
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9' && v < int.MaxValue)
            v = v * 10 + (s[i++] - '0');
        return (int)Math.Clamp(sign * v, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// Which attachment a numbered variant names, or -1 if this id is not one
    /// of that family. Zero-based, as the event ids count.
    /// </summary>
    public static int NumberedAttachmentFor(int eventId, int first)
    {
        int offset = eventId - first;

        if (offset < 0 || offset % AttachmentStride != 0)
            return -1;

        int attachment = offset / AttachmentStride;

        return attachment < NumberedAttachments ? attachment : -1;
    }
}

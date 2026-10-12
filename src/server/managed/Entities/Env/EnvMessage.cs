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
/// env_message: says a line from titles.txt, optionally with a noise.
///
/// <para>
/// The other of the mod's two ways of putting words on screen, and the older
/// one. Where <c>game_text</c> carries the sentence itself and every detail of
/// how to draw it, this carries only a NAME — the words, the colours, the
/// position and the timings all live in <c>titles.txt</c>, which is content
/// rather than map data. That is the whole difference between the two, and it is
/// why the client half of this needed a way to read that file at all
/// (<c>HudTextMessageGet</c>).
/// </para>
///
/// <para>
/// The client half is part of the port: stock's reader is
/// <c>CHudMessage::MsgFunc_HudText</c> in <c>message.cpp</c>, which this mod took
/// out of the client build when the HUD moved to C#. So until now the server
/// could send this message perfectly and nothing on the far side would look at
/// it. It is read in <c>HudMessages</c> now, alongside game_text's, because once
/// a name is resolved a titles.txt message IS a game_text and there is no reason
/// to draw it twice over.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvMessage)]
public sealed class EnvMessage : ScriptedEntity
{
    public EnvMessage(IUserMessageSender messages, IEnginePrecache precache, IEngineSound sound)
    {
        Messages = messages;
        Precache = precache;
        Sound = sound;
    }

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IUserMessageSender Messages;

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>making sounds, and what is heard.</summary>
    private readonly IEngineSound Sound;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => (SpawnFlag)SpawnFlags;


    /// <summary>What a map may set on this entity. See <see cref="EnvMessageVars"/>.</summary>
    [ScriptVars] public EnvMessageVars Vars = new();

    /// <summary>Fired after the message goes out, whether or not anyone saw it.</summary>
    /// <summary>
    /// env_message's own spawnflags.
    ///
    /// <para>
    /// Bit 1 is "all players" here, and it is also "all players" on env_hudhint and
    /// on game_text -- three separate #defines in stock that happen to agree. They
    /// are NOT modelled as a shared type for that reason: agreeing today is not the
    /// same as being one domain, and a shared member would invite a fourth entity
    /// to join it whose FGD spells the flag differently.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        /// <summary>Everyone sees it, rather than only whoever set it off.</summary>
        AllPlayers = 0b0000_0001,

        /// <summary>Gone after saying its piece once.</summary>
        Once = 0b0000_0010,
    }

    public readonly Output OnShowMessage = new();

    /// <summary>
    /// The sound level the message's sound is emitted at, resolved ONCE from
    /// the map's attenuation in <see cref="OnSpawn"/>, which a hot reload does
    /// not run again -- so it is <c>[SaveField]</c>. Uncarried it would come
    /// back zero, which stock hears as full rather than the map's level
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private int _soundLevel;

    /// <summary>
    /// Stock's four attenuations, resolved the way stock resolves them —
    /// including the oddity in the default case, which stores a SOUNDLEVEL
    /// (SNDLVL_IDLE, 60) where the other three store an ATTENUATION and convert.
    /// The conversion is ATTN_TO_SNDLVL: 50 + 20/attenuation.
    /// </summary>
    private static int SoundLevelFor(int attenuation) => attenuation switch
    {
        1 => 50 + (int)(20f / 1.25f),  // ATTN_STATIC, medium
        2 => 50 + (int)(20f / 0.8f),   // ATTN_NORM, large
        3 => 0,                        // ATTN_NONE — heard everywhere
        _ => 50 + (int)(20f / 60f),    // stock's default, and stock's own bug
    };

    public override void OnSpawn()
    {
        _soundLevel = SoundLevelFor(Vars.Attenuation);

        // 0..10 in the map, 0..1 on the wire, and zero reads as "did not say",
        // which stock treats as full rather than as silence.
        Vars.Volume *= 0.1f;

        if (Vars.Volume <= 0f)
            Vars.Volume = 1f;

        if (Vars.Sound.Length != 0)
            Precache.PrecacheSound(Vars.Sound);
    }

    [ScriptedEvent("ShowMessage")]
    private void OnShow(InputData input) => Show(input.Activator);

    [ScriptedEvent("Use")]
    private void OnUsed(InputData input) => Show(input.Activator);

    private void Show(EntityHandle activator)
    {
        int player = PlayerFor(activator);

        if (Vars.Message.Length != 0 && player != NoRecipient)
        {
            if (!Messages.Send(new HudText { Name = Vars.Message }, player, reliable: true))
                Log.Warning($"[SourceSharp] env_message '{Name}': title name too long; not sent\n");
        }

        if (Vars.Sound.Length != 0)
        {
            // Through the ambient path rather than EntityEmitSound, and the
            // reason is the two keys above: this entity lets a mapper set the
            // volume and how far the noise carries, and the ordinary emit takes
            // both from the soundscript instead. An index of zero is right here
            // — the sound comes from a place and never moves, is never retuned
            // and is never stopped, so it needs no channel identity of its own.
            Sound.EmitAmbientSound(0, Origin, Vars.Sound, Vars.Volume, _soundLevel,
                                    (int)SoundUpdate.None, AmbientModulation.PitchNormal);
        }

        // Fired before the removal below, so a map can still hear about a
        // one-shot message going out.
        OnShowMessage.Fire(activator);

        if (Flags.HasFlag(SpawnFlag.Once))
            Remove();
    }

    /// <summary>Not zero: zero is everybody.</summary>
    private const int NoRecipient = -1;

    /// <summary>
    /// Who sees it, where zero is everybody.
    ///
    /// Stock's middle case — no activator, one client, so use the local player —
    /// cannot arise here: this build has no singleplayer and
    /// <c>UTIL_GetLocalPlayer</c> is NULL on a multiplayer server. So a map that
    /// fires this from a timer without the All Players flag reaches nobody,
    /// which is what stock does on a multiplayer server too. Said aloud rather
    /// than silently, because a message that never appears looks like a broken
    /// client.
    /// </summary>
    private int PlayerFor(EntityHandle activator)
    {
        if (Flags.HasFlag(SpawnFlag.AllPlayers))
            return 0;

        if (Entities.IsPlayer(activator))
            return Entities.GetIndex(activator);

        Log.Warning($"[SourceSharp] env_message '{Name}': nothing to show '{Vars.Message}' to; " +
                        "whatever fired this was not a player and the All Players flag is not set\n");

        return NoRecipient;
    }
}

/// <summary>
/// The map keys <see cref="EnvMessage"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvMessageVars
{
    /// <summary>The titles.txt entry to show. A leading '#' is allowed and ignored.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>An optional noise to go with it. A soundscript entry or a wave.</summary>
    [VarName("messagesound")]
    public string Sound { get; set; } = string.Empty;

    /// <summary>
    /// How far the noise carries, as one of four sizes rather than a number:
    /// 0 small, 1 medium, 2 large, 3 everywhere. Turned into a soundlevel at
    /// spawn, which is what the engine wants.
    /// </summary>
    [VarName("messageattenuation")]
    public int Attenuation { get; set; }

    /// <summary>0..10 from the map, 0..1 on the wire. Zero means full.</summary>
    [VarName("messagevolume")]
    public float Volume { get; set; } = 10f;
}

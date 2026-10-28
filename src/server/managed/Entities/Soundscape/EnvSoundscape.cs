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
/// env_soundscape: a place in the map that sounds like something.
///
/// <para>
/// A soundscape is a named set of looping and random sounds defined in
/// <c>scripts/soundscapes_*.txt</c>. This entity does not play any of it — the
/// client does, out of its own copy of the same file. What the entity does is
/// claim a piece of the map: stand near enough, with a clear line, and this is
/// what you hear.
/// </para>
///
/// <para>
/// The choosing lives in <see cref="Soundscapes"/> because it is a decision
/// about all of them at once rather than about any one; what lives here is what
/// one soundscape IS — where it is, how far it carries, whether it is switched
/// on, and where its positioned sounds come from.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvSoundscape)]
public class EnvSoundscape : ScriptedEntity
{
    /// <summary>making sounds, and what is heard.</summary>
    protected readonly IEngineSound Sound;
    /// <summary>What a map may set on this entity. See <see cref="EnvSoundscapeVars"/>.</summary>
    [ScriptVars] public EnvSoundscapeVars Vars = new();

    /// <summary>Fired every time this soundscape becomes a player's.</summary>
    public readonly Output OnPlay = new();

    /// <summary>
    /// This entity's identity token, as the client sees it. One-based; zero
    /// means no soundscape.
    ///
    /// Granted by registering in <see cref="OnSpawn"/>, which a hot reload does
    /// not run again -- so it is <c>[SaveField]</c>, one mark here carrying the
    /// proxy and the triggerable through this base. Uncarried it would come
    /// back zero, which every player reads as no soundscape at all
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] public int SoundscapeId { get; private set; }

    /// <summary>Which soundscape in the script files, or -1 if the name is not one.</summary>
    public int SoundscapeIndex { get; protected set; } = -1;

    /// <summary>The name the map gave. For tests and for the proxy to copy.</summary>
    public string SoundscapeName => Vars.SoundscapeName;

    public bool Enabled => !Vars.Disabled;

    public float Radius => Vars.Radius;

    /// <summary>
    /// Where the sound is heard FROM. Stock calls this EarPosition, which for
    /// anything that is not a player is simply where it stands.
    /// </summary>
    public Vector Ears => Origin;

    /// <summary>
    /// Whether this one is reached by standing in a volume rather than by being
    /// near it. False here, and overridden by the triggerable subclass.
    ///
    /// <para>
    /// Stock leaves triggerables in the same list as everything else and
    /// excludes them by accident — their radius defaults to zero, so the range
    /// test never passes. Saying it outright means a mapper who sets a radius
    /// on one does not get behaviour nobody designed.
    /// </para>
    /// </summary>
    public virtual bool IsTriggerable => false;

    /// <summary>
    /// The system that decides who hears what.
    ///
    /// <para>
    /// Injected rather than reached as a static, and this entity is the reason
    /// the distinction is not academic: it puts ITSELF in the list at spawn to
    /// get its identity token, and that token is one-based and never reused
    /// within a level because the client compares it with the last it saw to
    /// decide whether anything changed. A static list surviving a managed
    /// reload would hand a fresh map's first soundscape a token the client had
    /// already heard, and the change would read as no change — silently.
    /// </para>
    /// </summary>
    protected readonly ISoundscapes Soundscapes;

    public EnvSoundscape(ISoundscapes soundscapes, IEngineSound sound)
    {
        Soundscapes = soundscapes;
        Sound = sound;
    }

    public override void OnSpawn()
    {
        SoundscapeId = Soundscapes.Register(this);
        Precache();
    }

    /// <summary>
    /// Resolves the script name to the index both sides of the wire share.
    ///
    /// <para>
    /// Virtual because the proxy has no name of its own to resolve and must not
    /// complain about the one it has not got.
    /// </para>
    /// </summary>
    protected virtual void Precache()
    {
        if (Vars.SoundscapeName.Length == 0)
        {
            Log.Msg($"[SourceSharp] env_soundscape '{Name}' has no soundscape name\n");
            return;
        }

        SoundscapeIndex = Soundscapes.IndexOf(Vars.SoundscapeName);

        if (SoundscapeIndex < 0)
            Log.Warning($"[SourceSharp] no such soundscape: '{Vars.SoundscapeName}'\n");
    }

    [ScriptedEvent("Enable")]
    private void OnEnable() => Vars.Disabled = false;

    [ScriptedEvent("Disable")]
    private void OnDisable() => Vars.Disabled = true;

    [ScriptedEvent("ToggleEnabled")]
    private void OnToggleEnabled() => Vars.Disabled = !Vars.Disabled;

    /// <summary>
    /// Where this soundscape's positioned sounds come from, resolved by name,
    /// and a bitfield saying which of the eight are live.
    ///
    /// <para>
    /// Resolved on every apply rather than cached at spawn, which is stock and
    /// is right: a position can name something that moves, and a soundscape
    /// that latched the origin at level load would leave the dripping pipe
    /// behind when the pipe went past on a train.
    /// </para>
    /// </summary>
    public int ResolveLocalSounds(Span<Vector> into)
    {
        int bits = 0;

        for (int i = 0; i < SourceSharp.Server.Soundscapes.LocalSoundCount && i < into.Length; i++)
        {
            string name = PositionName(i);

            if (name.Length == 0)
                continue;

            var target = Entities.FindByName(name);

            if (!Entities.IsValid(target))
                continue;

            into[i] = Entities.GetOrigin(target);
            bits |= 1 << i;
        }

        return bits;
    }

    /// <summary>One of the eight position keys, by index.</summary>
    public string PositionName(int index) => index switch
    {
        0 => Vars.Position0,
        1 => Vars.Position1,
        2 => Vars.Position2,
        3 => Vars.Position3,
        4 => Vars.Position4,
        5 => Vars.Position5,
        6 => Vars.Position6,
        7 => Vars.Position7,
        _ => "",
    };

    /// <summary>Copies what a proxy borrows from its main soundscape.</summary>
    protected void CopyFrom(EnvSoundscape main)
    {
        SoundscapeIndex = main.SoundscapeIndex;

        Vars.Position0 = main.Vars.Position0;
        Vars.Position1 = main.Vars.Position1;
        Vars.Position2 = main.Vars.Position2;
        Vars.Position3 = main.Vars.Position3;
        Vars.Position4 = main.Vars.Position4;
        Vars.Position5 = main.Vars.Position5;
        Vars.Position6 = main.Vars.Position6;
        Vars.Position7 = main.Vars.Position7;
    }
}

/// <summary>
/// env_soundscape_proxy: a second place that sounds like the first one.
///
/// <para>
/// It is a soundscape with no sound of its own: it names another one and
/// borrows its index and its positioned sounds, keeping only its own position
/// and radius. What that buys a mapper is one soundscape definition covering
/// several rooms without the sounds restarting as you move between them —
/// because the client only restarts a soundscape when the INDEX changes, and
/// every proxy of one main soundscape reports the same index.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvSoundscapeProxy)]
public sealed class EnvSoundscapeProxy : EnvSoundscape
{
    public EnvSoundscapeProxy(ISoundscapes soundscapes, IEngineSound sound)
        : base(soundscapes, sound) { }

    /// <summary>What a map may set on this entity. See <see cref="EnvSoundscapeProxyVars"/>.</summary>
    [ScriptVars] public EnvSoundscapeProxyVars EnvSoundscapeProxyVars = new();

    private bool _resolved;

    /// <summary>
    /// Nothing to precache: the index comes from the main soundscape, which may
    /// not have spawned yet. Stock overrides Precache to say exactly this, and
    /// gives the same reason — otherwise it prints a missing-soundscape warning
    /// about a name it was never given.
    /// </summary>
    protected override void Precache()
    {
    }

    /// <summary>
    /// The lookup is deferred to the first think because it names ANOTHER map
    /// entity, and a map entity cannot be found while the map is still being
    /// parsed. Stock does it in Activate, which is the same moment by a
    /// different name.
    /// </summary>
    public override void OnSpawn()
    {
        base.OnSpawn();
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        if (_resolved)
            return;

        _resolved = true;

        var main = FindByName<EnvSoundscape>(EnvSoundscapeProxyVars.MainName);

        if (main is null)
        {
            Log.Warning($"[SourceSharp] env_soundscape_proxy '{Name}' "
                         + $"can't find target soundscape: '{EnvSoundscapeProxyVars.MainName}'\n");
            return;
        }

        CopyFrom(main);
    }
}

/// <summary>
/// env_soundscape_triggerable: a soundscape you have to be standing in.
///
/// <para>
/// Identical to a plain <see cref="EnvSoundscape"/> except that the automatic
/// pass never picks it — a <c>trigger_soundscape</c> naming it does, by volume
/// rather than by distance. That is what a mapper reaches for when the shape
/// that should sound like a room is a room rather than a sphere.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvSoundscapeTriggerable)]
public sealed class EnvSoundscapeTriggerable : EnvSoundscape
{
    public EnvSoundscapeTriggerable(ISoundscapes soundscapes, IEngineSound sound)
        : base(soundscapes, sound) { }

    public override bool IsTriggerable => true;
}

/// <summary>
/// The map keys <see cref="EnvSoundscape"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvSoundscapeVars
{
    /// <summary>
    /// How far it carries. -1 is unlimited, which many maps use and which is
    /// why the selection pass has a trace budget at all.
    /// </summary>
    public float Radius { get; set; }

    /// <summary>Switched off at map load until something enables it.</summary>
    [VarName("StartDisabled")]
    public bool Disabled { get; set; }

    // Eight names of entities the positioned sounds come from. Eight fields
    // rather than an array because a [ScriptVars] key binds one map key to one
    // field, and the map writes them as eight separate keys.
    public string Position0 { get; set; } = string.Empty;

    public string Position1 { get; set; } = string.Empty;

    public string Position2 { get; set; } = string.Empty;

    public string Position3 { get; set; } = string.Empty;

    public string Position4 { get; set; } = string.Empty;

    public string Position5 { get; set; } = string.Empty;

    public string Position6 { get; set; } = string.Empty;

    public string Position7 { get; set; } = string.Empty;

    /// <summary>The name of the soundscape in the script files.</summary>
    [VarName("soundscape")]
    public string SoundscapeName { get; set; } = string.Empty;
}

/// <summary>
/// The map keys <see cref="EnvSoundscapeProxy"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvSoundscapeProxyVars
{
    /// <summary>The soundscape entity to copy, by targetname.</summary>
    [VarName("MainSoundscapeName")]
    public string MainName { get; set; } = string.Empty;
}

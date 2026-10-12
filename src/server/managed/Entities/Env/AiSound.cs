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
/// ai_sound: the map's way of saying "something loud happened here".
///
/// <para>
/// The write half of <see cref="EnvMicrophone"/>'s read half. Both act on
/// <c>CSoundEnt</c>, the world's record of recent noise — not on anything a
/// player hears. This entity plays no sound, reaches no client and precaches
/// nothing; it enters a place, a loudness, a kind and a lifetime into a list,
/// and something else decides whether that matters.
/// </para>
///
/// <para>
/// <b>Why this is worth having with the AI gone.</b> The report filed this as a
/// leaf of <c>soundent</c> — cheap, and worth nothing until soundent moves,
/// which it never will. That was true when the only readers of the list were
/// <c>ai_*.cpp</c> files. It stopped being true when <c>env_microphone</c>
/// became a C# entity built on <see cref="IServerSoundEnt"/>: the list has
/// a managed reader today, and had no managed writer, so the subsystem was
/// reachable in one direction only. That is what this closes.
/// </para>
///
/// <para>
/// <b>What a map can still do with it.</b> A microphone measures how loud its
/// corner of the world is and fires an output carrying the number. An ai_sound
/// is how a map creates something for one to measure — a machine that gets
/// louder, a distant explosion the player did not cause — without firing a
/// weapon at the wall.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.AiSound)]
public sealed class AiSound : ScriptedEntity
{
    // IServerSoundEnt, NOT IEngineSound, since plan_gamedll_after.md Phase A3's
    // second pair: game/server/soundent.cpp has left server_csharp.vpc and the
    // list this entity writes into is managed, so the SoundEntInsert engine
    // slot it used to reach is gone.
    public AiSound(IServerSoundEnt sounds)
    {
        Sounds = sounds;
    }

    /// <summary>making sounds, and what is heard.</summary>
    private readonly IServerSoundEnt Sounds;
    /// <summary>What a map may set on this entity. See <see cref="AiSoundVars"/>.</summary>
    [ScriptVars] public AiSoundVars Vars = new();

    /// <summary>
    /// Enters the noise this entity was configured with.
    ///
    /// <para>
    /// Stock ORs the type and the context together here and not in
    /// <see cref="OnInsertSound"/>, and the difference is not tidiness: the
    /// context bits are a later addition and the legacy input predates them, so
    /// a map written against the old input gets the old behaviour exactly.
    /// </para>
    /// </summary>
    [ScriptedEvent("EmitAISound")]
    private void OnEmitAiSound() => Send(Modern());

    /// <summary>
    /// The obsolete input, kept because maps hold it.
    ///
    /// <para>
    /// Stock's own comment above it reads "*** OBSOLETE **** Here for legacy
    /// support only!". It takes the volume as the input's parameter rather than
    /// from the keyvalue, and it does NOT add the context bits. Both are
    /// preserved: an input that quietly behaves like its replacement is a map
    /// that behaves differently for no visible reason.
    /// </para>
    /// </summary>
    [ScriptedEvent("InsertSound")]
    private void OnInsertSound(float volume) => Send(Legacy(volume));

    /// <summary>
    /// One entry, before anything has been told about it.
    ///
    /// A named thing rather than three arguments so that WHAT each input decides
    /// can be checked without an engine to insert it into — which is the whole
    /// of what is worth testing here, the two inputs differing in exactly two
    /// ways.
    /// </summary>
    internal readonly record struct Noise(SoundType Type, int Volume, float Duration);

    /// <summary>What <c>EmitAISound</c> enters: the keyed volume, and the context bits ORed on.</summary>
    internal Noise Modern()
        => new((SoundType)(Vars.SoundType | Vars.SoundContext), Vars.Volume, Vars.Duration);

    /// <summary>What <c>InsertSound</c> enters: the input's volume, and no context bits.</summary>
    internal Noise Legacy(float volume)
        => new((SoundType)Vars.SoundType, (int)volume, Vars.Duration);

    /// <summary>
    /// Where the noise happened, and it is not necessarily here.
    ///
    /// <para>
    /// A map may name another entity as the location, which is the whole reason
    /// the key exists: the ai_sound sits somewhere convenient and the noise is
    /// credited to something that moves. Resolved per emit rather than at spawn,
    /// because a proxy that does not move makes the key pointless.
    /// </para>
    ///
    /// <para>
    /// A named proxy that cannot be found warns and falls back to this entity's
    /// own origin — stock's behaviour, and stock's DevWarning is worth keeping
    /// because the failure is otherwise a noise entered at the wrong end of the
    /// map.
    /// </para>
    /// </summary>
    internal Vector Where()
    {
        if (Vars.LocationProxy.Length == 0)
            return Origin;

        var proxy = Entities.FindByName(Vars.LocationProxy);

        if (Entities.IsValid(proxy))
            return Entities.GetOrigin(proxy);

        Log.Warning($"[SourceSharp] ai_sound '{Name}': cannot find proxy entity " +
                       $"named '{Vars.LocationProxy}'. Using self.\n");

        return Origin;
    }

    /// <summary>
    /// The one line both inputs come down to.
    ///
    /// <para>
    /// No owner is passed, and that is the one place this differs from stock,
    /// which credits the ai_sound itself. A CSound's owner is an EHANDLE to a
    /// CBaseEntity and an entity on the parallel hierarchy has none, so there is
    /// nothing to hand over. What it costs is narrow and worth writing down: an
    /// env_microphone with a ListenFilter set culls sounds with no owner, so a
    /// filtered microphone will not hear a managed ai_sound. An unfiltered one —
    /// which is every microphone in every map in this tree — is unaffected. See
    /// docs/divergences.md.
    /// </para>
    /// </summary>
    private void Send(in Noise noise)
        => Sounds.Insert((int)noise.Type, Where(), noise.Volume, noise.Duration);
}

/// <summary>
/// The map keys <see cref="AiSound"/> declares.
///
/// The defaults are stock's constructor, not zero: <c>CAISound</c> sets them in
/// its constructor precisely so that a legacy instance carrying none of the
/// later keys still behaves, and a duration of zero would be a noise that is
/// gone before anything samples the list.
/// </summary>
[ScriptVars]
public partial class AiSoundVars
{
    /// <summary>The SOUND_* kind. An engine number, written straight into the list.</summary>
    [VarName("soundtype")]
    public int SoundType { get; set; }

    /// <summary>
    /// The SOUND_CONTEXT_* qualifiers, ORed onto the kind by
    /// <c>EmitAISound</c> and deliberately not by <c>InsertSound</c>.
    /// </summary>
    [VarName("soundcontext")]
    public int SoundContext { get; set; }

    /// <summary>How far it carries, in the engine's units. Not a gain.</summary>
    [VarName("volume")]
    public int Volume { get; set; }

    /// <summary>How long the entry stays in the list. Stock's default is 0.3s.</summary>
    [VarName("duration")]
    public float Duration { get; set; } = 0.3f;

    /// <summary>Another entity to credit the noise's position to. See <see cref="AiSound.Where"/>.</summary>
    [VarName("locationproxy")]
    public string LocationProxy { get; set; } = string.Empty;
}

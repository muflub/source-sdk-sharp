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
/// env_embers: a volume with embers drifting up out of it.
///
/// <para>
/// The server entity is a brush, three keys and a <c>Use</c>. Stock's
/// <c>Spawn</c> binds the model, makes it invisible and not solid, and works out
/// whether it starts emitting. Everything else is on the client, where an ember
/// is spawned at a random point inside the brush and drifts.
/// </para>
///
/// <para>
/// It needed a client half written before the classname could move, for the
/// reason the whole particle and trail family turns on: stock's embers draw
/// because the server networks a <c>CEmbers</c> and the engine builds the
/// <c>C_Embers</c> paired with <c>DT_Embers</c>. A managed entity is never on
/// the wire as that class — it arrives through one of the six tier tables in
/// <c>generic_nettable.h</c> — so moving the name alone would have delivered
/// the entity perfectly and drawn nothing. See
/// <see cref="Client.Standalone.ClientEmbers"/> and
/// its sim, SourceSharp.Client.Effects.EmbersSim (lane c2-sims).
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvEmbers, ScriptedEntityKind.Brush)]
public sealed class EnvEmbers : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="EnvEmbersVars"/>.</summary>
    [ScriptVars] public EnvEmbersVars Vars = new();

    [NetState] public EmbersNetState State = new();

    /// <summary>env_embers' own spawnflags.</summary>
    [Flags]
    internal enum SpawnFlag
    {
        None = 0,

        /// <summary>Emit from the start even though the mapper named it.</summary>
        StartOn = 1 << 0,

        /// <summary>Answer more than one Use. Without it the first press is the last.</summary>
        Toggle = 1 << 1,
    }

    private SpawnFlag Flags => (SpawnFlag)SpawnFlags;

    /// <summary>Whether a Use has already been spent. Stock clears its use function instead.</summary>
    private bool _useSpent;

    public override void OnSpawn()
    {
        // A volume, not an obstacle, and drawn by nothing — the embers are drawn
        // and the brush is not. SOLID_NONE as stock's CEmbers::Spawn leaves it:
        // the client reads the model's bounds to decide where an ember may appear.
        SetSolidNone();
        SetVisible(false);

        State.Density  = Vars.Density;
        State.Lifetime = Vars.Lifetime;
        State.Speed    = Vars.Speed;

        State.Emit = StartsOn(Flags, named: Name.Length > 0);
    }

    /// <summary>
    /// Whether a volume emits from the moment it spawns.
    ///
    /// <para>
    /// Stock's own condition, and the second half of it is the surprising part:
    /// an UNNAMED env_embers starts on whatever its flags say, because nothing
    /// could ever switch it on. Only a named one waits.
    /// </para>
    ///
    /// <para>
    /// Static and taking both facts, so it can be checked without an engine —
    /// <see cref="Name"/> asks the engine and a test has none to ask, which is
    /// the same reason <c>EnvShake.RadiusFor</c> exists.
    /// </para>
    /// </summary>
    internal static bool StartsOn(SpawnFlag flags, bool named)
        => flags.HasFlag(SpawnFlag.StartOn) || !named;

    /// <summary>
    /// Stock's <c>EmberUse</c>, which answers every use type and then usually
    /// stops answering at all.
    ///
    /// <para>
    /// Without the Toggle flag stock clears its own use function after the first
    /// press, so a one-shot volume can be switched on and never off. That is
    /// kept: it is what every shipped map with one has had, and a volume that
    /// could be switched back off would be a different entity.
    /// </para>
    ///
    /// <para>
    /// The use TYPE is not reachable here — the parallel hierarchy sends "Use"
    /// with an empty variant — so the four-way switch becomes the toggle its
    /// default case already was. See docs/divergences.md.
    /// </para>
    /// </summary>
    [ScriptedEvent("Use")]
    private void OnUsed()
    {
        (State.Emit, _useSpent) = AfterUse(Flags, State.Emit, _useSpent);
    }

    /// <summary>
    /// What a Use does, and whether it was the last one this volume answers.
    ///
    /// <para>
    /// Static and taking the flags, so it can be checked without an engine:
    /// <see cref="SpawnFlags"/> asks the engine and a test has none to ask, so
    /// every case would collapse into the unflagged one and pass for the wrong
    /// reason. Same reason <see cref="StartsOn"/> is one.
    /// </para>
    /// </summary>
    internal static (bool Emit, bool Spent) AfterUse(SpawnFlag flags, bool emitting, bool spent)
    {
        if (spent)
            return (emitting, true);

        return (!emitting, !flags.HasFlag(SpawnFlag.Toggle));
    }
}

/// <summary>
/// The map keys <see cref="EnvEmbers"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvEmbersVars
{
    /// <summary>Embers per second, not a count.</summary>
    public int Density { get; set; }

    public int Lifetime { get; set; }

    public int Speed { get; set; }
}

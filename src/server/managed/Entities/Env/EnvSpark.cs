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
/// env_spark: a shower of sparks, on a timer.
///
/// The effect is one temp entity written straight to the wire and has no
/// server-side object at all. What is a decision — and what this class is — is
/// WHEN: a spark that fires at a fixed rate reads as machinery, and one that
/// fires at a jittered rate reads as a fault. Stock picks the second, and the
/// jitter is the entity.
/// </summary>
[ScriptedEntity(EntityNames.EnvSpark)]
[Precache(PrecacheKind.Sound, "DoSpark")]   // CEnvSpark::Precache, EnvSpark.cpp:114
public sealed class EnvSpark : ScriptedEntity
{
    public EnvSpark(IEngineEffects effects, IEngineHost host)
    {
        Effects = effects;
        Host = host;
    }

    /// <summary>the small effects.</summary>
    private readonly IEngineEffects Effects;

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="EnvSparkVars"/>.</summary>
    [ScriptVars] public EnvSparkVars Vars = new();

    /// <summary>The floor between sparks. Stock's, and it is what stops a zero delay spinning.</summary>
    private const float MinimumInterval = 0.1f;

    public readonly Output OnSpark = new();

    /// <summary>
    /// env_spark's own spawnflags.
    ///
    /// <para>
    /// The numbers are the reason for the type. "Start on" is 64 here, where on
    /// almost every other entity in the port it is 1 -- env_spark inherits a block
    /// of flags it does not use and its own begin above them. A bare 64 read off
    /// this word is also func_platrot's "rotate roll" and a trigger's "everything
    /// else", and only the type says which.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000_0000_0000,

        StartOn = 0b0000_0000_0100_0000,
        Silent = 0b0000_0001_0000_0000,  // 0x100
        Directional = 0b0000_0010_0000_0000,  // 0x200
    }

    private bool _on;

    public override void OnSpawn()
    {

        if (!Flags.HasFlag(SpawnFlag.StartOn))
            return;

        _on = true;
        SetNextThink(NextInterval());
    }

    public override void OnThink()
    {
        if (!_on)
            return;

        Spark();
        SetNextThink(NextInterval());
    }

    /// <summary>
    /// How long until the next one.
    ///
    /// The randomness comes from the ENGINE's stream rather than a managed one,
    /// because demo playback replays server randomness and has to come out the
    /// same. Managed code may ask for a number and may not be the source of one.
    /// </summary>
    private float NextInterval()
        => MinimumInterval + (Vars.MaxDelay > 0f ? Host.RandomFloat(0f, Vars.MaxDelay) : 0f);

    private void Spark()
    {
        // Directional sparks go where the mapper pointed the entity; the rest
        // go everywhere, which is a NULL direction rather than a zero one.
        Vector? direction = Flags.HasFlag(SpawnFlag.Directional)
            ? Angles.Forward(Entities.GetAngles(Handle))
            : null;

        Effects.SparkEffect(Origin, Vars.Magnitude, Vars.TrailLength,
                           makeSound: !Flags.HasFlag(SpawnFlag.Silent), direction);

        OnSpark.Fire();
    }

    [ScriptedEvent("StartSpark")]
    private void OnStart()
    {
        _on = true;
        SetNextThink(NextInterval());
    }

    [ScriptedEvent("StopSpark")]
    private void OnStop()
    {
        _on = false;
        StopThinking();
    }

    [ScriptedEvent("ToggleSpark")]
    private void OnToggle()
    {
        if (_on) OnStop(); else OnStart();
    }

    /// <summary>
    /// One spark now, and it does NOT start the timer — the same distinction
    /// point_hurt's Hurt input draws. A map that wants both fires both.
    /// </summary>
    [ScriptedEvent("SparkOnce")]
    private void OnSparkOnce() => Spark();
}

/// <summary>
/// The map keys <see cref="EnvSpark"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvSparkVars
{
    /// <summary>
    /// The LONGEST wait between sparks, not the wait itself.
    ///
    /// The key is called MaxDelay and stock draws uniformly from zero to it,
    /// then adds a tenth of a second. So the name is honest and the average is
    /// about half of what a mapper writes.
    /// </summary>
    public float MaxDelay { get; set; }

    public int Magnitude { get; set; } = 1;

    public int TrailLength { get; set; } = 1;
}

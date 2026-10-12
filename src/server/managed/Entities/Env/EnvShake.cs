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
/// env_shake: the floor moving.
///
/// The whole of the camera half is a user message to every player in range,
/// which is why it is one engine call and not something managed. What is
/// managed is when to send it, at what strength, and for how long — and the
/// fact that a shake can be turned UP while it runs rather than only switched
/// on, which is what lets a map ramp one as something gets worse.
/// </summary>
[ScriptedEntity(EntityNames.EnvShake)]
public sealed class EnvShake : ScriptedEntity
{
    public EnvShake(IScreenEffects hud)
    {
        Hud = hud;
    }

    /// <summary>drawing, and the local player it is drawn for.</summary>
    private readonly IScreenEffects Hud;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="EnvShakeVars"/>.</summary>
    [ScriptVars] public EnvShakeVars Vars = new();

    /// <summary>
    /// env_shake's own spawnflags, stock's SF_SHAKE_*.
    ///
    /// <para>
    /// Numbered nothing like env_viewpunch's, which asks the same two opening
    /// questions: "everyone" is 1 on both, but "in air" is 4 here and 2 there.
    /// Two entities that read alike and count differently is the case a shared
    /// constant would get wrong, so they stay two types.
    /// </para>
    /// </summary>
    [Flags]
    internal enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        Everyone = 0b0000_0001,
        InAir = 0b0000_0100,
        NoView = 0b0010_0000,
        NoRumble = 0b0100_0000,
    }

    /// <summary>
    /// Zero radius means everyone, which is not the same as nobody.
    ///
    /// The engine reads a radius of zero as "no falloff, send to all", so the
    /// spawnflag and an unset radius arrive at the same place — and a map that
    /// sets the flag but leaves a radius behind must not have the radius win.
    /// </summary>
    internal float EffectiveRadius => RadiusFor((SpawnFlag)SpawnFlags, Vars.Radius);

    /// <summary>
    /// Static, and taking the flags rather than reading them, so it can be
    /// tested at all: SpawnFlags asks the engine, and a test has no engine to
    /// ask — it would silently see zero and pass for the wrong reason.
    /// </summary>
    internal static float RadiusFor(SpawnFlag spawnFlags, float radius)
        => spawnFlags.HasFlag(SpawnFlag.Everyone) ? 0f : radius;

    [ScriptedEvent("StartShake")]
    private void OnStartShake() => Shake(ShakeCommand.Start);

    [ScriptedEvent("StopShake")]
    private void OnStopShake() => Shake(ShakeCommand.Stop);

    /// <summary>
    /// Changing the strength of a shake already running, rather than starting a
    /// new one on top. Two shakes at once do not add up to a bigger shake —
    /// the client keeps one — so a map ramping a tremor needs this.
    /// </summary>
    [ScriptedEvent("Amplitude")]
    private void OnAmplitude(float value)
    {
        Vars.Amplitude = value;
        Shake(ShakeCommand.Amplitude);
    }

    [ScriptedEvent("Frequency")]
    private void OnFrequency(float value)
    {
        Vars.Frequency = value;
        Shake(ShakeCommand.Frequency);
    }

    private void Shake(ShakeCommand command)
    {
        // The rumble-only and no-rumble variants are the same shake with a
        // different half switched off, so the flags pick the command rather
        // than adding a second call.
        ShakeCommand actual = command switch
        {
            ShakeCommand.Start when Flags.HasFlag(SpawnFlag.NoView) => ShakeCommand.StartRumbleOnly,
            ShakeCommand.Start when Flags.HasFlag(SpawnFlag.NoRumble) => ShakeCommand.StartNoRumble,
            _ => command,
        };

        Hud.Shake(Origin, Vars.Amplitude, Vars.Frequency, Vars.Duration, EffectiveRadius,
                           actual, Flags.HasFlag(SpawnFlag.InAir));
    }
}

/// <summary>
/// The map keys <see cref="EnvShake"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvShakeVars
{
    public float Amplitude { get; set; }

    public float Frequency { get; set; }

    public float Duration { get; set; }

    /// <summary>How far it is felt. Ignored entirely when the map says everyone.</summary>
    public float Radius { get; set; }
}

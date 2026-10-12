//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using System;
using System.Globalization;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The eight movement ConVars the mod's tuning is written into, and the audit
/// that says when something moved one of them afterwards —
/// plan_server_novalve.md P2.3, out of shell_movement.cpp's SetEngineConVar and
/// shell_movement_audit.cpp.
///
/// <para>
/// <b>APPLIED ONCE, AS A STARTING VALUE.</b> <see cref="Movement.Current"/>'s
/// numbers are written into the eight at managed startup and never again, so
/// they behave as defaults: a server operator's config, read afterwards, still
/// wins. The declared defaults stay stock's, so a config that names one of them
/// resolves the way it always did.
/// </para>
///
/// <para>
/// <b>WHAT THE AUDIT SEPARATES.</b> A value that later moves is one of three
/// things (<see cref="Classify"/>): the mod's own number (nothing to say), the
/// DECLARED default (the defect docs/defect-convar-order.md diagnosed —
/// something re-applied a declaration after startup), or a third number (an
/// operator's override, said once at Msg because a movement number that is not
/// the mod's is worth seeing when somebody asks why the game feels wrong).
/// </para>
/// </summary>
public static class MovementTuning
{
    /// <summary>
    /// The eight, in the order they are written. Written out rather than
    /// derived: MovementParams carries twelve numbers and only these eight reach
    /// a ConVar.
    /// </summary>
    public static readonly ImmutableArray<string> Names =
    [
        "sv_gravity",
        "sv_friction",
        "sv_accelerate",
        "sv_airaccelerate",
        "sv_stopspeed",
        "sv_maxvelocity",
        "sv_stepsize",
        "sv_maxspeed",
    ];

    public enum Verdict
    {
        AsRegistered,
        RevertedToDeclared,
        Overridden,
    }

    /// <summary>
    /// A tolerance rather than ==: every value makes a round trip through a
    /// decimal string on its way into the engine ("%f", six decimals), so
    /// 0.33333334 comes back as 0.333333. The smallest of the eight is 1, and
    /// the reverts that matter are 600 against 800 and 75 against 100.
    /// </summary>
    private static bool Same(float a, float b) => MathF.Abs(a - b) <= 1e-3f;

    /// <summary>
    /// FIRST the mod's own number: when the mod's tuning IS the declaration,
    /// both tests match and "nothing has moved" is the true answer.
    /// </summary>
    public static Verdict Classify(float registered, float declaredDefault, float now)
    {
        if (Same(now, registered))
            return Verdict.AsRegistered;
        if (Same(now, declaredDefault))
            return Verdict.RevertedToDeclared;
        return Verdict.Overridden;
    }

    /// <summary>Case-sensitive: these are spelled once each, here.</summary>
    public static int IndexOf(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return -1;
        return Names.IndexOf(name);
    }

    /// <summary>The value the mod's tuning gives each of the eight.</summary>
    public static float ValueFor(int index, in MovementParams p) => index switch
    {
        0 => p.Gravity,
        1 => p.Friction,
        2 => p.Accelerate,
        3 => p.AirAccelerate,
        4 => p.StopSpeed,
        5 => p.MaxVelocity,
        6 => p.StepSize,
        7 => p.MaxSpeed,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    internal static FloatConVar? Find(ConVarRegistry convars, string name)
    {
        foreach (ConVarBase cv in convars.All)
            if (cv is FloatConVar f && string.Equals(cv.Name, name, StringComparison.Ordinal))
                return f;
        return null;
    }
}

/// <summary>
/// A movement ConVar the mod's tuning is written into: its every change is
/// audited by <see cref="MovementTuning.Audit"/>.
/// </summary>
public abstract class MovementTunedConVar : FloatConVar
{
    /// <summary>
    /// The record this convar's changes are audited against: the module's,
    /// set by <see cref="AppliedMovementTuning.Apply"/> when it writes this
    /// convar. Null until then, and a change before then is not audited — as
    /// it was not when the record was a static.
    /// </summary>
    internal IAppliedMovementTuning? Tuning { get; set; }

    protected override void OnChanged(float oldValue, float newValue)
        => Tuning?.Audit(Name, oldValue.ToString(CultureInfo.InvariantCulture), DefaultValue, newValue);
}

/// <summary>
/// What the module wrote into the eight movement convars, and the audit of
/// their later changes against it. See <see cref="MovementTuning"/>.
/// </summary>
public interface IAppliedMovementTuning
{
    /// <summary>Writes the tuning into the eight and remembers what was written.</summary>
    void Apply(ConVarRegistry convars, in MovementParams p);

    /// <summary>A tuned convar moved: say so when the move is not the mod's own number.</summary>
    void Audit(string name, string oldValue, string declaredDefault, float now);
}

/// <summary>
/// The server module's record of the movement tuning it applied, registered in
/// its container. It was a static array on <see cref="MovementTuning"/> that the
/// convars reached through a static <c>Audit</c>; the record is this module's
/// now, and each tuned convar is handed it when it is written.
/// </summary>
public sealed class AppliedMovementTuning(ILog log) : IAppliedMovementTuning
{
    private readonly float?[] _registered = new float?[MovementTuning.Names.Length];

    /// <summary>Records that the module wrote <paramref name="value"/> into the index'th convar.</summary>
    internal void Remember(int index, float value) => _registered[index] = value;

    /// <summary>
    /// Writes the tuning into the eight and remembers what was written. A name
    /// the registry does not hold is said by name: that parameter does nothing.
    /// </summary>
    public void Apply(ConVarRegistry convars, in MovementParams p)
    {
        for (int i = 0; i < MovementTuning.Names.Length; i++)
        {
            FloatConVar? cv = MovementTuning.Find(convars, MovementTuning.Names[i]);
            if (cv == null)
            {
                log.Warning($"[SourceSharp] movement: '{MovementTuning.Names[i]}' is not a convar in this build; " +
                            "that parameter does nothing\n");
                continue;
            }

            float value = MovementTuning.ValueFor(i, p);
            Remember(i, value);

            // The convar audits its later changes against THIS record. It
            // reached a static one before; setting it here, before the value is
            // written, keeps the order the static had.
            if (cv is MovementTunedConVar tuned)
                tuned.Tuning = this;

            cv.SetValue(value);
        }

        log.Msg($"[SourceSharp] movement: jump {p.JumpHeight:0}u, gravity {p.Gravity:0}, " +
                $"friction {p.Friction:0.0}, max speed {p.MaxSpeed:0}, air cap {p.AirSpeedCap:0}, " +
                $"duck {p.DuckSpeedScale:0.00}x\n");
    }

    /// <summary>
    /// A tuned ConVar moved. Silent until <see cref="Apply"/> has written it,
    /// and for a value that is the mod's own.
    /// </summary>
    public void Audit(string name, string oldValue, string declaredDefault, float now)
    {
        int i = MovementTuning.IndexOf(name);
        if (i < 0 || _registered[i] is not float registered)
            return;

        float declared = float.TryParse(declaredDefault, NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out float d) ? d : float.NaN;

        switch (MovementTuning.Classify(registered, declared, now))
        {
            case MovementTuning.Verdict.AsRegistered:
                return;
            case MovementTuning.Verdict.RevertedToDeclared:
                log.Warning($"[SourceSharp] movement: '{name}' is back at its DECLARED DEFAULT " +
                            $"{declared:g} (was '{oldValue}'; this mod set {registered:g}). Something " +
                            "re-applied a declaration after managed startup -- see " +
                            "docs/defect-convar-order.md\n");
                return;
            case MovementTuning.Verdict.Overridden:
                log.Msg($"[SourceSharp] movement: '{name}' overridden to {now:g} (this mod set " +
                        $"{registered:g}, declaration says {declared:g})\n");
                return;
        }
    }
}

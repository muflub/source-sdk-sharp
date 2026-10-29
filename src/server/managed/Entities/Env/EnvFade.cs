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
/// env_fade: the screen going to a colour and back.
///
/// The same shape as <see cref="EnvShake"/> and a natural pair with it — the
/// transport is a user message the engine sends, and what lives here is the
/// decision: which colour, how long, whether it comes back, and whether it is
/// for one player or for everybody.
/// </summary>
[ScriptedEntity(EntityNames.EnvFade)]
public sealed class EnvFade : ScriptedEntity
{
    public EnvFade(IScreenEffects hud)
    {
        Hud = hud;
    }

    /// <summary>drawing, and the local player it is drawn for.</summary>
    private readonly IScreenEffects Hud;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="EnvFadeVars"/>.</summary>
    [ScriptVars] public EnvFadeVars Vars = new();

    /// <summary>
    /// env_fade's own spawnflags, stock's SF_FADE_*.
    ///
    /// <para>
    /// These are the map's request; <see cref="FadeFlags"/> is what goes over the
    /// wire to the client, and the two are deliberately different types. Three of
    /// these four turn into wire bits and the fourth -- OnlyOne -- never leaves the
    /// server, because it decides WHO is faded rather than HOW. A single enum for
    /// both would put a bit on the wire that the client has no meaning for.
    /// </para>
    /// </summary>
    [Flags]
    internal enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        FadeIn = 0b0000_0001,
        Modulate = 0b0000_0010,
        /// <summary>The activator alone, rather than everybody. Never reaches the client.</summary>
        OnlyOne = 0b0000_0100,

        StayOut = 0b0000_1000,
    }

    public readonly Output OnBeginFade = new();

    [ScriptedEvent("Fade")]
    private void OnFade(InputData input)
    {
        // InputData rather than an EntityHandle parameter, because the binder
        // accepts float, string or InputData and nothing else -- it warns and
        // skips anything it cannot bind, which is how this arrived registered
        // with zero inputs and an entity that ignored being fired at.
        //
        // Only the player who set it off, when the map says so. Anything that
        // is not a player falls through to everybody rather than fading nobody,
        // which is what a map firing this from a timer means.
        EntityHandle target = Flags.HasFlag(SpawnFlag.OnlyOne) ? input.Activator : EntityHandle.None;

        Hud.Fade(target, (int)Vars.Colour.X, (int)Vars.Colour.Y, (int)Vars.Colour.Z, Vars.Alpha,
                          Vars.Duration, Vars.HoldTime, FlagsFor((SpawnFlag)SpawnFlags));

        OnBeginFade.Fire();
    }

    /// <summary>
    /// The map's spawnflags as the engine's fade flags.
    ///
    /// Static and taking the flags rather than reading SpawnFlags, because that
    /// property asks the ENGINE — a test has none to ask, would silently see
    /// zero, and would pass for the wrong reason.
    ///
    /// IN and OUT are exclusive and one of them is always set: a fade with
    /// neither is not a fade, and the engine's own comment on FFADE_IN says it
    /// exists so that zero is never passed in.
    /// </summary>
    internal static FadeFlags FlagsFor(SpawnFlag spawnFlags)
    {
        FadeFlags flags = spawnFlags.HasFlag(SpawnFlag.FadeIn) ? FadeFlags.In : FadeFlags.Out;

        if (spawnFlags.HasFlag(SpawnFlag.Modulate))
            flags |= FadeFlags.Modulate;

        if (spawnFlags.HasFlag(SpawnFlag.StayOut))
            flags |= FadeFlags.StayOut;

        return flags;
    }
}

/// <summary>
/// The map keys <see cref="EnvFade"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvFadeVars
{
    public float Duration { get; set; } = 2f;

    /// <summary>How long it stays at full colour before coming back.</summary>
    public float HoldTime { get; set; }

    /// <summary>
    /// The colour, from the standard render keys — which is how Hammer presents
    /// it, so a mapper picks a fade colour with the same swatch they use
    /// everywhere else.
    /// </summary>
    [VarName("rendercolor")]
    public Vector Colour { get; set; }

    [VarName("renderamt")]
    public int Alpha { get; set; } = 255;
}

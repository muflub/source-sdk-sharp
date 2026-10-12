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
/// env_viewpunch: one kick to the view of everyone nearby.
///
/// Not a shake, and the difference is what a mapper is reaching for. A shake
/// rattles for a duration; this is a single shove, for the moment something
/// lands.
/// </summary>
[ScriptedEntity(EntityNames.EnvViewpunch)]
public sealed class EnvViewPunch : ScriptedEntity
{
    public EnvViewPunch(IEngineHud hud)
    {
        Hud = hud;
    }

    /// <summary>drawing, and the local player it is drawn for.</summary>
    private readonly IEngineHud Hud;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => (SpawnFlag)SpawnFlags;

    /// <summary>What a map may set on this entity. See <see cref="EnvViewPunchVars"/>.</summary>
    [ScriptVars] public EnvViewPunchVars Vars = new();

    /// <summary>
    /// env_viewpunch's own spawnflags, and specifically not env_shake's.
    ///
    /// <para>
    /// The two entities ask the same two opening questions -- everyone or only those
    /// in range, and does it reach a player off the ground -- and number the second
    /// differently: 2 here, 4 there. Two types is what makes that impossible to get
    /// wrong; one shared constant would be right on one entity and silently wrong on
    /// the other.
    /// </para>
    /// </summary>
    [Flags]
    internal enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        Everyone = 0b0000_0001,
        InAir = 0b0000_0010,
    }

    [ScriptedEvent("ViewPunch")]
    private void OnViewPunch()
        => Hud.ViewPunch(Origin, Vars.Punch, RadiusFor(Flags, Vars.Radius),
                            Flags.HasFlag(SpawnFlag.InAir));

    /// <summary>
    /// Zero is everyone, the same convention the shake uses — so the flag and an
    /// unset radius arrive at the same place, and a map that set the flag but
    /// left a radius behind must not have the radius win.
    /// </summary>
    internal static float RadiusFor(SpawnFlag spawnFlags, float radius)
        => spawnFlags.HasFlag(SpawnFlag.Everyone) ? 0f : radius;
}

/// <summary>
/// The map keys <see cref="EnvViewPunch"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvViewPunchVars
{
    /// <summary>Pitch, yaw and roll of the kick.</summary>
    [VarName("punchangle")]
    public Vector Punch { get; set; }

    public float Radius { get; set; }
}

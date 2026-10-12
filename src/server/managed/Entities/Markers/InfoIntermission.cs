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
/// Where the camera stands when the round is over.
///
/// A marker that aims ITSELF, which is the only reason it is more than an
/// info_target. A mapper places it somewhere with a good view and names the
/// thing worth looking at; it works out the facing, so the angles in the map are
/// one fewer number to get wrong by hand.
///
/// The aim is taken from a think two seconds after spawn rather than at spawn,
/// which is stock's timing and is kept: the target is usually a brush entity or
/// a prop, and stock spawns those in hierarchical order around this one. Doing
/// it at spawn would work on the parallel hierarchy — logical entities spawn
/// after the whole CBaseEntity world — but only by relying on that, and a
/// two-second wait for a camera nobody looks at until the round ends buys
/// nothing worth the coupling.
/// </summary>
[ScriptedEntity(EntityNames.InfoIntermission)]
public sealed class InfoIntermission : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="InfoIntermissionVars"/>.</summary>
    [ScriptVars] public InfoIntermissionVars Vars = new();

    /// <summary>Stock's delay, and stock's reason: let the targets spawn.</summary>
    private const float LookDelay = 2f;

    public override void OnSpawn()
    {
        // Stock zeroes the angles here, and that DISCARDS whatever the map wrote
        // — an info_intermission with angles and no target faces east either
        // way. Kept rather than improved: a map built against stock has never
        // been able to set this by hand, so no map is relying on it, and a
        // managed version that honoured the key would aim differently from the
        // one every existing map was placed against.
        SetAngles(Vector.Zero);

        SetNextThink(LookDelay);
    }

    /// <summary>
    /// Aims once and never again. The think is not re-armed, which is stock —
    /// a camera that re-aimed every frame would follow a moving target, and
    /// none of the maps that use one expect that.
    /// </summary>
    public override void OnThink()
    {
        if (Vars.Target.Length == 0)
            return;

        var target = Entities.FindByName(Vars.Target);

        if (!Entities.IsValid(target))
        {
            // Stock gives up in silence. This says so: the entity is invisible,
            // has no output and is not looked at until the round ends, so a
            // misspelt target name is a mistake with no symptom at all until
            // somebody wins.
            Log.Warning($"[SourceSharp] info_intermission '{Name}': no entity named '{Vars.Target}' to look at\n");
            return;
        }

        SetAngles(Facing(Origin, Entities.GetOrigin(target)));
    }

    /// <summary>
    /// The angles that look from one place to another.
    ///
    /// Separated out because it is the only arithmetic here and the sign
    /// conventions are the part that goes wrong: Source's pitch looks DOWN when
    /// it is positive, so a camera above its target has a positive pitch.
    /// </summary>
    internal static Vector Facing(Vector from, Vector to) => Angles.FromVector((to - from).Normalized);
}

/// <summary>
/// The map keys <see cref="InfoIntermission"/> declares.
/// </summary>
[ScriptVars]
public partial class InfoIntermissionVars
{
    /// <summary>Name of what the camera should look at. Optional.</summary>
    public string Target { get; set; } = string.Empty;
}

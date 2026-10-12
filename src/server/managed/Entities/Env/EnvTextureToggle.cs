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
/// env_texturetoggle: changes which frame of an animated material a brush shows.
///
/// A material with several frames is how a map draws a screen that changes or a
/// panel that lights up. The frames are the artist's and stay entirely in the
/// engine; WHICH one is showing is the map's, and that is all of this.
/// </summary>
[ScriptedEntity(EntityNames.EnvTexturetoggle)]
public sealed class EnvTextureToggle : ScriptedEntity
{
    public EnvTextureToggle(IEngineEntityRender render)
    {
        Render = render;
    }

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;
    /// <summary>What a map may set on this entity. See <see cref="EnvTextureToggleVars"/>.</summary>
    [ScriptVars] public EnvTextureToggleVars Vars = new();

    [ScriptedEvent("IncrementTextureIndex")]
    private void OnIncrement() => Apply(current => current + 1);

    [ScriptedEvent("SetTextureIndex")]
    private void OnSetIndex(float index) => Apply(_ => (int)index);

    /// <summary>
    /// Every brush of that name, not the first.
    ///
    /// Each is stepped from ITS OWN current frame rather than from a number this
    /// entity remembers, which is what lets one toggle drive several screens
    /// that are not in step. Stock reads the index off each entity in the loop
    /// for the same reason.
    /// </summary>
    private void Apply(Func<int, int> next)
    {
        if (Vars.Target.Length == 0)
            return;

        using NameMatches targets = Entities.Named(Vars.Target);

        if (targets.Count == 0)
        {
            Log.Warning($"[SourceSharp] env_texturetoggle '{Name}': nothing named '{Vars.Target}'\n");
            return;
        }

        foreach (EntityHandle target in targets)
            Render.SetTextureFrame(target, next(Render.GetTextureFrame(target)));
    }
}

/// <summary>
/// The map keys <see cref="EnvTextureToggle"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvTextureToggleVars
{
    /// <summary>Name of the brush or brushes to change.</summary>
    public string Target { get; set; } = string.Empty;
}

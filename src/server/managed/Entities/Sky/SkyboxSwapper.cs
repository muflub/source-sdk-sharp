//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// skybox_swapper: changes the 2D sky when something triggers it.
///
/// <para>
/// The 2D skybox is the six-faced cube drawn behind everything, and which one is
/// showing is a single replicated convar, <c>sv_skyname</c>. So the entity is a
/// name, a precache and one write — day becomes night when a map says so.
/// </para>
///
/// <para>
/// The precache is the part that is easy to leave out and impossible to miss
/// once it is: the six faces have to be loaded before the convar changes, or the
/// first frame after the swap draws whatever the renderer has for a material it
/// has never seen. That is why <c>Trigger</c> is cheap here — all the work
/// happened at spawn.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SkyboxSwapper)]
public sealed class SkyboxSwapper : ScriptedEntity
{
    public SkyboxSwapper(IEngineConsole console, IEnginePrecache precache)
    {
        Console = console;
        Precache = precache;
    }

    /// <summary>the console: convars, and commands run on the server or a client.</summary>
    private readonly IEngineConsole Console;

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;
    /// <summary>What a map may set on this entity. See <see cref="SkyboxSwapperVars"/>.</summary>
    [ScriptVars] public SkyboxSwapperVars Vars = new();

    /// <summary>
    /// The six faces, in the order the engine names them. Right, back, left,
    /// front, up, down — an order that is neither alphabetical nor axis-major
    /// and is simply what the engine expects.
    /// </summary>
    private static readonly ImmutableArray<string> Faces = [ "rt", "bk", "lf", "ft", "up", "dn" ];

    /// <summary>The materials this entity precached, for tests.</summary>
    public static string[] MaterialNames(string skyboxName)
    {
        var names = new string[Faces.Length];

        for (int i = 0; i < Faces.Length; i++)
            names[i] = $"skybox/{skyboxName}{Faces[i]}";

        return names;
    }

    public override void OnSpawn()
    {
        if (Vars.SkyboxName.Length == 0)
        {
            Log.Warning($"[SourceSharp] skybox_swapper '{Name}' has no skybox specified\n");
            return;
        }

        foreach (var material in MaterialNames(Vars.SkyboxName))
            Precache.PrecacheMaterial(material);
    }

    /// <summary>
    /// Swaps the sky.
    ///
    /// <para>
    /// Through the console rather than through a convar handle, and that is a
    /// real difference worth naming: <c>sv_skyname</c> belongs to the engine, and
    /// the managed convar API addresses convars this mod declared. The command
    /// queue applies it at the end of the tick instead of immediately, which for
    /// a sky nobody is looking at during the same tick costs nothing.
    /// </para>
    ///
    /// <para>
    /// The existence check is stock's diagnostic, kept: a swap that silently
    /// does nothing because the convar is not there is exactly the kind of
    /// failure a mapper cannot debug.
    /// </para>
    /// </summary>
    [ScriptedEvent("Trigger")]
    private void OnTrigger()
    {
        if (Vars.SkyboxName.Length == 0)
            return;

        if (Console.FindConVarValue("sv_skyname") is null)
        {
            Log.Warning($"[SourceSharp] skybox_swapper '{Name}': no 'sv_skyname' convar\n");
            return;
        }

        Console.ExecuteServerCommand($"sv_skyname {Vars.SkyboxName}");
    }
}

/// <summary>
/// The map keys <see cref="SkyboxSwapper"/> declares.
/// </summary>
[ScriptVars]
public partial class SkyboxSwapperVars
{
    /// <summary>
    /// The sky to change to, as a material-name prefix: "sky_day01_01" names
    /// six materials under skybox/, one per face.
    /// </summary>
    public string SkyboxName { get; set; } = string.Empty;
}

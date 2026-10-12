using SourceSharp.Mods;

namespace SourceSharp.Bootstrap;

/// <summary>
/// The assemblies each side loads, from the mod's entry in mods.yaml.
///
/// <para>The launcher (source#) says which file and which mod through
/// SOURCESHARP_MODS_YAML and SOURCESHARP_MOD. A game started without it -- the
/// engine run directly with -game -- has neither, and then the file is the one
/// at the top of the game directory, two levels above the managed directory
/// (bin/&lt;config&gt;/bin/managed), and the mod is that file's default.
/// An -instance game runs on a game directory of its own, which source# names
/// in SOURCESHARP_GAME_DIR; the mod's GameDir is then that one.</para>
/// </summary>
internal static class ModAssemblies
{
    private static ModsConfig? s_Config;
    private static Mod? s_Mod;

    /// <summary>The mod whose assemblies are loaded, once one has been chosen.</summary>
    public static string ModId => s_Mod?.Id ?? "(none)";

    /// <summary>The mod resolved by the first <see cref="For"/>, or null before it.</summary>
    public static Mod? Current => s_Mod;

    public static IReadOnlyList<string> For(bool server, string managedDir)
    {
        if (s_Mod is null)
        {
            string path = Environment.GetEnvironmentVariable("SOURCESHARP_MODS_YAML") is { Length: > 0 } env
                ? env
                : Path.GetFullPath(Path.Combine(managedDir, "..", "..", "mods.yaml"));
            s_Config = ModsConfig.Load(path);
            s_Mod = s_Config.Select(Environment.GetEnvironmentVariable("SOURCESHARP_MOD") is { Length: > 0 } id ? id : null)
                .RunningIn(Environment.GetEnvironmentVariable(Mod.GameDirVariable));
        }

        return s_Mod.AssembliesFor(server);
    }
}

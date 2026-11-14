using SourceSharp.Abi;

namespace SourceSharp.Ai.Server.Experience;

/// <summary>
/// <c>ai_experience_db</c> (plan_bots_teams_tf2 Part G, G.3): the file the
/// bots' experience of maps and modes is kept in. <c>default</c> is the user's
/// data directory (<c>~/.local/share/sourcesharp/ai-experience.db</c>;
/// <c>%LOCALAPPDATA%\sourcesharp\</c> on Windows); empty is off, and the bots
/// read the empty experience. Read when a level starts.
/// </summary>
[ConVar("ai_experience_db", ExperienceDbPath.Default,
        Help = "The bots' experience database: a path, \"default\" for the user's data directory (~/.local/share/sourcesharp/ai-experience.db), \"\" for off; read at a level's start")]
public sealed class AiExperienceDbConVar : StringConVar { }

/// <summary><c>ai_experience_half_life</c>: a stored count halves every this many rounds played on its map and mode; 0 never fades.</summary>
[ConVar("ai_experience_half_life", "20", Help = "Rounds on a map and mode after which what the bots' experience holds of it counts half; 0 never fades", Min = 0, Max = 100000)]
public sealed class AiExperienceHalfLifeConVar : FloatConVar { }

/// <summary><c>ai_experience_learn</c>: 0 reads the experience without writing to it.</summary>
[ConVar("ai_experience_learn", "1", Help = "1: what players do is added to the bots' experience; 0: the experience is read and nothing is written")]
public sealed class AiExperienceLearnConVar : BoolConVar { }

/// <summary>Where <c>ai_experience_db</c>'s value points.</summary>
public static class ExperienceDbPath
{
    /// <summary>The value that stands for the user's data directory.</summary>
    public const string Default = "default";

    /// <summary>The directory under the user's data directory, and the file in it.</summary>
    public const string Folder = "sourcesharp", FileName = "ai-experience.db";

    /// <summary>The launcher's switch on a game a tool, a gate or a lane started (src/launcher/Launcher.cs, ToolGameFlag).</summary>
    public const string ToolGameSwitch = "-toolgame";

    /// <summary>
    /// The file <paramref name="value"/> names, or null for off: an empty value;
    /// or <see cref="Default"/> in a tool's game (<paramref name="toolGame"/>),
    /// which never touches the user's own file, or with no user data directory
    /// to put it in. A path is taken as given, in a tool's game too: that is
    /// how a tool runs on a private store. <paramref name="userData"/> is the
    /// platform's per-user data directory (<c>~/.local/share</c>,
    /// <c>%LOCALAPPDATA%</c>).
    /// </summary>
    public static string? Resolve(string value, string userData, bool toolGame)
    {
        string named = value.Trim();
        if (named.Length == 0)
            return null;
        if (!named.Equals(Default, StringComparison.OrdinalIgnoreCase))
            return named;
        return toolGame || userData.Length == 0 ? null : Path.Combine(userData, Folder, FileName);
    }

    /// <summary>Whether <paramref name="args"/> (the process's command line and the launcher's own flags) say a tool started the game.</summary>
    public static bool IsToolGame(IReadOnlyList<string> args) => args.Contains(ToolGameSwitch, StringComparer.OrdinalIgnoreCase);
}

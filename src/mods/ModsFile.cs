using YamlDotNet.Serialization;

namespace SourceSharp.Mods;

// The shape of mods.yaml as written, before merging and validation. Keys are
// snake_case in the file (UnderscoredNamingConvention); a key not declared here
// is an error, not ignored, so a misspelt setting cannot silently fall back to
// a default.
//
// `defaults` has the shape of one mod's entry; each mod overrides any of it
// (ModsConfig.Merge: objects merge field by field, maps key by key, a scalar or
// a list the mod gives replaces the default's).

[YamlSerializable]
public sealed class ModsFile
{
    public string? DefaultMod { get; set; }
    public ModEntry? Defaults { get; set; }
    public Dictionary<string, ModEntry>? Mods { get; set; }
}

[YamlSerializable]
public sealed class ModEntry
{
    public string? Title { get; set; }

    public string? GameDir { get; set; }

    public string? Content { get; set; }

    public uint? SteamAppid { get; set; }

    public EngineEntry? Engine { get; set; }
    public AssembliesEntry? Assemblies { get; set; }

    public string? ManagedDir { get; set; }

    /// <summary>The hosted server-browser entry (host plan_steam_auth S-18); only a hosted mod has one.</summary>
    public BrowserEntry? Browser { get; set; }
}

/// <summary>
/// The server-browser entry a hosted mod is listed with (A2S and the Steam
/// browser). The host renders the templates over its world-status keys
/// (<see cref="ModsConfig.BrowserKeys"/>); the game hands the block to the
/// host SDK before Booting. Merged field by field, <c>rules</c> key by key.
/// </summary>
[YamlSerializable]
public sealed class BrowserEntry
{
    /// <summary>A template.</summary>
    public string? Name { get; set; }

    /// <summary>The product / game description; a template.</summary>
    public string? Description { get; set; }

    /// <summary>A template.</summary>
    public string? Tags { get; set; }

    /// <summary>Steam's region string, as given; not a template.</summary>
    public string? Region { get; set; }

    /// <summary>0: the host's capacity.</summary>
    public int? MaxPlayers { get; set; }

    /// <summary>A template; empty is the host's <c>{hub_map}</c>.</summary>
    public string? Map { get; set; }

    /// <summary>Server rules shown in the browser: key to template.</summary>
    public Dictionary<string, string>? Rules { get; set; }
}

/// <summary>The two ways a mod's engine runs, each with its own settings.</summary>
[YamlSerializable]
public sealed class EngineEntry
{
    /// <summary>The game client, which is also the listen server: what <c>source# -mod X</c> starts.</summary>
    public EngineRoleEntry? Client { get; set; }

    /// <summary>The dedicated server: <c>source# -mod X -dedicated</c>, and a hosted pod.</summary>
    public EngineRoleEntry? Dedicated { get; set; }
}

[YamlSerializable]
public sealed class EngineRoleEntry
{
    /// <summary>The Steam app whose install is the engine directory (cwd, bin/linux64).</summary>
    public uint? Appid { get; set; }

    public string? Exe { get; set; }

    /// <summary>
    /// The app whose install holds <see cref="Exe"/>, when it is not the engine
    /// directory's; its bin/linux64 joins the library path after the engine's.
    /// The dedicated server needs this: 244310 ships the 64-bit *_srv.so but
    /// no srcds_linux64, which only 243750 ships (plan_host_sdk P0(5)).
    /// </summary>
    public uint? ExeAppid { get; set; }

    public int? RestartExitCode { get; set; }

    public int? FdLimit { get; set; }

    public List<string>? Args { get; set; }

    public Dictionary<string, string>? Env { get; set; }
}

[YamlSerializable]
public sealed class AssembliesEntry
{
    public List<string>? Server { get; set; }
    public List<string>? Client { get; set; }
}

[YamlStaticContext]
public partial class ModsYamlContext : YamlDotNet.Serialization.StaticContext
{
}

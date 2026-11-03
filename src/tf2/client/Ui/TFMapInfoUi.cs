//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace TF2.Client.Ui;

/// <summary>
/// The TF2 map info menu in game (stock's <c>CTFMapInfoMenu</c>,
/// game/client/tf/vgui/tf_mapinfomenu.cpp): the map's name read off
/// <c>engine-&gt;GetLevelName()</c> the way <c>ApplySchemeSettings</c>
/// takes it (<c>Q_FileBase</c>, :99-105), the display name and game type
/// stock's <c>GetMapDisplayName</c>/<c>GetMapType</c> derive from it
/// (tf_gamerules.cpp:18974-19206 — the cleanup arms; the Valve/community
/// name tables are the maps' own content, and the fall-through
/// <c>GetGameTypeName</c> the rules' own, which this port's blank page
/// stands in for), the briefing stock's <c>LoadMapPage</c> looks up —
/// through the mod's own token file (<see cref="IUiLocalisation"/>: the
/// engine's tables hold none of the mod's, as the live game showed) —
/// the <c>#&lt;map&gt;_description</c> localisation arm (:379-387; the
/// maps/&lt;map&gt;_&lt;lang&gt;.txt files and the per-mode defaults are
/// stock content, ours is the one default) — and the photo material
/// (<c>VGUI/maps/menu_photos_&lt;map&gt;</c> lower-cased, :349-368). The
/// reads are cached per level, the map name only changing with it.
///
/// <para>
/// The clicks are stock's <c>OnCommand</c> (:257-327): CONTINUE hides
/// the menu and — with no team yet — runs the team setup
/// (<c>team_ui_setup</c>, :288-298, named here as the panel's show, the
/// text window's OK way); the intro-movie arm is the deferred video
/// system's. BACK raises the text window again (<c>PANEL_INFO</c>,
/// :261-269), reachable only without a team, which the screen's gate
/// shares.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFMapInfoSource), typeof(ITFMapInfoActions)])]
public sealed class TFMapInfoUi(IClientStandalones standalones, IEngineHud screen, IFwdEngineClient engine,
                                IUiLocalisation localiser, ITFMenuNavigation nav) : ITFMapInfoSource, ITFMapInfoActions
{
    private const int Slots = PlayerResourceNetState.Slots;

    private ClientPlayerResource? _resource;

    private string? _level;
    private string _mapName = "";
    private string _gameMode = "";
    private string _briefing = "";
    private string _photo = "";

    public string MapName
    {
        get
        {
            Refresh();
            return _mapName;
        }
    }

    public string GameMode
    {
        get
        {
            Refresh();
            return _gameMode;
        }
    }

    public string Briefing
    {
        get
        {
            Refresh();
            return _briefing;
        }
    }

    public string? Photo
    {
        get
        {
            Refresh();
            return _photo.Length > 0 ? _photo : null;
        }
    }

    public int Team
    {
        get
        {
            int local = screen.LocalPlayerIndex;
            return Resource is { } r && local is > 0 and < Slots ? r.State.Team[local] : 0;
        }
    }

    public void Continue()
    {
        nav.Close("tf2mapinfo");
        // Stock's no-team arm (:288-298): the team setup the text
        // window's OK used to go straight to — the panel named, as
        // stock's viewport shows it. On a team (the lobby case) stock
        // sends nothing, and neither does this.
        if (Team <= 1)
            nav.OpenPanel("team");
    }

    public void Back()
    {
        nav.Close("tf2mapinfo");
        nav.OpenPanel("info");
    }

    /// <summary>The per-level reads, taken once per level name.</summary>
    private void Refresh()
    {
        string level = engine.LevelName() ?? string.Empty;
        if (_level == level)
            return;
        _level = level;

        // Stock's Q_FileBase of GetLevelName (:99-105): "maps/<map>.bsp"
        // names the map.
        string map = FileNameOf(level);
        _mapName = DisplayName(map);
        _gameMode = GameType(map) is { Length: > 0 } type ? localiser.Find(type) ?? "" : "";
        _briefing = BriefingOf(map);
        _photo = map.Length > 0 ? "vgui/maps/menu_photos_" + map.ToLowerInvariant() : "";
    }

    private string BriefingOf(string map)
    {
        // Stock's localisation arm first (#<map>_description, :379-387).
        if (map.Length > 0 && localiser.Find("#" + map.ToLowerInvariant() + "_description") is { } found)
            return found;
        // The .txt files and the per-mode defaults (:390-561) are stock
        // content; ours is the one default.
        return localiser.Find("#TF2_MapInfo_Default") ?? "";
    }

    /// <summary>Stock's <c>GetMapDisplayName</c> (tf_gamerules.cpp:18974):
    /// the <c>_final</c> suffix and the mode prefix stripped, underscores
    /// for spaces, and TF2's LOUD name. The Valve/community name tables
    /// (:18990-19005) are the maps' own content and are not carried; the
    /// cleanup path names every map.</summary>
    internal static string DisplayName(string map)
    {
        string name = map;
        int final = name.IndexOf("_final", StringComparison.Ordinal);
        if (final >= 0 && (name.Length == final + 6 || (name.Length == final + 7 && name[final + 6] == '1')))
            name = name[..final];
        if (PrefixOf(name) is { } prefix)
            name = name[prefix.Length..];
        return name.Replace('_', ' ').ToUpperInvariant();
    }

    /// <summary>Stock's <c>GetMapType</c> (tf_gamerules.cpp:19111): the
    /// mode prefix names the token the menu localises. The name tables
    /// (:19117-19133) are the maps' own content, and the fall-through
    /// <c>GetGameTypeName</c> (:19198-19201) the rules' own — the empty
    /// string here is stock's no-rules arm (:19205).</summary>
    internal static string GameType(string map) => map switch
    {
        _ when Has(map, "cp_") => "#TF2_Gametype_Cp",        // Gametype_CP (:19138)
        _ when Has(map, "tc_") => "#TF2_Gametype_Tc",        // TF_TerritoryControl (:19142)
        _ when Has(map, "pl_") => "#TF2_Gametype_Pl",        // Gametype_Escort (:19146)
        _ when Has(map, "plr_") => "#TF2_Gametype_Plr",      // Gametype_EscortRace (:19150)
        _ when Has(map, "ad_") => "#TF2_Gametype_Ad",        // TF_AttackDefend (:19154)
        _ when Has(map, "ctf_") => "#TF2_Gametype_Ctf",      // Gametype_CTF (:19158)
        _ when Has(map, "koth_") => "#TF2_Gametype_Koth",    // Gametype_Koth (:19162)
        _ when Has(map, "arena_") => "#TF2_Gametype_Arena",  // Gametype_Arena (:19166)
        _ when Has(map, "sd_") => "#TF2_Gametype_Sd",        // Gametype_SD (:19170)
        _ when Has(map, "mvm_") => "#TF2_Gametype_Mvm",      // Gametype_MVM (:19180)
        _ when Has(map, "pass_") => "#TF2_Gametype_Passtime", // GameType_Passtime (:19184)
        _ when Has(map, "rd_") => "#TF2_Gametype_Rd",        // Gametype_RobotDestruction (:19188)
        _ when Has(map, "pd_") => "#TF2_Gametype_Pd",        // Gametype_PlayerDestruction (:19192)
        _ => "",
    };

    /// <summary>The prefixes both ports strip, stock's own spellings
    /// (tf_gamerules.cpp:19048-19081), longest first so plr_ and pass_
    /// win over their prefixes.</summary>
    private static string? PrefixOf(string map) => map switch
    {
        _ when Has(map, "koth_") => "koth_",
        _ when Has(map, "arena_") => "arena_",
        _ when Has(map, "pass_") => "pass_",
        _ when Has(map, "plr_") => "plr_",
        _ when Has(map, "ctf_") => "ctf_",
        _ when Has(map, "mvm_") => "mvm_",
        _ when Has(map, "cp_") => "cp_",
        _ when Has(map, "tc_") => "tc_",
        _ when Has(map, "pl_") => "pl_",
        _ when Has(map, "ad_") => "ad_",
        _ when Has(map, "sd_") => "sd_",
        _ when Has(map, "rd_") => "rd_",
        _ when Has(map, "pd_") => "pd_",
        _ => null,
    };

    private static bool Has(string map, string prefix) => map.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    // "maps/<map>.bsp" names the map: the file base without either part.
    private static string FileNameOf(string level)
    {
        string name = level;
        int slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    /// <summary>The player resource's rows, re-found if it has gone (the
    /// team menu's way): the local team reads live.</summary>
    private ClientPlayerResource? Resource
    {
        get
        {
            if (_resource is null || !standalones.Live().Contains(_resource))
                _resource = standalones.OfClass(EntityNames.PlayerManager)
                                    .OfType<ClientPlayerResource>().FirstOrDefault();
            return _resource;
        }
    }
}

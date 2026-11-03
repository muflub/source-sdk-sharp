//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Voice;

/// <summary>An item of a voice menu: <c>shared/multiplay_gamerules.h</c>'s
/// <c>VoiceCommandMenuItem_t</c>, the server's half of it — the concept the
/// item speaks, whether the subtitle shows and whether that subtitle is
/// distance-cut, and the gesture activity's name. The client's subtitle and
/// menu-label strings are its half, read by the client's menu.</summary>
public sealed record TFVoiceCommandItem
{
    /// <summary>The concept the MP-concept table names, spoken through the response rules.</summary>
    public string Concept { get; init; } = string.Empty;

    /// <summary><c>m_bShowSubtitle</c>.</summary>
    public bool ShowSubtitle { get; init; }

    /// <summary><c>m_bDistanceBasedSubtitle</c>.</summary>
    public bool DistanceBasedSubtitle { get; init; }

    /// <summary><c>m_szGestureActivity</c>: the activity the gesture animates with.</summary>
    public string GestureActivity { get; init; } = string.Empty;
}

/// <summary>
/// The voice menus, <c>CMultiplayRules::LoadVoiceCommandScript</c>
/// (shared/multiplay_gamerules.cpp:1594-1642): every child of
/// <c>scripts/voicecommands.txt</c>'s root is a menu, in file order, and every
/// child of a menu an item, also in file order — past nine the rest are
/// warned away and skipped, stock's warning word for word. A menu the script
/// does not have, or a menu shorter than the item asked, is the empty
/// answer stock's bounds check returns through.
///
/// <para>
/// The client's half of the same loop — <c>m_szSubtitle</c> and
/// <c>m_szMenuLabel</c> — is not read here; the client's menu reads its own
/// copy. A concept the MP-concept table does not name is stock's warning and
/// an index of none; this port keeps concepts as the names the response
/// rules match, so every name is known and no table stands between.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFVoiceCommandMenus
{
    /// <summary>The script, as <c>LoadFromFile</c> names it.</summary>
    public const string VoiceCommandsFile = "scripts/voicecommands.txt";

    /// <summary>Stock's item cap, extras warned and skipped (:1611-1615).</summary>
    public const int MaxItemsPerMenu = 9;

    private readonly IEngineFiles _files;

    private readonly ILog _log;

    private bool _loaded;

    private TFVoiceCommandItem[][] _menus = [];

    public TFVoiceCommandMenus(IEngineFiles files, ILog log)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>The menus, each its items in file order; empty before a load and for a missing script.</summary>
    public IReadOnlyList<IReadOnlyList<TFVoiceCommandItem>> Menus
    {
        get
        {
            Load();
            return _menus;
        }
    }

    /// <summary>The item at <paramref name="menu"/>/<paramref name="item"/>, or null past the script's bounds.</summary>
    public TFVoiceCommandItem? Item(int menu, int item)
    {
        var menus = Menus;
        if (menu < 0 || menu >= menus.Count || item < 0 || item >= menus[menu].Count)
            return null;

        return menus[menu][item];
    }

    private void Load()
    {
        if (_loaded)
            return;

        _loaded = true;

        string? text = _files.ReadGameFile(VoiceCommandsFile);
        if (text is null)
            return;

        // the script's single root key; the parse's synthetic root holds it
        var root = KeyValuesText.Parse(text).Find("voicemenus");
        if (root is null)
            return;

        var menus = new List<TFVoiceCommandItem[]>();

        foreach (var menu in root.Children)
        {
            var items = new List<TFVoiceCommandItem>();

            // The count includes the extras the cap throws away, as stock's
            // iNumItems does before its continue: each one warns again.
            foreach (var entry in menu.Children)
            {
                if (items.Count >= MaxItemsPerMenu)
                {
                    _log.Warning($"[TF2/Voice] Trying to load more than {MaxItemsPerMenu} menu items in voicecommands.txt, extras ignored\n");
                    continue;
                }

                items.Add(new TFVoiceCommandItem
                {
                    Concept = entry.GetString("concept", string.Empty) ?? string.Empty,
                    ShowSubtitle = entry.GetInt("show_subtitle", 0) > 0,
                    DistanceBasedSubtitle = entry.GetInt("distance_check_subtitle", 0) > 0,
                    GestureActivity = entry.GetString("activity", string.Empty) ?? string.Empty,
                });
            }

            menus.Add([.. items]);
        }

        _menus = [.. menus];
    }
}

using System.Text.RegularExpressions;

using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The mod's gameinfo.txt, as the engine's GameUI reads it. GameUI paints
/// gameinfo's <c>title</c> and <c>title2</c> as its own menu title, over the
/// main menu, and keeps painting them after the client hands it a main-menu
/// override: stock TF2's gameinfo names no title (tf/gameinfo.txt: "game"
/// alone), so stock's menu shows the client's panel and nothing of GameUI's.
/// Live, our "TF2" title came up in the corner as soon as a GameUI dialog
/// opened over the menu.
/// </summary>
public sealed class TFGameInfoTests
{
    private static IReadOnlyList<string> TopLevelKeys()
    {
        // The keys of the GameInfo block itself, before its FileSystem block.
        string text = File.ReadAllText(RepoLayout.Repo("game", "mod_tf2", "gameinfo.txt"));
        int nested = text.IndexOf("FileSystem", StringComparison.Ordinal);
        Assert.True(nested > 0, "gameinfo.txt has no FileSystem block");
        return Regex.Matches(text[..nested], @"^\s*""?(\w+)""?\s", RegexOptions.Multiline)
                    .Select(m => m.Groups[1].Value)
                    .ToList();
    }

    /// <summary>
    /// The HL2 packs the mod mounts from the engine's install: each
    /// uncommented search path that names <c>|all_source_engine_paths|hl2/X.vpk</c>.
    /// </summary>
    private static IReadOnlyList<string> Hl2PacksMounted()
    {
        string text = File.ReadAllText(RepoLayout.Repo("game", "mod_tf2", "gameinfo.txt"));
        return Regex.Matches(text, @"^\s*[\w+]+\s+\|all_source_engine_paths\|hl2/(\w+)\.vpk\s*$", RegexOptions.Multiline)
                    .Select(m => m.Groups[1].Value)
                    .ToList();
    }

    /// <summary>
    /// Stock TF2's gameinfo mounts four HL2 packs (tf/gameinfo.txt: textures,
    /// sound_vo_english, sound_misc, misc). Without hl2_sound_misc every
    /// surface impact sound failed to load live ("Cannot figure out which
    /// search path sound/physics/concrete/concrete_impact_bullet1.wav"): a
    /// bullet or a melee swing on a wall made no noise.
    /// </summary>
    [Fact]
    public void TheModMountsTheHl2PacksStockTF2Mounts()
    {
        var packs = Hl2PacksMounted();
        Assert.Equal(["hl2_textures", "hl2_sound_vo_english", "hl2_sound_misc", "hl2_misc"], packs);
    }

    [Fact]
    public void TheGameInfoNamesNoTitleForGameUIToPaint()
    {
        var keys = TopLevelKeys();
        Assert.Contains("game", keys);
        Assert.DoesNotContain("title", keys);
        Assert.DoesNotContain("title2", keys);
    }
}

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

    [Fact]
    public void TheGameInfoNamesNoTitleForGameUIToPaint()
    {
        var keys = TopLevelKeys();
        Assert.Contains("game", keys);
        Assert.DoesNotContain("title", keys);
        Assert.DoesNotContain("title2", keys);
    }
}

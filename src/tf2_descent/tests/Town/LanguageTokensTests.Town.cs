using TF2.Descent.Server.Town;

using Xunit;

namespace TF2.Descent.Tests;

public sealed partial class LanguageTokensTests
{
    /// <summary>Every status line the town tells (each verb's success, each worded refusal, the fallback) and every stall's title and stance.</summary>
    [Fact]
    public void Every_town_status_line_and_stall_title_is_in_the_language_file()
    {
        var tokens = Tokens();
        var wanted = TownStatusTokens.Verbs.Select(TownStatusTokens.Done)
            .Concat(TownStatusTokens.Worded.Select(TownService.TokenOf))
            .Append(TownStatusTokens.Refused)
            .Concat(TownStalls.All.Select(s => s.NameToken))
            .Concat(TownStalls.All.Select(s => s.StanceToken).Where(t => t.Length > 0))
            .ToList();

        Assert.True(wanted.Count >= 50, $"only {wanted.Count} tokens asked");
        string missing = string.Join(", ", wanted.Distinct().Where(t => !tokens.ContainsKey(t)));
        Assert.True(missing.Length == 0, "missing from the language file: " + missing);
    }

    [Fact]
    public void A_refusal_the_language_file_does_not_word_is_told_by_the_fallback()
    {
        Assert.Equal(TownStatusTokens.Refused, TownService.TokenOf("host:Conflict:busy"));
        Assert.Equal("#Descent_Town_backpack_full", TownService.TokenOf("backpack_full"));
    }
}

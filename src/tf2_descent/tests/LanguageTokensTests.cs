extern alias client;
using System.Text;
using System.Text.RegularExpressions;
using TF2.Descent.Server;
using Xunit;
using LevelNames = client::TF2.Descent.Client.LevelNames;

namespace TF2.Descent.Tests;

/// <summary>
/// Every #Descent_* token the server sends in a notice, and every one the
/// client's arrival line reads, is in the mod's
/// language file (game/mod_tf2_descent/resource/tf2_descent_english.txt), read
/// as the engine reads it: UTF-16 little-endian with its byte-order mark. A
/// token the file lacks reaches the HUD as the token itself.
/// </summary>
public sealed partial class LanguageTokensTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SourceSharp.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("SourceSharp.slnx not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex("^\"([^\"]+)\"\\s+\"(.*)\"$")]
    private static partial Regex Row();

    /// <summary>The file's tokens and their texts, keyed with the leading # a notice sends.</summary>
    private static Dictionary<string, string> Tokens()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "game", "mod_tf2_descent", "resource", "tf2_descent_english.txt"));
        Assert.True(bytes is [0xFF, 0xFE, ..], "the language file starts with the UTF-16 LE byte-order mark");

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2).Split('\n'))
        {
            if (Row().Match(line.Trim()) is { Success: true } m)
                tokens["#" + m.Groups[1].Value] = m.Groups[2].Value;
        }

        Assert.True(tokens.Count > 100, $"only {tokens.Count} tokens read");   // the reading worked
        return tokens;
    }

    /// <summary>Every token the travel notices send.</summary>
    private static IEnumerable<string> TravelTokens() =>
        Enum.GetValues<WayDown>().Select(WaysDown.Token)
            .Concat([TravelService.DepthClosedToken, TravelService.WaitingToken, TravelService.RefusedToken,
                     TravelService.UpToken, TravelService.CorpseToken]);

    [Fact]
    public void Every_travel_notice_token_is_in_the_language_file()
    {
        var tokens = Tokens();

        string missing = string.Join(", ", TravelTokens().Where(t => !tokens.ContainsKey(t)));
        Assert.True(missing.Length == 0, "missing from the language file: " + missing);
    }

    [Fact]
    public void Every_use_refusal_token_is_in_the_language_file_and_names_the_item()
    {
        var tokens = Tokens();

        foreach (string token in TF2.Descent.Server.Hero.UseRefusals.Tokens)
            Assert.Contains("%s1", tokens.GetValueOrDefault(token) ?? "(missing: " + token + ")");
    }

    [Fact]
    public void The_lost_and_found_notices_are_in_the_language_file_and_the_fee_one_says_the_fee()
    {
        var tokens = Tokens();

        Assert.Contains("%s1", tokens.GetValueOrDefault(TF2.Descent.Server.Hero.HeroCorpses.LostToken) ?? "(missing)");
        Assert.True(tokens.ContainsKey(TF2.Descent.Server.Hero.HeroCorpses.LostTokenNoFee), "missing: " + TF2.Descent.Server.Hero.HeroCorpses.LostTokenNoFee);
    }

    [Fact]
    public void The_waiting_refused_and_closed_notices_each_say_their_argument()
    {
        var tokens = Tokens();

        foreach (string token in new[] { TravelService.WaitingToken, TravelService.RefusedToken, TravelService.DepthClosedToken })
            Assert.Contains("%s1", tokens[token]);
    }

    [Fact]
    public void Every_level_and_act_name_and_the_arrival_line_are_in_the_language_file()
    {
        var tokens = Tokens();
        var wanted = Enumerable.Range(1, LevelNames.Levels)
            .SelectMany(d => new[] { LevelNames.LevelToken(d)!, LevelNames.ActToken(d)! })
            .Append(LevelNames.ArrivalDepthToken)
            .Distinct()
            .ToList();
        Assert.Equal(LevelNames.Levels + 3 + 1, wanted.Count);   // fifteen levels, three acts, the line

        string missing = string.Join(", ", wanted.Where(t => !tokens.ContainsKey(t)));
        Assert.True(missing.Length == 0, "missing from the language file: " + missing);
    }

    [Fact]
    public void Every_level_has_a_name_of_its_own()
    {
        var tokens = Tokens();
        var names = Enumerable.Range(1, LevelNames.Levels).Select(d => tokens.GetValueOrDefault(LevelNames.LevelToken(d)!)).ToList();

        Assert.DoesNotContain(null, names);
        Assert.Equal(LevelNames.Levels, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_way_down_reads_differently()
    {
        var tokens = Tokens();
        var texts = Enum.GetValues<WayDown>().Select(w => tokens[WaysDown.Token(w)]).ToList();

        Assert.Equal(texts.Count, texts.Distinct(StringComparer.Ordinal).Count());
    }
}

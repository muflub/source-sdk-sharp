using System.Reflection;

using TF2.Descent.Server.Party;

using Xunit;

namespace TF2.Descent.Tests;

public sealed partial class LanguageTokensTests
{
    /// <summary>Every refusal code of the parties (each a const of PartyRefusals) as its notice token.</summary>
    private static List<string> PartyRefusalTokens() =>
        [.. typeof(PartyRefusals).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => PartyVerbs.RefusalToken((string)f.GetRawConstantValue()!))];

    /// <summary>Every notice the party sends (WO9): its changes, each verb's refusal, the leader's pick of the way down.</summary>
    [Fact]
    public void Every_party_notice_and_refusal_is_in_the_language_file()
    {
        var tokens = Tokens();
        var wanted = PartyNotices.Tokens().Concat(PartyRefusalTokens()).Append(PartyTravel.LeaderPicksToken).ToList();

        Assert.True(wanted.Count >= 25, $"only {wanted.Count} tokens asked");
        string missing = string.Join(", ", wanted.Where(t => !tokens.ContainsKey(t)));
        Assert.True(missing.Length == 0, "missing from the language file: " + missing);
    }

    /// <summary>A notice that names someone says the name.</summary>
    [Fact]
    public void The_party_notices_that_name_a_hero_say_the_name()
    {
        var tokens = Tokens();

        foreach (string token in new[] { PartyNotices.JoinedToken, PartyNotices.LeftToken, PartyNotices.KickedToken, PartyNotices.NewLeaderToken,
                                          PartyNotices.InvitedToken, PartyNotices.InviteSentToken, PartyNotices.DeclinedToken })
            Assert.Contains("%s1", tokens[token]);
    }
}

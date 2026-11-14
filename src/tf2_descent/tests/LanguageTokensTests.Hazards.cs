using TF2.Descent.Rules.World;

using Xunit;

namespace TF2.Descent.Tests;

public sealed partial class LanguageTokensTests
{
    /// <summary>The kill feed names a hazard by its kind's token (HE10): every kind has one, its name.</summary>
    [Fact]
    public void Every_hazard_kind_has_its_kill_feed_name_in_the_language_file()
    {
        var tokens = Tokens();
        var kinds = new TF2.Descent.Rules.TF2DescentRules().Content.Hazards.Kinds;
        Assert.NotEmpty(kinds);

        var missing = kinds.Where(k => !tokens.TryGetValue(HazardRules.Token(k.Id), out var text) || text != k.Name)
            .Select(k => k.Id).ToList();
        Assert.Empty(missing);
    }
}

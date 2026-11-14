using TF2.Descent.Server.Hero;

using Xunit;

namespace TF2.Descent.Tests;

public sealed partial class LanguageTokensTests
{
    /// <summary>The kill feed names a robot by its template's token (WO6): every template has one, its name.</summary>
    [Fact]
    public void Every_robot_template_has_its_kill_feed_name_in_the_language_file()
    {
        var tokens = Tokens();
        var robots = new TF2.Descent.Rules.TF2DescentRules().Content.Robots.Rows;
        Assert.NotEmpty(robots);

        var missing = robots.Where(r => !tokens.TryGetValue(RobotKillerNames.Prefix + r.Id, out var text) || text != r.Name)
            .Select(r => r.Id).ToList();
        Assert.Empty(missing);
    }
}

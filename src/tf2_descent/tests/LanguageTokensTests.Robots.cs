using TF2.Descent.Server.Horde;
using Xunit;

namespace TF2.Descent.Tests;

public sealed partial class LanguageTokensTests
{
    [Fact]
    public void The_immune_notice_is_in_the_language_file_and_says_the_type() =>
        Assert.Contains("%s1", Tokens()[RobotImmuneNotices.ImmuneToken]);
}

using TF2.Descent.Rules;
using TF2.Descent.Server.Furniture;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests;

public sealed partial class LanguageTokensTests
{
    /// <summary>Every name the furniture shows (the catalog's), every shrine message and every notice the furniture sends.</summary>
    [Fact]
    public void Every_furniture_name_message_and_notice_is_in_the_language_file()
    {
        var tokens = Tokens();
        var wanted = FurnitureCatalog.All().Select(e => e.NameToken)
            .Concat(new TF2DescentRules().Content.Furniture.Shrines.Select(s => s.MessageToken))
            .Concat([FurnitureService.ShrineSpentToken, FurnitureService.LockedToken, FurnitureService.OpenedToken, FurnitureService.KeyFoundToken])
            .ToList();

        Assert.True(wanted.Count >= 28, $"only {wanted.Count} tokens asked");
        string missing = string.Join(", ", wanted.Where(t => !tokens.ContainsKey(t)));
        Assert.True(missing.Length == 0, "missing from the language file: " + missing);
    }
}

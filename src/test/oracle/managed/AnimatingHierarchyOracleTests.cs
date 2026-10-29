using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// A managed entity class is an <see cref="AnimatingEntity"/> exactly when
/// stock's class for its classname derives from CBaseAnimating, so every model
/// entity answers CBaseAnimating's keys and inputs, and nothing else does.
/// Found 2026-10-28: ours had no such base, and the 43 classnames stock
/// derives from CBaseAnimating answered none of skin, SetBodyGroup,
/// SetModelScale, SetModel or the ignites.
/// </summary>
public class AnimatingHierarchyOracleTests
{
    private const string StockBase = "CBaseAnimating";

    [Fact]
    public void A_class_is_animating_exactly_when_its_stock_class_is()
    {
        var stock = StockContract.Shared.Value;
        var wrong = new List<string>();
        int animating = 0;

        foreach (Type type in typeof(ScriptedEntity).Assembly.GetTypes())
        {
            foreach (var attr in type.GetCustomAttributes<ScriptedEntityAttribute>())
            {
                var s = stock.Resolve(attr.ClassName);
                if (s is null)
                    continue;

                bool stockAnimates = s.Chain.Contains(StockBase);
                bool oursAnimates = typeof(AnimatingEntity).IsAssignableFrom(type);
                if (stockAnimates)
                    animating++;

                if (stockAnimates != oursAnimates)
                    wrong.Add($"{attr.ClassName} ({type.Name}): stock {(stockAnimates ? "is" : "is not")} a {StockBase} " +
                              $"({string.Join(" <- ", s.Chain)}), ours {(oursAnimates ? "is" : "is not")} an AnimatingEntity");
            }
        }

        Assert.True(animating > 40, $"only {animating} stock classname(s) derive from {StockBase}");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>
    /// The define reader the macro base is resolved through: an <c>#if
    /// defined</c> arm the project does not define is skipped for its
    /// <c>#else</c>, as basecombatweapon_shared.h's is.
    /// </summary>
    [Fact]
    public void A_macro_base_takes_the_arm_the_project_defines()
    {
        const string code = "#if defined USES_ECON_ITEMS\n#define BASE_FROM CEconEntity\n#else\n#define BASE_FROM CBaseAnimating\n#endif\n";

        Assert.Equal(new[] { ("BASE_FROM", "CBaseAnimating") }, StockContract.LiveDefines(code, new HashSet<string> { "HL2MP" }));
        Assert.Equal(new[] { ("BASE_FROM", "CEconEntity") }, StockContract.LiveDefines(code, new HashSet<string> { "USES_ECON_ITEMS" }));
    }

    [Fact]
    public void HL2MPs_project_defines_HL2MP_and_not_the_econ_items()
    {
        var defined = StockContract.ProjectDefines(Path.Combine(ValveSdk.Root, "src", "game"));

        Assert.Contains("HL2MP", defined);
        Assert.Contains("GAME_DLL", defined);
        Assert.DoesNotContain("USES_ECON_ITEMS", defined);
    }
}

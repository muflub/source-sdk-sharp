using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// <see cref="HitGroup"/> held against stock's <c>HITGROUP_*</c> defines in
/// the pristine <c>shareddefs.h</c> (tf2 stage 0.8).
///
/// <para>
/// The trace hands over the engine's raw <c>trace_t.hitgroup</c> and the
/// managed side casts it, so the enum's values ARE the contract: a member
/// renumbered here would read a headshot as a chest hit with nothing failing
/// to compile. Both directions are checked — every stock define has a member
/// with its value, and every member is a stock define — so a group added on
/// either side alone fails.
/// </para>
/// </summary>
public class HitGroupOracleTests
{
    private const string SharedDefs = "src/game/shared/shareddefs.h";

    /// <summary>Stock's names, spelled as the enum spells them.</summary>
    private static string Member(string define) => define switch
    {
        "GENERIC"  => nameof(HitGroup.Generic),
        "HEAD"     => nameof(HitGroup.Head),
        "CHEST"    => nameof(HitGroup.Chest),
        "STOMACH"  => nameof(HitGroup.Stomach),
        "LEFTARM"  => nameof(HitGroup.LeftArm),
        "RIGHTARM" => nameof(HitGroup.RightArm),
        "LEFTLEG"  => nameof(HitGroup.LeftLeg),
        "RIGHTLEG" => nameof(HitGroup.RightLeg),
        "GEAR"     => nameof(HitGroup.Gear),
        _          => "HITGROUP_" + define,
    };

    private static Dictionary<string, int> StockDefines()
    {
        var text = ValveSdk.Read(SharedDefs);
        var defines = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(text, @"^#define\s+HITGROUP_(?<name>\w+)\s+(?<value>\d+)", RegexOptions.Multiline))
            defines[Member(m.Groups["name"].Value)] = int.Parse(m.Groups["value"].Value);

        return defines;
    }

    [Fact]
    public void EveryStockHitgroupIsAMemberWithItsValue()
    {
        var stock = StockDefines();

        // The control: the read found the block (GENERIC through GEAR is nine).
        Assert.True(stock.Count == 9, $"shareddefs.h defines nine HITGROUP_ values (read {stock.Count})");

        foreach (var (name, value) in stock)
        {
            Assert.True(Enum.TryParse<HitGroup>(name, out var member), $"HitGroup has a member for stock's {name}");
            Assert.True((int)member == value, $"HitGroup.{name} is {(int)member}, stock says {value}");
        }
    }

    [Fact]
    public void EveryMemberIsAStockHitgroup()
    {
        var stock = StockDefines();

        foreach (var member in Enum.GetValues<HitGroup>())
            Assert.True(stock.TryGetValue(member.ToString(), out int value) && value == (int)member,
                $"HitGroup.{member} = {(int)member} is a stock HITGROUP_ define");
    }
}

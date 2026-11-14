using System.Security.Cryptography;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using Xunit;

namespace TF2.Descent.Tests.Items.Ledger;

/// <summary>
/// The in-memory ledger's new hero starts with the rules' starting kit (starting_kits.json)
/// exactly as it did when the kit was the server's code: same items, same ids, same rolls.
/// </summary>
public sealed class StartingKitLedgerFacts
{
    private const string Hero = "76561190000000001";

    private sealed class NoHooks : IHooks<IItemLedgerHook>
    {
        public IReadOnlyList<IItemLedgerHook> All => [];
        public void Each(string what, Action<IItemLedgerHook> call) { }
    }

    /// <summary>
    /// SHA-256 over a new Soldier's backpack and belt (each slot, item id and the item's
    /// encoded bytes) from the server's hard-coded kit (StartingKit.cs as at 720e23724),
    /// measured at f11970a1a.
    /// </summary>
    private const string SoldierKitHash = "2b5886c868f90462347adb09b805cdf77c7f1a1b8d8f9b2e0d96ade707df7807";

    private static async Task<string> KitHash(InMemoryItemLedger ledger)
    {
        var hero = (await ledger.Load(Hero)).Hero!;
        var text = new StringBuilder();
        foreach (var (slot, held) in hero.Inventory.Backpack)
            text.Append("backpack ").Append(slot).Append(' ').Append(held.Id).Append(' ').Append(Convert.ToHexString(ItemCodec.Encode(held.Item))).Append('\n');
        foreach (var (slot, held) in hero.Inventory.Belt)
            text.Append("belt ").Append(slot).Append(' ').Append(held.Id).Append(' ').Append(Convert.ToHexString(ItemCodec.Encode(held.Item))).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    [Fact]
    public async Task A_new_soldiers_kit_is_byte_for_byte_the_kit_the_server_code_gave()
    {
        var rules = new TF2DescentRules();
        var ledger = new InMemoryItemLedger(rules, new NoHooks(), new StartingKit(rules));

        string hash = await KitHash(ledger);
        Assert.True(hash == SoldierKitHash, $"the kit's hash is {hash}");
    }

    [Theory]
    [InlineData("scout")]
    [InlineData("pyro")]
    [InlineData("heavy")]
    [InlineData("medic")]
    [InlineData("spy")]
    public async Task A_new_hero_of_any_class_starts_with_that_classs_kit_from_the_rules(string classId)
    {
        var rules = new TF2DescentRules();
        var ledger = new InMemoryItemLedger(rules, new NoHooks(), new StartingKit(rules)) { NewHeroClass = classId };

        var hero = (await ledger.Load(Hero)).Hero!;

        Assert.Equal(rules.StartingKit(classId).Backpack, hero.Inventory.Backpack.Values.Select(h => h.Item.BaseId));
        Assert.Equal(rules.StartingKit(classId).Belt, hero.Inventory.Belt.Values.Select(h => h.Item.BaseId));
        Assert.NotEmpty(hero.Inventory.Backpack);
    }
}

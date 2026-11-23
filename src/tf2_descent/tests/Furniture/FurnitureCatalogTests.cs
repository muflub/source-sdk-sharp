using TF2.Descent.Rules;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Furniture;

/// <summary>The catalog both sides read furniture by (plan_descent_logic WO3), held to the rules' table.</summary>
public sealed class FurnitureCatalogTests
{
    private static readonly TF2DescentRules Rules = new();

    private static IEnumerable<(FurnitureKindId Kind, string Variant)> RulesVariants()
    {
        var f = Rules.Content.Furniture;
        return f.Shrines.Select(s => (FurnitureKindId.Shrine, s.Id))
            .Concat(f.Containers.Kinds.Select(k => (FurnitureKindId.Container, k.Id)))
            .Append((FurnitureKindId.Dispenser, "dispenser"))
            .Concat(f.Explosives.Select(x => (FurnitureKindId.Explosive, x.Id)))
            .Concat(f.Traps.Select(t => (FurnitureKindId.Trap, t.Id)))
            .Concat(f.Breakables.Kinds.Select(k => (FurnitureKindId.Breakable, k)));
    }

    [Fact]
    public void Every_variant_the_rules_can_place_has_a_catalog_entry_and_none_more()
    {
        var rules = RulesVariants().ToHashSet();
        var catalog = FurnitureCatalog.All().Select(e => (e.Kind, e.Variant)).ToHashSet();

        Assert.Equal(FurnitureCatalog.Count, catalog.Count);
        Assert.True(rules.SetEquals(catalog), $"rules {string.Join(",", rules.Except(catalog))} / catalog {string.Join(",", catalog.Except(rules))}");
    }

    [Fact]
    public void A_shrines_name_is_the_rules_hint()
    {
        foreach (var s in Rules.Content.Furniture.Shrines)
            Assert.Equal(s.NameToken, FurnitureCatalog.Of(FurnitureCatalog.Number(FurnitureKindId.Shrine, s.Id))!.NameToken);
    }

    [Fact]
    public void The_kinds_are_the_rules_kinds_in_the_same_order()
    {
        Assert.Equal(Enum.GetNames<FurnitureKind>(), Enum.GetNames<FurnitureKindId>());
        Assert.All(Enum.GetValues<FurnitureKind>(), k => Assert.Equal((int)k, (int)Enum.Parse<FurnitureKindId>(k.ToString())));
    }

    [Fact]
    public void A_wire_number_names_its_entry_and_zero_names_none()
    {
        Assert.Null(FurnitureCatalog.Of(0));
        Assert.Null(FurnitureCatalog.Of(FurnitureCatalog.Count + 1));
        Assert.All(FurnitureCatalog.All(), e => Assert.Equal(e, FurnitureCatalog.Of(FurnitureCatalog.Number(e.Kind, e.Variant))));
    }
}

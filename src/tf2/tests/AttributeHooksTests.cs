using SourceSharp.Tests;

using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The attribute hooks (stage1 1.5): <c>ApplyAttribute</c>'s per-format rule,
/// the walk's order (own, providers, owner), its skips and its loop guard,
/// over hand-built definitions and the trimmed shipped schema.
/// </summary>
public sealed class AttributeHooksTests
{
    private const string Hook = "mult_test";
    private int _next = 1000;

    private AttributeDefinition Def(AttributeDescriptionFormat format, string hook = Hook,
                                    AttributeValueType type = AttributeValueType.Default, bool integer = false) =>
        new(_next++, "attr " + _next, hook, format, AttributeEffectType.Neutral, integer, false, type, MergedKeys.Block("x"));

    private AttributeContainer With(AttributeDescriptionFormat format, float value,
                                    AttributeProviderType type = AttributeProviderType.Generic)
    {
        var c = new AttributeContainer(null, type);
        c.Set(AttributeValue.OfFloat(Def(format), value));
        return c;
    }

    [Theory]
    [InlineData(AttributeDescriptionFormat.Additive, 10f, 3f, 13f)]
    [InlineData(AttributeDescriptionFormat.AdditivePercentage, 1f, 0.25f, 1.25f)]
    [InlineData(AttributeDescriptionFormat.ParticleIndex, 0f, 7f, 7f)]
    [InlineData(AttributeDescriptionFormat.Percentage, 10f, 1.5f, 15f)]
    [InlineData(AttributeDescriptionFormat.InvertedPercentage, 10f, 0.8f, 8f)]
    [InlineData(AttributeDescriptionFormat.Or, 5f, 2f, 7f)]
    [InlineData(AttributeDescriptionFormat.KillstreakEffectIndex, 4f, 2f, 2f)]
    [InlineData(AttributeDescriptionFormat.KillstreakIdleEffectIndex, 4f, 3f, 3f)]
    [InlineData(AttributeDescriptionFormat.FromLookupTable, 4f, 9f, 9f)]
    [InlineData(AttributeDescriptionFormat.Date, 4f, 9f, 4f)]
    [InlineData(AttributeDescriptionFormat.None, 4f, 9f, 4f)]
    public void EachFormatAppliesStocksRule(AttributeDescriptionFormat format, float value, float modifier, float expected)
    {
        var hooks = new AttributeHooks();

        Assert.Equal(expected, hooks.Float(value, Hook, With(format, modifier)));
    }

    [Fact]
    public void AnUnknownClassReturnsTheValueUnchanged()
    {
        var hooks = new AttributeHooks();
        var entity = With(AttributeDescriptionFormat.Percentage, 2f);

        Assert.Equal(5f, hooks.Float(5f, "no_such_class", entity));
        Assert.Equal(10f, hooks.Float(5f, Hook, entity));
    }

    [Fact]
    public void AClassMatchesWhateverItsCase()
    {
        Assert.Equal(10f, new AttributeHooks().Float(5f, "MULT_TEST", With(AttributeDescriptionFormat.Percentage, 2f)));
    }

    [Fact]
    public void NoEntityOrNoClassReturnsTheValue()
    {
        var hooks = new AttributeHooks();

        Assert.Equal(5f, hooks.Float(5f, Hook, null));
        Assert.Equal(5f, hooks.Float(5f, "", With(AttributeDescriptionFormat.Percentage, 2f)));
    }

    [Fact]
    public void TwoProvidersComposeInTheOrderTheyAreMet()
    {
        // (5 + 10) * 2, not 5 * 2 + 10: no add-then-multiply pass.
        var player = new AttributeContainer();
        With(AttributeDescriptionFormat.Additive, 10f).ProvideTo(player);
        With(AttributeDescriptionFormat.Percentage, 2f).ProvideTo(player);

        Assert.Equal(30f, new AttributeHooks().Float(5f, Hook, player));
    }

    [Fact]
    public void ReversedProvidersComposeTheOtherWay()
    {
        var player = new AttributeContainer();
        With(AttributeDescriptionFormat.Percentage, 2f).ProvideTo(player);
        With(AttributeDescriptionFormat.Additive, 10f).ProvideTo(player);

        Assert.Equal(20f, new AttributeHooks().Float(5f, Hook, player));
    }

    [Fact]
    public void TheEntitysOwnAttributesComeBeforeItsProvidersAndTheOwnerLast()
    {
        // own x2, provider +1, owner x3: ((1 * 2) + 1) * 3.
        var owner = With(AttributeDescriptionFormat.Percentage, 3f);
        var weapon = With(AttributeDescriptionFormat.Percentage, 2f);
        weapon.Owner = owner;
        With(AttributeDescriptionFormat.Additive, 1f).ProvideTo(weapon);

        Assert.Equal(9f, new AttributeHooks().Float(1f, Hook, weapon));
    }

    [Fact]
    public void TheInitiatorProvidingToItsOwnerIsNotAppliedTwice()
    {
        // The wearable provides to the player that owns it: asked on the wearable, the owner's walk skips it.
        var player = new AttributeContainer();
        var wearable = With(AttributeDescriptionFormat.Percentage, 2f);
        wearable.Owner = player;
        wearable.ProvideTo(player);

        Assert.Equal(2f, new AttributeHooks().Float(1f, Hook, wearable));
        Assert.Equal(2f, new AttributeHooks().Float(1f, Hook, player));
    }

    [Fact]
    public void AProvisionCycleIsWalkedOnce()
    {
        var a = With(AttributeDescriptionFormat.Percentage, 2f);
        var b = With(AttributeDescriptionFormat.Percentage, 3f);
        var asked = new AttributeContainer();
        a.ProvideTo(asked);
        b.ProvideTo(a);
        a.ProvideTo(b);

        Assert.Equal(6f, new AttributeHooks().Float(1f, Hook, asked));
    }

    [Fact]
    public void AWeaponIsNotAppliedToAnotherWeaponOfItsOwner()
    {
        var player = new AttributeContainer();
        var primary = With(AttributeDescriptionFormat.Percentage, 2f, AttributeProviderType.Weapon);
        var secondary = With(AttributeDescriptionFormat.Percentage, 5f, AttributeProviderType.Weapon);
        primary.Owner = player;
        primary.ProvideTo(player);
        secondary.ProvideTo(player);

        Assert.Equal(2f, new AttributeHooks().Float(1f, Hook, primary));
    }

    [Fact]
    public void AWearableOfTheOwnerIsAppliedToTheWeapon()
    {
        // The control for the fact above: the same shape with a generic provider.
        var player = new AttributeContainer();
        var primary = With(AttributeDescriptionFormat.Percentage, 2f, AttributeProviderType.Weapon);
        var hat = With(AttributeDescriptionFormat.Percentage, 5f);
        primary.Owner = player;
        hat.ProvideTo(player);

        Assert.Equal(10f, new AttributeHooks().Float(1f, Hook, primary));
    }

    [Fact]
    public void StoppingAProviderMovesTheLastIntoItsPlace()
    {
        // [+10, x2, +1] minus the first is [+1, x2] (FindAndFastRemove), not [x2, +1].
        var player = new AttributeContainer();
        var first = With(AttributeDescriptionFormat.Additive, 10f);
        first.ProvideTo(player);
        With(AttributeDescriptionFormat.Percentage, 2f).ProvideTo(player);
        With(AttributeDescriptionFormat.Additive, 1f).ProvideTo(player);

        first.StopProvidingTo(player);

        Assert.Equal(2, player.Providers.Count);
        Assert.Equal(12f, new AttributeHooks().Float(5f, Hook, player));
    }

    [Fact]
    public void AnOwnAttributeShadowsTheStaticOfItsDefinition()
    {
        var item = SchemaParser.Parse(
            "\"g\" { \"qualities\" { } \"items\" { \"1\" { \"name\" \"A\" \"item_class\" \"x\" \"static_attrs\" { \"mult\" \"2\" } } } " +
            "\"attributes\" { \"5\" { \"name\" \"mult\" \"attribute_class\" \"mult_test\" \"description_format\" \"value_is_percentage\" } } }").Items[1];
        var plain = new AttributeContainer(item);
        var overridden = new AttributeContainer(item);
        overridden.Set(AttributeValue.OfFloat(item.Attributes[0].Definition, 3f));

        var hooks = new AttributeHooks();
        Assert.Equal(2f, hooks.Float(1f, Hook, plain));
        Assert.Equal(3f, hooks.Float(1f, Hook, overridden));
    }

    [Fact]
    public void AnIntegerAttributesBitsAreAppliedAsAFloat()
    {
        // *reinterpret_cast<float *>( &value ): integer 1 adds the smallest denormal.
        var c = new AttributeContainer();
        c.Set(new AttributeValue(Def(AttributeDescriptionFormat.Additive, integer: true), 1u));

        Assert.Equal(BitConverter.UInt32BitsToSingle(1u), new AttributeHooks().Float(0f, Hook, c));
    }

    [Fact]
    public void AFloatTypedAttributeDoesNotReachTheFloatHook()
    {
        var c = new AttributeContainer();
        c.Set(AttributeValue.OfFloat(Def(AttributeDescriptionFormat.Percentage, type: AttributeValueType.Float), 2f));

        Assert.Equal(1f, new AttributeHooks().Float(1f, Hook, c));
    }

    [Theory]
    [InlineData(2, 1.25f, 2)]   // 2.5 rounds to even
    [InlineData(3, 1.25f, 4)]   // 3.75
    [InlineData(6, 1.25f, 8)]   // 7.5 rounds to even
    [InlineData(1, 0.5f, 0)]    // 0.5 rounds to even
    public void TheIntHookRoundsAsRoundFloatToInt(int value, float multiplier, int expected)
    {
        Assert.Equal(expected, new AttributeHooks().Int(value, Hook, With(AttributeDescriptionFormat.Percentage, multiplier)));
    }

    [Fact]
    public void RoundFloatToIntAnswersTheIndefiniteOutOfRange()
    {
        Assert.Equal(int.MinValue, AttributeHooks.RoundFloatToInt(3e9f));
        Assert.Equal(int.MinValue, AttributeHooks.RoundFloatToInt(float.NaN));
        Assert.Equal(-2, AttributeHooks.RoundFloatToInt(-2.5f));
    }

    private AttributeContainer Strings(params string[] values)
    {
        var c = new AttributeContainer();
        foreach (string v in values)
            c.Set(new AttributeValue(Def(AttributeDescriptionFormat.None, "custom_name", AttributeValueType.String), 0, v));
        return c;
    }

    [Fact]
    public void TheFirstStringAnEntityHoldsReplacesTheValue()
    {
        Assert.Equal("first", new AttributeHooks().String("stock", "custom_name", Strings("first", "second")));
    }

    [Fact]
    public void ALaterProvidersStringWins()
    {
        var player = Strings("own");
        Strings("a").ProvideTo(player);
        Strings("b").ProvideTo(player);

        Assert.Equal("b", new AttributeHooks().String("stock", "custom_name", player));
        Assert.Equal("stock", new AttributeHooks().String("stock", "other", player));
    }

    [Fact]
    public void AShippedItemsStaticAttributeHooksItsClass()
    {
        // The scattergun's static_attrs: weapon_stattrak_module_scale 0.608 (value_is_percentage).
        var scattergun = EconTestFiles.Trimmed().Items[13];
        var weapon = new AttributeContainer(scattergun, AttributeProviderType.Weapon);

        Assert.Equal(0.608f, new AttributeHooks().Float(1f, "weapon_stattrak_module_scale", weapon));
    }

    [Fact]
    public void AWalkAllocatesNothing()
    {
        var hooks = new AttributeHooks();
        var player = With(AttributeDescriptionFormat.Additive, 1f);
        var weapon = With(AttributeDescriptionFormat.Percentage, 2f, AttributeProviderType.Weapon);
        weapon.Owner = player;
        With(AttributeDescriptionFormat.Additive, 3f).ProvideTo(weapon);
        Assert.Equal(6f, hooks.Float(1f, Hook, weapon));
        _ = hooks.Int(1, Hook, weapon);

        // Nothing in a walk allocates — so a window's charge is the runtime's
        // one-off on the measuring thread: a tier-up or a call-counting stub's
        // install (SteadyAllocation's note; this fact read 4,872 bytes in one
        // loaded full run, 0 alone). Each hot method installs once, so of 20
        // windows at least one is clean, while a steady per-walk allocation is
        // in every window and the fewest bytes are still in the thousands.
        float sum = 0;
        long allocated = SteadyAllocation.LeastAllocated(() =>
        {
            sum = 0;
            for (int i = 0; i < 1000; i++)
                sum += hooks.Float(1f, Hook, weapon) + hooks.Int(1, Hook, weapon);
        }, warm: 2, samples: 20);

        Assert.Equal(12000f, sum);
        Assert.Equal(0, allocated);
    }
}

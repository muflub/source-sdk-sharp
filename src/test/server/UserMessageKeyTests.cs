using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_side_isolation §2.2: a stock message keeps its bare name; any other is
/// keyed <c>&lt;module&gt;:&lt;id&gt;</c>, the module being the assembly name
/// without its side suffix.
/// </summary>
public class UserMessageKeyTests
{
    // Undecorated on purpose: an invalid [UserMessage] in this assembly would be
    // found by every registry scan in the run. The attribute is built by hand.
    private sealed class Probe;

    [Theory]
    [InlineData("SourceSharp.Shared", "SourceSharp")]
    [InlineData("SourceSharp.Server", "SourceSharp")]
    [InlineData("SourceSharp.Client", "SourceSharp")]
    [InlineData("TF2.Descent.Shared", "TF2.Descent")]
    [InlineData("TF2.Descent.Server", "TF2.Descent")]
    public void The_module_is_the_assembly_name_without_its_side(string assembly, string module)
    {
        Assert.Equal(module, LoadedAssemblies.ModuleOf(new FakeAssembly(assembly)));
    }

    [Fact]
    public void An_assembly_without_a_side_suffix_is_its_own_module()
    {
        Assert.Equal("SourceSharp.Server.Tests", LoadedAssemblies.ModuleOf(typeof(Probe).Assembly));
    }

    [Fact]
    public void A_stock_message_keeps_its_bare_name()
    {
        Assert.Equal("Shake", UserMessageKey.Of<ShakeMessage>());
    }

    [Fact]
    public void Any_other_message_is_keyed_under_its_module()
    {
        Assert.Equal("SourceSharp:Layout", UserMessageKey.Of<LayoutCheckMessage>());
        Assert.Equal("SourceSharp.Server.Tests:probe",
                     UserMessageKey.Of(typeof(Probe), new UserMessageAttribute("probe")));
    }

    [Fact]
    public void Stock_is_refused_for_a_name_valve_code_does_not_use()
    {
        var e = Assert.Throws<InvalidOperationException>(
            () => UserMessageKey.Of(typeof(Probe), new UserMessageAttribute("Probe") { Stock = true }));
        Assert.Contains("not a name Valve's code uses", e.Message);
    }

    [Theory]
    [InlineData("a:b")]
    [InlineData("a b")]
    [InlineData("")]
    [InlineData("café")]
    public void An_id_is_printable_ascii_with_no_colon_or_space(string id)
    {
        Assert.Throws<InvalidOperationException>(
            () => UserMessageKey.Of(typeof(Probe), new UserMessageAttribute(id)));
    }

    [Fact]
    public void A_key_longer_than_a_string_table_entry_is_refused()
    {
        Assert.Throws<InvalidOperationException>(
            () => UserMessageKey.Of(typeof(Probe), new UserMessageAttribute(new string('x', 250))));
    }

    [Fact]
    public void The_stock_list_is_exactly_the_messages_declared_stock()
    {
        var declaredStock = TestServices.ServerGame.GetTypes()
            .Select(t => t.GetCustomAttribute<UserMessageAttribute>())
            .Where(a => a is { Stock: true })
            .Select(a => a!.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(36, UserMessageKey.StockNames.Count);
        Assert.True(declaredStock.SetEquals(UserMessageKey.StockNames),
            "declared stock but not listed: " + string.Join(", ", declaredStock.Except(UserMessageKey.StockNames)) +
            "; listed but not declared stock: " + string.Join(", ", UserMessageKey.StockNames.Except(declaredStock)));
    }

    [Fact]
    public void Two_declarations_under_one_key_are_refused_naming_both()
    {
        UserMessageRegistry.RefuseDuplicateKeys([(typeof(Probe), "M:a"), (typeof(ShakeMessage), "M:b")]);

        var e = Assert.Throws<InvalidOperationException>(() =>
            UserMessageRegistry.RefuseDuplicateKeys([(typeof(Probe), "M:a"), (typeof(ShakeMessage), "M:a")]));
        Assert.Contains(typeof(Probe).FullName!, e.Message);
        Assert.Contains(typeof(ShakeMessage).FullName!, e.Message);
    }

    private sealed class FakeAssembly(string name) : Assembly
    {
        public override AssemblyName GetName() => new(name);
    }
}

/// <summary>
/// A side scans its own side (plan_side_isolation P7, LoadedAssemblies.All):
/// the shared source compiles into each side, so a server anchor never keeps a
/// client assembly and the other way round. This replaces the exemption the
/// old shared-assembly warning made for another module's side assemblies.
/// </summary>
public class OtherSideSuffixTests
{
    private sealed class Named(string name) : System.Reflection.Assembly
    {
        public override System.Reflection.AssemblyName GetName() => new(name);
    }

    [Theory]
    [InlineData("SourceSharp.Server", ".Client")]
    [InlineData("TF2.Descent.Server", ".Client")]
    [InlineData("SourceSharp.Client", ".Server")]
    [InlineData("Debug.Api.Client", ".Server")]
    [InlineData("SourceSharp.Shared.Interop", null)]   // both sides load it
    [InlineData("SourceSharp.Server.Tests", null)]     // not a side assembly
    [InlineData("TF2.Descent.Rules", null)]
    public void A_side_anchor_names_the_other_side(string name, string? other)
        => Assert.Equal(other, LoadedAssemblies.OtherSideSuffix(new Named(name)));
}

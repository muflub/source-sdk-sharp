extern alias client;
using System.Reflection;
using SourceSharp.Abi;
using client::SourceSharp.Client.Standalone;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Part 4b.3's Audit 3: a two-half entity's halves must both exist and agree.
///
/// <para>
/// The failure this exists for is the Phase 8 default: a server class
/// declares [NetState], spawns, networks, reports every field through the
/// payload — and no client reader ever decodes it. Nothing logs. The schema
/// hash catches a TYPE mismatch at runtime by silently refusing the entity;
/// this catches both shapes at test time, by classname, with both assemblies
/// actually loaded — a rule over what is compiled, not over what is written
/// down.
/// </para>
/// </summary>
public class PairingAuditTests
{
    //-------------------------------------------------------------------------
    // Extraction: the two sides' declarations, as data.
    //-------------------------------------------------------------------------

    private static Dictionary<string, SideAudit.Half> ServerHalves()
    {
        var halves = new Dictionary<string, SideAudit.Half>(StringComparer.OrdinalIgnoreCase);

        foreach (Type type in typeof(SourceSharp.Server.ServerModule).Assembly.GetTypes())
        {
            var names = type.GetCustomAttributes<ScriptedEntityAttribute>().Select(a => a.ClassName).ToList();
            if (names.Count == 0)
                continue;

            var states = NetStateBinding.FieldsOn(type)
                                        .Select(f => f.FieldType.Name)
                                        .ToHashSet(StringComparer.Ordinal);

            foreach (string name in names)
                halves[name] = new SideAudit.Half(name, states);
        }

        return halves;
    }

    private static Dictionary<string, SideAudit.Half> ClientHalves()
    {
        var halves = new Dictionary<string, SideAudit.Half>(StringComparer.OrdinalIgnoreCase);

        foreach (Type type in typeof(client::SourceSharp.Client.ClientModule).Assembly.GetTypes())
        {
            var attribute = type.GetCustomAttribute<ClientStandaloneEntityAttribute>();
            if (attribute is null)
                continue;

            var states = NetStateBinding.FieldsOn(type)
                                        .Select(f => f.FieldType.Name)
                                        .ToHashSet(StringComparer.Ordinal);

            halves[attribute.ClassName] = new SideAudit.Half(attribute.ClassName, states);
        }

        return halves;
    }

    /// <summary>
    /// Server state types that deliberately have no standalone reader. This
    /// is the taxonomy's server-only bucket made explicit: adding a
    /// [NetState] nobody reads means either writing the reader or writing a
    /// line here that a reviewer can challenge. The list can only shrink or
    /// be argued with — never silently grow, because growing it is a diff.
    /// </summary>
    private static readonly HashSet<string> DeclaredServerOnlyState = new(StringComparer.Ordinal);

    //-------------------------------------------------------------------------
    // The real tree.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheCensusIsFound()
    {
        var server = ServerHalves();
        var client = ClientHalves();

        // The extraction proving it extracted: the survey counted 22 reader
        // files and 254 served classnames, so numbers far below that mean the
        // walk broke, not that the mod shrank.
        Assert.True(server.Count >= 200, $"server classnames found ({server.Count})");
        Assert.True(client.Count >= 20, $"client readers found ({client.Count})");

        Assert.True(server.ContainsKey("env_steam") && client.ContainsKey("env_steam"),
            "the census contains the two-half steam jet on both sides");
        Assert.True(client["env_steam"].StateTypes.Contains("EnvSteamNetState"),
            "the steam reader declares the steam state");
    }

    [Fact]
    public void EveryTwoHalfEntityHasBothHalvesAndTheyAgree()
    {
        var violations = SideAudit.PairingViolations(ServerHalves(), ClientHalves(), DeclaredServerOnlyState);

        Assert.True(violations.Count == 0,
            "every reader has a writer, every networked state has a reader or a declaration, and the halves declare the same types:\n"
            + string.Join("\n", violations));
    }

    //-------------------------------------------------------------------------
    // The provocations: every rule made to fail on a synthetic pair.
    //-------------------------------------------------------------------------

    private static Dictionary<string, SideAudit.Half> Map(params SideAudit.Half[] halves)
        => halves.ToDictionary(h => h.ClassName, h => h, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> NoWaivers = new(StringComparer.Ordinal);

    [Fact]
    public void AStateNobodyDecodesIsNamed()
    {
        // The Phase 8 default failure: server half lands, reader is forgotten.
        var violations = SideAudit.PairingViolations(
            Map(new SideAudit.Half("env_widget", new HashSet<string> { "WidgetNetState" })),
            Map(),
            NoWaivers);

        Assert.True(violations.Count == 1 && violations[0].Contains("env_widget"),
            "a networked state with no reader is one complaint naming the classname: " + string.Join("; ", violations));
    }

    [Fact]
    public void ADeclaredServerOnlyStateIsNot()
    {
        var violations = SideAudit.PairingViolations(
            Map(new SideAudit.Half("env_widget", new HashSet<string> { "WidgetNetState" })),
            Map(),
            new HashSet<string>(StringComparer.Ordinal) { "WidgetNetState" });

        Assert.True(violations.Count == 0,
            "declaring the state server-only is the reviewable way to silence the rule");
    }

    [Fact]
    public void AnOrphanReaderIsNamed()
    {
        var violations = SideAudit.PairingViolations(
            Map(),
            Map(new SideAudit.Half("env_widget", new HashSet<string> { "WidgetNetState" })),
            NoWaivers);

        Assert.True(violations.Count == 1 && violations[0].Contains("no server writer"),
            "a reader with no writer is one complaint: " + string.Join("; ", violations));
    }

    [Fact]
    public void AHalfPairThatDisagreesOnTheStateTypeIsNamed()
    {
        // The shape the schema hash only catches at runtime, by silently
        // refusing the entity: the two halves compiled against different
        // state types.
        var violations = SideAudit.PairingViolations(
            Map(new SideAudit.Half("env_widget", new HashSet<string> { "WidgetNetState" })),
            Map(new SideAudit.Half("env_widget", new HashSet<string> { "GadgetNetState" })),
            NoWaivers);

        Assert.True(violations.Count == 1 && violations[0].Contains("GadgetNetState"),
            "a reader declaring a state its writer does not is one complaint naming the type: " + string.Join("; ", violations));
    }

    [Fact]
    public void AReaderMayDeclareFewerStatesThanItsWriter()
    {
        // EnvSteam declares both classnames' tables on one class; each reader
        // reads its own. Subset in the reader's direction is the working
        // arrangement, not a violation.
        var violations = SideAudit.PairingViolations(
            Map(new SideAudit.Half("env_widget", new HashSet<string> { "WidgetNetState", "GadgetNetState" })),
            Map(new SideAudit.Half("env_widget", new HashSet<string> { "WidgetNetState" })),
            NoWaivers);

        Assert.True(violations.Count == 0,
            "a reader reading part of what its writer networks is the EnvSteam arrangement and passes");
    }
}

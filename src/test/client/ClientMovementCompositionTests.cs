using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Client.Ui;
using TF2.Shared.Movement;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's movement composition — <c>ClientModule</c>'s
/// <c>_services.Create&lt;MovementRulesResolver&gt;()</c> — is the first resolve
/// of a mod service, and the container's walk is TRANSITIVE: the resolver's
/// <c>ResolveAll</c> constructs the mod's <see cref="IMovementRules"/> and every
/// service its constructors name, at that moment. A contract the module
/// registers below the composition is one the walk cannot see, and the live
/// load died on exactly that (7f4b8e713:
/// <c>ServiceResolutionException: TFClientWeaponOwners cannot be constructed:
/// parameter 'standalones' ... IClientStandalones</c> — the registry is
/// registered later, for the pool and the temp-ent players).
///
/// <para>
/// Composed the way the game is, as far as a test process reaches: the real
/// container, the real mod scan over the TF2 assemblies, the real activator
/// choosing constructors, the real resolver. What is mirrored is the module's
/// own registration set, taken from the source's order — the same witness the
/// wiring-order facts read — so a registration moved below the composition
/// leaves the graph without it again.
/// </para>
/// </summary>
public sealed class ClientMovementCompositionTests
{
    private static string Source()
        => File.ReadAllText(RepoLayout.Sdk("src/sourcesharp/managed/SourceSharp.Client/ClientModule.cs"));

    private static readonly Regex Registration =
        new(@"_services\.(?:AddInstance|AddFactory|Add)<\s*([A-Za-z_][\w.]*)\s*[,>]", RegexOptions.Compiled);

    /// <summary>
    /// The graph's walk ends at the client's <see cref="TFClientWeaponOwners"/>
    /// (<see cref="TFMovementRules"/>'s owners): the arsenal is a weapon-side
    /// service the walk names, and a registration below the composition is one
    /// the walk cannot see — the optional <c>arsenal</c> baked null and the
    /// min-viewmodel offset (<c>tf_use_min_viewmodels</c>) read nothing live.
    /// Composed as the source lays it out, the weapon side precedes the walk.
    /// </summary>
    [Fact]
    public void The_weapon_services_precede_the_composition_the_graph_walks()
    {
        string source = Source();
        int open = source.IndexOf("public void Initialize(Side side)", StringComparison.Ordinal);
        Assert.True(open >= 0, "ClientModule declares Initialize(Side)");

        int create = source.IndexOf("_services.Create<MovementRulesResolver>()", open, StringComparison.Ordinal);
        Assert.True(create > 0, "the movement composition is a statement of Initialize");

        int? arsenal = Registration.Matches(source, open)
            .Where(m => m.Groups[1].Value == "ILocalArsenal")
            .Select(m => (int?)m.Index)
            .FirstOrDefault();

        Assert.True(arsenal.HasValue, "the module registers the client arsenal under ILocalArsenal");
        Assert.True(arsenal.Value < create, "the arsenal is registered above the composition, whose owners walk reads it");
    }

    [Fact]
    public void The_movement_composition_resolves_over_the_container_the_client_has_built()
    {
        string source = Source();
        int open = source.IndexOf("public void Initialize(Side side)", StringComparison.Ordinal);
        Assert.True(open >= 0, "ClientModule declares Initialize(Side)");

        int create = source.IndexOf("_services.Create<MovementRulesResolver>()", open, StringComparison.Ordinal);
        Assert.True(create > 0, "the movement composition is a statement of Initialize");

        // The module's own registrations that precede the composition, by the
        // source's order: the standalones are the one the graph below needs.
        bool standalonesIn = Registration.Matches(source, open)
            .Any(m => m.Index < create && m.Groups[1].Value == "IClientStandalones");

        var services = new ServiceContainer();
        var engine = TestServices.Fake();
        services.AddInstance<ILog>(new RecordingLog());
        foreach (Type area in engine.GetType().GetInterfaces()
                     .Where(i => i.Namespace == "SourceSharp.Abi" && i.Name.StartsWith("IEngine")))
            if (!services.IsRegistered(area))
                services.AddInstance(area, engine);

        // The net-state catalogue, as the module's constructor builds it.
        services.AddInstance(NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()));

        // The mod's scan, as the composition's is: the mod's assemblies (the
        // shared TF2 sources compile into TF2.Client, Descent-pattern), each
        // once. Their registrars are parameterless and their services build
        // lazily, so the scan itself needs nothing else.
        SideServices.Register(services, Side.Client,
            new[] { typeof(TFMovementRules).Assembly, typeof(TFMenuNavigation).Assembly }
                .Distinct()
                .SelectMany(a => a.GetTypes()));

        // The standalones, when the module has registered them by now: the
        // registry over this container, as the module's field is.
        if (standalonesIn)
            services.AddInstance<IClientStandalones>(new ClientStandaloneRegistry(services));

        // The convar publish, when the module has published by now (it precedes
        // the composition in Initialize): the graph below names the mod's
        // convars by concrete type, so those are the instances the publish
        // leaves — the spy cloak's four, through the real registry.
        int publish = source.IndexOf("_convars.PublishTo(_services)", open, StringComparison.Ordinal);
        if (publish >= 0 && publish < create)
        {
            int nextId = 0;
            engine.Returns("Console.FindConVarFlags", -1)
                  .ReturnsEachCall("Console.RegisterConVar", () => ++nextId)
                  .Returns("Console.FindConVarValue", "0");
            var convars = ConVarRegistry.For((IEngineConsole)engine);
            convars.RegisterTypes(
            [
                typeof(TF2.Shared.TFSpyCloakConsumeRateConVar),
                typeof(TF2.Shared.TFSpyCloakRegenRateConVar),
                typeof(TF2.Shared.TFSpyInvisTimeConVar),
                typeof(TF2.Shared.TFSpyInvisUnstealthTimeConVar),
            ]);
            convars.PublishTo(services);
        }

        MovementRulesResolver resolver;
        try
        {
            resolver = new MovementRulesResolver(services);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }

        // The composition resolved, and it is the mod's: TF2's constants, not
        // the core's stock table.
        Assert.False(resolver.IsStock, "the mod's rules own the client's movement");
        Assert.Equal(TFViewVectors.JumpHeight, resolver.Constants.JumpHeight);
    }
}

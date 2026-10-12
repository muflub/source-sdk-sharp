using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The declaration shape — plan_gamedll_after.md Phase A8, as the owner asked
/// for it after: a table is DECLARED by <c>[StringTable]</c> on an interface
/// and RESOLVED by dependency injection, and no mod code ever holds a
/// <c>TABLEID</c> or calls a create.
///
/// <para>
/// These drive the shipped <see cref="StringTableRegistry"/> and the shipped
/// <see cref="StringTableHandle"/> over <see cref="RecordingEngine"/>'s
/// id-addressed area, so the policy under test is the one the game runs.
/// </para>
/// </summary>
public class StringTableDeclarationTests
{
    /// <summary>
    /// A registry over the strict double, with the two declarations this
    /// assembly can see named EXPLICITLY rather than swept for.
    ///
    /// <para>
    /// <see cref="StringTableRegistry.RegisterAll"/>'s own walk spans every
    /// loaded assembly, which is right in the game and wrong in a fact: what it
    /// found would depend on which other test assembly happened to be loaded.
    /// The walk itself is covered by
    /// <see cref="TheModsOwnDeclarationIsFoundByTheRealWalk"/>.
    /// </para>
    /// </summary>
    private static (StringTableRegistry Registry, ServiceContainer Services) Registered(
        RecordingEngine engine, params Type[] declarations) => Registered(engine, new RecordingLog(), declarations);

    private static (StringTableRegistry Registry, ServiceContainer Services) Registered(
        RecordingEngine engine, ILog log, params Type[] declarations)
    {
        var registry = new StringTableRegistry((IEngineStringTables)engine, new StringTableWriter((IEngineStringTables)engine), log);
        var services = new ServiceContainer();

        registry.Register(declarations.Length > 0
                              ? declarations
                              : new[] { typeof(ManagedStringTable), typeof(ProbeStringTable) },
                          services);

        return (registry, services);
    }

    private static RecordingEngine Double(int id = 4)
    {
        var engine = TestServices.Mock();
        engine.Returns("StringTables.Create", id);
        engine.Returns("StringTables.Find", id);
        return engine;
    }

    // ---- discovery and registration ----------------------------------------

    [Fact]
    public void ADeclarationIsRegisteredUnderItsOwnInterface()
    {
        var (_, services) = Registered(Double());

        Assert.True(services.Resolve<ManagedStringTable>() is not null,
            "a consumer injects the declaration it wrote, so the container has to answer for it");
    }

    [Fact]
    public void EveryDeclarationIsFoundAndNotJustTheFirst()
    {
        var (registry, _) = Registered(Double());

        Assert.Equal(new[] { "SourceSharpManaged", "SourceSharpTestProbe" },
                     registry.Declarations.Select(d => d.Name).ToArray());
    }

    [Fact]
    public void TheModsOwnDeclarationIsFoundByTheRealWalk()
    {
        // The walk RegisterAll actually does, anchored where the module anchors
        // it. Containment rather than a count, because what else is loaded in a
        // test process is not this fact's business.
        var (registry, services) = (new StringTableRegistry((IEngineStringTables)Double(), writer: null, new RecordingLog()),
                                    new ServiceContainer());

        registry.RegisterAll(TestServices.ServerGame, services);

        Assert.Contains("SourceSharpManaged", registry.Declarations.Select(d => d.Name));
    }

    /// <summary>
    /// <b>Registration touches the engine not at all</b>, and that is the split
    /// the design turns on. It runs at module start, where a create would be
    /// outside the engine's window and refused — and it runs AGAIN after a
    /// <c>cs_sv_reload</c>, which gets no second
    /// <c>CreateNetworkStringTables</c>.
    /// </summary>
    [Fact]
    public void RegisteringMakesNoEngineCallAtAll()
    {
        var engine = Double();

        Registered(engine);

        Assert.DoesNotContain(engine.Calls,
                              c => c.Member.StartsWith("StringTables.", StringComparison.Ordinal));
    }

    [Fact]
    public void ADeclarationThatDoesNotExtendTheTableInterfaceIsRefused()
    {
        // The generator refuses this at compile time (SSTAB001), so it can only
        // arrive from an assembly built without it. Refused rather than
        // skipped: a declaration that quietly did not register is a consumer
        // that fails to resolve much later, with the reason a hundred lines
        // away.
        var thrown = Assert.Throws<InvalidOperationException>(
            () => Registered(Double(), BadDeclarations.Declaring(
                    "NotATableDeclaration", "SourceSharpTestNotATable", 8, extendsTable: false)));

        Assert.Contains(nameof(IEngineStringTable), thrown.Message);
    }

    [Fact]
    public void ATableWithNoRoomInItIsRefused()
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => Registered(Double(), BadDeclarations.Declaring(
                    "NoRoomStringTable", "SourceSharpTestNoRoom", 0, extendsTable: true)));

        Assert.Contains("no resize", thrown.Message);
    }

    [Fact]
    public void TwoDeclarationsCannotClaimOneName()
    {
        // The engine registers tables BY NAME, so two interfaces over one name
        // is each of them reading the other's strings -- and the second create
        // answering the first's id, which looks exactly like success.
        var thrown = Assert.Throws<InvalidOperationException>(
            () => Registered(Double(), typeof(ManagedStringTable), BadDeclarations.Declaring(
                    "SameNameStringTable", "SourceSharpManaged", 8, extendsTable: true)));

        // THE DUPLICATE'S OWN WORDS, not the table's name. Asserting on the
        // name passed with the duplicate check DELETED -- the second
        // declaration then reached BindingFor, which threw "has nothing
        // implementing it" for the emitted type, and that message carries the
        // name too. A check that cannot fail, found by the harness reporting
        // this case green.
        Assert.Contains("reading the other's strings", thrown.Message);
    }

    // ---- the create pass ----------------------------------------------------

    [Fact]
    public void CreateAllAsksForEveryDeclaredTableAtItsDeclaredSize()
    {
        var engine = Double();
        var (registry, _) = Registered(engine);

        registry.CreateAll();

        Assert.Equal(new object?[] { "SourceSharpManaged", 32 },
                     engine.AllOf("StringTables.Create").First().Args);
        Assert.Equal(new object?[] { "SourceSharpTestProbe", 8 },
                     engine.AllOf("StringTables.Create").Last().Args);
    }

    [Fact]
    public void ARefusedCreateIsCountedRatherThanThrown()
    {
        // Not fatal, and argued: the refusals the engine really produces here
        // are per-table -- the 32-table cap, or a name something else took --
        // and taking a live server down at map load for one mod's table is a
        // worse outcome than that table not working. What the mod is left with
        // is the absent-table path, which refuses every operation and names it.
        var engine = Double(id: -1);
        var (registry, _) = Registered(engine);

        Assert.Equal(0, registry.CreateAll());
    }

    /// <summary>
    /// <b>The object is never replaced.</b> A consumer that injected the table
    /// once keeps the same instance across every map, so there is nothing to
    /// re-bind — which is the strongest form of "prefer re-binding" rather than
    /// an exception to it. What makes it possible is that the instance holds
    /// the NAME.
    /// </summary>
    [Fact]
    public void TwoCreatePassesLeaveTheConsumerHoldingTheSameObject()
    {
        var (registry, services) = Registered(Double());

        object first = services.Resolve<ManagedStringTable>()!;

        registry.CreateAll();
        registry.CreateAll();

        Assert.Same(first, services.Resolve<ManagedStringTable>());
    }

    /// <summary>
    /// And it is ONE object, not two that agree. The registry's own listing and
    /// the container answer with the same instance, so a fact about what the
    /// registry declared is a fact about what a consumer gets — a second
    /// binding built for the container would make the two silently different
    /// objects over the same name.
    /// </summary>
    [Fact]
    public void TheContainerAnswersWithTheDeclarationsOwnTable()
    {
        var (registry, services) = Registered(Double());

        Assert.Same(registry.Declarations.First(d => d.Contract == typeof(ManagedStringTable)).Table,
                    services.Resolve<ManagedStringTable>());
    }

    // ---- the handle: a name, not an id --------------------------------------

    private static IEngineStringTable Table(RecordingEngine engine, ILog? log = null)
        => (IEngineStringTable)Registered(engine, log ?? new RecordingLog(), typeof(ManagedStringTable)).Services
                                   .Resolve<ManagedStringTable>()!;

    /// <summary>
    /// <b>The staleness answer, as a fact.</b> The table was id 4 when the
    /// handle was made and the engine says 7 by the time it is used. The add
    /// goes to 7.
    ///
    /// <para>
    /// A handle carrying its id would send 4 — and on the next map 4 is not
    /// nothing, it is whatever that map put in that position. The native half
    /// cannot catch it: <c>CShellStringTables::Resolve</c> only knows an id, so
    /// an id that resolves to the WRONG table is indistinguishable there from a
    /// good one.
    /// </para>
    /// </summary>
    [Fact]
    public void AnAddGoesToTheTableThatHasThisNameNow()
    {
        var engine = Double();
        var table = Table(engine);

        engine.Returns("StringTables.Find", 7);
        engine.Returns("StringTables.AddString", 0);

        table.AddString("abi 209");

        Assert.Equal(7, engine.AllOf("StringTables.AddString").Single().Args[0]);
    }

    [Fact]
    public void AHandleThatWorkedOnTheLastMapAsksAgainOnThisOne()
    {
        // The tempting shortcut is to resolve on first use and keep the answer,
        // which is the same design as carrying the id, just arrived at later.
        var engine = Double();
        var table = Table(engine);

        engine.Returns("StringTables.AddString", 0);
        table.AddString("abi 209");

        engine.Returns("StringTables.Find", 7);
        table.AddString("abi 209");

        Assert.Equal(new object?[] { 4, 7 },
                     engine.AllOf("StringTables.AddString").Select(c => c.Args[0]).ToArray());
    }

    [Fact]
    public void AnAbsentTableTouchesNothing()
    {
        var engine = Double(id: -1);
        var table = Table(engine);

        Assert.Equal(-1, table.AddString("abi 209"));
        Assert.Equal(-1, table.FindStringIndex("abi 209"));
        Assert.Null(table.GetString(0));

        Assert.Empty(engine.AllOf("StringTables.AddString"));
        Assert.Empty(engine.AllOf("StringTables.FindStringIndex"));
        Assert.Empty(engine.AllOf("StringTables.GetString"));
    }

    [Fact]
    public void AnAbsentTableCountsMinusOneRatherThanEmpty()
    {
        // The same distinction the native half makes: "there is no such table"
        // and "the table is empty" are different answers, and a mod handed 0
        // concludes its own table is empty.
        var engine = Double(id: -1);

        Assert.Equal(-1, Table(engine).Count);
        Assert.Empty(engine.AllOf("StringTables.GetNumStrings"));
    }

    /// <summary>
    /// <b>The client's lazy find, as a fact.</b> Nothing is cached, so a use
    /// before the table has replicated is refused and the very next use — after
    /// it arrives — succeeds. A handle that remembered the failure would answer
    /// nothing for the rest of the map.
    /// </summary>
    [Fact]
    public void ATableThatArrivesLateIsUsableTheMomentItArrives()
    {
        var engine = Double(id: -1);
        var table = Table(engine);

        Assert.Null(table.GetString(0));

        engine.Returns("StringTables.Find", 7);
        engine.Returns("StringTables.GetString", "abi 209");

        Assert.Equal("abi 209", table.GetString(0));
    }

    [Fact]
    public void AnAbsentTableSaysSoOnceAndNamesItself()
    {
        var engine = Double(id: -1);
        var log = new RecordingLog();
        var table = Table(engine, log);

        var said = Warnings(log, () =>
        {
            table.AddString("abi 209");
            table.AddString("abi 209");
            _ = table.Count;
            _ = table.GetString(0);
        });

        Assert.Single(said);
        Assert.Contains("SourceSharpManaged", said[0]);
    }

    [Fact]
    public void AskingWhetherTheTableIsThereIsSilent()
    {
        // Id answering -1 is how a caller ASKS whether the table has arrived,
        // so a console line for it is a line for code that did the right thing.
        var engine = Double(id: -1);
        var log = new RecordingLog();
        var table = Table(engine, log);

        Assert.Empty(Warnings(log, () => Assert.Equal(-1, table.Id)));
    }

    // ---- the sides ----------------------------------------------------------

    /// <summary>
    /// <b>The client cannot add</b>, and it is refused before the boundary
    /// rather than after it. A client-side add to a server table is the desync
    /// string tables exist to avoid; the client also fills neither the create
    /// nor the add engine slot, so this is the near half of a refusal that
    /// exists on both sides of the ABI.
    /// </summary>
    [Fact]
    public void TheClientCannotAddToAStringTable()
    {
        var engine = Double();
        var log = new RecordingLog();
        var registry = new StringTableRegistry((IEngineStringTables)engine, writer: null, log);
        var services = new ServiceContainer();

        registry.Register(new[] { typeof(ManagedStringTable) }, services);

        var table = services.Require<ManagedStringTable>();

        var said = Warnings(log, () => Assert.Equal(-1, table.AddString("abi 209")));

        Assert.DoesNotContain(engine.Calls, c => c.Member == "StringTables.AddString");
        Assert.Contains("CLIENT", said.Single());
    }

    [Fact]
    public void AndTheClientCanStillRead()
    {
        // Find-only, which is the owner's ruling: the server creates and
        // writes, the client reads what replicated to it.
        var engine = Double();
        engine.Returns("StringTables.GetString", "abi 209");

        var registry = new StringTableRegistry((IEngineStringTables)engine, writer: null, new RecordingLog());
        var services = new ServiceContainer();

        registry.Register(new[] { typeof(ManagedStringTable) }, services);

        Assert.Equal("abi 209", services.Require<ManagedStringTable>().GetString(0));
    }

    [Fact]
    public void AndTheServerCan()
    {
        var engine = Double();
        engine.Returns("StringTables.AddString", 0);

        Assert.Equal(0, Table(engine).AddString("abi 209"));
    }

    // ---- the shape ----------------------------------------------------------

    /// <summary>
    /// The id-addressed area is INTERNAL, which is what makes a
    /// <c>TABLEID</c> unavailable to mod code rather than merely discouraged.
    /// </summary>
    [Fact]
    public void TheIdAddressedAreaIsNotPublic()
    {
        Type? area = typeof(IEngineStringTable).Assembly.GetType("SourceSharp.Abi.IEngineStringTables");

        Assert.True(area is not null, "the id-addressed area is gone; the handle has nothing to ask");
        Assert.False(area!.IsPublic, "an id-addressed string-table interface is reachable from outside the ABI");
        Assert.Equal(6, area.GetMethods().Length);
    }

    /// <summary>
    /// Nothing a consumer can name takes or answers a table id — reflected over
    /// rather than read, because the way this goes vacuous is somebody ADDING a
    /// member beside the six rather than changing one of them.
    /// </summary>
    [Fact]
    public void NothingAConsumerCanNameCarriesATableId()
    {
        var members = typeof(IEngineStringTable).GetMethods();

        Assert.Equal(6, members.Length);
        Assert.DoesNotContain(members.SelectMany(m => m.GetParameters()),
                              p => p.Name!.Contains("table", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// And there is no create anywhere a consumer can reach, which is what
    /// makes a create outside the engine's window — or on the client —
    /// unwritable rather than merely refused.
    /// </summary>
    [Fact]
    public void ThereIsNoCreateOnAnythingAConsumerHolds()
        => Assert.DoesNotContain(typeof(IEngineStringTable).GetMethods(),
                                 m => m.Name.Contains("Create", StringComparison.Ordinal));

    /// <summary>
    /// A per-table handle is NOT an engine area, even though it is spelt
    /// <c>IEngine*</c>: there is one object per declaration, each with a
    /// different name, so there is no single binding for a container to hold.
    /// The area behind them all is the plural one, and it is bound normally.
    /// </summary>
    [Fact]
    public void ThePerTableHandleIsNotAnEngineArea()
    {
        Assert.False(EngineServices.IsArea(typeof(IEngineStringTable)));
        Assert.True(EngineServices.IsArea(
            typeof(IEngineStringTable).Assembly.GetType("SourceSharp.Abi.IEngineStringTables")!));
    }

    [Fact]
    public void AndADeclarationIsNotAnAreaEither()
        => Assert.False(EngineServices.IsArea(typeof(ManagedStringTable)));

    //-------------------------------------------------------------------------

    /// <summary>
    /// The warnings <paramref name="log"/> was handed while <paramref name="work"/>
    /// ran: the subject's own log (statics to zero), which the fact built it with.
    /// </summary>
    private static List<string> Warnings(RecordingLog log, Action work)
    {
        int before = log.Lines.Count;
        work();
        return log.Lines.Skip(before).Where(l => l.Warning).Select(l => l.Text).ToList();
    }
}

/// <summary>
/// The declarations the registry must refuse, built at runtime.
///
/// <para>
/// <b>They cannot be written in this assembly, and that is the generator
/// working.</b> Every one of them is a compile ERROR here — SSTAB001 for a
/// declaration that does not extend the interface, SSTAB002 for one with no
/// room in it — and a duplicate name would be found by the registry's own walk
/// and would fail every other fact in this file. So they are emitted into a
/// dynamic assembly, which is also exactly the case the registry's checks are
/// FOR: a declaration compiled somewhere the generator did not run.
/// </para>
/// </summary>
internal static class BadDeclarations
{
    private static readonly System.Reflection.Emit.ModuleBuilder Module =
        System.Reflection.Emit.AssemblyBuilder
            .DefineDynamicAssembly(new AssemblyName("SourceSharp.Tests.BadStringTables"),
                                   System.Reflection.Emit.AssemblyBuilderAccess.Run)
            .DefineDynamicModule("BadStringTables");

    internal static Type Declaring(string typeName, string tableName, int maxEntries, bool extendsTable)
    {
        var type = Module.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            parent: null,
            interfaces: extendsTable ? new[] { typeof(IEngineStringTable) } : Type.EmptyTypes);

        type.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(
            typeof(StringTableAttribute).GetConstructor(new[] { typeof(string), typeof(int) })!,
            new object[] { tableName, maxEntries }));

        return type.CreateType();
    }
}

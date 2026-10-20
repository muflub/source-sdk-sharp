using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The mod's own string table — plan_gamedll_after.md Phase A8 — as
/// <see cref="ServerGameDll"/> uses it.
///
/// <para>
/// <b>What is checkable from here and what is not.</b> The engine's numbering
/// lives in the engine, so these facts cannot say a string really landed at
/// index 0 of a real table; that is <c>stringtable_abi_test.cpp</c> (the shipped
/// native bodies, run against a container) and the live gate. What they can say
/// is the part that is this class's own: that the create pass runs from the one
/// slot the engine allows a create in and from nowhere else, and that what it
/// reports is what the engine answered rather than what it wrote.
/// </para>
///
/// <para>
/// The DECLARATION mechanism — discovery, registration, the handle's
/// re-find-by-name — is <see cref="StringTableDeclarationTests"/>. This file is
/// only about the one class that drives it.
/// </para>
/// </summary>
public class ModStringTableTests
{
    /// <summary>
    /// The double, with the string-table area answering like a real one, and
    /// the mod's table built through the SHIPPED registry from the SHIPPED
    /// <c>[StringTable]</c> declaration rather than stubbed.
    /// </summary>
    private static (ServerGameDll Dll, RecordingEngine Engine) Build(
        int createAnswers = 4, int addAnswers = 0, string? readBack = "abi from the engine")
    {
        var engine = TestServices.Mock();

        // The constructor reads -tickrate off the command line once, and the
        // double refuses an unstubbed ASK on purpose.

        engine.Returns("StringTables.Create", createAnswers);
        engine.Returns("StringTables.Find", createAnswers);
        engine.Returns("StringTables.AddString", addAnswers);
        engine.Returns("StringTables.GetString", readBack);
        engine.Returns("StringTables.GetNumStrings", 1);

        var (stringTables, modTable) = TestStringTables.For(engine);

        var dll = new ServerGameDll(new RecordingLog(), engine, ConVarRegistry.For(TestServices.UnattachedConsole()), engine, new RecordingCommandLine("srcds"), engine, engine, engine, engine,
                                    stringTables, modTable,
                                    // Phase A3: the lifecycle driver MapLoadPreEntity
                                    // dispatches into. An empty container here, so the
                                    // pass reaches no participant -- these facts are
                                    // about the engine steps, and a driver with
                                    // participants in it would put another object's
                                    // calls on the same recording list.
                                    new LifecycleDriver(new RecordingLog(), new ServiceContainer()),
                                    new TemplateStore(new RecordingLog()),
                                    // plan_entity.md step 2d: over an empty
                                    // registry, so every block is residue and the
                                    // lump reaches the native parse whole.
                                    TestMapSpawner.Over(engine, engine), new EntityIoRoutes());
        return (dll, engine);
    }

    private static EngineCall? First(RecordingEngine engine, string member)
    {
        foreach (var call in engine.AllOf(member))
            return call;

        return null;
    }

    [Fact]
    public void TheCreatePassRunsFromTheSlotTheEngineAllowsACreateIn()
    {
        var (dll, engine) = Build();

        dll.CreateNetworkStringTables();

        var call = First(engine, "StringTables.Create");

        Assert.True(call is not null,
            "CreateNetworkStringTables did not ask for a table at all. The engine opens the "
          + "creation window in this call and nowhere else, so a mod that asks later gets a "
          + "refusal it has no way to recover from.");
    }

    [Fact]
    public void WhatIsCreatedIsWhatTheDeclarationSays()
    {
        // The name and the size come off [StringTable] on ManagedStringTable
        // and are read from the declaration here rather than written twice: a
        // table created under one spelling and looked up under another answers
        // -1 forever with nothing to say why.
        var (dll, engine) = Build();

        dll.CreateNetworkStringTables();

        var declared = typeof(ManagedStringTable)
            .GetCustomAttributes(typeof(StringTableAttribute), false)
            .Cast<StringTableAttribute>()
            .Single();

        Assert.Equal(new object?[] { declared.Name, declared.MaxEntries },
                     First(engine, "StringTables.Create")!.Value.Args);
    }

    [Fact]
    public void NothingElseOnThisClassEverAsksForACreate()
    {
        var (dll, engine) = Build();

        // Every other lifecycle method this class has, driven. A create from
        // any of them reaches the engine outside its window and is refused —
        // and the symptom is a table that is simply absent, on a path nobody
        // was watching.
        dll.SystemStart();
        dll.GameStart();
        dll.GamePostStart();
        dll.MapUnload();
        dll.GameStop();

        Assert.Empty(engine.AllOf("StringTables.Create"));
    }

    [Fact]
    public void TheStringGoesIntoTheTableTheEngineHasForThatName()
    {
        var (dll, engine) = Build(createAnswers: 9);

        dll.CreateNetworkStringTables();

        var add = First(engine, "StringTables.AddString");

        Assert.True(add is not null, "the table was created and nothing was put in it");
        Assert.Equal(9, add!.Value.Args[0]);
    }

    [Fact]
    public void WhatGoesIntoTheTableIsTheModEntry()
    {
        var (dll, engine) = Build();

        dll.CreateNetworkStringTables();

        Assert.Equal(ServerGameDll.ModTableEntry,
                     First(engine, "StringTables.AddString")!.Value.Args[1]);
    }

    /// <summary>
    /// The read-back is the phase's own gate — "created, filled and read back" —
    /// and it has to be a read of the ENGINE.
    /// </summary>
    [Fact]
    public void TheEntryIsReadBackThroughTheAbiAtTheIndexTheAddAnswered()
    {
        var (dll, engine) = Build(createAnswers: 9, addAnswers: 3);

        dll.CreateNetworkStringTables();

        var read = First(engine, "StringTables.GetString");

        Assert.True(read is not null,
            "the entry was written and never read back. Reporting the string you just wrote is "
          + "equally true of a table that was never filled and of a GetString that answers "
          + "nothing.");
        Assert.Equal(9, read!.Value.Args[0]);
        Assert.Equal(3, read.Value.Args[1]);
    }

    [Fact]
    public void ARefusedCreateDoesNotGoOnToWriteIntoIt()
    {
        // The create was refused, so the name resolves to nothing -- and the
        // handle refuses before any id crosses. There is no -1 to hand to an
        // AddString because the caller never holds an id at all.
        var (dll, engine) = Build(createAnswers: -1);

        dll.CreateNetworkStringTables();

        Assert.Empty(engine.AllOf("StringTables.AddString"));
        Assert.Empty(engine.AllOf("StringTables.GetString"));
    }

    [Fact]
    public void ARefusedAddDoesNotGoOnToReadTheIndexItWasRefusedWith()
    {
        // -1 as an index, handed to GetString, is a read at a negative offset on
        // the other side of the boundary. The native half refuses it too — this
        // is the near half of the same guard, and both halves are cheap.
        var (dll, engine) = Build(addAnswers: -1);

        dll.CreateNetworkStringTables();

        Assert.Empty(engine.AllOf("StringTables.GetString"));
    }

    /// <summary>
    /// The order, as a sequence rather than as a set. "A create, an add and a
    /// read happened" is true of those three in any order, and only one of the
    /// six orders works.
    ///
    /// <para>
    /// <c>Find</c> and <c>GetNumStrings</c> are filtered out, and the filter is
    /// not a convenience: a handle holds the table's NAME and asks which id it
    /// has on every use, so a <c>Find</c> sits in front of each of these. That
    /// is <see cref="IEngineStringTable"/>'s own behaviour and has its own facts
    /// in <see cref="StringTableDeclarationTests"/>; pinning it here as well
    /// would make this fact about the handle instead of about this class.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSequenceIsCreateThenAddThenRead()
    {
        var (dll, engine) = Build();

        dll.CreateNetworkStringTables();

        string[] asked = engine.Calls
            .Where(c => c.Member is "StringTables.Create"
                                 or "StringTables.AddString"
                                 or "StringTables.GetString")
            .Select(c => c.Member)
            .ToArray();

        Assert.Equal(
            new[] { "StringTables.Create", "StringTables.AddString", "StringTables.GetString" },
            asked);
    }
}

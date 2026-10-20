using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <b>`ent_fire` REACHING AN ENTITY THAT IS IN NO NATIVE LIST</b> —
/// <c>plan_entity.md</c> step 8e-entfire.
///
/// <para>
/// The defect these close: every native find walks <c>CEntInfo</c>, and a
/// shell-less entity has no C++ object to have an entry — so
/// <c>ent_fire env_shake StartShake</c> answered "nothing is called that and
/// nothing is of that class" in the same second <c>debug-api ents env_shake</c>
/// reported count 1 (status/7c.md §11). That is every point entity
/// <c>ent_create</c> makes.
/// </para>
///
/// <para>
/// <b>What is actually being tested here is the SIZE-THEN-FILL contract</b>, and
/// it is worth its own file because the two numbers it turns on are easy to
/// confuse and the confusion is silent both ways. Answering the FILLED count
/// would make an undersized buffer look like a smaller world, and the caller
/// would deliver the input to some of the entities and report success; writing
/// past the buffer would corrupt the event queue's stack frame. Neither shows
/// up as an error.
/// </para>
///
/// <para>
/// The native half of the same contract — the grow path a map cannot reach —
/// is <c>sourcesharp/test/shellless_find_test.cpp</c>.
/// </para>
/// </summary>
public class ShellLessEntFireTests
{
    private const string Shellless = "test_shellless_relay";
    private const string Sink = "test_shellless_sink";

    /// <summary>
    /// A registry with this assembly's classes in it, over routes of its own
    /// (plan_abi_hotreload D7: the shell-less route is an instance per side,
    /// read back as <see cref="ScriptedEntityRegistry.ShellLess"/>).
    /// </summary>
    private static ScriptedEntityRegistry Fresh()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ShellLessEntFireTests).Assembly);
        return registry;
    }

    /// <summary><c>key\0value\0…</c>, the map spawner's payload.</summary>
    private static byte[] Named(string name)
    {
        var bytes = new List<byte>();

        bytes.AddRange(System.Text.Encoding.UTF8.GetBytes("targetname"));
        bytes.Add(0);
        bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(name));
        bytes.Add(0);

        return bytes.ToArray();
    }

    //=========================================================================
    // THE QUERY FORM — what ent_fire asks before it queues an event.
    //=========================================================================

    [Fact]
    public void AnEmptyBufferStillAnswersHowManyMatched()
    {
        var registry = Fresh();
        registry.Create(Shellless, Named("shaker"));

        // ent_fire's own call: it wants to know whether to print "nothing is
        // called that", not which entities they are.
        Assert.Equal(1, registry.ShellLess.Find("shaker", byClassname: false, Span<uint>.Empty));
    }

    [Fact]
    public void AnEmptyBufferAnswersZeroForAWordNothingCarries()
    {
        var registry = Fresh();

        Assert.Equal(0, registry.ShellLess.Find("nobody_is_called_this",
                                                byClassname: false, Span<uint>.Empty));
    }

    [Fact]
    public void TheClassnameIndexAnswersTheClassEntCreateMade()
    {
        var registry = Fresh();
        registry.Create(Shellless, default);

        // The `ent_fire <classname>` case, which is the one a person uses on an
        // entity ent_create has just made and which therefore has no name.
        Assert.Equal(1, registry.ShellLess.Find(Shellless, byClassname: true, Span<uint>.Empty));
    }

    [Fact]
    public void AnUnnamedEntityIsNotFoundByName()
    {
        var registry = Fresh();
        registry.Create(Shellless, default);

        Assert.Equal(0, registry.ShellLess.Find(Shellless, byClassname: false, Span<uint>.Empty));
    }

    //=========================================================================
    // THE FILL — and the two numbers that must not be the same one.
    //=========================================================================

    [Fact]
    public void AFittingBufferGetsEveryHandle()
    {
        var registry = Fresh();
        var first = registry.Create(Shellless, Named("shaker"));
        var second = registry.Create(Shellless, Named("shaker"));

        Span<uint> into = stackalloc uint[4];

        Assert.Equal(2, registry.ShellLess.Find("shaker", byClassname: false, into));

        // As a SET: the walk's order is the directory's, which is not a
        // promise the ABI makes and not one the event queue needs. What it
        // does need is that both handles are there and neither is the invalid
        // one — a fill that wrote the same handle twice would deliver the
        // input once and report two.
        uint[] got = { into[0], into[1] };

        Assert.Contains(first.Value, got);
        Assert.Contains(second.Value, got);
    }

    [Fact]
    public void AnUndersizedBufferStillAnswersTheTotal()
    {
        var registry = Fresh();
        registry.Create(Shellless, Named("shaker"));
        registry.Create(Shellless, Named("shaker"));
        registry.Create(Shellless, Named("shaker"));

        Span<uint> into = stackalloc uint[2];

        // THREE, not two. The answer is how many matched; the buffer is how
        // many fit. A caller that got 2 back would size a buffer of 2, ask
        // again, and go on missing the third for ever.
        Assert.Equal(3, registry.ShellLess.Find("shaker", byClassname: false, into));
    }

    [Fact]
    public void AnUndersizedBufferIsNotWrittenPast()
    {
        var registry = Fresh();
        registry.Create(Shellless, Named("shaker"));
        registry.Create(Shellless, Named("shaker"));
        registry.Create(Shellless, Named("shaker"));

        Span<uint> into = stackalloc uint[4];
        into[2] = 0xDEADBEEF;
        into[3] = 0xDEADBEEF;

        registry.ShellLess.Find("shaker", byClassname: false, into.Slice(0, 2));

        Assert.Equal(new[] { 0xDEADBEEFu, 0xDEADBEEFu }, new[] { into[2], into[3] });
    }

    //=========================================================================
    // THE SAME MATCHING RULE AS THE TWO HIERARCHIES THAT HAVE A C++ OBJECT.
    //=========================================================================

    [Fact]
    public void AWildcardAnswersAFamilyOfClassnames()
    {
        var registry = Fresh();
        registry.Create(Shellless, default);
        registry.Create(Sink, default);

        // EntityNameMatch.Match is the managed spelling of EntityNames_Match,
        // so "test_shellless_*" answers here exactly what it answers on the
        // shelled hierarchy's FindGameEntityByClassname.
        Assert.Equal(2, registry.ShellLess.Find("test_shellless_*",
                                             byClassname: true, Span<uint>.Empty));
    }

    [Fact]
    public void AClassnameQueryDoesNotAnswerADifferentClass()
    {
        var registry = Fresh();
        registry.Create(Sink, default);

        Assert.Equal(0, registry.ShellLess.Find(Shellless, byClassname: true, Span<uint>.Empty));
    }

    //=========================================================================
    // THE REFUSALS. Both of them are the safe direction: nothing answers.
    //=========================================================================

    [Fact]
    public void AnUnboundRouteFindsNothing()
    {
        var registry = Fresh();
        registry.Create(Shellless, Named("shaker"));

        Assert.Equal(1, registry.ShellLess.Find("shaker", byClassname: false, Span<uint>.Empty));   // the control

        registry.ShellLess.Unbind();

        Assert.Equal(0, registry.ShellLess.Find("shaker", byClassname: false, Span<uint>.Empty));
    }

    /// <summary>
    /// The CLIENT's route finds nothing of the server's: each engine side has
    /// its own route, and only the server's registry binds one. It was a side
    /// check on one static route before plan_abi_hotreload D7.
    /// </summary>
    [Fact]
    public void TheRouteFindsNothingOnADifferentSide()
    {
        var server = TestSides.Create(Side.Server);
        var client = TestSides.Create(Side.Client);
        var registry = new ScriptedEntityRegistry(server.ShellLess, server.EntityIo, server.Removal, TestEngines.Server());
        registry.RegisterAll(typeof(ShellLessEntFireTests).Assembly);
        registry.Create(Shellless, Named("shaker"));

        Assert.Equal(0, client.ShellLess.Find("shaker", byClassname: false, Span<uint>.Empty));
        Assert.Equal(1, server.ShellLess.Find("shaker", byClassname: false, Span<uint>.Empty));   // the control
    }

    [Fact]
    public void AnEmptyQueryFindsNothing()
    {
        var registry = Fresh();
        registry.Create(Shellless, Named("shaker"));

        Assert.Equal(0, registry.ShellLess.Find("", byClassname: false, Span<uint>.Empty));
    }

    [Fact]
    public void ANullQueryFindsNothing()
    {
        var registry = Fresh();
        registry.Create(Shellless, Named("shaker"));

        Assert.Equal(0, registry.ShellLess.Find(null, byClassname: false, Span<uint>.Empty));
    }

    //=========================================================================
    // AND THE OTHER HALF: the handle a find answers is one an input REACHES.
    //
    // Separate facts because "the search found it" and "the input arrived" are
    // two different failures, and the whole defect was that the second looked
    // exactly like the first from a console.
    //=========================================================================

    [Fact]
    public void AHandleTheNameSearchAnsweredTakesAnInput()
    {
        var registry = Fresh();
        registry.Create(Shellless, Named("shaker"));

        Span<uint> into = stackalloc uint[4];
        int found = registry.ShellLess.Find("shaker", byClassname: false, into);

        Assert.Equal(1, found);

        var target = new EntityHandle(into[0]);

        Assert.True(registry.Input(target, "StartShake", "", 0.0f, EntityHandle.None));

        var instance = (TestShellLessRelay)registry.Get(target)!;

        Assert.Equal(new[] { "StartShake" }, instance.Inputs);
    }

    [Fact]
    public void EveryHandleTheClassnameSearchAnsweredTakesTheInput()
    {
        var registry = Fresh();
        registry.Create(Shellless, default);
        registry.Create(Shellless, default);

        Span<uint> into = stackalloc uint[4];
        int found = registry.ShellLess.Find(Shellless, byClassname: true, into);

        Assert.Equal(2, found);

        // The event queue's own loop: EVERY entity that answered, not the first.
        // The shelled pass it sits beside resumes through the whole list for
        // exactly this reason.
        for (int i = 0; i < found; i++)
            registry.Input(new EntityHandle(into[i]), "StartShake", "", 0.0f, EntityHandle.None);

        for (int i = 0; i < found; i++)
        {
            var instance = (TestShellLessRelay)registry.Get(new EntityHandle(into[i]))!;
            Assert.Equal(new[] { "StartShake" }, instance.Inputs);
        }
    }

    [Fact]
    public void TheInputCarriesTheActivatorAcross()
    {
        var registry = Fresh();
        var activator = registry.Create(Sink, default);
        registry.Create(Shellless, Named("shaker"));

        Span<uint> into = stackalloc uint[2];
        registry.ShellLess.Find("shaker", byClassname: false, into);

        var target = new EntityHandle(into[0]);

        registry.Input(target, "StartShake", "", 0.0f, activator);

        var instance = (TestShellLessRelay)registry.Get(target)!;

        // ent_fire's activator is the player who typed it, and an input that
        // reads !activator is the reason it has to survive the crossing.
        Assert.Equal(new[] { activator }, instance.Activators);
    }
}

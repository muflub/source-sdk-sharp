using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_novalve.md P3: the server-browser tags the engine derived from
/// IServerGameTags' KeyValues list, declared on the convars (<c>Tag =</c>) and
/// pushed into sv_tags from C# on change (ServerTags).
/// </summary>
public class ServerTagsTests
{
    private static SvGravityConVar Gravity(string engineValue)
    {
        var g = new SvGravityConVar();
        g.Bind(-1, "sv_gravity", "800", engineValue: engineValue, tag: "gravity");
        return g;
    }

    private static (ConVarRegistry Registry, SvGravityConVar Gravity) Declared(string engineValue)
    {
        var registry = new ConVarRegistry(TestServices.UnattachedConsole());
        var g = Gravity(engineValue);
        registry.Adopt(g);
        return (registry, g);
    }

    private static int SvTagsWrites(RecordingEngine engine)
        => engine.Calls.Count(c => c.Member == "Console.SetConVarValueByName" && (string)c.Args[0]! == "sv_tags");

    private static string LastSvTags(RecordingEngine engine)
        => (string)engine.Calls.Last(c => c.Member == "Console.SetConVarValueByName" && (string)c.Args[0]! == "sv_tags").Args[1]!;

    //-------------------------------------------------------------------------
    // The engine's add/remove rule.
    //-------------------------------------------------------------------------

    [Fact]
    public void AddPutsTheTagFirst()
        => Assert.Equal("gravity,a,b", ServerTags.Add("a,b", "gravity"));

    [Fact]
    public void AddToAnEmptyStringLeavesTheEnginesTrailingComma()
        => Assert.Equal("gravity,", ServerTags.Add("", "gravity"));

    [Fact]
    public void AddDoesNotRepeatATagInAnyCase()
        => Assert.Equal("x,GRAVITY", ServerTags.Add("x,GRAVITY", "gravity"));

    [Fact]
    public void RemoveKeepsTheOthersEachWithAComma()
        => Assert.Equal("a,b,", ServerTags.Remove("gravity,a,b", "gravity"));

    [Fact]
    public void RemoveMatchesIgnoringCase()
        => Assert.Equal("a,", ServerTags.Remove("a,Gravity", "gravity"));

    [Fact]
    public void RemoveOfAnAbsentTagChangesNothing()
    {
        Assert.Equal("a,,b", ServerTags.Remove("a,,b", "gravity"));
        Assert.Equal("", ServerTags.Remove("", "gravity"));
    }

    [Fact]
    public void EmptyPiecesAreDroppedLikeTheEnginesSplit()
        => Assert.Equal("a,b,", ServerTags.Remove("a,,gravity,,b", "gravity"));

    [Fact]
    public void TheStringIsCappedAtTheEnginesLength()
        => Assert.Equal(ServerTags.MaxTagStringLength - 1, ServerTags.Add(new string('x', 200), "gravity").Length);

    //-------------------------------------------------------------------------
    // The declaration.
    //-------------------------------------------------------------------------

    [Fact]
    public void SvGravityDeclaresTheGravityTag()
    {
        var attr = (ConVarAttribute)System.Attribute.GetCustomAttribute(typeof(SvGravityConVar), typeof(ConVarAttribute))!;
        Assert.Equal("gravity", attr.Tag);
    }

    [Fact]
    public void RegistrationCarriesTheTagOntoTheConVar()
    {
        var registry = new ConVarRegistry(TestServices.UnattachedConsole());
        registry.RegisterAll(typeof(SvGravityConVar).Assembly);
        Assert.Equal("gravity", registry.Get<SvGravityConVar>()!.Tag);
        Assert.Null(registry.Get<SvMaxSpeedConVar>()!.Tag);
    }

    [Fact]
    public void GravityIsTheOnlyTagTheServerDeclares()
    {
        // Stock's list was sv_gravity and sv_alltalk; this mod has no sv_alltalk.
        var registry = new ConVarRegistry(TestServices.UnattachedConsole());
        registry.RegisterAll(typeof(SvGravityConVar).Assembly);
        Assert.Equal(new[] { "sv_gravity:gravity" },
                     registry.All.Where(c => c.Tag is not null).Select(c => c.Name + ":" + c.Tag).ToArray());
    }

    //-------------------------------------------------------------------------
    // Derive.
    //-------------------------------------------------------------------------

    [Fact]
    public void OffItsDefaultIsTagged()
        => Assert.Equal("gravity,", ServerTags.Derive("", new ConVarBase[] { Gravity("600") }));

    [Fact]
    public void AtItsDefaultIsUntagged()
        => Assert.Equal("a,", ServerTags.Derive("gravity,a", new ConVarBase[] { Gravity("800") }));

    [Fact]
    public void TheDefaultIsComparedAsTextAsTheEngineComparedIt()
        => Assert.Equal("gravity,", ServerTags.Derive("", new ConVarBase[] { Gravity("800.000000") }));

    //-------------------------------------------------------------------------
    // Applied at construction, pushed on change.
    //-------------------------------------------------------------------------

    [Fact]
    public void ConstructionAppliesAValueThatCameUpOffItsDefault()
    {
        var engine = new RecordingEngine().Returns("Console.FindConVarValue", "a");
        var (registry, _) = Declared("600");

        new ServerTags(engine, registry);

        Assert.Equal(1, SvTagsWrites(engine));
        Assert.Equal("gravity,a", LastSvTags(engine));
    }

    [Fact]
    public void AChangeIsPushedAtOnce()
    {
        var engine = new RecordingEngine().Returns("Console.FindConVarValue", "a");
        var (registry, g) = Declared("800");
        new ServerTags(engine, registry);
        Assert.Equal(0, SvTagsWrites(engine));

        g.NotifyChanged("600", "800");

        Assert.Equal(1, SvTagsWrites(engine));
        Assert.Equal("gravity,a", LastSvTags(engine));
    }

    [Fact]
    public void AChangeBackToTheDefaultRemovesTheTag()
    {
        var engine = new RecordingEngine().Returns("Console.FindConVarValue", "gravity,a");
        var (registry, g) = Declared("600");
        new ServerTags(engine, registry);
        Assert.Equal(0, SvTagsWrites(engine));

        g.NotifyChanged("800", "600");

        Assert.Equal("a,", LastSvTags(engine));
    }

    [Fact]
    public void AnUntaggedChangePushesNothing()
    {
        var engine = new RecordingEngine().Returns("Console.FindConVarValue", "a");
        var registry = new ConVarRegistry(TestServices.UnattachedConsole());
        var g = Gravity("800");
        var speed = new SvMaxSpeedConVar();
        speed.Bind(-1, "sv_maxspeed", "320");
        registry.Adopt(g);
        registry.Adopt(speed);
        new ServerTags(engine, registry);
        int calls = engine.Calls.Count;

        speed.NotifyChanged("400", "320");

        Assert.Equal(calls, engine.Calls.Count);
    }

    [Fact]
    public void DetachStopsThePush()
    {
        var engine = new RecordingEngine().Returns("Console.FindConVarValue", "a");
        var (registry, g) = Declared("800");
        new ServerTags(engine, registry).Detach();

        g.NotifyChanged("600", "800");

        Assert.Equal(0, SvTagsWrites(engine));
    }

    [Fact]
    public void WithNothingTaggedSvTagsIsNotEvenRead()
    {
        // Strict: an unanswered read would throw.
        var engine = new RecordingEngine();
        new ServerTags(engine, new ConVarRegistry(TestServices.UnattachedConsole()));
        Assert.Empty(engine.Calls);
    }
}

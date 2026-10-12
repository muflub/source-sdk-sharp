using System.Collections.Generic;

using SourceSharp.Abi;
using Debug.Api;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// How <c>./debug-api fire</c> reads a written target.
///
/// <para>
/// It read one: the first entity carrying that TARGETNAME, and "no entity
/// named" for everything else. So <c>./debug-api fire trigger_multiple Disable</c>
/// was refused against forty-seven live triggers, because a classname is not a
/// targetname — and six lights sharing one name had one of them lit, because a
/// name is not unique and never was.
/// </para>
///
/// <para>
/// The console's own rule is name first, class only if NOTHING is called that,
/// and every entity that answers. That order is not cosmetic: a map is allowed
/// to name one <c>func_button</c> "func_button", and a resolver that searched
/// both at once would fire every button in the level.
/// </para>
/// </summary>
public class EntityTargetTests
{
    /// <summary>The server's IDebugWorld over the recorded engine (G2).</summary>
    private static SourceSharp.Server.DebugWorld World(RecordingEngine engine)
        => new(engine, engine, engine, engine, engine, engine);

    private static List<EntityHandle> Handles(params uint[] values)
    {
        var list = new List<EntityHandle>();
        foreach (uint v in values)
            list.Add(new EntityHandle(v));

        return list;
    }

    [Fact]
    public void ANameMatchesEveryEntityCarryingIt()
    {
        var engine = Fake()
            .Returns("Entities.FindAllByName", Handles(11, 22, 33));

        var found = EntityTargets.Resolve(World(engine), "six_lights", out var matched);

        Assert.True(found.Count == 3, "all three entities sharing the name are addressed, not the first");
        Assert.True(matched == EntityTargets.MatchKind.Name, "a name that matched is reported as a name");
    }

    [Fact]
    public void AClassIsNotSearchedWhileAnythingCarriesTheName()
    {
        var engine = Fake()
            .Returns("Entities.FindAllByName", Handles(11));

        EntityTargets.Resolve(World(engine), "func_button", out _);

        Assert.True(engine.CountOf("Entities.FindAllByClassname") == 0,
            "a map may name one entity after its own class, and that one entity is what was meant");
    }

    [Fact]
    public void AClassnameMatchesWhenNothingCarriesTheName()
    {
        var engine = Fake()
            .Returns("Entities.FindAllByName", Handles())
            .Returns("Entities.FindAllByClassname", Handles(7, 8, 9, 10));

        var found = EntityTargets.Resolve(World(engine), "trigger_multiple", out var matched);

        Assert.True(found.Count == 4, "every live entity of the class is addressed");
        Assert.True(matched == EntityTargets.MatchKind.ClassName, "a class that matched is reported as a class");
    }

    [Fact]
    public void NothingAnsweringIsSaidRatherThanGuessedAt()
    {
        var engine = Fake()
            .Returns("Entities.FindAllByName", Handles())
            .Returns("Entities.FindAllByClassname", Handles());

        var found = EntityTargets.Resolve(World(engine), "no_such_thing", out var matched);

        Assert.True(found.Count == 0, "nothing answers to a name nothing carries and no class uses");
        Assert.True(matched == EntityTargets.MatchKind.None, "and the answer says which of the two readings failed: both");
    }

    [Fact]
    public void AnEmptyTargetAsksTheEngineNothing()
    {
        // Strict by default, so a search the resolver should not have made
        // would throw here rather than quietly answering empty.
        var engine = Fake();

        var found = EntityTargets.Resolve(World(engine), "  ", out var matched);

        Assert.True(found.Count == 0, "whitespace addresses nobody");
        Assert.True(matched == EntityTargets.MatchKind.None, "and matches nothing");
        Assert.True(engine.CountOf("Entities.FindAllByName") == 0, "and is not carried to the engine as a search");
    }
}

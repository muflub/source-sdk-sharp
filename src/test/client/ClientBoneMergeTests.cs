using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// What cs_bonemerge's filter picks (plan_baseentity.md Phase 10). The merge
/// itself is native and proven live; what is C#'s to decide — and to get
/// quietly wrong — is WHICH entity a person's filter names.
/// </summary>
public class ClientBoneMergeTests
{
    private sealed class Entity : ClientStandaloneEntity
    {
        public Entity(IEngineEntityRender render, string className) : base(render)
        {
            ClassName = className;
        }

        public override string ClassName { get; }
    }

    private static List<ClientStandaloneEntity> Live(params string[] classNames)
    {
        var render = (IEngineEntityRender)Fake();
        return classNames.Select(name => (ClientStandaloneEntity)new Entity(render, name)).ToList();
    }

    [Fact]
    public void PicksTheFirstMatchCaseInsensitively()
    {
        var live = Live("env_wind", "ss_barrel", "ss_barrel");

        var picked = ClientBoneMerge.Pick(live, "SS_BAR");

        Assert.Same(live[1], picked);
    }

    [Fact]
    public void APartialNameIsEnough()
    {
        var live = Live("func_areaportalwindow");

        Assert.Same(live[0], ClientBoneMerge.Pick(live, "portal"));
    }

    [Fact]
    public void NoMatchIsNullNotAGuess()
    {
        var live = Live("ss_barrel");

        Assert.Null(ClientBoneMerge.Pick(live, "weapon_rpg"));
    }

    [Fact]
    public void ANumberIsAnEntityIndexAndMatchesExactly()
    {
        // Three barrels of one class is the normal state of a test session,
        // and a classname filter can only ever reach the first of them. The
        // index is the low bits of the handle — the same packing the native
        // resolver unpacks.
        var live = Live("ss_barrel", "ss_barrel", "ss_barrel");

        SetHandle(live[0], 413);
        SetHandle(live[1], 414);
        SetHandle(live[2], 415);

        Assert.Same(live[1], ClientBoneMerge.Pick(live, "414"));
        Assert.Null(ClientBoneMerge.Pick(live, "999"));
    }

    private static void SetHandle(ClientStandaloneEntity entity, int index)
        => entity.Handle = new EntityHandle((uint)((7 << 16) | index)); // any serial, this entry

    [Fact]
    public void AnEmptyFilterRefusesRatherThanPickingTheFirst()
    {
        // "Merge the first entity I have" is never what anyone meant, and a
        // command that guesses would point some unrelated entity's bones at a
        // skeleton nobody chose — the failure would be visual and unexplained.
        var live = Live("ss_barrel");

        Assert.Null(ClientBoneMerge.Pick(live, string.Empty));
    }
}

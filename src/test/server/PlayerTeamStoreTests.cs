using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A player's team is stored in managed code (plan_server_native_v2 T4): the
/// native store had no native reader, so it and its two slots left together.
/// Stock's CBaseEntity::ChangeTeam store: unassigned (0) until set, -1 for a
/// handle that is not a player.
/// </summary>
public sealed class PlayerTeamStoreTests
{
    private static readonly EntityHandle Player = new(0x00010001);
    private static readonly EntityHandle Other = new(0x00020002);

    private readonly PlayerTeams _teams = new();

    [Fact]
    public void APlayerNeverPutOnATeamIsUnassigned()
    {
        Assert.Equal(0, _teams.Get(Player, isPlayer: true));
    }

    [Fact]
    public void TheTeamSetIsTheTeamRead()
    {
        _teams.Set(Player, 3, isPlayer: true);

        Assert.Equal(3, _teams.Get(Player, isPlayer: true));
    }

    [Fact]
    public void EachPlayerKeepsItsOwnTeam()
    {
        _teams.Set(Player, 2, isPlayer: true);
        _teams.Set(Other, 3, isPlayer: true);

        Assert.Equal((2, 3), (_teams.Get(Player, isPlayer: true), _teams.Get(Other, isPlayer: true)));
    }

    [Fact]
    public void AHandleThatIsNotAPlayerIsOnNoTeam()
    {
        Assert.Equal(-1, _teams.Get(Player, isPlayer: false));
    }

    [Fact]
    public void ATeamGivenToAHandleThatIsNotAPlayerIsNotKept()
    {
        _teams.Set(Player, 2, isPlayer: false);

        Assert.Equal(0, _teams.Get(Player, isPlayer: true));
    }

    [Fact]
    public void AGonePlayersTeamIsForgotten()
    {
        _teams.Set(Other, 3, isPlayer: true);

        Assert.Equal(-1, _teams.Get(Other, isPlayer: false));
        Assert.Equal(0, _teams.Get(Other, isPlayer: true));
    }

    // The binding asks the engine whether the handle is a player. The slot is
    // [SuppressGCTransition], so a managed callback may not serve it: libc's
    // getpagesize (Windows: the ucrt's _getmaxstdio), which ignores its
    // arguments and answers non-zero, stands in for "a player".
    [Fact]
    public void TheServersPlayersKeepTheTeamTheyAreGiven()
    {
        nint libc = NativeLibrary.Load(OperatingSystem.IsWindows() ? "ucrtbase.dll" : "libc.so.6");
        nint alwaysPlayer = NativeLibrary.GetExport(libc, OperatingSystem.IsWindows() ? "_getmaxstdio" : "getpagesize");

        using var root = new FakeShimRoot();
        root.Live("SourceSharpEntitiesServer001", nameof(ShimEntitiesServerFwd.EntityIsPlayer), alwaysPlayer);
        var side = TestSides.Create(Side.Server);
        side.Attachment.Initialize();
        Assert.True(side.Attachment.AttachShims(root.Bind(), out string why), why);
        var players = SideBindings.Of<IEnginePlayers>(side);

        Assert.Equal(0, players.GetTeam(Player));
        players.SetTeam(Player, 2);
        Assert.Equal(2, players.GetTeam(Player));
    }
}

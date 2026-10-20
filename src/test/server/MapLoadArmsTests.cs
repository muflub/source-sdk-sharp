using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Which arm of <c>IServerGameDLL::LevelInit</c> a load takes, held to stock's
/// <c>CServerGameDLL::LevelInit</c> (gameinterface.cpp): <c>if (loadGame) { if
/// (pOldLevel) Transition else LoadGame } else { if (background) Background
/// else NewGame }</c>. The shell chose in C++ until plan_server_native_v2 T4e;
/// <see cref="MapLoadArms.For"/> chooses now.
/// </summary>
public class MapLoadArmsTests
{
    [Fact]
    public void ALoadGameWithAnOldLevelIsATransition()
        => Assert.Equal(MapLoadType.Transition, MapLoadArms.For(loadGame: true, hasOldLevel: true, background: false));

    [Fact]
    public void ALoadGameWithNoOldLevelIsASavedGame()
        => Assert.Equal(MapLoadType.LoadGame, MapLoadArms.For(loadGame: true, hasOldLevel: false, background: false));

    [Fact]
    public void ABackgroundMapThatIsNotALoadGameIsABackgroundLoad()
        => Assert.Equal(MapLoadType.Background, MapLoadArms.For(loadGame: false, hasOldLevel: false, background: true));

    [Fact]
    public void APlainLoadIsANewGame()
        => Assert.Equal(MapLoadType.NewGame, MapLoadArms.For(loadGame: false, hasOldLevel: false, background: false));

    /// <summary>Stock tests <c>loadGame</c> first: background is read only on the other arm.</summary>
    [Fact]
    public void LoadGameIsAskedBeforeBackground()
        => Assert.Equal(MapLoadType.LoadGame, MapLoadArms.For(loadGame: true, hasOldLevel: false, background: true));

    /// <summary>The old level is read only inside the <c>loadGame</c> arm.</summary>
    [Fact]
    public void AnOldLevelWithoutLoadGameIsStillANewGame()
        => Assert.Equal(MapLoadType.NewGame, MapLoadArms.For(loadGame: false, hasOldLevel: true, background: false));
}

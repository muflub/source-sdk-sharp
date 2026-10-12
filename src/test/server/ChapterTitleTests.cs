using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// worldspawn's chaptertitle key, announced by C# (Phase 12).
///
/// <para>
/// Stock spends the key in <c>CWorld::Precache</c> on a native env_message that
/// (a) this build's factory answers NULL for, because env_message is managed,
/// and (b) even in stock reaches nobody on a multiplayer server. The managed
/// rule announced instead: each player sees the title once per level, on their
/// first spawn, as the same HudText a managed env_message sends.
/// </para>
/// </summary>
public class ChapterTitleTests
{
    private static readonly EntityHandle PlayerOne = new(0x0001_0002u);
    private static readonly EntityHandle PlayerTwo = new(0x0002_0007u);

    [Fact]
    public void FirstSpawnGetsTheTitleOnce()
    {
        var engine = Fake();
        var title = new ChapterTitle(new RecordingLog(), engine);

        title.LevelInit("#HL2_Chapter1_Title");

        Assert.True(title.OnPlayerSpawned(PlayerOne, 1), "the first spawn is announced to");

        // A respawn is the same player; the title is not nagged.
        Assert.True(!title.OnPlayerSpawned(PlayerOne, 1), "a respawn is not announced to again");

        var sends = engine.Calls.Where(c => c.Member == "UserMessages.Send").ToList();
        Assert.True(sends.Count == 1, "exactly one HudText went out");
        Assert.True(sends[0].ArgAs<string>(0) == "HudText", "it is the HUD's titles.txt message");
        Assert.True(sends[0].ArgAs<int>(2) == 1, "sent to that player alone, not broadcast");
    }

    [Fact]
    public void EachPlayerIsAnnouncedToSeparately()
    {
        var engine = Fake();
        var title = new ChapterTitle(new RecordingLog(), engine);

        title.LevelInit("A RED LETTER DAY");
        title.OnPlayerSpawned(PlayerOne, 1);

        Assert.True(title.OnPlayerSpawned(PlayerTwo, 2), "a second player gets their own copy");

        var sends = engine.Calls.Where(c => c.Member == "UserMessages.Send").ToList();
        Assert.True(sends.Count == 2, "one send per player");
        Assert.True(sends[1].ArgAs<int>(2) == 2, "the second went to the second player's index");
    }

    [Fact]
    public void ANewLevelForgetsWhoHasSeenIt()
    {
        var engine = Fake();
        var title = new ChapterTitle(new RecordingLog(), engine);

        title.LevelInit("chapter one");
        title.OnPlayerSpawned(PlayerOne, 1);

        // Same handle value on the next level — handles restart with the map,
        // so the record of who saw what must restart with them.
        title.LevelInit("chapter two");

        Assert.True(title.OnPlayerSpawned(PlayerOne, 1), "the new level's title shows again");
    }

    [Fact]
    public void ATitlelessLevelSaysNothing()
    {
        var engine = Fake();
        var title = new ChapterTitle(new RecordingLog(), engine);

        // Every stock multiplayer map: no chaptertitle key at all.
        title.LevelInit(string.Empty);

        Assert.True(!title.OnPlayerSpawned(PlayerOne, 1), "no title, no announcement");
        Assert.True(engine.Calls.All(c => c.Member != "UserMessages.Send"), "nothing was sent");
    }

    [Fact]
    public void AnOverlongTitleIsRefusedNotTruncated()
    {
        var engine = Fake();
        var title = new ChapterTitle(new RecordingLog(), engine);

        // The HudText payload is 256 bytes; a name longer than that would
        // arrive cut mid-token and match nothing in titles.txt. Refusing is
        // the honest failure, and it is env_message's own rule.
        title.LevelInit(new string('x', 300));

        Assert.True(!title.OnPlayerSpawned(PlayerOne, 1), "an unsendable title reports itself unsent");
        Assert.True(engine.Calls.All(c => c.Member != "UserMessages.Send"), "and nothing went out");
    }
}

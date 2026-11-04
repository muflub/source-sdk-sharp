using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>What a server asks this player's screen to point at: lives, goes, and shows once per place.</summary>
public class PlayerMarkerTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 100f;
    }

    private sealed class View : IMarkerView
    {
        public Vector LocalOrigin { get; set; }
        public bool Ahead { get; set; } = true;
        public (float X, float Y) Screen { get; set; } = (0.5f, 0.5f);

        public bool ToScreen(Vector world, out float x, out float y)
        {
            (x, y) = Screen;
            return Ahead;
        }
    }

    private sealed class Files(byte[]? map) : IEngineFiles
    {
        public int Reads;
        public string? Asked;
        public int GameFileSize(string path) => 0;
        public string? ReadGameFile(string path) => null;
        public byte[]? ReadGameFileBytes(string path)
        {
            Reads++;
            Asked = path;
            return map;
        }
        public byte[]? ReadFileOnPath(string path, string pathId) => null;
    }

    private sealed class NoWorld : IMinimapWorld
    {
        public Vector LocalOrigin => default;
        public float LocalYaw => 0f;
        public void OtherPlayers(List<Vector> into) { }
        public bool OnScreen(Vector origin) => false;
    }

    private static (PlayerMarkerBoard Board, Clock Clock, View View) Make()
    {
        var clock = new Clock();
        var view = new View();
        var map = new Minimap(new Files(MinimapTests.OneRoomBsp), clock, new NoWorld());
        map.OnLevelInit("dm_lockdown");
        return (new PlayerMarkerBoard(clock, map, view), clock, view);
    }

    private static PlayerMarker Place(float x = 160f, string label = "Health kit", float seconds = 10f, PlayerMarkerKind kind = PlayerMarkerKind.Exact, float radius = 0f)
        => new() { Position = new Vector(x, 160f, 0f), Label = label, Seconds = seconds, Kind = (byte)kind, Radius = radius };

    /// <summary>A place shows on the minimap and in the world, named, with how far it is.</summary>
    [Fact]
    public void APlaceShowsOnTheMinimapAndInTheWorld()
    {
        var (board, _, view) = Make();
        view.LocalOrigin = new Vector(160f + 525f, 160f, 0f);
        board.OnPlayerMarker(Place());
        Assert.Equal(1, board.PingCount);
        Assert.Equal(0f, board.Ping(0).Radius);
        Assert.Equal(1, board.MarkerCount);
        WorldMarker m = board.Marker(0);
        Assert.Equal(("Health kit", 10), (m.Label, m.Metres));
    }

    /// <summary>A rough area is the minimap's alone: a large soft circle, and nothing in the world.</summary>
    [Fact]
    public void AnAreaIsOnTheMinimapAlone()
    {
        var (board, _, _) = Make();
        board.OnPlayerMarker(Place(kind: PlayerMarkerKind.Area, radius: 100f));
        Assert.Equal(1, board.PingCount);
        Assert.True(board.Ping(0).Radius > 0f, "an area has a radius on the map");
        Assert.Equal(0, board.MarkerCount);
    }

    /// <summary>A marker lives its seconds and goes.</summary>
    [Fact]
    public void AMarkerExpires()
    {
        var (board, clock, _) = Make();
        board.OnPlayerMarker(Place());
        clock.Now += 9.9f;
        Assert.Equal(1, board.MarkerCount);
        Assert.InRange(board.Marker(0).Age, 0.9f, 1f);
        clock.Now += 0.2f;
        Assert.Equal(0, board.MarkerCount);
        Assert.Equal(0, board.PingCount);
    }

    /// <summary>A marker from before a level change goes, and one with no life never shows.</summary>
    [Fact]
    public void ALevelChangeClearsThem()
    {
        var (board, clock, _) = Make();
        board.OnPlayerMarker(Place());
        board.OnLevelShutdown();
        Assert.Equal(0, board.MarkerCount);
        board.OnPlayerMarker(Place());
        clock.Now = 1f;
        Assert.Equal(0, board.MarkerCount);
        board.OnPlayerMarker(Place(seconds: 0f));
        Assert.Equal(0, board.MarkerCount);
    }

    /// <summary>The same place named again starts over and does not double up; another of the same name is another marker.</summary>
    [Fact]
    public void OneMarkerPerPlace()
    {
        var (board, clock, _) = Make();
        board.OnPlayerMarker(Place());
        clock.Now += 5f;
        board.OnPlayerMarker(Place());
        Assert.Equal(1, board.MarkerCount);
        Assert.Equal(0f, board.Marker(0).Age);
        board.OnPlayerMarker(Place(x: 200f));
        Assert.Equal(2, board.MarkerCount);
    }

    /// <summary>A small cap, the oldest giving way.</summary>
    [Fact]
    public void TheNumberOfMarkersIsCapped()
    {
        var (board, _, _) = Make();
        for (int i = 0; i <= PlayerMarkerBoard.MaxMarkers; i++)
            board.OnPlayerMarker(Place(x: 100f + i * 10f, label: $"m{i}"));
        Assert.Equal(PlayerMarkerBoard.MaxMarkers, board.MarkerCount);
        Assert.Equal("m1", board.Marker(0).Label);
    }

    /// <summary>A place on the screen stands where it is; behind the player it holds to the edge on the side to turn to.</summary>
    [Fact]
    public void APlaceBehindHoldsToTheEdge()
    {
        var (board, _, view) = Make();
        view.Screen = (0.25f, 0.5f);
        board.OnPlayerMarker(Place());
        Assert.Equal(1, board.MarkerCount);
        Assert.Equal(0.25f, board.Marker(0).X, 0.001f);
        view.Ahead = false;
        board.OnPlayerMarker(Place(x: 200f));
        Assert.Equal(2, board.MarkerCount);
        view.Screen = (0.25f, 0.02f);
        board.OnPlayerMarker(Place(x: 200f));
        Assert.Equal(2, board.MarkerCount);
        Assert.Equal(1f - PlayerMarkerBoard.EdgeInset, board.Marker(1).X, 0.001f);
        Assert.Equal(0.5f, board.Marker(1).Y, 0.001f);
    }

    /// <summary>A map with no outline has nowhere to ping, though the world marker still shows.</summary>
    [Fact]
    public void NoOutlineNoPing()
    {
        var clock = new Clock();
        var map = new Minimap(new Files([]), clock, new NoWorld());
        map.OnLevelInit("dm_lockdown");
        var board = new PlayerMarkerBoard(clock, map, new View());
        board.OnPlayerMarker(Place());
        Assert.Equal(0, board.PingCount);
        Assert.Equal(1, board.MarkerCount);
    }

    /// <summary>The message's payload round-trips, as the wire has it, and what it carries is what a generic marker is.</summary>
    [Fact]
    public void TheMessageRoundTrips()
    {
        Span<byte> buffer = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        int length = Place().Write(buffer);
        var reader = new UserMessageReader(buffer[..length]);
        PlayerMarker back = PlayerMarker.Read(ref reader);
        Assert.Equal(("Health kit", 10f, 160f), (back.Label, back.Seconds, back.Position.X));
    }
}

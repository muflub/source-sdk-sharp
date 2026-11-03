using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>A fixed game of players; a mute flips the row, as the engine's would.</summary>
public sealed class FixedPlayerList(params PlayerListEntry[] players) : IPlayerListSource
{
    private readonly PlayerListEntry[] _players = players;

    public int Refreshes { get; private set; }

    public void Refresh() => Refreshes++;

    public int Count => _players.Length;

    public PlayerListEntry Player(int i) => _players[i];

    public void ToggleMute(int i) => _players[i] = _players[i] with { Muted = !_players[i].Muted };
}

public static class PlayerListSamples
{
    public static FixedPlayerList Game() => new(
        new PlayerListEntry("Gordon", false, false, true),
        new PlayerListEntry("Alyx Vance", false, false, false),
        new PlayerListEntry("a very long player name that runs past the column", true, false, false),
        new PlayerListEntry("Bot01", false, true, false),
        new PlayerListEntry("Barney", false, false, false));

    public static PlayerList Build(UiSampleServices s, IPlayerListSource players)
    {
        s.Add<IUiWindowStore>(new MemoryWindowStore());
        s.Add<IPlayerListSource>(players);
        return s.Create<PlayerList>();
    }

    [UiSample("five players, one muted, one bot")]
    private static UiScreen Players(UiSampleServices s) => Build(s, Game());

    [UiSample("alone")]
    private static UiScreen Alone(UiSampleServices s) => Build(s, new FixedPlayerList());
}

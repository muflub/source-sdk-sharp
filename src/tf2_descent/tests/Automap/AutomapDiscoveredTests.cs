using TF2.Descent.Rules.Automap;
using Xunit;
using Map = TF2.Descent.Rules.Automap.Automap;

namespace TF2.Descent.Tests.Automap;

/// <summary>
/// Discovered rooms (user 2026-11-15): a room joined by a door to a room entered is discovered,
/// its outline drawn fainter; it is explored once entered; a room two doors away stays hidden.
/// <code>
///   arrival_hall ─d1─ mess_room ─d2─ secret_vault
///                        │
///                        d3
///                        │
///                     loft_room (storey 1)
/// </code>
/// </summary>
public sealed class AutomapDiscoveredTests
{
    private static readonly AutomapLevel Level = AutomapLevel.Create(
        [new AutomapRoom("arrival_hall"), new AutomapRoom("mess_room"), new AutomapRoom("secret_vault"), new AutomapRoom("loft_room", 1)],
        [new AutomapDoor("d1", "arrival_hall", "mess_room"), new AutomapDoor("d2", "mess_room", "secret_vault"), new AutomapDoor("d3", "mess_room", "loft_room")]);

    private static Map Visit(params string[] rooms) => rooms.Aggregate(Map.Empty, (m, r) => AutomapRules.Enter(m, Level, r));

    private static string[] Discovered(Map map) => [.. AutomapRules.Discovered(map, Level).Select(r => r.Id)];

    [Fact]
    public void A_neighbour_of_an_explored_room_is_discovered()
        => Assert.Equal(["mess_room"], Discovered(Visit("arrival_hall")));

    [Fact]
    public void A_room_two_doors_away_stays_hidden()
    {
        var discovered = Discovered(Visit("arrival_hall"));

        Assert.NotEmpty(discovered);                                  // the stimulus: something was discovered
        Assert.DoesNotContain("secret_vault", discovered);
        Assert.DoesNotContain("loft_room", discovered);
    }

    [Fact]
    public void A_discovered_room_is_explored_once_entered_and_its_own_neighbours_are_discovered()
    {
        var map = Visit("arrival_hall", "mess_room");

        Assert.True(map.Seen("mess_room"));
        Assert.Equal(["secret_vault", "loft_room"], Discovered(map));
    }

    [Fact]
    public void A_discovered_room_keeps_its_storey()
        => Assert.Equal(1, AutomapRules.Discovered(Visit("mess_room"), Level).Single(r => r.Id == "loft_room").Storey);

    [Fact]
    public void Nothing_entered_discovers_nothing()
        => Assert.Empty(Discovered(Map.Empty));

    [Fact]
    public void Every_room_entered_leaves_nothing_discovered()
        => Assert.Empty(Discovered(AutomapRules.RevealAll(Map.Empty, Level)));
}

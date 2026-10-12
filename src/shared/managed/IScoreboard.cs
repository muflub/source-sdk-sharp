namespace SourceSharp.Shared;

/// <summary>
/// The round's tally, as anything reading or writing it sees one.
///
/// <para>
/// An interface rather than a static because a scoreboard is state, and shared
/// mutable state is what made three of this project's tests depend on the order
/// the suites happened to run in. What holds a score should be something a
/// caller was handed, so that two of them can exist and neither can surprise
/// the other.
/// </para>
///
/// <para>
/// <c>Subscribe</c> is off it, matching every other service here: attaching to
/// the disconnect event is the module's, done once at load.
/// </para>
/// </summary>
public interface IScoreboard
{
    /// <summary>Kills per attacker userid. Built on demand, for diagnostics.</summary>
    IReadOnlyDictionary<int, int> Kills { get; }

    /// <summary>Everyone with anything recorded against them.</summary>
    IEnumerable<(int UserId, Scoreboard.Entry Entry)> All { get; }

    /// <summary>The highest score and whose it is. Userid zero when nobody has scored.</summary>
    (int UserId, int Score) Best();

    /// <summary>What is recorded against one player.</summary>
    Scoreboard.Entry Of(int userId);

    /// <inheritdoc cref="Of"/>
    int KillsFor(int userId);

    /// <inheritdoc cref="Of"/>
    int DeathsFor(int userId);

    /// <inheritdoc cref="Of"/>
    int ScoreFor(int userId);

    /// <summary>Adds to what is recorded. Deltas, because a kill and a point are not one event.</summary>
    void Add(int userId, int kills, int deaths, int score);

    /// <summary>Clears the board, at the end of a round.</summary>
    void Reset();
}

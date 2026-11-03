using System.Collections.Immutable;
using SourceSharp.Abi;

namespace SourceSharp.Ai.Hl2mp.Client.Setup;

/// <summary>
/// One custom bot on the create-server list: its skills (named for the bot,
/// or "" for one the server names by its trait) and the revisions of the
/// profile it came from, so editing it again keeps its history.
/// </summary>
public sealed record CustomBot(SkillProfile Profile, ImmutableArray<ProfileRevision> Revisions)
{
    /// <summary>Whether the bot has a name of its own (a saved profile's); without one the server draws a name.</summary>
    public bool Named => Profile.Name.Length > 0;

    /// <summary>The roster entry that adds this bot: <c>name=&lt;n&gt;:skills=…</c>, or <c>skills=…</c> alone.</summary>
    public RosterEntry Entry => new(Named ? Profile.Name : null, "skills=" + string.Join(",", Profile.Levels));
}

/// <summary>
/// The create-server screen's custom bots, shared by the screen's model and
/// the editor's: which bot the editor is on (<see cref="Editing"/>, -1 a new
/// one), and the bots themselves.
/// </summary>
[SideService(Side.Client)]
public sealed class CustomBots
{
    private readonly List<CustomBot> _bots = new();

    public IReadOnlyList<CustomBot> All => _bots;

    public int Count => _bots.Count;

    /// <summary>Counts every change, so a reader keeps its rows with a compare.</summary>
    public int Version { get; private set; }

    /// <summary>The bot the editor is on: an index, or -1 for a bot not yet on the list.</summary>
    public int Editing { get; set; } = -1;

    public CustomBot this[int i] => _bots[i];

    public void Add(CustomBot bot)
    {
        _bots.Add(bot);
        Version++;
    }

    public void Replace(int i, CustomBot bot)
    {
        _bots[i] = bot;
        Version++;
    }

    public void RemoveAt(int i)
    {
        _bots.RemoveAt(i);
        if (Editing == i)
            Editing = -1;
        else if (Editing > i)
            Editing--;
        Version++;
    }
}

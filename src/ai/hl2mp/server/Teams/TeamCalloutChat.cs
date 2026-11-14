using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Ai.Hl2mp.Senses;

namespace SourceSharp.Ai.Hl2mp.Teams;

/// <summary>
/// The humans' share of the callouts (plan_bots_teams_tf2 B.4): a callout the speaker
/// asked to be said (<see cref="SayIntent.Text"/>) goes out as its <c>say_team</c>
/// line, through the same say path a player's team chat takes, at most one line per
/// bot per <see cref="CadenceSeconds"/>. Templated words, so it needs no chat model.
/// </summary>
[SideService(Side.Server)]
public sealed class TeamCalloutChat(Hl2mpAiSession ai, Hl2mpBots bots, HostSay say, IEngineFrame frame) : IFrameLifecycle, IGameLifecycle
{
    /// <summary>One line per bot this often at most (chat's own cadence, B.4: 20 s).</summary>
    public const float CadenceSeconds = 20f;

    private readonly Dictionary<int, int> _lastLine = new();
    private ChatPlaces? _places;
    private MemoryWorld? _placesOf;

    public int FrameOrder => ModStages.First + 640;

    public int LifecycleOrder => ModStages.First + 640;

    /// <summary>Lines said, for the facts and <c>/ai/chat</c>.</summary>
    public int LinesSaid { get; private set; }

    public void OnLevelInitPostEntity(string mapName)
    {
        _lastLine.Clear();
        _places = null;
        _placesOf = null;
    }

    public void OnPostThink(float frameTime)
    {
        IReadOnlyList<SpokenCallout> spoken = ai.Session.TakeSpoken();
        if (spoken.Count == 0 || ai.Session.World is not { } world)
            return;
        if (!ReferenceEquals(world, _placesOf))
        {
            _places = new ChatPlaces(world.Map, world.Graph);
            _placesOf = world;
        }

        int tick = frame.TickCount();
        foreach (SpokenCallout c in spoken)
        {
            if (!Due(_lastLine, c.Speaker.Slot, tick, Hl2mpAiSession.TickSeconds) || CalloutWords.Line(c.Say, _places!, world.Map) is not { } line)
                continue;
            EntityHandle p = PlayerOf(c.Speaker);
            if (p == EntityHandle.None)
                continue;
            say.Say(p, line, teamOnly: true);
            LinesSaid++;
        }
    }

    /// <summary>Whether <paramref name="slot"/> may say a line at <paramref name="tick"/>: none in the last <see cref="CadenceSeconds"/>. Notes it when so.</summary>
    public static bool Due(Dictionary<int, int> last, int slot, int tick, float tickSeconds)
    {
        ArgumentNullException.ThrowIfNull(last);
        if (last.TryGetValue(slot, out int was) && (tick - was) * tickSeconds < CadenceSeconds)
            return false;
        last[slot] = tick;
        return true;
    }

    private EntityHandle PlayerOf(ActorId id)
    {
        foreach (Hl2mpBot b in bots.Bots)
        {
            if (b.Id == id)
                return bots.Applier.PlayerOf(b.Id);
        }

        return EntityHandle.None;
    }
}

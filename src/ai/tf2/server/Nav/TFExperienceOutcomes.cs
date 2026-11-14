using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Experience;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// Hands TF2's bots' tries of a way into the enemy's base to the experience store (plan_bots_teams_tf2_part_g G.4's
/// "outcomes", lane G4b): every <see cref="ReadFrames"/> frames, each bot's decider is read under its brain's lock
/// (<see cref="AiSession.TryReadBrain"/>, which a running step makes wait for the next read) for the tries it finished since
/// the last read (<see cref="TFDecider.FamilyTries"/>), and each is handed to <see cref="ExperienceService.NoteOutcome"/>
/// on the game's thread, in full, under its side and class. A slot whose decider is new (a bot added again) is read from
/// its first try.
/// </summary>
[SideService(Side.Server)]
public sealed class TFExperienceOutcomes(AiBots bots, ServerAiSession ai, ExperienceService experience) : IFrameLifecycle
{
    /// <summary>The deciders are read this often.</summary>
    public const int ReadFrames = 16;

    private readonly Dictionary<int, (TFDecider Decider, int Read)> _cursors = new();
    private readonly List<FamilyTry> _fresh = new();
    private int _frames;

    /// <summary>After the experience feed's frame (First + 660) and TF2's nemesis tap (665).</summary>
    public int FrameOrder => ModStages.First + 670;

    /// <summary>Tries read from the deciders, and those the store took.</summary>
    public int Read { get; private set; }

    public int Noted { get; private set; }

    public void OnPostThink(float frameTime)
    {
        if (++_frames < ReadFrames)
            return;
        _frames = 0;
        foreach (AiBot b in bots.Bots)
        {
            int slot = b.Id.Slot;
            _fresh.Clear();
            if (!ai.Session.TryReadBrain(slot, br => Take(slot, br.Decider), out bool _))
                continue;
            foreach (FamilyTry t in _fresh)
            {
                Read++;
                if (experience.NoteOutcome(t.Team, t.Class, TFDecider.RouteFamilyKind, t.Choice, t.Score))
                    Noted++;
            }
        }
    }

    /// <summary>Under the brain's lock: the tries the slot's decider finished since its cursor, into <see cref="_fresh"/>.</summary>
    private bool Take(int slot, IDecider? decider)
    {
        if (decider is not TFDecider d)
            return false;
        int read = _cursors.TryGetValue(slot, out (TFDecider Decider, int Read) c) && ReferenceEquals(c.Decider, d) ? c.Read : 0;
        IReadOnlyList<FamilyTry> tries = d.FamilyTries;
        for (int i = read; i < tries.Count; i++)
            _fresh.Add(tries[i]);
        _cursors[slot] = (d, tries.Count);
        return true;
    }
}

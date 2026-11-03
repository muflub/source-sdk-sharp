using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// What a recording holds beside the brains' inputs and outputs, so a human
/// reference session can be measured against the bots (plan_hl2mp_ai §3.4.4):
/// while <c>ai_record</c> runs, every person is an observer of the bots'
/// vision pass (<see cref="VisionPass.PersonVision"/>) and its sight changes
/// are written as Observed records, never pushed to a brain; every living
/// actor's frame (origin, eye, velocity) is written each tick, humans' and
/// bots' alike. While nothing records, no person is observed.
/// </summary>
[SideService(Side.Server)]
public sealed class ReferenceRecorder(Hl2mpAiSession ai, VisionPass vision)
{
    private readonly List<SensedBot> _observers = new();
    private readonly List<int> _gone = new();

    /// <summary>The observers to serve this tick: every living person while recording, none otherwise.</summary>
    public ReadOnlySpan<SensedBot> Observers(ReadOnlySpan<SensedActor> living, Func<int, ActorId> botIn)
    {
        _observers.Clear();
        if (ai.Recording.Recording)
        {
            foreach (SensedActor a in living)
            {
                if (botIn(a.Id.Slot).IsValid)
                    continue;
                if (vision.ObserverIn(a.Id.Slot) != a.Id)
                    vision.AddObserver(a.Id);
                _observers.Add(new SensedBot(a.Id, a.Player, VisionPass.PersonVision));
            }
        }

        // A person who left (or every one, when the recording stopped) is no longer observed.
        _gone.Clear();
        foreach (int slot in vision.ObserverSlots)
        {
            if (!_observers.Exists(o => o.Id.Slot == slot))
                _gone.Add(slot);
        }

        foreach (int slot in _gone)
            vision.RemoveObserver(slot);
        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_observers);
    }

    /// <summary>The tick's observed sight changes and every living actor's frame, into the recording.</summary>
    public void Write(Tick tick, ReadOnlySpan<SensedActor> living, List<AiEvent> observed)
    {
        if (!ai.Recording.Recording)
            return;
        foreach (AiEvent e in observed)
            ai.Recording.Observed(e);
        foreach (SensedActor a in living)
        {
            if (vision.ReadOf(a.Id.Slot) is { } read)
                ai.Recording.Frame(new AiActorFrame(a.Id, tick, read.Origin, read.Eye, read.Velocity));
        }
    }
}

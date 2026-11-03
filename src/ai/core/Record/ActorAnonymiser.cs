namespace SourceSharp.Ai;

/// <summary>
/// Anonymises a recording at write time (plan_hl2mp_ai §3.4.4): every actor
/// the binding marks as a person (<c>anonymise</c>) is written as an actor of
/// the same slot with a serial drawn at random for this session, the same
/// for the whole session, so a recording names a human by nothing it had
/// outside it. Bots are written as they are (their replay needs their ids).
/// The new serial keeps <c>markBits</c>, so it is still known for a person's.
/// Pure; one per recording.
/// </summary>
public sealed class ActorAnonymiser
{
    private readonly Func<ActorId, bool> _anonymise;
    private readonly int _markBits;
    private readonly Dictionary<ActorId, ActorId> _map = new();
    private readonly HashSet<int> _drawn = new();
    private ulong _state;

    /// <param name="anonymise">Whether an actor is a person's (the binding's mark).</param>
    /// <param name="markBits">The serial bits that mark a person's actor, kept in the drawn serial.</param>
    /// <param name="seed">The session's random seed (the binding draws it from the OS's generator).</param>
    public ActorAnonymiser(Func<ActorId, bool> anonymise, int markBits, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(anonymise);
        _anonymise = anonymise;
        _markBits = markBits;
        _state = seed;
    }

    /// <summary>The actors anonymised so far.</summary>
    public int Count => _map.Count;

    /// <summary><paramref name="id"/> as the recording names it.</summary>
    public ActorId Map(ActorId id)
    {
        if (!id.IsValid || !_anonymise(id))
            return id;
        if (_map.TryGetValue(id, out ActorId known))
            return known;

        int serial;
        do
            serial = (int)(Next() & 0x7FFF_FFFFUL & ~(ulong)(uint)_markBits) | _markBits;
        while (serial == 0 || serial == id.Serial || !_drawn.Add(serial));
        var anon = new ActorId(id.Slot, serial);
        _map[id] = anon;
        return anon;
    }

    public AiEvent Map(in AiEvent e)
    {
        AiEvent m = e with { Bot = Map(e.Bot), Other = Map(e.Other) };
        if (e.Kind == AiEventKind.KillFeed)
        {
            // The killer rides in the codes (AiEvent.KillFeed).
            ActorId killer = Map(new ActorId(e.Code, e.Code2));
            m = m with { Code = killer.Slot, Code2 = killer.Serial };
        }

        return m;
    }

    public IntentCommand Map(in IntentCommand i) => i with { Bot = Map(i.Bot), Look = i.Look with { Actor = Map(i.Look.Actor) } };

    public AiUserCmd Map(in AiUserCmd c) => c with { Actor = Map(c.Actor) };

    public AiActorFrame Map(in AiActorFrame f) => f with { Actor = Map(f.Actor) };

    // splitmix64: the session's stream of serials.
    private ulong Next()
    {
        ulong z = _state += 0x9E37_79B9_7F4A_7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
        return z ^ (z >> 31);
    }
}

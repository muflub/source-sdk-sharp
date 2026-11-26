using SourceSharp.Abi;

namespace SourceSharp.Ai.Server.Senses;

/// <summary>
/// Who each bot takes each player it perceives for (plan_bots_teams_tf2 C.3, the Spy's honesty fix): the game's
/// <see cref="IBotGameView.ShownAs"/>, held per pair of bot and player from the sighting that told the bot of them,
/// so every event about that player and every view of them names the same actor until the bot loses sight of them.
/// A disguised Spy is an alias to his enemies; the real player's row in their memory learns nothing of him while he
/// wears it. When what the game draws changes in sight (the disguise drops, or another goes on), the bot loses the
/// one actor and gains the other at that place, as a player sees one body turn into another. Out of sight nothing
/// links them. A game that draws everyone as themselves (HL2MP) changes no event and no view.
/// </summary>
[SideService(Side.Server)]
public sealed class ShownActors(IBotGame game)
{
    private readonly ActorId[] _held = new ActorId[VisionPass.Slots * VisionPass.Slots];

    /// <summary>Lost-and-gained pairs made because what the game drew changed in sight: <c>ai.shown.changes</c>.</summary>
    public int Changes { get; private set; }

    private static int Pair(int botSlot, int slot) => botSlot * VisionPass.Slots + slot;

    private readonly Tick[] _bumpAt = new Tick[VisionPass.Slots * VisionPass.Slots];
    private Tick _lastRefresh;

    /// <summary>A touch is told again at most this often for one pair (half a second).</summary>
    public const int BumpEveryTicks = 33;

    /// <summary>
    /// Two hulls touch: their 48 u boxes meet across (two half widths, with 1 u for a step's rounding) and overlap in height.
    /// A body standing clear of another is no touch (review 2, G6: a 52 u box called a Spy 4 u away bumped).
    /// </summary>
    public const float TouchAcross = 49f, TouchHeight = 72f;

    /// <summary>Touches told: two bodies a solid pair, in contact.</summary>
    public int Bumps { get; private set; }

    /// <summary>Whether bodies standing at <paramref name="a"/> and <paramref name="b"/> touch (their hulls meet).</summary>
    public static bool Touching(AiVector a, AiVector b)
        => MathF.Abs(a.X - b.X) <= TouchAcross && MathF.Abs(a.Y - b.Y) <= TouchAcross && MathF.Abs(a.Z - b.Z) < TouchHeight;

    /// <summary>The actor bot <paramref name="botSlot"/> was told of for the player in <paramref name="slot"/>, while it holds them in sight; none otherwise.</summary>
    public ActorId HeldFor(int botSlot, int slot)
        => (uint)botSlot < VisionPass.Slots && (uint)slot < VisionPass.Slots ? _held[Pair(botSlot, slot)] : ActorId.None;

    /// <summary>
    /// For each bot, each player it holds in sight whose drawing changed since it was told of them: a
    /// <see cref="AiEventKind.SightLost"/> of the old actor and a <see cref="AiEventKind.SightGained"/> of the new one, into
    /// <paramref name="changes"/>, already as told (not to be passed through <see cref="Told"/>). Runs after the vision and
    /// before its events are told; a pair the vision gained or lost this tick is not among them.
    /// </summary>
    public void Refresh(Tick tick, ReadOnlySpan<SensedBot> bots, VisionPass vision, List<AiEvent> changes)
    {
        // A new level starts its tick count again (AiSenses.OnLevelInitPostEntity): a stamp of the last level's would hold a
        // pair's next touch back for as long as that level ran, and a held actor is the last level's (review 2, G1).
        if (tick < _lastRefresh)
        {
            Array.Clear(_bumpAt);
            Array.Clear(_held);
        }

        _lastRefresh = tick;
        foreach (ref readonly SensedBot b in bots)
        {
            if (vision.SightOf(b.Id.Slot) is not { } sight)
                continue;
            for (int t = 1; t < VisionPass.Slots; t++)
            {
                int pair = Pair(b.Id.Slot, t);
                ActorId held = _held[pair];
                if (!held.IsValid || t == b.Id.Slot || !sight.Sees(t) || vision.ReadOf(t) is not { } them)
                    continue;
                if (vision.ReadOf(b.Id.Slot) is { } me && Touching(me.Origin, them.Origin) && tick >= _bumpAt[pair] && game.View.Solid(b.Player, them.Player))
                {
                    changes.Add(AiEvent.Bumped(b.Id, tick, held, them.Origin));
                    _bumpAt[pair] = tick + BumpEveryTicks;
                    Bumps++;
                }

                ActorId now = game.View.ShownAs(b.Player, them.Player, sight.TargetOf(t));
                if (now == held)
                    continue;
                changes.Add(AiEvent.SightLost(b.Id, tick, held, them.Origin, them.Velocity));
                changes.Add(AiEvent.SightGained(b.Id, tick, now, them.Origin, them.Velocity, sight.FractionOf(t)));
                _held[pair] = now;
                Changes++;
            }
        }
    }

    /// <summary>
    /// <paramref name="e"/> as its bot is told it: an event about a player perceived (a sighting, a shot or a reload seen,
    /// a sound, the bot's own hit) names the actor the bot takes them for. <paramref name="playerOf"/> answers a slot's
    /// player (the bot's own among them), none when gone.
    /// </summary>
    public AiEvent Told(in AiEvent e, Func<int, EntityHandle> playerOf)
    {
        if (e.Kind is not (AiEventKind.SightGained or AiEventKind.SightLost or AiEventKind.SeenFire or AiEventKind.SeenReload
                           or AiEventKind.SoundHeard or AiEventKind.DealtDamage or AiEventKind.Damaged)
            || !e.Other.IsValid || (uint)e.Bot.Slot >= VisionPass.Slots || (uint)e.Other.Slot >= VisionPass.Slots)
            return e;
        int pair = Pair(e.Bot.Slot, e.Other.Slot);
        ActorId held = _held[pair];
        switch (e.Kind)
        {
            case AiEventKind.SightGained:
                _held[pair] = Live(e, playerOf);
                return e with { Other = _held[pair] };
            case AiEventKind.SightLost:
                _held[pair] = ActorId.None;
                return held.IsValid ? e with { Other = held } : e;
            default:
                return e with { Other = held.IsValid ? held : Live(e, playerOf) };
        }
    }

    private ActorId Live(in AiEvent e, Func<int, EntityHandle> playerOf)
    {
        EntityHandle viewer = playerOf(e.Bot.Slot), them = playerOf(e.Other.Slot);
        return viewer == EntityHandle.None || them == EntityHandle.None ? e.Other : game.View.ShownAs(viewer, them, e.Other);
    }

    /// <summary>A bot left: what it held goes.</summary>
    public void Forget(int botSlot)
    {
        if ((uint)botSlot < VisionPass.Slots)
            Array.Clear(_held, Pair(botSlot, 0), VisionPass.Slots);
    }
}

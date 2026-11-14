using SourceSharp.Ai;

namespace TF2.Descent.Ai.Brains;

/// <summary>
/// The horde session's slots (plan_descent_logic 3.6, the conventions the core
/// imposes): slot 0 means nobody in the core's facts, so a session has
/// <see cref="Slots"/>; bodies are brains at their pool index, 1 to
/// <see cref="LastBody"/> (48 bodies and the boss's); heroes are actors at
/// <see cref="FirstHero"/> to <see cref="FirstHero"/> + <see cref="Heroes"/> - 1.
/// </summary>
public static class HordeSlots
{
    public const int Slots = 54;

    /// <summary>The highest body slot.</summary>
    public const int LastBody = 49;

    public const int FirstHero = 50;

    /// <summary>Hero actors a session holds at once.</summary>
    public const int Heroes = 4;

    /// <summary>Actors a robot's view holds at once: every hero it may see, and its nearest pack-mates (<see cref="HordeView"/>).</summary>
    public const int Sighted = Heroes + HordeView.PackMates;

    public static bool IsHero(int slot) => slot >= FirstHero && slot < FirstHero + Heroes;
}

/// <summary>
/// Which hero seat holds which of the session's hero slots (plan_descent_logic
/// 3.6). The horde's seat is the hero's player entity index (1 to the player
/// count, <c>HordeHeroes</c>). A seat seen for the first time takes the lowest
/// free slot, under a new serial, so the actor id of a slot another seat held
/// before is never this one's. A seat keeps its slot while it is away (dead,
/// respawning) until another seat needs the slot: then the seat away longest,
/// and away at least <see cref="AwayTicks"/>, gives it up. A fifth hero while
/// four are present and seated holds no slot: no robot sees or hears it as an
/// actor (counted in <see cref="Unseated"/>) until a slot frees.
/// </summary>
public sealed class HeroSlots
{
    private readonly int[] _seat = new int[HordeSlots.Heroes];          // by hero index; 0 none
    private readonly int[] _serial = new int[HordeSlots.Heroes];
    private readonly long[] _lastPresent = new long[HordeSlots.Heroes];

    public HeroSlots(long awayTicks) => AwayTicks = awayTicks;

    /// <summary>How long a seat must have been away before a new seat may take its slot.</summary>
    public long AwayTicks { get; }

    /// <summary>Times a present hero found no slot.</summary>
    public long Unseated { get; private set; }

    /// <summary>
    /// The actor for <paramref name="seat"/> present at <paramref name="now"/>, taking a
    /// slot for it when it holds none; <see cref="ActorId.None"/> when none is free.
    /// </summary>
    public ActorId Present(int seat, long now)
    {
        if (seat <= 0)
            throw new ArgumentOutOfRangeException(nameof(seat), seat, "a hero seat is a player index, 1 or more");

        int free = -1, oldest = -1;
        for (int h = 0; h < _seat.Length; h++)
        {
            if (_seat[h] == seat)
            {
                _lastPresent[h] = now;
                return Actor(h);
            }

            if (_seat[h] == 0 && free < 0)
                free = h;
            else if (_seat[h] != 0 && now - _lastPresent[h] >= AwayTicks && (oldest < 0 || _lastPresent[h] < _lastPresent[oldest]))
                oldest = h;
        }

        int take = free >= 0 ? free : oldest;
        if (take < 0)
        {
            Unseated++;
            return ActorId.None;
        }

        _seat[take] = seat;
        _serial[take]++;
        _lastPresent[take] = now;
        return Actor(take);
    }

    /// <summary>The actor <paramref name="seat"/> holds now, or <see cref="ActorId.None"/>.</summary>
    public ActorId ActorOf(int seat)
    {
        for (int h = 0; h < _seat.Length; h++)
            if (seat > 0 && _seat[h] == seat)
                return Actor(h);
        return ActorId.None;
    }

    /// <summary>The seat whose actor is <paramref name="actor"/>, or -1.</summary>
    public int SeatOf(ActorId actor)
    {
        int h = actor.Slot - HordeSlots.FirstHero;
        return (uint)h < (uint)_seat.Length && _seat[h] != 0 && _serial[h] == actor.Serial ? _seat[h] : -1;
    }

    /// <summary>The seat in hero index <paramref name="h"/> (0 for none).</summary>
    public int SeatAt(int h) => _seat[h];

    /// <summary>The actor in hero index <paramref name="h"/>, or none.</summary>
    public ActorId ActorAt(int h) => _seat[h] != 0 ? Actor(h) : ActorId.None;

    private ActorId Actor(int h) => new(HordeSlots.FirstHero + h, _serial[h]);
}

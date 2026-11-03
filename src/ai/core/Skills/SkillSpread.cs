using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>
/// A bot's own allocation, drawn from a seed (plan_hl2mp_ai §10; user rulings
/// 2026-11-02): the given levels are the mean, and a seeded walk moves single
/// points between skills, spending exactly the same total, within the cap,
/// each skill keeping the floor. Two favourites are drawn first; the first
/// moves lift the first favourite to <see cref="Standout"/> above the bot's
/// even spread and most of the rest feed them, so a bot has a real best trait
/// for its name and its play.
/// The same draw makes a preset bot's levels when it joins the roster and
/// re-spreads a bot when <c>ai_bot_reroll_on_map</c> gives it a new seed.
/// Pure: the same seed gives the same allocation on every runtime.
/// </summary>
public static class SkillSpread
{
    /// <summary>A skill's least level in a draw, for a total that can pay it (a bot useless at something reads as a bug, not a style).</summary>
    public const int Floor = 1;

    /// <summary>One move per this many points of the total: Easy (20) 4, Medium (35) 7, Hard (50) 10, Expert (60) 12.</summary>
    public const int PointsPerMove = 5;

    /// <summary>Of each 4 moves after the lift, how many feed the first favourite and the second; the rest feed any skill.</summary>
    public const int FirstFavourite = 1, SecondFavourite = 1, FavouriteOutOf = 4;

    /// <summary>How far above its even spread a bot's first favourite stands (the name generator reads it).</summary>
    public const int Standout = 2;

    /// <summary>The walk's length for a total.</summary>
    public static int Moves(int total) => (total + PointsPerMove / 2) / PointsPerMove;

    /// <summary>The floor a total can pay over <paramref name="skills"/> skills.</summary>
    public static int FloorFor(int total, int skills) => total >= skills * Floor ? Floor : 0;

    /// <summary><paramref name="mean"/>'s total spread anew from <paramref name="seed"/>, under <paramref name="name"/>.</summary>
    public static SkillProfile Draw(SkillProfile mean, ulong seed, string? name = null, int cap = StandardSkills.Cap)
    {
        ArgumentNullException.ThrowIfNull(mean);
        int[] levels = [.. mean.Levels];
        int total = levels.Sum();
        int floor = FloorFor(total, levels.Length);
        var random = new AiRandom(seed);
        Repair(levels, floor);

        int first = random.NextInt(levels.Length);
        int second = (first + 1 + random.NextInt(levels.Length - 1)) % levels.Length;
        int lift = Math.Min(cap, (int)MathF.Ceiling(total / (float)levels.Length) + Standout);
        int moves = Moves(total);
        for (int m = 0; m < moves; m++)
        {
            int to = levels[first] < lift ? first : Recipient(random, levels, first, second, cap);
            int from = Donor(random, levels, to, first, second, floor);
            if (to < 0 || from < 0)
                continue;
            levels[to]++;
            levels[from]--;
        }

        return new SkillProfile(name ?? mean.Name, mean.Skills, [.. levels]);
    }

    /// <summary>A custom allocation may hold a skill under the floor: the highest skills pay it up.</summary>
    private static void Repair(int[] levels, int floor)
    {
        for (int i = 0; i < levels.Length; i++)
        {
            while (levels[i] < floor)
            {
                int highest = 0;
                for (int j = 1; j < levels.Length; j++)
                {
                    if (levels[j] > levels[highest])
                        highest = j;
                }

                levels[highest]--;
                levels[i]++;
            }
        }
    }

    private static int Recipient(AiRandom random, int[] levels, int first, int second, int cap)
    {
        int roll = random.NextInt(FavouriteOutOf);
        int to = roll < FirstFavourite ? first : roll < FirstFavourite + SecondFavourite ? second : random.NextInt(levels.Length);
        return levels[to] < cap ? to : Any(random, levels, -1, i => levels[i] < cap);
    }

    /// <summary>A skill to take a point from: not a favourite, unless only the favourites have one to give.</summary>
    private static int Donor(AiRandom random, int[] levels, int to, int first, int second, int floor)
    {
        int from = Any(random, levels, to, i => i != first && i != second && levels[i] > floor);
        return from >= 0 ? from : Any(random, levels, to, i => levels[i] > floor);
    }

    /// <summary>A skill other than <paramref name="except"/> that <paramref name="fits"/>, from a random place; -1 for none.</summary>
    private static int Any(AiRandom random, int[] levels, int except, Func<int, bool> fits)
    {
        int start = random.NextInt(levels.Length);
        for (int k = 0; k < levels.Length; k++)
        {
            int i = (start + k) % levels.Length;
            if (i != except && fits(i))
                return i;
        }

        return -1;
    }
}

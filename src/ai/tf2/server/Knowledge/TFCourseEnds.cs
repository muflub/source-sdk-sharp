using System.Collections.Immutable;
using SourceSharp.Ai.Server.Nav;
using TF2.Server.Entities;

namespace SourceSharp.Ai.TF2.Knowledge;

/// <summary>
/// One end of a jump map's course (lane Ge): a capture area whose point the level does not place, as
/// <c>jump_academy2_rc9a</c>'s 19 are, each titled with its course's name. In stock such an area captures nothing,
/// so an end is reached, not taken: a body stands in its volume. <see cref="Rank"/> is how hard its title says the
/// course is (<see cref="TFCourseEnds.RankOf"/>).
/// </summary>
public readonly record struct TFCourseEnd(int Index, string Title, int Rank, AiVector Mins, AiVector Maxs)
{
    /// <summary>The middle of the volume: where the bot goes for it, as a capture zone's place is its volume's middle.</summary>
    public AiVector Middle => new((Mins.X + Maxs.X) * 0.5f, (Mins.Y + Maxs.Y) * 0.5f, (Mins.Z + Maxs.Z) * 0.5f);

    /// <summary>Whether a body standing with its feet at <paramref name="feet"/> touches the volume: its box overlaps it, as a trigger's touch is.</summary>
    public bool Holds(AiVector feet)
        => feet.X + TFCourseEnds.BodyHalfWidth >= Mins.X && feet.X - TFCourseEnds.BodyHalfWidth <= Maxs.X
           && feet.Y + TFCourseEnds.BodyHalfWidth >= Mins.Y && feet.Y - TFCourseEnds.BodyHalfWidth <= Maxs.Y
           && feet.Z + TFCourseEnds.BodyHeight >= Mins.Z && feet.Z <= Maxs.Z;
}

/// <summary>
/// The course ends a level places (lane Ge), from its placements as the bots know them
/// (<see cref="ITFLevelEntities"/>): every <c>trigger_capture_area</c> whose <c>area_cap_point</c> names no
/// <c>team_control_point</c> of the level, compared as the game finds an entity by name (case aside). An area with no
/// point named at all is no course's end. Pure.
/// </summary>
public static class TFCourseEnds
{
    /// <summary>The key that names an area's point.</summary>
    public const string PointKey = "area_cap_point";

    /// <summary>A TF2 player's box: 48 u across, 82 u tall standing.</summary>
    public const float BodyHalfWidth = 24f, BodyHeight = 82f;

    /// <summary>The rank of a title with no word of how hard it is: a medium course.</summary>
    public const int UnknownRank = 2;

    /// <summary>The ends among <paramref name="placed"/>, in the order the level places them, each numbered by its place in the list.</summary>
    public static ImmutableArray<TFCourseEnd> Of(MapEntities placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        var points = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MapEntity e in placed.All)
        {
            if (e.ClassName == TeamControlPoint.MapClass && e.Name is { Length: > 0 } name)
                points.Add(name);
        }

        var ends = ImmutableArray.CreateBuilder<TFCourseEnd>();
        foreach (MapEntity e in placed.All)
        {
            if (e.ClassName != TriggerAreaCapture.MapClass || e.Get(PointKey) is not { Length: > 0 } title || points.Contains(title)
                || e.Mins is not { } mins || e.Maxs is not { } maxs)
                continue;
            ends.Add(new TFCourseEnd(ends.Count, title, RankOf(title), mins, maxs));
        }

        return ends.ToImmutable();
    }

    /// <summary>
    /// How hard a course's title says it is, as a person reads the sign at its door: basic 0, easy 1, medium 2, hard 3,
    /// expert or insane 4, by the first such word; <see cref="UnknownRank"/> for a title with none.
    /// </summary>
    public static int RankOf(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        foreach (string word in title.Split([' ', '-', '_', '/', '(', ')', '.', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            switch (word.ToUpperInvariant())
            {
                case "BASIC" or "BEGINNER":
                    return 0;
                case "EASY":
                    return 1;
                case "MEDIUM" or "INTERMEDIATE":
                    return 2;
                case "HARD" or "ADVANCED":
                    return 3;
                case "EXPERT" or "INSANE":
                    return 4;
            }
        }

        return UnknownRank;
    }
}

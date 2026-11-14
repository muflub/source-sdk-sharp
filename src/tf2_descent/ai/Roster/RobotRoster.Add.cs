using System.Collections.Immutable;
using TF2.Descent.Ai.Level;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Ai.Roster;

public sealed partial class RobotRoster
{
    /// <summary>
    /// Puts one robot on the roster after the roll, as its own pack of one, in
    /// <paramref name="room"/> at <paramref name="at"/> (a dev command's robot,
    /// HO2): <paramref name="template"/> at <paramref name="scale"/> (the
    /// template's rolled scale when null), the level's monster level, the
    /// champion <paramref name="affixes"/> given (none by default), its stats and temperament rolled by the rules from
    /// <paramref name="seed"/>. It starts far, at full life, with a full clip
    /// and calm memory, so the embodiment rules may lend it a body. Answers
    /// none when the roster is full. Allocates the row's own values only.
    /// With <paramref name="joinLastPack"/> it joins the newest pack instead of
    /// starting its own (a dev command's pack-mate, a fact's pack): only the
    /// newest, so a pack's rows stay one run; with no pack yet it starts one.
    /// With <paramref name="boss"/> (HO13: a dev boss on a level with no arena) the row is a boss:
    /// it takes only the boss body, as the level's boss does.
    /// </summary>
    public RobotId Add(string template, double? scale, int room, PackMarker at, ulong seed, int partySize = 1,
                       IReadOnlyList<string>? affixes = null, bool joinLastPack = false, bool boss = false)
    {
        var level = Level ?? throw new InvalidOperationException("the roster has no level: Load it first");
        if ((uint)room >= (uint)level.RoomCount)
            throw new ArgumentOutOfRangeException(nameof(room), room, $"the level has {level.RoomCount} rooms");
        if (Count == Capacity)
            return RobotId.None;

        var t = _rules.Robots.Template(template);
        double s = scale ?? RobotRules.RollScale(t, seed, 0);
        int monsterLevel = _rules.Robots.MonsterLevel(level.Depth);
        ImmutableArray<string> champion = affixes is null ? [] : [.. affixes];
        var stats = _rules.Robots.Stats(t, s, monsterLevel, champion, partySize);
        var temperament = _rules.Temperaments.Roll(seed, 0, t, s);
        int p = joinLastPack && PackCount > 0 && _packFirst[PackCount - 1] + _packSize[PackCount - 1] == Count
            ? PackCount - 1
            : BeginPack(room, seed);
        Put(p, t.Id, Seeded.From(seed, "robot", 0).NextULong(), monsterLevel, s, stats, temperament, champion, boss, room, -1, at);
        int i = Count - 1;
        _state[i] = RobotState.Far;
        return Id(i);
    }
}

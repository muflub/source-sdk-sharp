using SourceSharp.Ai;

namespace TF2.Descent.Ai.Level;

/// <summary>
/// The hunters' leash (plan_descent_logic L25; the user's ruling of 2026-11-22: "a hunting robot
/// gives up once it's a few doors from its own room"). A robot's home is the room its pack was
/// placed in. A robot hunting a hero stops when the room it would go to next, or the room its
/// quarry is in, is more than <see cref="Doors"/> doors from its home on the room graph
/// (<see cref="HordeLevel.Distance"/>); a quarry in its home room never stops it. The far
/// simulation asks it of a pack's contact and of each far robot's next room; a brain's hunt asks it
/// of the hero in sight (out of its engage range: a robot in a fight finishes it) and of each place
/// it would go and look. A room not known (-1: a place in no room) is not asked.
/// <para><see cref="Doors"/> below 1 is no leash (<c>default</c>): a boss, which holds its arena,
/// and deciders made without the level's rules.</para>
/// </summary>
public readonly record struct HordeLeash(int Doors)
{
    /// <summary>Whether the leash holds a robot back at all.</summary>
    public bool Binds => Doors >= 1;

    /// <summary>
    /// Whether a robot whose home is room <paramref name="home"/> stops hunting a quarry in room
    /// <paramref name="quarry"/> when the room it would go to next is <paramref name="next"/>.
    /// </summary>
    public bool Past(HordeLevel level, int home, int next, int quarry)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (!Binds || (uint)home >= (uint)level.RoomCount || quarry == home)
            return false;
        return Beyond(level, home, quarry) || Beyond(level, home, next);
    }

    /// <summary>
    /// <see cref="Past(HordeLevel, int, int, int)"/> for a robot at <paramref name="from"/> going to
    /// <paramref name="to"/>, the rooms by place: its next room is the room graph's next towards the
    /// quarry's room, the quarry's own when it stands in it.
    /// </summary>
    public bool Past(HordeLevel level, int home, AiVector from, AiVector to)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (!Binds)
            return false;
        int quarry = level.RoomAt(to), self = level.RoomAt(from);
        return Past(level, home, NextOf(level, self, quarry), quarry);
    }

    /// <summary>
    /// Whether a robot that sees its nearest hero at <paramref name="hero"/> lets him go: he is out of
    /// its <paramref name="engage"/> range (a robot fighting a hero within it is never pulled away)
    /// and past its leash.
    /// </summary>
    public bool LetsGo(HordeLevel level, int home, AiVector self, AiVector hero, float engage) =>
        Binds && self.DistanceTo(hero) > engage && Past(level, home, self, hero);

    /// <summary>The room a walk from <paramref name="room"/> to <paramref name="quarry"/> enters next: the quarry's own when it is there; -1 when either is not known.</summary>
    public static int NextOf(HordeLevel level, int room, int quarry)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (room < 0 || quarry < 0)
            return -1;
        return room == quarry ? quarry : level.NextRoom(room, quarry);
    }

    private bool Beyond(HordeLevel level, int home, int room) => (uint)room < (uint)level.RoomCount && level.Distance(home, room) > Doors;
}

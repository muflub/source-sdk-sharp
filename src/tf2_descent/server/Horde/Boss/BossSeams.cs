using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Shared;

namespace TF2.Descent.Server.Horde.Boss;

/// <summary>
/// THE SEAM WHERE A ROBOT'S ABILITY GOES WHEN THE HANDS HAVE NO VERB FOR IT (HO13): the horde's
/// hands (<see cref="Attacks.RobotAttacks"/>) carry out fire, the medic's heal and the spy's
/// cloak; any other ability a brain asks (a boss's special and adds, the Tank's stations) is
/// handed to each member in its order until one takes it. A member answers true when the ask was
/// its own, whether or not it could carry it out (a refusal is its to count). The hands it is
/// handed launch projectiles counted against the robots' cap.
/// </summary>
public interface IRobotAbility
{
    bool Act(HordeController horde, RobotBody body, int row, in ActionIntent action, IRobotAbilityHands hands, long now);
}

/// <summary>What an ability may ask of the horde's hands (HO13).</summary>
public interface IRobotAbilityHands
{
    /// <summary>A TF projectile in <paramref name="body"/>'s name on the robots' team, counted against the robot projectile cap; none when the cap is full or the engine refused it.</summary>
    EntityHandle Launch(RobotBody body, string projectile, Vector origin, Vector velocity, float damage, bool crit);
}

/// <summary>
/// THE SEAM WHERE A BOSS CALLS ITS ADDS (HO13): <paramref name="count"/> robots of
/// <paramref name="template"/> put on the roster in the arena of the boss in row
/// <paramref name="bossRow"/> and lent bodies where the pool has them; answers how many went on
/// the roster (fewer when the roster's 256 rows or the boss's share of adds run out).
/// </summary>
public interface IBossAdds
{
    int Summon(HordeController horde, int bossRow, string template, int count, ulong seed, long now);
}

/// <summary>
/// THE SEAM THAT HOLDS A BOSS LEVEL'S WAY DOWN (mine plan D6; HO13): closed while the level's
/// boss stands, open once it is dead. <see cref="MayLeave"/> answers for a hero asking to go down
/// and, when closed, tells him why.
/// </summary>
public interface IBossExit
{
    /// <summary>Whether the way down is closed now: the level has a boss and it is not dead.</summary>
    bool Closed { get; }

    bool MayLeave(int player);
}

/// <summary>
/// What a boss fight asks of the engine (HO13): a notice to a hero, a sound on an entity, a
/// particle, a hero's player entity, the boss bar to a hero's client, and a hit on a hero. The
/// facts answer their own.
/// </summary>
public interface IBossWorld
{
    void Notice(int player, string token, string arg);

    void Sound(EntityHandle entity, string sound);

    void Particle(string name, Vector at);

    EntityHandle Player(int slot);

    void Bar(int player, BossBar bar);

    /// <summary>Deals <paramref name="damage"/> to hero <paramref name="victim"/> in <paramref name="attacker"/>'s name; answers what it took.</summary>
    int Hurt(EntityHandle victim, EntityHandle attacker, float damage, DamageType bits, Vector direction);

    /// <summary>Whether a line from <paramref name="from"/> to <paramref name="to"/> reaches <paramref name="target"/> (or its end) clear of the world, past <paramref name="self"/>.</summary>
    bool Clear(Vector from, Vector to, EntityHandle self, EntityHandle target);
}

/// <summary>The engine's answers to <see cref="IBossWorld"/>.</summary>
[SideService(Side.Server, As = [typeof(IBossWorld)])]
public sealed class EngineBossWorld(ClientMessages messages, Attacks.IRobotAttackWorld world, IEnginePlayers players, IUserMessageSender sender)
    : IBossWorld
{
    public void Notice(int player, string token, string arg) => messages.Notice(player, token, arg);

    public void Sound(EntityHandle entity, string sound) => world.Sound(entity, sound);

    public void Particle(string name, Vector at) => world.Particle(name, at);

    public EntityHandle Player(int slot) => players.GetHandle(slot);

    public void Bar(int player, BossBar bar) => sender.Send(bar, player);

    public int Hurt(EntityHandle victim, EntityHandle attacker, float damage, DamageType bits, Vector direction) =>
        world.Hurt(victim, attacker, damage, bits, direction);

    public bool Clear(Vector from, Vector to, EntityHandle self, EntityHandle target)
    {
        var t = world.Line(from, to, self);
        return !t.Hit || t.Entity == target || t.Fraction >= 0.97f;
    }
}

/// <summary>The dispatch of HO13's ability chain.</summary>
[SideService(Side.Server)]
public sealed class BossRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<IRobotAbility>();
}

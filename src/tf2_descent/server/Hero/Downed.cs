using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Death;
using TF2.Descent.Server.Mode;
using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Descent.Server.Hero;

/// <summary>The engine work the downed state does (WO6), behind one interface so each rule has a fact over a fake world.</summary>
public interface IDownedWorld
{
    /// <summary>The server tick now.</summary>
    int Tick { get; }

    /// <summary>The engine's ticks a second: what the rules' seconds are measured in.</summary>
    double TicksPerSecond { get; }

    bool IsAlive(EntityHandle player);

    Vector Origin(EntityHandle player);

    int Health(EntityHandle player);

    /// <summary>The player's TF maximum health (the hero's maximum life, HE3).</summary>
    int MaxHealth(EntityHandle player);

    void SetHealth(EntityHandle player, int health);

    /// <summary>The player holds +use now.</summary>
    bool HoldingUse(EntityHandle player);

    /// <summary>
    /// The downed hero cannot move, jump, duck or fire for <paramref name="seconds"/>:
    /// TF's controls stun, which the movement rules read on both sides (the
    /// client predicts the hold), with its stunned pose.
    /// </summary>
    void Freeze(EntityHandle player, float seconds);

    /// <summary>The freeze lifted (a revive): TF's stun condition removed.</summary>
    void Unfreeze(EntityHandle player);

    /// <summary>Kills the player outright, as the self-kill command does (the downed state lets its own kill through).</summary>
    void Kill(EntityHandle player);
}

/// <summary>The engine's <see cref="IDownedWorld"/>: the globals' clock, the player's health, TF's stun and the self-kill's damage.</summary>
[SideService(Side.Server, As = [typeof(IDownedWorld)])]
public sealed class EngineDownedWorld(IEngineEnvironment environment, IEngineEntities entities, IEnginePlayers players,
                                      IPlayerButtons buttons, ITFPlayerResupply resupply, ITFPlayerConditions conditions,
                                      TFPlayerCarriers carriers, IEngineDamage damage) : IDownedWorld
{
    public int Tick => environment.Globals is { IsAvailable: true } g ? g.TickCount : 0;

    public double TicksPerSecond =>
        environment.Globals is { IsAvailable: true, IntervalPerTick: > 0f } g ? 1.0 / g.IntervalPerTick : 66.666;

    public bool IsAlive(EntityHandle player) => players.IsAlive(player);

    public Vector Origin(EntityHandle player) => entities.GetOrigin(player);

    public int Health(EntityHandle player) => entities.GetHealth(player);

    public int MaxHealth(EntityHandle player) => resupply.MaxHealth(player);

    public void SetHealth(EntityHandle player, int health) => entities.SetHealth(player, health);

    public bool HoldingUse(EntityHandle player) => (buttons.Held(player) & GameMovement.InUse) != 0;

    public void Freeze(EntityHandle player, float seconds) =>
        carriers.Part<TFStunStack>(player.EntryIndex)?.Stun(player, seconds, 1f, TFStunFlags.Controls, EntityHandle.None);

    public void Unfreeze(EntityHandle player) => conditions.Remove(player, TFCond.Stunned);

    public void Kill(EntityHandle player) =>
        damage.Apply(player, EntityHandle.None, PlayerSelfCommand.SuicideDamage, DamageType.Generic);
}

/// <summary>Told when a hero goes down or gets up (WO6): the horde's brains (B.3's <c>HeroDowned</c>), and any later listener.</summary>
public interface IHeroDownedListener
{
    /// <summary>The hero in <paramref name="playerIndex"/> went down (<paramref name="down"/>) or got up, at <paramref name="at"/>.</summary>
    void HeroDowned(int playerIndex, bool down, Vector at);
}

/// <summary>What a lethal hit on a hero comes to (<see cref="HeroDowned.Lethal"/>).</summary>
public enum LethalAnswer
{
    /// <summary>Not the downed state's: the hit lands as dealt (no mode, no hero).</summary>
    Land,
    /// <summary>The hero goes down: the hit leaves him at 1 life.</summary>
    Down,
    /// <summary>The hero dies of it: nobody else is standing (solo, or the last one up).</summary>
    Die,
    /// <summary>The hero is in a revive's grace, or already down: the hit leaves him alive.</summary>
    Spare,
}

/// <summary>
/// THE DOWNED STATE (plan_descent_logic WO6; plan_tf2_rpg 2g, R26): the
/// rules' machine (<see cref="DownedRules"/>) carried out in the game. Every
/// hero on the level is a member of one party (Descent has no parties yet,
/// WO9: one level is one party's instance, R7).
///
/// <list type="bullet">
/// <item>A LETHAL HIT (<see cref="DownedDamage"/>) puts a hero down when
/// another member stands: he keeps 1 life, cannot move, fire, cast or drink
/// (<see cref="IDownedWorld.Freeze"/>, and each action's own refusal), takes no
/// damage, and bleeds out over the rules' window. Solo, or the last one up,
/// he dies of it.</item>
/// <item>A REVIVE: a member up and in range who holds +use on him for the
/// rules' time (not hurt, not moving out of range, not letting go) stands him
/// up at the rules' share of his life, with the rules' grace.</item>
/// <item>THE BLEED-OUT, and the PARTY WIPE (the last one up goes down or
/// leaves): the downed die, killed outright here
/// (<see cref="IDownedWorld.Kill"/>). What a death costs is
/// <see cref="HeroDeathCosts"/>', whatever killed him.</item>
/// <item>Each change is on the hero's state, to every client
/// (<see cref="HeroComponent.ShowDowned"/>, <see cref="HeroComponent.ShowRevive"/>),
/// and told to the <see cref="IHeroDownedListener"/>s.</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroDowned(IDescentModeState mode, Heroes heroes, TF2DescentRules rules, IDownedWorld world,
                               IHooks<IHeroDownedListener> listeners, ILog log) : IGameLifecycle
{
    /// <summary>In TF2 Descent's downed block.</summary>
    public const int Order = ModStages.First + 950;

    public int LifecycleOrder => Order;

    private DownedParty? _party;
    private readonly HashSet<int> _killing = [];
    private readonly HashSet<int> _devHolds = [];

    /// <summary>The party as the machine holds it now.</summary>
    public DownedParty Party => _party ??= DownedRules.NewParty(world.TicksPerSecond);

    /// <summary>The player index a member id names.</summary>
    public static string Id(int playerIndex) => playerIndex.ToString(CultureInfo.InvariantCulture);

    private static int IndexOf(string id) => int.Parse(id, CultureInfo.InvariantCulture);

    /// <summary>The member of <paramref name="playerIndex"/>, or null when the machine has never seen him.</summary>
    public DownedMember? MemberOf(int playerIndex) => Party.Members.FirstOrDefault(m => m.Id == Id(playerIndex));

    /// <summary>Whether the hero in <paramref name="playerIndex"/> is down.</summary>
    public bool IsDown(int playerIndex) => MemberOf(playerIndex)?.Standing == Standing.Down;

    /// <summary>Whether this service is killing the player now (its own kill passes the damage layer's guards).</summary>
    public bool Killing(int playerIndex) => _killing.Contains(playerIndex);

    /// <summary>Downs told to the listeners since the module started, and ups.</summary>
    public int DownsTold { get; private set; }

    public int UpsTold { get; private set; }

    //-------------------------------------------------------------------------
    // What the damage layer asks
    //-------------------------------------------------------------------------

    /// <summary>
    /// A hit that would kill the hero in <paramref name="playerIndex"/>: the
    /// rules decide whether he goes down, dies, or is spared (his grace, or
    /// already down). A down is carried out here (the state, the freeze, the
    /// listeners); a death is the hit's own to deal.
    /// </summary>
    public LethalAnswer Lethal(int playerIndex)
    {
        if (!mode.Active || heroes.Of(playerIndex) is null)
            return LethalAnswer.Land;
        JoinAll();
        var outcomes = Step(new DownedEvent.LethalDamage(Id(playerIndex)), dying: playerIndex);
        if (outcomes.Any(o => o is DownedOutcome.Downed d && d.Member == Id(playerIndex)))
            return LethalAnswer.Down;
        if (outcomes.Any(o => o is DownedOutcome.Died d && d.Member == Id(playerIndex)))
            return LethalAnswer.Die;
        return LethalAnswer.Spare;
    }

    /// <summary>A hit that does not kill <paramref name="playerIndex"/>: a revive he is making is broken.</summary>
    public void Hurt(int playerIndex)
    {
        if (mode.Active && MemberOf(playerIndex) is { Reviving: not null })
            Step(new DownedEvent.Damaged(Id(playerIndex)));
    }

    /// <summary>
    /// The hero in <paramref name="playerIndex"/> died (the death hook): a death
    /// the machine did not decide (a suicide, a kill outright) makes him dead
    /// there too, and the party falls if nobody is left up.
    /// </summary>
    public void Died(int playerIndex)
    {
        if (!mode.Active || MemberOf(playerIndex) is not { } m || m.Standing == Standing.Dead)
            return;
        Step(new DownedEvent.Killed(Id(playerIndex)), dying: playerIndex);
    }

    //-------------------------------------------------------------------------
    // Each frame
    //-------------------------------------------------------------------------

    /// <summary>
    /// The frame's work: members join and leave with the heroes, the dead who
    /// stand again are up, timers run (revives complete, windows lapse), and
    /// each standing hero's +use starts, holds or lets go of a revive.
    /// </summary>
    public void Think()
    {
        if (!mode.Active)
            return;

        JoinAll();
        foreach (var gone in Party.Members.Where(m => m.InInstance && heroes.Of(IndexOf(m.Id)) is null).ToList())
            Step(new DownedEvent.Left(gone.Id));

        foreach (var dead in Party.Members.Where(m => m.Standing == Standing.Dead && m.InInstance).ToList())
            if (heroes.Of(IndexOf(dead.Id)) is { } hero && world.IsAlive(hero.Player))
                Step(new DownedEvent.Respawned(dead.Id));

        Step(new DownedEvent.Tick());

        foreach (var member in Party.Members.Where(m => m.Standing == Standing.Up && m.InInstance).ToList())
        {
            int index = IndexOf(member.Id);
            if (heroes.Of(index) is not { } hero || !world.IsAlive(hero.Player))
                continue;
            bool holding = _devHolds.Contains(index) || world.HoldingUse(hero.Player);
            var current = MemberOf(index)!;
            if (current.Reviving is { } target)
            {
                if (!holding)
                    Step(new DownedEvent.ReviveStop(member.Id));
                else if (!_devHolds.Contains(index) && heroes.Of(IndexOf(target)) is { } downed)
                    Step(new DownedEvent.ReviverMoved(member.Id, Distance(hero, downed)));
                continue;
            }
            if (!holding)
                continue;
            // The nearest downed member nobody is reviving, if he is in reach.
            var nearest = Party.Members
                .Where(m => m.Standing == Standing.Down && !Party.Members.Any(o => o.Reviving == m.Id))
                .Select(m => (Member: m, Hero: heroes.Of(IndexOf(m.Id))))
                .Where(p => p.Hero is not null)
                .Select(p => (p.Member, Distance: Distance(hero, p.Hero!)))
                .OrderBy(p => p.Distance)
                .FirstOrDefault();
            if (nearest.Member is { } down)
                Step(new DownedEvent.ReviveStart(member.Id, down.Id, nearest.Distance));   // the rules refuse one out of reach
        }
        _devHolds.RemoveWhere(i => MemberOf(i)?.Reviving is null);
    }

    private double Distance(HeroComponent a, HeroComponent b) => (world.Origin(a.Player) - world.Origin(b.Player)).Length;

    //-------------------------------------------------------------------------
    // Dev commands
    //-------------------------------------------------------------------------

    /// <summary>Puts the hero down as a lethal hit would (or kills him, solo): his life drops to 1 when he goes down.</summary>
    public LethalAnswer DevDown(int playerIndex)
    {
        if (heroes.Of(playerIndex) is not { } hero)
            return LethalAnswer.Land;
        JoinAll();
        var outcomes = Step(new DownedEvent.LethalDamage(Id(playerIndex)));
        if (outcomes.Any(o => o is DownedOutcome.Downed d && d.Member == Id(playerIndex)))
        {
            world.SetHealth(hero.Player, 1);
            return LethalAnswer.Down;
        }
        return outcomes.Any(o => o is DownedOutcome.Died) ? LethalAnswer.Die : LethalAnswer.Spare;
    }

    /// <summary>
    /// <paramref name="by"/> (or the nearest member up) starts a revive of the
    /// downed hero and holds it, as a held +use would, through the same
    /// machine: it completes after the rules' hold unless he is hurt. Answers
    /// the reviver, or why not.
    /// </summary>
    public string DevRevive(int playerIndex, int by)
    {
        if (!IsDown(playerIndex) || heroes.Of(playerIndex) is not { } downed)
            return $"player {playerIndex} is not down";
        int reviver = by > 0
            ? by
            : Party.Members.Where(m => m.Standing == Standing.Up && m.InInstance && heroes.Of(IndexOf(m.Id)) is not null)
                .Select(m => IndexOf(m.Id)).OrderBy(i => Distance(heroes.Of(i)!, downed)).FirstOrDefault();
        if (reviver <= 0 || heroes.Of(reviver) is null)
            return "nobody is up to revive";
        var outcomes = Step(new DownedEvent.ReviveStart(Id(reviver), Id(playerIndex), 0));
        if (outcomes.OfType<DownedOutcome.Refused>().FirstOrDefault() is { } refused)
            return $"refused: {refused.Why}";
        _devHolds.Add(reviver);
        return $"player {reviver} holds a revive of player {playerIndex}";
    }

    /// <summary>The downed hero's window lapses now: he bleeds out through the machine's own timer.</summary>
    public bool DevBleedOut(int playerIndex)
    {
        if (!IsDown(playerIndex))
            return false;
        long now = Math.Max(world.Tick, Party.Tick);
        _party = Party with
        {
            Members = [.. Party.Members.Select(m => m.Id == Id(playerIndex) ? m with { BleedOutTick = now } : m)],
        };
        Step(new DownedEvent.Tick());
        return true;
    }

    /// <summary>Every member's state, one line each, for <c>tf2_descent_downed</c>.</summary>
    public IEnumerable<string> Lines()
    {
        long now = Party.Tick;
        double rate = Party.TicksPerSecond;
        long hold = DownedRules.Ticks(Party, rules.Content.Death.Downed.ReviveHoldSeconds);
        foreach (var m in Party.Members)
        {
            string line = $"player {m.Id}: {m.Standing}{(m.InInstance ? "" : " (away)")}, downs this level {m.DownsThisLevel}";
            if (m.Standing == Standing.Down)
                line += string.Create(CultureInfo.InvariantCulture,
                    $", bleeds out at tick {m.BleedOutTick} ({Math.Max(0, m.BleedOutTick - now) / rate:0.0}s)");
            if (Party.Members.FirstOrDefault(o => o.Reviving == m.Id) is { } reviver)
                line += string.Create(CultureInfo.InvariantCulture,
                    $", revived by player {reviver.Id} {Math.Clamp((now - reviver.ReviveStartTick) / (double)hold, 0, 1):0%}");
            if (m.Reviving is { } target)
                line += $", reviving player {target}";
            if (DownedRules.InGrace(m, now))
                line += $", in grace to tick {m.GraceUntilTick}";
            yield return line;
        }
    }

    //-------------------------------------------------------------------------

    /// <summary>Every hero in the game is a member, in the instance.</summary>
    private void JoinAll()
    {
        foreach (var hero in heroes.All.ToList())
            Join(hero.PlayerIndex);
    }

    /// <summary>The hero in <paramref name="playerIndex"/> is a member, in the instance.</summary>
    private void Join(int playerIndex)
    {
        if (playerIndex <= 0)
            return;
        if (MemberOf(playerIndex) is { InInstance: true })
            return;
        Step(new DownedEvent.Joined(Id(playerIndex)));
    }

    /// <summary>
    /// One event through the machine at the world's tick, its outcomes carried
    /// out. <paramref name="dying"/> is a hero whose death the caller deals
    /// itself (the lethal hit, the death hook): he is not killed again.
    /// </summary>
    private IReadOnlyList<DownedOutcome> Step(DownedEvent e, int dying = -1)
    {
        long tick = world.Tick;
        if (tick < Party.Tick)
        {
            // The engine's tick went back (a new map the level hook missed): the party starts again.
            log.Warning($"[TF2Descent/Downed] the tick went back from {Party.Tick} to {tick}: a new party\n");
            _party = DownedRules.NewParty(world.TicksPerSecond);
        }
        var step = rules.Downed.Step(Party, e, tick);
        _party = step.Party;
        foreach (var outcome in step.Outcomes)
            Carry(outcome, (int)tick, dying);
        return step.Outcomes;
    }

    private void Carry(DownedOutcome outcome, int now, int dying)
    {
        switch (outcome)
        {
            case DownedOutcome.Downed d when heroes.Of(IndexOf(d.Member)) is { } hero:
            {
                hero.ShowDowned(true, now, (int)d.BleedOutTick);
                float seconds = (float)((d.BleedOutTick - now) / Party.TicksPerSecond) + 1f;
                world.Freeze(hero.Player, seconds);
                log.Msg(string.Create(CultureInfo.InvariantCulture,
                    $"[TF2Descent/Downed] player {d.Member} is DOWN at tick {now}: bleeds out at tick {d.BleedOutTick} ({seconds - 1f:0.0}s), down {MemberOf(hero.PlayerIndex)?.DownsThisLevel} on this level\n"));
                Tell(hero, true);
                break;
            }
            case DownedOutcome.Revived r when heroes.Of(IndexOf(r.Member)) is { } hero:
            {
                world.Unfreeze(hero.Player);
                int life = Math.Max(1, (int)Math.Round(world.MaxHealth(hero.Player) * r.LifePct / 100.0, MidpointRounding.AwayFromZero));
                world.SetHealth(hero.Player, life);
                hero.ShowDowned(false, 0, 0);
                log.Msg($"[TF2Descent/Downed] player {r.Member} REVIVED by player {r.By} at tick {now}: life {life} ({r.LifePct}% of {world.MaxHealth(hero.Player)}), grace to tick {r.GraceUntilTick}\n");
                Tell(hero, false);
                break;
            }
            case DownedOutcome.Died d:
            {
                int index = IndexOf(d.Member);
                var hero = heroes.Of(index);
                bool wasDown = hero?.Downed == true;
                if (hero is not null)
                {
                    hero.ShowDowned(false, 0, 0);
                    if (wasDown)
                        world.Unfreeze(hero.Player);
                }
                log.Msg($"[TF2Descent/Downed] player {d.Member} DIES ({d.Cause}) at tick {now}\n");
                // The machine's own deaths are dealt here; a lethal hit, a hook's death or a leave deal their own.
                if (index != dying && d.Cause != DeathCause.LeftWhileDown && hero is not null && world.IsAlive(hero.Player))
                {
                    _killing.Add(index);
                    try
                    {
                        world.Kill(hero.Player);
                    }
                    finally
                    {
                        _killing.Remove(index);
                    }
                }
                break;
            }
            case DownedOutcome.ReviveStarted s when heroes.Of(IndexOf(s.Target)) is { } target:
                target.ShowRevive(IndexOf(s.Reviver), now, (int)s.CompletesAtTick);
                log.Msg($"[TF2Descent/Downed] player {s.Reviver} starts reviving player {s.Target}: done at tick {s.CompletesAtTick}\n");
                break;
            case DownedOutcome.ReviveInterrupted i:
                heroes.Of(IndexOf(i.Target))?.ShowRevive(0, 0, 0);
                log.Msg($"[TF2Descent/Downed] player {i.Reviver}'s revive of player {i.Target} broke: {i.Why}\n");
                break;
        }
    }

    private void Tell(HeroComponent hero, bool down)
    {
        if (down)
            DownsTold++;
        else
            UpsTold++;
        int index = hero.PlayerIndex;
        var at = world.Origin(hero.Player);
        listeners.Each(nameof(IHeroDownedListener.HeroDowned), l => l.HeroDowned(index, down, at));
    }

    /// <summary>A new level: nobody is down, the downs counted are the last level's, a new party.</summary>
    public void OnLevelInit(string mapName)
    {
        _party = null;
        _killing.Clear();
        _devHolds.Clear();
    }
}

/// <summary>
/// The lethal hit caught (WO6): the last link of the damage fold
/// (<see cref="IDamageModifier"/>, after TF2's layer and the hero's numbers),
/// so it sees what the hit will really take. A hit that would kill a hero is
/// put to <see cref="HeroDowned.Lethal"/>: a down or a spare leaves him at 1
/// life, a death lands. A hit that does not kill breaks a revive the victim is
/// making. And the rule half (<see cref="IDamageRule"/>): a downed hero takes
/// no damage. The downed state's own kill and the self-kill command's pass both.
/// </summary>
[SideService(Side.Server)]
public sealed class DownedDamage(IDescentModeState mode, HeroDowned downed, Heroes heroes, IEngineEntities entities, IDownedWorld world,
                                  RobotKillerNames killers)
    : IDamageModifier, IDamageRule, IOrderedHook
{
    /// <summary>The fold's last link in TF2 Descent's band, and a rule of the downed block.</summary>
    public const int Order = ModStages.First + 990;

    public int HookOrder => Order;

    private int? HeroIndex(EntityHandle victim) =>
        victim != EntityHandle.None && entities.IsPlayer(victim) && heroes.Of(entities.GetIndex(victim)) is { } hero
            ? hero.PlayerIndex
            : null;

    /// <summary>A kill that is not a hit: the downed state's own, or one the size of the self-kill command's.</summary>
    private bool Outright(int index, float damage) => downed.Killing(index) || damage >= PlayerSelfCommand.SuicideDamage;

    public Decision Decide(in DamageContext hit)
    {
        if (!mode.Active || HeroIndex(hit.Victim) is not { } index)
            return Decision.UseStockRule;
        return downed.IsDown(index) && !Outright(index, hit.Damage) ? Decision.No : Decision.UseStockRule;
    }

    public float Modify(in DamageContext hit, float damage)
    {
        if (!mode.Active || !(damage > 0f) || HeroIndex(hit.Victim) is not { } index || Outright(index, damage))
            return damage;

        if (downed.IsDown(index))
            return 0f;

        int health = world.Health(hit.Victim);
        if ((int)damage < health)
        {
            downed.Hurt(index);
            return damage;
        }

        var answer = downed.Lethal(index);
        if (answer == LethalAnswer.Down)
            killers.Downed(index, hit.Attacker);
        return answer is LethalAnswer.Down or LethalAnswer.Spare ? Math.Max(0, health - 1) : damage;
    }
}

/// <summary>The downed state's and the corpses' frame: after the core's death cycle and the mode's respawn.</summary>
[SideService(Side.Server)]
public sealed class DownedFrame(HeroDowned downed, HeroCorpses corpses) : IFrameLifecycle
{
    public const int Order = ModStages.First + 951;

    public int FrameOrder => Order;

    public void OnThink(float frameTime)
    {
        downed.Think();
        corpses.Think();
    }
}

/// <summary>The downed state's chain registration.</summary>
[SideService(Side.Server)]
public sealed class DownedRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<IHeroDownedListener>();
}

/// <summary>
/// The horde's brains hear a hero go down and get up (WO6; plan_tf2_rpg 2g:
/// a downed hero's threat drops to zero so the fight moves on), through the
/// session's own feed (<see cref="Horde.Session.HordeSession.HeroDowned"/>).
/// </summary>
[SideService(Side.Server)]
public sealed class DownedHordeTap(Horde.Session.HordeSession session) : IHeroDownedListener, IOrderedHook
{
    public const int Order = ModStages.First + 955;

    public int HookOrder => Order;

    public void HeroDowned(int playerIndex, bool down, Vector at) => session.HeroDowned(playerIndex, down, at);
}

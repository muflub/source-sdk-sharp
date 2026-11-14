using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.World;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Level;
using TF2.Descent.Server.Mode;
using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;

namespace TF2.Descent.Server.Hazards;

/// <summary>
/// A hazard volume: a box of one kind (hazards.json's id) in level coordinates. <see cref="Source"/>
/// says where it came from: <c>map</c> (a brush entity named <c>tf2_descent_hazard_&lt;kind&gt;</c>)
/// or <c>dev</c> (<c>tf2_descent_hazard</c>).
/// </summary>
public sealed record HazardVolume(int Id, string Kind, Vector Min, Vector Max, string Source)
{
    /// <summary>Whether <paramref name="feet"/> stand inside the box (faces included).</summary>
    public bool Holds(Vector feet) =>
        feet.X >= Min.X && feet.X <= Max.X && feet.Y >= Min.Y && feet.Y <= Max.Y && feet.Z >= Min.Z && feet.Z <= Max.Z;

    /// <summary>The middle of the box.</summary>
    public Vector Centre => new((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2, (Min.Z + Max.Z) / 2);
}

/// <summary>What the hazards ask of the world: the clock, the depth, the level's volumes, a player's feet and life, a hit, the robots in a box.</summary>
public interface IHazardWorld
{
    /// <summary>The server's clock, in seconds.</summary>
    double Now { get; }

    /// <summary>The level's depth: 0 for the town or a map that is no Descent level.</summary>
    int Depth { get; }

    /// <summary>The level's own hazard volumes of the named kinds: its brush entities named <c>tf2_descent_hazard_&lt;kind&gt;</c>.</summary>
    IReadOnlyList<(string Kind, Vector Min, Vector Max)> MapVolumes(IReadOnlyList<string> kinds);

    bool IsAlive(EntityHandle player);

    /// <summary>Where the player's feet are (his origin).</summary>
    Vector Feet(EntityHandle player);

    /// <summary>One hit through the damage rules, from no attacker (the world's): a hero's resistance applies, a robot body's rules take it.</summary>
    void Hurt(EntityHandle victim, float amount, DamageType bits, Vector at);

    /// <summary>The living robot bodies whose feet stand in the box.</summary>
    IReadOnlyList<EntityHandle> RobotsIn(Vector min, Vector max);
}

/// <summary>The engine's clock, the level the server read, its named brush entities, players, damage and the horde's bodies.</summary>
[SideService(Side.Server, As = [typeof(IHazardWorld)])]
public sealed class EngineHazardWorld(IHordeClock clock, ILevelData level, IEngineEntities entities, IEnginePlayers players,
                                      IEngineDamage damage, TFDamageScopes scopes, IEngineWorldQuery world, IEntityDirectory directory)
    : IHazardWorld
{
    /// <summary>The weapon name a hazard's hit carries (kill feeds, logs).</summary>
    public const string WeaponName = "tf2_descent_hazard";

    private readonly EntityHandle[] _near = new EntityHandle[256];

    public double Now => clock.TicksPerSecond > 0 ? clock.Tick / clock.TicksPerSecond : 0;

    public int Depth => level.Current?.Depth ?? 0;

    public IReadOnlyList<(string Kind, Vector Min, Vector Max)> MapVolumes(IReadOnlyList<string> kinds)
    {
        var found = new List<(string, Vector, Vector)>();
        foreach (string kind in kinds)
        {
            foreach (var e in entities.FindAllByName(HazardRules.VolumePrefix + kind))
            {
                var origin = entities.GetOrigin(e);
                var (mins, maxs) = entities.GetBounds(e);
                found.Add((kind, origin + mins, origin + maxs));
            }
        }

        return found;
    }

    public bool IsAlive(EntityHandle player) => players.IsAlive(player);

    public Vector Feet(EntityHandle player) => entities.GetOrigin(player);

    public void Hurt(EntityHandle victim, float amount, DamageType bits, Vector at)
    {
        using var named = scopes.Push(TFDamageCustom.None, TFWeaponId.None, WeaponName);
        damage.ApplyAt(victim, EntityHandle.None, amount, bits, default, at);
    }

    public IReadOnlyList<EntityHandle> RobotsIn(Vector min, Vector max)
    {
        var centre = (min + max) * 0.5f;
        float radius = (max - min).Length * 0.5f + 1f;
        int count = world.EntitiesInSphere(centre, radius, _near);
        var found = new List<EntityHandle>(count);
        for (int i = 0; i < count; i++)
        {
            EntityHandle e = _near[i];
            if (entities.IsPlayer(e) || directory.Get(e) is not RobotBody { Parked: false } body || body.EngineHealth <= 0)
                continue;
            var o = entities.GetOrigin(e);
            if (o.X >= min.X && o.X <= max.X && o.Y >= min.Y && o.Y <= max.Y && o.Z >= min.Z && o.Z <= max.Z)
                found.Add(e);
        }

        return found;
    }
}

/// <summary>Told each hazard bite on a hero (the kill feed's namer listens).</summary>
public interface IHazardBiteListener
{
    /// <summary>The hero in <paramref name="playerIndex"/> was bitten by <paramref name="kind"/> at <paramref name="now"/> seconds.</summary>
    void Bitten(int playerIndex, string kind, double now);
}

/// <summary>
/// THE LEVEL'S HAZARDS (plan_descent_logic HE10; mine plan D8): lava, slime, as volumes the level
/// names (a brush entity <c>tf2_descent_hazard_&lt;kind&gt;</c>; no generated level carries one
/// yet) or a dev command stands. Each frame, a living hero who is up and whose feet stand in a
/// volume is bitten at the rules' cadence (<see cref="HazardRules.Due"/>: at once on stepping in,
/// then each cadence) by the kind's bite at the depth (<see cref="HazardRules.Bite"/>), dealt
/// through the damage rules from no attacker with the kind's damage type, so the hero damage layer
/// takes his resistance to it (a world hit takes no armour: <c>HeroDamage</c>'s rule); a downed
/// hero takes none, as the downed rules say. Robot bodies stand in it too: a kind that hurts robots
/// bites them by the same cadence through their own damage rules. The hazard a hero stands in is on
/// his networked state (<see cref="Shared.HeroNetState.Hazard"/>), for his HUD. A level's end
/// forgets every volume and every bite.
/// </summary>
[SideService(Side.Server)]
public sealed class HazardService(IDescentModeState mode, Heroes heroes, TF2DescentRules rules, IHazardWorld world,
                                  IHooks<IHazardBiteListener> listeners, ILog log)
    : IGameLifecycle, IFrameLifecycle
{
    /// <summary>TF2 Descent's lamp and hazard block, after the lamp.</summary>
    public const int Order = ModStages.First + 1510;

    /// <summary>A dev hazard's height above the feet it stands at, and its depth below them.</summary>
    public const float DevHeight = 72f, DevBelow = 8f;

    public int LifecycleOrder => Order;

    public int FrameOrder => Order;

    private readonly List<HazardVolume> _volumes = [];
    private readonly Dictionary<int, double> _heroBites = new();
    private readonly Dictionary<EntityHandle, double> _robotBites = new();
    private readonly HashSet<EntityHandle> _robotsNow = [];
    private int _nextId;

    /// <summary>Every volume this level: the map's, then the dev ones.</summary>
    public IReadOnlyList<HazardVolume> Volumes => _volumes;

    /// <summary>Bites dealt this level, to heroes and to robots.</summary>
    public int HeroBites { get; private set; }

    public int RobotBites { get; private set; }

    private HazardSettings S => rules.Content.Hazards;

    public void OnLevelReady(string mapName)
    {
        Forget();
        if (!mode.Active)
            return;
        foreach (var (kind, min, max) in world.MapVolumes([.. S.Kinds.Select(k => k.Id)]))
            _volumes.Add(new HazardVolume(_nextId++, kind, min, max, "map"));
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Hazard] {mapName}: {_volumes.Count} hazard volume(s) [{string.Join(", ", _volumes.GroupBy(v => v.Kind).Select(g => g.Count() + " " + g.Key))}], depth {world.Depth}\n"));
    }

    public void OnLevelShutdown() => Forget();

    private void Forget()
    {
        _volumes.Clear();
        _heroBites.Clear();
        _robotBites.Clear();
        _nextId = 0;
        HeroBites = 0;
        RobotBites = 0;
    }

    /// <summary>
    /// A dev hazard of <paramref name="kind"/>: a box <paramref name="radius"/> each way across
    /// <paramref name="feet"/>, from just below them to a man's height above. Null for a kind
    /// hazards.json does not name.
    /// </summary>
    public HazardVolume? Stand(string kind, Vector feet, float radius)
    {
        if (S.Kind(kind) is null)
            return null;
        var v = new HazardVolume(_nextId++, kind, new Vector(feet.X - radius, feet.Y - radius, feet.Z - DevBelow),
                                 new Vector(feet.X + radius, feet.Y + radius, feet.Z + DevHeight), "dev");
        _volumes.Add(v);
        return v;
    }

    /// <summary>Every dev hazard gone; the map's stay. How many went.</summary>
    public int ClearDev() => _volumes.RemoveAll(v => v.Source == "dev");

    /// <summary>The first volume holding <paramref name="feet"/>, or null.</summary>
    public HazardVolume? At(Vector feet) => _volumes.FirstOrDefault(v => v.Holds(feet));

    public void OnThink(float frameTime)
    {
        if (!mode.Active)
            return;
        double now = world.Now;
        foreach (var hero in heroes.All)
            HeroTick(hero, now);
        if (_volumes.Count > 0)
            RobotTick(now);
    }

    private void HeroTick(HeroComponent hero, double now)
    {
        HazardVolume? at = _volumes.Count == 0 || !world.IsAlive(hero.Player) ? null : At(world.Feet(hero.Player));
        if (hero.State is { } state)
            state.Hazard = at is null ? (byte)0 : (byte)(KindIndex(at.Kind) + 1);
        if (at is null || hero.Downed || S.Kind(at.Kind) is not { } kind)
            return;
        if (!HazardRules.Due(S, _heroBites.TryGetValue(hero.PlayerIndex, out double last) ? last : null, now))
            return;

        _heroBites[hero.PlayerIndex] = now;
        HeroBites++;
        int index = hero.PlayerIndex;
        listeners.Each(nameof(IHazardBiteListener.Bitten), l => l.Bitten(index, kind.Id, now));
        world.Hurt(hero.Player, (float)HazardRules.Bite(S, kind, world.Depth), BitsOf(kind.DamageType), world.Feet(hero.Player));
    }

    private void RobotTick(double now)
    {
        _robotsNow.Clear();
        foreach (var volume in _volumes)
        {
            if (S.Kind(volume.Kind) is not { Robots: true } kind)
                continue;
            foreach (var robot in world.RobotsIn(volume.Min, volume.Max))
            {
                if (!_robotsNow.Add(robot))
                    continue;   // one bite a robot a cadence, however many volumes hold him
                if (!HazardRules.Due(S, _robotBites.TryGetValue(robot, out double last) ? last : null, now))
                    continue;
                _robotBites[robot] = now;
                RobotBites++;
                world.Hurt(robot, (float)HazardRules.Bite(S, kind, world.Depth), BitsOf(kind.DamageType), volume.Centre);
            }
        }
    }

    /// <summary>The position of <paramref name="kind"/> in hazards.json, or -1.</summary>
    public int KindIndex(string kind)
    {
        for (int i = 0; i < S.Kinds.Count; i++)
            if (S.Kinds[i].Id == kind)
                return i;
        return -1;
    }

    /// <summary>
    /// The engine bits a hazard's damage type is dealt with, which the hero damage layer and the robots'
    /// rules read back as the type: fire burns, blast, shock and bullet as their own; an unresisted
    /// kind is poison, which neither reads as any of the four.
    /// </summary>
    public static DamageType BitsOf(string type) => type switch
    {
        "fire" => DamageType.Burn,
        "blast" => DamageType.Blast,
        "shock" => DamageType.Shock,
        "bullet" => DamageType.Bullet,
        _ => DamageType.Poison,
    };

    /// <summary>The volumes and the heroes in them, as lines (<c>tf2_descent_hazards</c>).</summary>
    public string Report()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"[TF2Descent/Hazard] depth {world.Depth}, cadence {S.Cadence:0.##} s: {_volumes.Count} volume(s), {HeroBites} hero bite(s), {RobotBites} robot bite(s) this level\n");
        foreach (var k in S.Kinds)
            sb.Append(CultureInfo.InvariantCulture,
                $"[TF2Descent/Hazard]   kind {k.Id}: {k.DamageType}, {HazardRules.PerSecond(k, world.Depth):0.##}/s = {HazardRules.Bite(S, k, world.Depth):0.##} a bite, robots {(k.Robots ? "hurt" : "untouched")}\n");
        foreach (var v in _volumes)
            sb.Append(CultureInfo.InvariantCulture,
                $"[TF2Descent/Hazard]   #{v.Id} {v.Kind} ({v.Source}) ({v.Min.X:0} {v.Min.Y:0} {v.Min.Z:0}) to ({v.Max.X:0} {v.Max.Y:0} {v.Max.Z:0})\n");
        foreach (var hero in heroes.All.OrderBy(h => h.PlayerIndex))
        {
            var feet = world.Feet(hero.Player);
            sb.Append(CultureInfo.InvariantCulture,
                $"[TF2Descent/Hazard]   player {hero.PlayerIndex} at ({feet.X:0} {feet.Y:0} {feet.Z:0}): {(At(feet) is { } v ? "in " + v.Kind + " #" + v.Id : "clear")}{(hero.Downed ? ", downed" : "")}\n");
        }

        return sb.ToString();
    }
}

/// <summary>The hazard bite chain (<see cref="IHazardBiteListener"/>), which only a registrar can name.</summary>
[SideService(Side.Server)]
public sealed class HazardRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<IHazardBiteListener>();
}

/// <summary>
/// The kill feed names the hazard that killed a hero (the death notice's <see cref="ITFKillerNames"/>):
/// <c>#Descent_Hazard_&lt;kind&gt;</c>, in no team's colour, for a death from no attacker within
/// <see cref="Window"/> seconds of his last bite, or the bleed-out of a hero a bite put down. Asked
/// after the robots' namer (<see cref="RobotKillerNames"/>), so a hero a robot put down stays the
/// robot's.
/// </summary>
[SideService(Side.Server)]
public sealed class HazardKillerNames(IDescentModeState mode, IHazardWorld world, IEngineEntities entities, ILog log)
    : ITFKillerNames, IHazardBiteListener, Hero.IHeroDownedListener, IGameLifecycle, IOrderedHook
{
    /// <summary>After the robots' namer (First + 956), in the lamp and hazard block.</summary>
    public const int Order = ModStages.First + 1520;

    /// <summary>How long after a bite a death from no attacker is still the hazard's, in seconds.</summary>
    public const double Window = 1.0;

    public int HookOrder => Order;

    public int LifecycleOrder => Order;

    private readonly Dictionary<int, (string Kind, double At)> _bitten = new();
    private readonly Dictionary<int, string> _downedBy = new();

    public void Bitten(int playerIndex, string kind, double now) => _bitten[playerIndex] = (kind, now);

    public void HeroDowned(int playerIndex, bool down, Vector at)
    {
        if (down && _bitten.TryGetValue(playerIndex, out var b) && world.Now - b.At <= 1e-6)
            _downedBy[playerIndex] = b.Kind;
        else if (down)
            _downedBy.Remove(playerIndex);
    }

    /// <summary>Nothing at the death: the feed asks its name after it.</summary>
    public void HeroDied(int playerIndex, Vector at)
    {
    }

    /// <summary>A respawned hero starts with no bite and no hazard to his name.</summary>
    public void HeroRespawned(int playerIndex, Vector at)
    {
        _bitten.Remove(playerIndex);
        _downedBy.Remove(playerIndex);
    }

    public bool TryToken(in DeathNoticeFacts death, out string token, out int team)
    {
        (token, team) = ("", 0);
        if (!mode.Active || death.Victim == EntityHandle.None || (death.Attacker != EntityHandle.None && death.Attacker.EntryIndex != 0))
            return false;
        int index = entities.GetIndex(death.Victim);
        string? kind = _downedBy.TryGetValue(index, out var downed) ? downed
            : _bitten.TryGetValue(index, out var b) && world.Now - b.At <= Window ? b.Kind
            : null;
        _downedBy.Remove(index);
        _bitten.Remove(index);
        if (kind is null)
            return false;
        token = HazardRules.Token(kind);
        log.Msg($"[TF2Descent/Hazard] player {index}'s death is named {token} in the kill feed\n");
        return true;
    }

    public void OnLevelInit(string mapName)
    {
        _bitten.Clear();
        _downedBy.Clear();
    }
}

/// <summary>
/// <c>tf2_descent_hazard &lt;kind|clear&gt; [radius] [player]</c>: stands a test hazard at the hero's
/// feet (the lowest-numbered living hero's when no player is named), or clears the dev ones (sv_cheats).
/// </summary>
[ConCommand("tf2_descent_hazard", Help = "tf2_descent_hazard <kind|clear> [radius] [player slot]: stand a test hazard at the hero's feet, or clear them (sv_cheats).")]
public sealed class HazardCommand(SvCheatsConVar cheats, HazardService hazards, Heroes heroes, IHazardWorld world, ILog log)
{
    public void Execute(string kind, [ArgRange(8, 4096)] float radius = 96f, [ArgRange(0, 64)] int slot = 0)
    {
        if (!cheats.Value)
        {
            log.Warning("[TF2Descent/Hazard] tf2_descent_hazard runs only under sv_cheats\n");
            return;
        }
        if (kind == "clear")
        {
            log.Msg($"[TF2Descent/Hazard] cleared {hazards.ClearDev()} dev hazard(s)\n");
            return;
        }
        var hero = slot > 0 ? heroes.Of(slot)
            : heroes.All.Where(h => world.IsAlive(h.Player)).OrderBy(h => h.PlayerIndex).FirstOrDefault();
        if (hero is null)
        {
            log.Warning("[TF2Descent/Hazard] tf2_descent_hazard: no hero to stand it at\n");
            return;
        }
        var feet = world.Feet(hero.Player);
        if (hazards.Stand(kind, feet, radius) is not { } v)
        {
            log.Warning($"[TF2Descent/Hazard] tf2_descent_hazard: '{kind}' is no hazard (hazards.json)\n");
            return;
        }
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Hazard] #{v.Id} {kind} stood at player {hero.PlayerIndex}'s feet ({feet.X:0} {feet.Y:0} {feet.Z:0}), radius {radius:0}\n"));
    }
}

/// <summary><c>tf2_descent_hazards</c>: the level's hazard volumes, the kinds' bites at this depth, and where each hero stands (sv_cheats).</summary>
[ConCommand("tf2_descent_hazards", Help = "tf2_descent_hazards: the level's hazard volumes, each kind's bite at this depth, and the heroes in them (sv_cheats).")]
public sealed class HazardsCommand(SvCheatsConVar cheats, HazardService hazards, ILog log)
{
    public void Execute()
    {
        if (!cheats.Value)
        {
            log.Warning("[TF2Descent/Hazard] tf2_descent_hazards runs only under sv_cheats\n");
            return;
        }
        log.Msg(hazards.Report());
    }
}

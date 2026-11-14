using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Skills;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Mode;
using TF2.Descent.Server.Skills;
using TF2.Descent.Shared;
using TF2.Descent.Tests.Hero;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// The skills runtime over a fake world (plan_descent_logic HE6): the HE2
/// hero rig's heroes, unhosted, with TF2 Descent's skill services built as
/// the server composes them, a <see cref="FakeWorld"/> in place of the engine,
/// and a channel that records what each hero was told.
/// </summary>
internal sealed class SkillRig
{
    public const int Index = HeroRig.Index;

    public readonly HeroRig Heroes = new();
    public readonly FakeWorld World = new();
    public readonly Channel Told = new();
    public readonly Sink Robots = new();
    public readonly SkillCasters Casters = new();
    public readonly SkillBuffs Buffs = new();
    public readonly SkillSlotStore Slots;
    public readonly SkillBinders Binders;
    public readonly SkillAttributes Attributes;
    public readonly SkillRuntime Runtime;
    public readonly SkillContributions Contributions;

    public SkillRig()
    {
        Slots = new SkillSlotStore(Heroes.Heroes, Heroes.Sheets);
        Binders = new SkillBinders(World, Heroes.Heroes, Buffs, Robots, Heroes.Log);
        Attributes = new SkillAttributes(new ModeOn(), new NoBots(), new ItemSchema(new SchemaFiles(), Heroes.Log));
        Runtime = new SkillRuntime(Heroes.Rules, Heroes.Heroes, Casters, Slots, Buffs, Binders, Attributes, Told, Heroes.Log);
        Contributions = new SkillContributions(Heroes.Rules, Casters, Buffs);
        Heroes.Contributions.Add(Contributions);
    }

    public SkillRules Skills => Heroes.Rules.Skills;

    /// <summary>The player of <paramref name="index"/>, as the hero rig's engine names it.</summary>
    public static EntityHandle PlayerOf(int index = Index) => HeroRig.PlayerOf(index);

    /// <summary>A hero joins in <paramref name="index"/> at <paramref name="level"/>, its skills attached, standing at the origin.</summary>
    public HeroComponent Join(int index = Index, int level = 1)
    {
        var hero = Heroes.Join(index) ?? throw new InvalidOperationException("no hero joined");
        if (level > 1)
            Assert(hero.Apply(sheet => Heroes.AtLevel(sheet, level)).Ok, "level up refused");
        World.Team[hero.Player] = (int)TFTeam.Red;
        Runtime.Attach(index, hero.Player);
        return hero;
    }

    /// <summary>Buys <paramref name="skillId"/> up to <paramref name="rank"/>; every buy must go through.</summary>
    public void Learn(string skillId, int rank = 1, int index = Index)
    {
        for (int r = 0; r < rank; r++)
        {
            var result = Runtime.Buy(index, skillId);
            Assert(result is { Ok: true }, $"buy {skillId}: {result?.Refusal} {result?.Detail}");
        }
    }

    /// <summary>Learns and slots <paramref name="skillId"/> in <paramref name="slot"/>.</summary>
    public void Slot(int slot, string skillId, int index = Index)
    {
        string? refused = Runtime.Assign(index, slot, skillId);
        Assert(refused is null, refused ?? "");
    }

    /// <summary>The server ticks <paramref name="ticks"/> times: the hero's own tick, then the skills'.</summary>
    public void Run(int ticks, int index = Index)
    {
        for (int i = 0; i < ticks; i++)
        {
            World.Tick++;
            Heroes.Heroes.Of(index)?.Tick(World.Tick * World.TickInterval);
            Runtime.Tick(index);
        }
    }

    /// <summary>Ticks until <paramref name="seconds"/> have passed (rounded to whole ticks).</summary>
    public void RunSeconds(double seconds, int index = Index) => Run(Binders.Ticks(seconds), index);

    public HeroComponent Hero(int index = Index) => Heroes.Heroes.Of(index)!;

    /// <summary>The rules' damage of <paramref name="skillId"/> for the hero now.</summary>
    public double RulesDamage(string skillId, int index = Index) =>
        Skills.Damage(Hero(index).Sheet!, Hero(index).Stats!, SkillBonuses.None, skillId);

    private static void Assert(bool ok, string why)
    {
        if (!ok)
            throw new InvalidOperationException(why);
    }

    //-------------------------------------------------------------------------

    /// <summary>The engine as the binders see it: answers set by the fact, every call recorded.</summary>
    internal sealed class FakeWorld : ISkillWorld
    {
        public int Tick { get; set; } = 1000;
        public float TickInterval => 0.015f;

        public Dictionary<EntityHandle, Vector> Origins { get; } = new();
        public Dictionary<EntityHandle, int> Team { get; } = new();
        public Dictionary<EntityHandle, string?> Weapon { get; } = new();
        public HashSet<EntityHandle> Jumping { get; } = [];
        public HashSet<EntityHandle> InAir { get; } = [];
        public Dictionary<EntityHandle, int> Life { get; } = new();
        public int MaxLife { get; set; } = 200;
        public List<EntityHandle> Hurtable { get; } = [];
        public TraceResult NextTrace { get; set; }

        public List<SkillShot> Spawned { get; } = [];
        public List<(EntityHandle Victim, EntityHandle Attacker, float Damage, DamageType Type, string Weapon)> Hurts { get; } = [];
        public List<(Vector At, float Damage, float Radius, EntityHandle Attacker, string Weapon, int Tick)> Blasts { get; } = [];
        public List<(EntityHandle Player, TFCond Cond, float Seconds)> Added { get; } = [];
        public List<(EntityHandle Player, TFCond Cond)> Removed { get; } = [];
        public List<(EntityHandle Player, Vector Impulse)> Pushes { get; } = [];
        public List<EntityHandle> BlastJumps { get; } = [];
        public List<(EntityHandle Player, float Amount, int Tick)> Heals { get; } = [];

        public SkillAim AimOf(EntityHandle player) =>
            new(OriginOf(player) + new Vector(0, 0, 64), new Vector(1, 0, 0), new Vector(0, -1, 0), new Vector(0, 0, 1));

        public Vector OriginOf(EntityHandle entity) => Origins.GetValueOrDefault(entity);
        public int TeamOf(EntityHandle player) => Team.GetValueOrDefault(player);
        public bool IsPlayer(EntityHandle entity) => Team.ContainsKey(entity);
        public HashSet<EntityHandle> Dead { get; } = [];
        public bool IsAlive(EntityHandle player) => !Dead.Contains(player);
        public bool Airborne(EntityHandle player) => InAir.Contains(player);
        public bool BlastJumping(EntityHandle player) => Jumping.Contains(player);
        public string? ActiveItemClass(EntityHandle player) => Weapon.GetValueOrDefault(player);
        public int Health(EntityHandle player) => Life.GetValueOrDefault(player, MaxLife);
        public int MaxHealth(EntityHandle player) => MaxLife;

        public EntityHandle Spawn(in SkillShot shot)
        {
            Spawned.Add(shot);
            return new EntityHandle(0x0003_0000u | (uint)(100 + Spawned.Count));
        }

        public TraceResult Trace(Vector start, Vector end, EntityHandle pass) => NextTrace with { End = NextTrace.Hit ? NextTrace.End : end };

        public void Hurt(EntityHandle victim, EntityHandle attacker, float damage, DamageType type, Vector force, Vector at, string weaponName) =>
            Hurts.Add((victim, attacker, damage, type, weaponName));

        public void Blast(Vector origin, float damage, float radius, EntityHandle attacker, string weaponName) =>
            Blasts.Add((origin, damage, radius, attacker, weaponName, Tick));

        public IReadOnlyList<EntityHandle> Near(Vector origin, float radius) =>
            [.. Hurtable.Where(e => (OriginOf(e) - origin).Length <= radius)];

        public bool AddCondition(EntityHandle player, TFCond cond, float duration, EntityHandle provider)
        {
            Added.Add((player, cond, duration));
            return true;
        }

        public void RemoveCondition(EntityHandle player, TFCond cond) => Removed.Add((player, cond));
        public void Push(EntityHandle player, Vector impulse) => Pushes.Add((player, impulse));
        public void BeginBlastJump(EntityHandle player) => BlastJumps.Add(player);

        public int Heal(EntityHandle player, float amount)
        {
            Heals.Add((player, amount, Tick));
            return (int)amount;
        }
    }

    /// <summary>What each hero was told, and each skill bar sent.</summary>
    internal sealed class Channel : ISkillChannel
    {
        public List<(int Index, string Text)> Notices { get; } = [];
        public List<(int Index, SkillBarView Bar)> Bars { get; } = [];

        public void Tell(int playerIndex, string text) => Notices.Add((playerIndex, text));

        public void Bar(int playerIndex, SkillBar bar) => Bars.Add((playerIndex, bar.View()));
    }

    /// <summary>A status sink that records each ask and answers as told.</summary>
    internal sealed class Sink : IRobotStatusSink
    {
        public RobotStatusAnswer Answer { get; set; } = RobotStatusAnswer.NotAvailable;
        public List<(EntityHandle Robot, string Condition, double Seconds, string Skill, string Target)> Applied { get; } = [];
        public List<(EntityHandle Robot, double Seconds, string Skill)> Threats { get; } = [];

        public RobotStatusAnswer Apply(EntityHandle robot, string condition, double seconds, EntityHandle hero, string skillId, string target)
        {
            Applied.Add((robot, condition, seconds, skillId, target));
            return Answer;
        }

        public RobotStatusAnswer Threaten(EntityHandle robot, EntityHandle hero, double seconds, string skillId)
        {
            Threats.Add((robot, seconds, skillId));
            return Answer;
        }
    }

    private sealed class ModeOn : IDescentModeState
    {
        public bool Active => true;
    }

    private sealed class NoBots : IBotPlayers
    {
        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal, int team = 0)
        {
            refusal = "no bots here";
            return EntityHandle.None;
        }

        public bool IsBot(EntityHandle player) => false;
        public bool Kick(EntityHandle bot, string reason) => false;
        public IReadOnlyList<BotPlayer> Bots => [];
    }

    /// <summary>An item schema of the three attributes the skills set, by their stock classes.</summary>
    private sealed class SchemaFiles : IEngineFiles
    {
        private const string Schema = """
            "items_game"
            {
                "qualities" { }
                "items" { }
                "attributes"
                {
                    "135" { "name" "rocket jump damage reduction" "attribute_class" "rocket_jump_dmg_reduction" "description_format" "value_is_percentage" }
                    "1001" { "name" "self dmg push force increased" "attribute_class" "mult_dmgself_push_force" "description_format" "value_is_percentage" }
                    "232" { "name" "crit mod disabled hidden" "attribute_class" "crit_while_airborne" "description_format" "value_is_or" }
                }
            }
            """;

        public int GameFileSize(string path) => ReadGameFileBytes(path)?.Length ?? -1;
        public string? ReadGameFile(string path) => ReadGameFileBytes(path) is { } b ? Encoding.UTF8.GetString(b) : null;
        public byte[]? ReadGameFileBytes(string path) => path == ItemSchema.FilePath ? Encoding.UTF8.GetBytes(Schema) : null;
        public byte[]? ReadFileOnPath(string path, string pathId) => pathId == "GAME" ? ReadGameFileBytes(path) : null;
    }
}

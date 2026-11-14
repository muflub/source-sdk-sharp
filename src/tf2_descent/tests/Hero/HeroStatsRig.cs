using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Mode;
using TF2.Shared.Econ;
using TF2.Shared.Movement;

namespace TF2.Descent.Tests.Hero;

/// <summary>The three sheets the HE3 facts measure.</summary>
public enum StatsSheet
{
    /// <summary>A new Soldier: Brawn 20, Smarts 5, Aim 20, Grit 15.</summary>
    Level1,

    /// <summary>A level-10 Soldier with 25 points in Aim (45) and 20 in Grit (35).</summary>
    Level10,

    /// <summary>The level-10 Soldier with test gear: some active, one inactive, one broken.</summary>
    Level10Contributions,
}

/// <summary>
/// HE3's facts: a <see cref="HeroRig"/> hero with one of three sheets, the dev
/// contribution source in its chain, and the HE3 services over fakes of the
/// TF numbers they read (a Soldier's 200 health and 240 speed, the two
/// attribute definitions, the weapon in the hands).
/// </summary>
internal sealed class HeroStatsRig
{
    public const int SoldierHealth = 200;
    public const float SoldierSpeed = 240f;

    /// <summary>A robot (or anything) that hurts a hero: not a player.</summary>
    public static readonly EntityHandle Robot = new(0x0005_0123u);

    public readonly HeroRig Rig = new();
    public readonly DevContributions Dev = new();
    public readonly Weapons Held = new();
    public readonly Levels AttackerLevels = new();
    public readonly HeroAttributes Attributes;
    public readonly HeroDamage Damage;
    public readonly HeroCrits Crits;
    public readonly HeroLifeKeeper Life;
    public readonly HeroLeech Leech;
    public readonly HeroComponent Hero;
    public readonly Dictionary<EntityHandle, int> Health = new();

    public static readonly AttributeDefinition MaxHealthDefinition =
        Definition(26, HeroAttributes.MaxHealthName, HeroAttributes.MaxHealthClass, AttributeDescriptionFormat.Additive);

    public static readonly AttributeDefinition MoveSpeedDefinition =
        Definition(107, HeroAttributes.MoveSpeedName, TFMaxSpeed.MoveSpeedAttrib, AttributeDescriptionFormat.Percentage);

    /// <summary>The skill the hit in flight is from, as the damage modifier is told it: null for a weapon's hit.</summary>
    public sealed class SkillHitNow : TF2.Descent.Server.Skills.ISkillHits
    {
        public string? Skill { get; set; }
        public string? Current() => Skill;
    }

    public SkillHitNow SkillHit { get; } = new();

    public HeroStatsRig(StatsSheet sheet = StatsSheet.Level1)
    {
        Rig.Contributions.Add(Dev);
        var mode = new ModeOn();
        var bots = new NoBots();
        Attributes = new HeroAttributes(mode, bots, Rig.Heroes, new SoldierBase(), new Lookup());
        Rig.Watchers.Add(Attributes);

        var debug = DeclaredConVars.Holding<HeroDebugDamageConVar>("0");
        Damage = new HeroDamage(mode, Rig.Heroes, Rig.Engine, Held, new HeroHitKinds(Rig.Rules), AttackerLevels, Rig.Rules,
                                SkillHit, debug, Rig.Log);
        Crits = new HeroCrits(mode, Rig.Heroes);
        Life = new HeroLifeKeeper(Rig.Engine, Rig.Engine, Rig.Health);
        Leech = new HeroLeech(mode, Rig.Heroes, Rig.Engine, Life, debug, Rig.Log);

        Rig.Engine.ReturnsEachCall("Entities.IsPlayer", () => Rig.Engine.Calls[^1].ArgAs<EntityHandle>(0).EntryIndex is > 0 and <= 64);
        Rig.Engine.Returns("Players.IsAlive", true);
        Rig.Engine.ReturnsEachCall("Entities.GetHealth",
                                   () => Health.GetValueOrDefault(Rig.Engine.Calls[^1].ArgAs<EntityHandle>(0), 100));

        Hero = Rig.Join() ?? throw new InvalidOperationException("no hero joined");
        Assert(Hero.Apply(s => SheetOf(s, sheet)).Ok);
        if (sheet == StatsSheet.Level10Contributions)
            ContributeLevel10Gear();
    }

    public EntityHandle Player => Hero.Player;

    private HeroSheet SheetOf(HeroSheet sheet, StatsSheet which)
    {
        if (which == StatsSheet.Level1)
            return sheet;

        var raised = Rig.AtLevel(sheet, 10);
        var aim = Rig.Rules.Heroes.Spend(raised, "aim", 25);
        var grit = Rig.Rules.Heroes.Spend(aim.Sheet, "grit", 20);
        Assert(aim.Ok && grit.Ok && grit.Sheet.AttributePoints == 0);
        return grit.Sheet;
    }

    /// <summary>
    /// +20 % blast and +5 % all damage, active; +50 % bullet from an INACTIVE
    /// item (a requirement unmet) and +50 % melee from a BROKEN one, which
    /// count for nothing.
    /// </summary>
    private void ContributeLevel10Gear()
    {
        Dev.Add(HeroRig.Index, "damage_blast_pct", 20);
        Dev.Add(HeroRig.Index, "damage_all_pct", 5);
        Dev.Add(HeroRig.Index, "damage_bullet_pct", 50, SourceState.Inactive);
        Dev.Add(HeroRig.Index, "damage_melee_pct", 50, SourceState.Broken);
        Hero.Recompute();
    }

    /// <summary>TF2's walk over the player's manager, as <c>TFPlayerResupply.MaxHealth</c> and <c>TFMaxSpeed</c> take it.</summary>
    public (int MaxHealth, float MaxSpeed) TFNumbers()
    {
        var manager = new AttributeContainer();
        Attributes.AttributesOf(Player)?.ProvideTo(manager);
        var hooks = new AttributeHooks();
        return (hooks.Int(SoldierHealth, HeroAttributes.MaxHealthClass, manager),
                hooks.Float(SoldierSpeed, TFMaxSpeed.MoveSpeedAttrib, manager));
    }

    /// <summary>A hit as TF2's layer left it, through the hero's layer.</summary>
    public float Hit(EntityHandle victim, EntityHandle attacker, float tfDamage, DamageType type, string? weapon = null)
    {
        if (weapon is not null)
            Held.ItemClass[attacker] = weapon;
        return Damage.Modify(new DamageContext(victim, attacker, tfDamage, (int)type), tfDamage);
    }

    private static void Assert(bool ok)
    {
        if (!ok)
            throw new InvalidOperationException("the rig's sheet could not be built");
    }

    private static AttributeDefinition Definition(int defindex, string name, string hook, AttributeDescriptionFormat format)
        => new(defindex, name, hook, format, AttributeEffectType.Positive, false, false, AttributeValueType.Default,
               MergedKeys.Block("x"));

    //-------------------------------------------------------------------------

    internal sealed class Weapons : IHeroWeapons
    {
        public Dictionary<EntityHandle, string> ItemClass { get; } = new();
        public string? ActiveItemClass(EntityHandle player) => ItemClass.GetValueOrDefault(player);
    }

    internal sealed class Levels : IAttackerLevel
    {
        public int Level { get; set; } = 1;
        public int LevelOf(EntityHandle attacker) => Level;
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

    private sealed class SoldierBase : IHeroClassBase
    {
        public TFClassBase? Of(EntityHandle player) => new TFClassBase(SoldierHealth, SoldierSpeed);
    }

    private sealed class Lookup : ITFAttributeLookup
    {
        public AttributeDefinition? Find(string attributeClass, string preferredName) => attributeClass switch
        {
            HeroAttributes.MaxHealthClass => MaxHealthDefinition,
            TFMaxSpeed.MoveSpeedAttrib => MoveSpeedDefinition,
            _ => null,
        };
    }
}

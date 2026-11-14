using SourceSharp.Abi;

namespace TF2.Descent.Shared;

/// <summary>
/// What every client knows of one hero (plan_descent_logic HE2, E4): the
/// scalars party frames and the owner's HUD draw, on the hero's one
/// <c>tf2_descent_hero</c> entity, sent to every client. Item views are not
/// here: they go by message (E4).
///
/// <para>
/// Every number is a whole number. <see cref="Mana"/>, <see cref="ManaMax"/>
/// and <see cref="LifeMax"/> are the server's values rounded down
/// (<see cref="Whole"/>), so mana regenerating by a fraction a tick leaves
/// the entity unchanged, and so not re-sent, until the number a HUD shows
/// changes; full mana shows as <see cref="ManaMax"/> exactly.
/// </para>
/// </summary>
[StandaloneNetState(ClassName)]
public partial class HeroNetState
{
    /// <summary>The entity's classname (plan section 3's <c>tf2_descent_*</c>).</summary>
    public const string ClassName = "tf2_descent_hero";

    /// <summary>The hero's player index (1..64): whose state this is.</summary>
    [NetField(Bits = 7)] public partial byte OwnerIndex { get; set; }

    /// <summary>The hero's class as a TF2 class number (<c>TFClass</c>: 3 is the Soldier); 0 before the sheet is known.</summary>
    [NetField(Bits = 4)] public partial byte ClassId { get; set; }

    /// <summary>The sheet's level; 0 before the sheet is known.</summary>
    [NetField(Bits = 6)] public partial byte Level { get; set; }

    /// <summary>Total experience.</summary>
    public partial int Xp { get; set; }

    /// <summary>The total experience at which <see cref="Level"/> was reached: the XP bar's left end.</summary>
    public partial int LevelXp { get; set; }

    /// <summary>The total experience at which the next level is reached: the bar's right end (at the cap, <see cref="LevelXp"/>).</summary>
    public partial int NextLevelXp { get; set; }

    /// <summary>The sheet's maximum life, for display (the life itself is the player's TF health).</summary>
    public partial ushort LifeMax { get; set; }

    /// <summary>Current mana, rounded down.</summary>
    public partial ushort Mana { get; set; }

    /// <summary>Maximum mana, rounded down.</summary>
    public partial ushort ManaMax { get; set; }

    /// <summary>Attribute points earned and not spent.</summary>
    public partial ushort AttributePoints { get; set; }

    /// <summary>Skill points earned and not spent.</summary>
    public partial ushort SkillPoints { get; set; }

    /// <summary>The hero is downed (R26, WO6): on the ground, bleeding out, waiting for a party member.</summary>
    public partial bool Downed { get; set; }

    /// <summary>The server tick at which the downed hero bleeds out; 0 when not downed.</summary>
    public partial int DownedEndTick { get; set; }

    /// <summary>The server tick at which the hero went down; 0 when not downed. The window is <c>DownedEndTick - DownedStartTick</c>.</summary>
    public partial int DownedStartTick { get; set; }

    /// <summary>The player index of the party member reviving this hero; 0 when nobody is.</summary>
    [NetField(Bits = 7)] public partial byte ReviverIndex { get; set; }

    /// <summary>The server tick the revive began; 0 when nobody is reviving.</summary>
    public partial int ReviveStartTick { get; set; }

    /// <summary>The server tick the revive completes if held; 0 when nobody is reviving.</summary>
    public partial int ReviveEndTick { get; set; }

    /// <summary>A hardcore hero dies once.</summary>
    public partial bool Hardcore { get; set; }

    /// <summary>
    /// The hero's miner's lamp is lit (HE10; mine plan M9, D7, E5): every client lights it for a
    /// hero it can see. The server writes it lit only for a living hero whose switch is on.
    /// </summary>
    public partial bool LampOn { get; set; }

    /// <summary>
    /// The hero's look, as whole steps of lamp.json's pitch step above straight up
    /// (<c>LampRules.PitchSteps</c>): where every other client points his lamp. Written while the
    /// lamp is lit; a client may not know another player's pitch otherwise.
    /// </summary>
    [NetField(Bits = 7)] public partial byte LampPitch { get; set; }

    /// <summary>
    /// The hazard the hero stands in (HE10, mine plan D8): hazards.json's kind, its position plus
    /// one; 0 for none. For his HUD's warning; every client holds it.
    /// </summary>
    [NetField(Bits = 4)] public partial byte Hazard { get; set; }

    /// <summary>
    /// The wire's rounding: a value as the whole number a HUD shows, rounded
    /// down and held inside 0..65535. A NaN is 0.
    /// </summary>
    public static ushort Whole(double value) =>
        double.IsNaN(value) || value <= 0 ? (ushort)0
        : value >= ushort.MaxValue ? ushort.MaxValue
        : (ushort)Math.Floor(value);
}

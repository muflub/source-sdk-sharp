using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace TF2.Descent.Server.Skills.Dev;

/// <summary>
/// <c>tf2_descent_skill_target</c>: a THROWAWAY target for the skills' live
/// proofs (HE6, HE7), to be folded into the stats lane's
/// <c>tf2_descent_dummy</c> (HE3), which this base does not have. A robot
/// Heavy's model in a TF player's standing box, solid, running the damage
/// rules, with a pool it never drops below 1 of; each hit logged as
/// <c>[TF2Descent/Target] took N (type) from player P</c>, unrounded.
/// </summary>
[ScriptedEntity(EntityClass, ScriptedEntityKind.Item)]
[Precache(PrecacheKind.Model, Model)]
public sealed class SkillTarget : ScriptedEntity
{
    public const string EntityClass = "tf2_descent_skill_target";

    public const string Model = "models/bots/heavy/bot_heavy.mdl";

    public const int Pool = 100_000;

    public override bool RunsDamageRules => true;

    public override bool TakesDamage => true;

    public override void OnSpawn()
    {
        SetModel(Model);
        Health = Pool;
        Entities.SetSolidity(Handle, SolidType.BoundingBox, SolidFlags.None);
        Entities.SetBounds(Handle, new Vector(-24f, -24f, 0f), new Vector(24f, 24f, 82f));
    }

    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
    {
        int health = Health;
        int taken = Math.Min(Math.Max(0, (int)MathF.Round(damage, MidpointRounding.AwayFromZero)), Math.Max(0, health - 1));
        Log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Target] took {damage:0.00} ({damageType}) from player " +
            $"{(attacker == EntityHandle.None ? 0 : Entities.GetIndex(attacker))}: {taken} of {health}\n"));
        return taken;
    }
}

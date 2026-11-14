using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// <c>tf2_descent_dummy</c>: a target dummy, the measuring instrument for the
/// hero's damage (HE3) and the packages after it. A robot Heavy's model in a
/// TF player's standing box, solid, running the damage rules (TF2's layer and
/// the hero's numbers both fold every hit on it), with a large health pool.
/// Each hit is logged — <c>[TF2Descent/Dummy] took N (type, crit) from player
/// P</c> — with the damage the rules handed it, unrounded. It never falls
/// below 1 health unless <see cref="Mortal"/>: what it takes is the hit
/// rounded, capped at its health less one.
/// </summary>
[ScriptedEntity(EntityClass, ScriptedEntityKind.Item)]
[Precache(PrecacheKind.Model, Model)]
public sealed class HeroDummy(DamageHitGroups hits) : ScriptedEntity
{
    public const string EntityClass = "tf2_descent_dummy";

    /// <summary>The robot Heavy (the TF2 mount has it: plan section 3.11, row 11).</summary>
    public const string Model = "models/bots/heavy/bot_heavy.mdl";

    /// <summary>The pool a dummy starts with.</summary>
    public const int DefaultHealth = 100_000;

    /// <summary>A TF player's standing hull (48 x 48 x 82).</summary>
    public static Vector HullMins => new(-24f, -24f, 0f);

    /// <summary>The hull's top corner.</summary>
    public static Vector HullMaxs => new(24f, 24f, 82f);

    /// <summary>Whether a hit may take the last point of health (a kill).</summary>
    public bool Mortal { get; set; }

    /// <summary>Sets the dummy's health pool (the command's <c>health</c>).</summary>
    public void SetPool(int health) => Health = Math.Max(1, health);

    public override bool RunsDamageRules => true;

    public override bool TakesDamage => true;

    public override void OnSpawn()
    {
        SetModel(Model);
        Health = DefaultHealth;
        Entities.SetSolidity(Handle, SolidType.BoundingBox, SolidFlags.None);
        Entities.SetBounds(Handle, HullMins, HullMaxs);
    }

    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
    {
        int health = Health;
        int taken = Taken(damage, health, Mortal);
        Log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Dummy] took {damage:0.00} ({damageType}, {(hits.Critical ? "crit" : "no crit")}) from player " +
            $"{(attacker == EntityHandle.None ? 0 : Entities.GetIndex(attacker))}: {taken} of {health}\n"));
        return taken;
    }

    /// <summary>What a hit of <paramref name="damage"/> takes from <paramref name="health"/>: rounded, and never the last point unless mortal.</summary>
    public static int Taken(float damage, int health, bool mortal)
    {
        int whole = Math.Max(0, (int)MathF.Round(damage, MidpointRounding.AwayFromZero));
        return mortal ? whole : Math.Min(whole, Math.Max(0, health - 1));
    }
}

//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>One step of an impact damage table: at or above this impulse, this damage.</summary>
public readonly record struct ImpactEntry(float Impulse, float Damage);

/// <summary>
/// impactdamagetable_t (physics_impact_damage.h): the linear and angular steps
/// and the gates in front of them.
/// </summary>
public sealed record ImpactDamageTable(
    ImpactEntry[] Linear, ImpactEntry[] Angular,
    float MinSpeedSqr, float MinRotSpeedSqr, float MinMass,
    float SmallMassMax, float SmallMassCap, float SmallMassMinSpeedSqr,
    float LargeMassMin, float LargeMassScale, float LargeMassFallingScale,
    float MyMinVelocity);

/// <summary>
/// What CalculatePhysicsImpactDamage asks of the two entities rather than of
/// the record: whether each is a player, whether the other is the world, and
/// whether this one is floating.
/// </summary>
public readonly record struct ImpactParties(bool SelfIsPlayer, bool OtherIsPlayer, bool OtherIsWorld, bool SelfIsFloating);

/// <summary>
/// The four tables stock registers (gDamageTableRegistry), with stock's numbers
/// exactly, including glass's two quirks: its linear table is out of order
/// (250*250 after 500*500, so anything past 250^2 reads 500 through
/// ReadDamageTable's first-break walk), and its initialiser has one field fewer,
/// so its large-mass falling scale and min velocity are 0.
///
/// Built on each read rather than held in static fields: a table holds arrays,
/// and a static field holding one is mutable state (StaticStateCensusTests).
/// </summary>
public static class ImpactDamageTables
{
    private static ImpactEntry E(float speed, float damage) => new(speed * speed, damage);

    /// <summary>gDefaultPlayerImpactDamageTable.</summary>
    public static ImpactDamageTable Player => new(
        new[] { E(150, 5), E(250, 10), E(450, 20), E(550, 50), E(700, 100), E(1000, 500) },
        new[] { E(100, 10), E(150, 20), E(200, 50), E(300, 500) },
        MinSpeedSqr: 24 * 24f, MinRotSpeedSqr: 360 * 360f, MinMass: 2f,
        SmallMassMax: 5f, SmallMassCap: 5f, SmallMassMinSpeedSqr: 36 * 36f,
        LargeMassMin: 0f, LargeMassScale: 1f, LargeMassFallingScale: 2f, MyMinVelocity: 320f);

    /// <summary>gDefaultPlayerVehicleImpactDamageTable.</summary>
    public static ImpactDamageTable PlayerVehicle => new(
        new[] { E(450, 5), E(600, 10), E(700, 25), E(1000, 50), E(1500, 100), E(2000, 500) },
        new[] { E(100, 10), E(150, 20), E(200, 50), E(300, 500) },
        MinSpeedSqr: 24 * 24f, MinRotSpeedSqr: 360 * 360f, MinMass: 80f,
        SmallMassMax: 150f, SmallMassCap: 5f, SmallMassMinSpeedSqr: 36 * 36f,
        LargeMassMin: 0f, LargeMassScale: 1f, LargeMassFallingScale: 1f, MyMinVelocity: 0f);

    /// <summary>gDefaultNPCImpactDamageTable, every prop's default.</summary>
    public static ImpactDamageTable Npc => new(
        new[] { E(150, 5), E(250, 10), E(350, 50), E(500, 100), E(1000, 500) },
        new[] { E(100, 10), E(150, 25), E(200, 50), E(250, 500) },
        MinSpeedSqr: 24 * 24f, MinRotSpeedSqr: 360 * 360f, MinMass: 2f,
        SmallMassMax: 5f, SmallMassCap: 5f, SmallMassMinSpeedSqr: 36 * 36f,
        LargeMassMin: 500f /* VPHYSICS_LARGE_OBJECT_MASS */, LargeMassScale: 4f, LargeMassFallingScale: 5f, MyMinVelocity: 0f);

    /// <summary>gGlassImpactDamageTable.</summary>
    public static ImpactDamageTable Glass => new(
        new[] { E(25, 10), E(50, 20), E(100, 50), E(200, 75), E(500, 100), E(250, 500) },
        new[] { E(50, 25), E(100, 50), E(200, 100), E(250, 500) },
        MinSpeedSqr: 8 * 8f, MinRotSpeedSqr: 360 * 360f, MinMass: 2f,
        SmallMassMax: 1f, SmallMassCap: 10f, SmallMassMinSpeedSqr: 8 * 8f,
        LargeMassMin: 50f, LargeMassScale: 4f, LargeMassFallingScale: 0f, MyMinVelocity: 0f);

    /// <summary>A table by the name a map or a class gives (stock's registry), or null.</summary>
    public static ImpactDamageTable? Find(string name) => name switch
    {
        "player" => Player,
        "player_vehicle" => PlayerVehicle,
        "npc" => Npc,
        "glass" => Glass,
        _ => null,
    };
}

/// <summary>
/// Stock's CalculatePhysicsImpactDamage and ReadDamageTable (physics stock
/// parity C6), over the collision record, line for line in stock's order.
/// What this mod cannot reach is left out and says so: a held object's real
/// mass (gpGlobals->maxClients == 1 only), a ragdoll's implicit velocity (no
/// server ragdoll has physics) and multi-object mass sums (a managed entity has
/// one object). A pure function: the entity applies the damage.
/// </summary>
public static class PhysicsImpactDamage
{
    /// <param name="index">The side taking the damage (0 or 1).</param>
    public static float Calculate(in PhysicsEvent ev, int index, ImpactDamageTable table, float energyScale,
                                  bool allowStaticDamage, bool damageFromHeldObjects, ImpactParties parties,
                                  out DamageType damageType)
    {
        damageType = DamageType.Crush;
        int other = 1 - index;

        // UNDONE in stock too: no self-inflicted damage.
        if (ev.Entity0 == ev.Entity1)
            return 0f;

        uint selfFlags = index == 0 ? ev.GameFlags0 : ev.GameFlags1;
        uint otherFlags = index == 0 ? ev.GameFlags1 : ev.GameFlags0;

        if ((otherFlags & PhysicsGameFlags.NoNpcImpactDamage) != 0 && parties.SelfIsPlayer)
            return 0f;

        // Dissolving impact damage results in death always.
        if ((otherFlags & PhysicsGameFlags.DissolveDamage) != 0)
        {
            damageType |= DamageType.Dissolve;
            return 1000f;
        }

        if (energyScale <= 0f)
            return 0f;

        // A non-moving object due to a constraint, or flagged no damage.
        const uint noDamage = PhysicsGameFlags.ConstraintStatic | PhysicsGameFlags.NoImpactDamage;
        if ((otherFlags & noDamage) != 0)
            return 0f;

        if (!damageFromHeldObjects && (otherFlags & PhysicsGameFlags.PlayerHeld) != 0)
            return 0f;

        if ((selfFlags & PhysicsGameFlags.PlayerHeld) != 0)
        {
            // players can't damage held objects
            if (parties.OtherIsPlayer)
                return 0f;

            allowStaticDamage = false;
        }

        Vector otherPre = ev.PreVelocity(other);
        float otherSpeedSqr = otherPre.LengthSquared;
        float otherAngSqr = 0f;

        // factor in angular for sharp objects
        if ((otherFlags & PhysicsGameFlags.SliceDamage) != 0)
            otherAngSqr = ev.PreAngularVelocity(other).LengthSquared;

        float otherMass = index == 0 ? ev.Mass1 : ev.Mass0;

        if ((otherFlags & PhysicsGameFlags.HeavyObject) != 0)
        {
            otherMass = table.LargeMassMin;
            if (energyScale < 2f)
                energyScale = 2f;
        }

        if (!allowStaticDamage)
        {
            if (otherMass < table.MinMass)
                return 0f;
            // check to see if the object is small
            if (otherMass < table.SmallMassMax && otherSpeedSqr < table.SmallMassMinSpeedSqr)
                return 0f;
            if (otherSpeedSqr < table.MinSpeedSqr && otherAngSqr < table.MinRotSpeedSqr)
                return 0f;
        }

        // Add extra oomph for floating objects
        if (parties.SelfIsFloating && !parties.OtherIsWorld && energyScale < 3f)
            energyScale = 3f;

        float damage = 0f;

        // don't ever take spin damage from slowly spinning objects
        if (otherAngSqr > table.MinRotSpeedSqr)
        {
            Vector inertia = ev.Inertia(other);
            Vector ang = ev.PreAngularVelocity(other);
            float angularMom = MathF.Abs(inertia.X * ang.X) + MathF.Abs(inertia.Y * ang.Y) + MathF.Abs(inertia.Z * ang.Z);
            damage = ReadTable(table.Angular, angularMom * energyScale);
            if (damage > 0f)
                damageType |= DamageType.Slash;
        }

        float deltaV = ev.PreVelocity(index).Length - ev.PostVelocity(index).Length;
        float mass = index == 0 ? ev.Mass0 : ev.Mass1;

        // If I lost speed, and I lost less than min velocity, then filter out this energy
        if (deltaV > 0f && deltaV < table.MyMinVelocity)
            deltaV = 0f;
        float eliminatedEnergy = deltaV * deltaV * mass;

        deltaV = otherPre.Length - ev.PostVelocity(other).Length;
        float otherEliminatedEnergy = deltaV * deltaV * otherMass;

        // exaggerate the effects of really large objects
        if (otherMass >= table.LargeMassMin)
        {
            otherEliminatedEnergy *= table.LargeMassScale;
            float dz = otherPre.Z - ev.PostVelocity(other).Z;

            if (deltaV > 0f && dz < 0f && otherPre.Z < 0f)
            {
                float factor = MathF.Abs(dz / deltaV);
                otherEliminatedEnergy *= 1f + factor * (table.LargeMassFallingScale - 1f);
            }
        }

        eliminatedEnergy += otherEliminatedEnergy;

        // now in units of this character's speed squared. A non-moveable
        // object's inverse mass is zero but impact damage is enabled on it, so
        // stock recomputes it from the mass -- which is what a moveable one's is.
        float invMass = mass > 0f ? 1f / mass : 0f;

        eliminatedEnergy *= invMass * energyScale;

        damage += ReadTable(table.Linear, eliminatedEnergy);

        bool otherStatic = (index == 0 ? ev.Static1 : ev.Static0) != 0;
        if (!otherStatic && otherMass < table.SmallMassMax && table.SmallMassCap > 0f)
            damage = Math.Clamp(damage, 0f, table.SmallMassCap);

        return damage;
    }

    /// <summary>
    /// ReadDamageTable: the damage of the last step walked before the first
    /// step the impulse is under, or 0 when it is under the first.
    /// </summary>
    public static float ReadTable(ImpactEntry[] table, float impulse)
    {
        int i;
        for (i = 0; i < table.Length; i++)
        {
            if (impulse < table[i].Impulse)
                break;
        }
        return i > 0 ? table[i - 1].Damage : 0f;
    }
}

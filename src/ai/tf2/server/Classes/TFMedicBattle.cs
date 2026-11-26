using SourceSharp.Ai.TF2.Senses;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Battle Medic (P7 round 2, round2.md §5; the guide's Battle Medic: Support/Flank, "riskier and less efficient", so its
/// weight is low; medic.md §2.1 E, §4.5): heals less and fights more. Played by the archetypes <see cref="TFMedicClass.Battle"/>
/// (the Blutsauger, whose hits heal the Medic) and <see cref="TFMedicClass.BattleBow"/> (the Crusader's Crossbow, whose bolts
/// also heal a calling teammate at range), and by any Medic whose kit holds a Blutsauger:
/// <list type="bullet">
/// <item>The primary comes out at an enemy in sight within <see cref="BattleReach"/> (the crossbow's further,
/// <see cref="BattleBowReach"/>) whenever no teammate it knows is hurt: the beam's patient's panel at or over
/// <see cref="BattleHealShare"/> of his most (or unread) and no teammate its HUD shows calling within the beam's hold.
/// With the charge ready or running, or the Medic invulnerable, the medigun stays out, as for every Medic.</item>
/// <item>The fight branch weighs as a fighter's (1), not a Medic's (0.5).</item>
/// </list>
/// Original rules: stock's bot has no battle Medic.
/// </summary>
public sealed partial class TFDecider
{
    private sealed partial class MedicPlay
    {
        /// <summary>The syringe gun's fighting reach (medic.md §4.5: syringes under 800 u), the crossbow's, and the patient's share under which he is hurt.</summary>
        public const float BattleReach = 800f, BattleBowReach = 1500f, BattleHealShare = TFHud.CallingShare;

        private bool _battle;
        private long _battleDecides;

        /// <summary>Whether this Medic plays the Battle Medic: its archetype, or a Blutsauger in its kit (its hits heal: <c>add_onhit_addhealth</c>).</summary>
        private void ReadBattle(TFHud hud)
            => _battle = Archetype is TFMedicClass.Battle or TFMedicClass.BattleBow
                         || (hud.TryWielded(TFLoadoutSlot.Primary, out TFKitItem primary) && primary.Has("add_onhit_addhealth"));

        /// <summary>Whether the Battle Medic takes its primary to the enemy now: one in sight within its reach and no teammate it knows hurt.</summary>
        private bool BattleFight(int primary)
        {
            if (!_battle || primary < 0 || !D._targetSighted || _ready || _releasing || (D._self.Conditions & ActorConditions.Invulnerable) != 0)
                return false;
            float reach = primary == _crossbow ? BattleBowReach : BattleReach;
            return D._targetDist <= reach && !MateKnownHurt();
        }

        /// <summary>A teammate the Medic knows is hurt: the beam's patient under <see cref="BattleHealShare"/> by his panel, or one its HUD shows calling within the beam's hold.</summary>
        private bool MateKnownHurt()
        {
            if (_patient.Known && _patientMost > 0 && _patientHealth < BattleHealShare * _patientMost)
                return true;
            if (D.Kit is not { } hud)
                return false;
            foreach (TFMateHealth m in hud.Mates)
            {
                if ((m.Why & TFMateSeen.Calling) != 0 && D._self.Origin.DistanceTo(m.At) <= BeamHold)
                    return true;
            }

            return false;
        }
    }
}

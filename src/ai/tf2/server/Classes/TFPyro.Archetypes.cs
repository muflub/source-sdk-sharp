using SourceSharp.Ai.TF2.Senses;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

public sealed partial class TFDecider
{
    /// <summary>
    /// The Pyro's archetypes past the stock one (research pyro.md §2, §4.3's table; the review's ship order: the Combo before
    /// the Guard), each a rule set on the shared play:
    /// <list type="bullet">
    /// <item>Combo ("puff and sting": Degreaser, Flare Gun, Axtinguisher): the flare at mid range (it lights a target and
    /// crits a burning one), holding its range on a burning target instead of closing; the Axtinguisher on a burning target
    /// in reach; the Degreaser's fast switch (<see cref="ComboSwitchSeconds"/>).</item>
    /// <item>Backburner (Backburner, Shotgun, Axtinguisher): the flame only on a target facing away (the back crit's own test,
    /// TFFlameManager's 0.8) or right on top of it, swinging wide round a target that faces it; a blast only for a rocket at it
    /// or an Über (each costs 50 of its 200).</item>
    /// </list>
    /// </summary>
    private sealed partial class PyroPlay
    {
        /// <summary>The Combo's weapon changes: the Degreaser deploys 60 % and holsters 30 % faster (wiki), so the combo's switches come this close.</summary>
        public const float ComboSwitchSeconds = 0.25f;

        /// <summary>The Combo's flare reaches this far (research A3's 300-1200 u harassing band).</summary>
        public const float FlareReach = 1200f;

        /// <summary>The Combo takes the Axtinguisher to a burning target this near (a melee weapon's reach and a step).</summary>
        public const float AxeNear = 110f;

        /// <summary>A Soldier or a Demoman this near keeps the Combo's Degreaser out, for his spam.</summary>
        public const float SpamNear = 500f;

        /// <summary>The Backburner's flame from the front only this near: right on top of it there is no going round.</summary>
        public const float FrontFlameNear = 120f;

        /// <summary>The back crit's test (TFFlameManagerDefs.BehindCritThreshold, stock tf_flame.cpp:730-745): the flame's way along the target's flat facing over this.</summary>
        public const float BehindDot = 0.8f;

        private long _flankDecides, _backFlames, _flareOuts, _axeOuts;

        private bool IsCombo => Archetype == TFPyroClass.Combo;

        private bool IsBackburner => Archetype == TFPyroClass.Backburner;

        /// <summary>Whether the target in sight burns, as the bot's client draws it.</summary>
        private bool TargetBurning
            => D._target is { } t && D._view.TryActor(t.Id, out ActorFacts f) && (f.Conditions & ActorConditions.Burning) != 0;

        /// <summary>Whether the target in sight faces away from the bot: the way from the bot to it along its flat facing over <see cref="BehindDot"/>.</summary>
        private bool TargetFacesAway
        {
            get
            {
                if (D._target is not { } t || !D._view.TryActor(t.Id, out ActorFacts f))
                    return false;
                float yaw = f.Facing.Yaw * (MathF.PI / 180f);
                var facing = new AiVector(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
                AiVector way = new AiVector(f.Origin.X - D._self.Origin.X, f.Origin.Y - D._self.Origin.Y, 0f).Normalised();
                return way.Dot(facing) > BehindDot;
            }
        }

        /// <summary>
        /// The Combo's weapon against its target in sight: the Degreaser against a Soldier's or a Demoman's spam within
        /// <see cref="SpamNear"/>; the Axtinguisher on a burning target within <see cref="AxeNear"/>; the flare past the flame's
        /// reach out to <see cref="FlareReach"/>; else the Degreaser.
        /// </summary>
        private int ComboWeapon(in TFKitItem flame, TFClass them)
        {
            int secondary = D.SecondaryKind, melee = D.KindIn(TFLoadoutSlot.Melee);
            float d = D._targetDist;
            if ((them is TFClass.Soldier or TFClass.Demoman) && d < SpamNear && _flameAmmo > 0)
                return flame.WeaponKind;
            if (melee >= 0 && TargetBurning && d <= AxeNear)
                return Took(melee, ref _axeOuts);
            if (secondary >= 0 && (_flameAmmo <= 0 || (d > FlameReach && d <= FlareReach) || them == TFClass.Pyro))
                return Took(secondary, ref _flareOuts);
            return flame.WeaponKind;
        }

        private int Took(int kind, ref long outs)
        {
            if (kind != D._self.ActiveWeapon)
                outs++;
            return kind;
        }

        /// <summary>
        /// The Backburner's flame gate: on a target facing away (its crit), or one within <see cref="FrontFlameNear"/>; never a
        /// front fight at range, which it goes round instead (<see cref="Flank"/>).
        /// </summary>
        private bool BackburnerMayFlame()
        {
            return TargetFacesAway || D._targetDist <= FrontFlameNear;
        }

        /// <summary>
        /// The Backburner's footwork on a target facing it: wide round its side (mostly across, a little in), the side kept for
        /// <see cref="CircleSeconds"/>, so the bot comes to its back; true when it did.
        /// </summary>
        private bool Flank()
        {
            if (TargetFacesAway || D._targetDist <= FrontFlameNear)
                return false;
            AiVector to = new AiVector(D._targetPos.X - D._self.Origin.X, D._targetPos.Y - D._self.Origin.Y, 0f).Normalised();
            var across = new AiVector(-to.Y, to.X, 0f);
            int side = Consistent(CircleSeconds, 0x464C4E4B) < 0.5f ? 1 : -1;
            if (D.NoRoom(across * side))
                side = -side;
            D._move = new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, (across * side + to * 0.35f).Normalised(), false, 0);
            D._verdict = "flanking";
            _flankDecides++;
            return true;
        }

        /// <summary>The Combo on a burning target past the flame's reach holds its range and flares it, sidestepping, rather than close.</summary>
        private bool HoldFlareRange()
        {
            if (!TargetBurning || D._targetDist <= FlameReach || D._targetDist > FlareReach)
                return false;
            if (D.Feel.Fight)
                D.FightStep(0f);
            else
                D.Strafe(0f);
            D._verdict = "flaring";
            return true;
        }

        private void CountArchetypes(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("pyro.flank_decides", _flankDecides));
            into.Add(new("pyro.back_flames", _backFlames));
            into.Add(new("pyro.flare_outs", _flareOuts));
            into.Add(new("pyro.axe_outs", _axeOuts));
        }
    }
}

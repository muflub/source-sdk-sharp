using System.Collections.Immutable;
using SourceSharp.Ai.TF2.Knowledge;
using TF2.Server.Econ;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Medic's own (plan_bots_teams_tf2 C.3, C.3.1, P7): its play (<see cref="TFDecider.ClassPlay"/>'s members it
/// overrides), its ability rows, its archetypes and whether the composition may field it. The Medic lane's file:
/// another class's lane never edits it, and it never edits another's.
/// </summary>
public static class TFMedicClass
{
    /// <summary>Whether the composition fields the Medic (C.4): false until the Medic lane says its play is ready: until then the composition fields no Medic (a pin still may).</summary>
    public const bool Ready = false;

    /// <summary>
    /// The Über: both triggers (stock's bot presses both, tf_bot_medic_heal.cpp:612, :696) for <see cref="PopTicks"/> commands, longer than
    /// a decide period, so the attack is held until the next decide's beam (an ability one-shot's intent holds no trigger of its own: the
    /// Fable review, C5).
    /// </summary>
    public const string Pop = "medic_uber";

    /// <summary>"ÜberCharge ready!" (the voice menu's second menu, eighth line: TLK_PLAYER_CHARGEREADY).</summary>
    public const string VoiceUberReady = "medic_voice_uber_ready";

    /// <summary>Commands the pop holds both triggers: over one decide period (6), so the beam is held into the next decide's Fire.</summary>
    public const int PopTicks = 8;

    /// <summary>
    /// The Medic's ability rows, appended in its own block of the one table (<see cref="TFClassSpecs.BlockOf"/>): a row
    /// added here moves no other row, so recordings keep their meaning. Add at the end only.
    /// </summary>
    public static ImmutableArray<AbilityRow> Abilities() =>
    [
        new AbilityRow(Pop, AbilityRun.Buttons, Buttons: (int)(SourceSharp.Abi.InputButtons.Attack | SourceSharp.Abi.InputButtons.Attack2), HoldTicks: PopTicks),
        new AbilityRow(VoiceUberReady, AbilityRun.Command, TFAbilities.Voice(1, 7)),
    ];

    /// <summary>The archetypes' names (medic.md §2.1, §4.1 as the review corrected it).</summary>
    public const string Stock = "Stock", Combo = "Combo", Kritz = "Kritz", QuickFix = "QuickFix";

    /// <summary>The items the archetypes are drawn round (items_working.tsv; an item with no attributes stands for its reskins).</summary>
    public const int MediGun = 29, Kritzkrieg = 35, QuickFixGun = 411, SyringeGun = 17, Crossbow = 305, Bonesaw = 8, Ubersaw = 37;

    /// <summary>
    /// The Medic's archetypes (C.3.1), drawn by the bot's seed and weight (medic.md §4.2, a design choice): the stock Über
    /// kit, the competitive Combo kit (Crusader's Crossbow, Ubersaw), the Kritzkrieg and the Quick-Fix. What each plays
    /// differently is read off the kit (TFMedicKits.cs).
    /// </summary>
    public static TFArchetypeTable Archetypes() => new(TFClass.Medic,
    [
        new TFArchetype(Stock, 25f, [new(TFLoadoutSlot.Primary, [SyringeGun]), new(TFLoadoutSlot.Secondary, [MediGun]), new(TFLoadoutSlot.Melee, [Bonesaw])]),
        new TFArchetype(Combo, 30f, [new(TFLoadoutSlot.Primary, [Crossbow]), new(TFLoadoutSlot.Secondary, [MediGun]), new(TFLoadoutSlot.Melee, [Ubersaw])]),
        new TFArchetype(Kritz, 15f, [new(TFLoadoutSlot.Primary, [Crossbow]), new(TFLoadoutSlot.Secondary, [Kritzkrieg]), new(TFLoadoutSlot.Melee, [Ubersaw])]),
        new TFArchetype(QuickFix, 15f, [new(TFLoadoutSlot.Primary, [Crossbow]), new(TFLoadoutSlot.Secondary, [QuickFixGun]), new(TFLoadoutSlot.Melee, [Bonesaw])]),
    ]);

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes.</summary>
    public static TFClassSpec Spec() => new(TFClass.Medic, Ready, Abilities(), Archetypes());
}

public sealed partial class TFDecider
{
    /// <summary>
    /// The Medic's play (medic.md §4.4 over stock's CTFBotMedicHeal, TFMedicHeal.cs): a patient chosen and beamed, the
    /// medigun out unless the bot must fight, a place behind the patient out of enemy sight, the Über popped to save or to
    /// push, and "ÜberCharge ready!" said when the bar fills. With no kit read (a replay of an older recording) or no
    /// medigun in it, the general play.
    /// </summary>
    private sealed partial class MedicPlay(TFDecider d) : ClassPlay(d, TFClass.Medic)
    {
        public override void Hands()
        {
            if (!Refresh())
            {
                D.GeneralHands();
                return;
            }

            MedicHands();
        }

        public override void Engage()
        {
            if (!Refresh())
            {
                D.GeneralEngage();
                return;
            }

            MedicEngage();
        }

        public override void OneShots()
        {
            if (Refresh())
                MedicOneShots();
        }

        public override float Weigh(string branch) => Refresh() ? MedicWeigh(branch) : 1f;

        public override float PlaceUtility() => Refresh() ? PlaceScore() : 0f;

        public override bool GoPlace() => Refresh() && GoHealPlace();

        public override void Count(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("medic.ubers", _ubers));
            into.Add(new("medic.dropped", _dropped));
            into.Add(new("medic.beam", _beamDecides));
            into.Add(new("medic.alive", _aliveDecides));
            into.Add(new("medic.patients", _patientChanges));
            into.Add(new("medic.calls", _callsAnswered));
            into.Add(new("medic.ready_said", _readySaid));
            into.Add(new("medic.near", _nearDecides));
            into.Add(new("medic.bolts", _bolts));
            into.Add(new("medic.swings", _swings));
            into.Add(new("medic.charge_peak", _peakCharge));
            into.Add(new("medic.beam_on", _beamOnDecides));
            into.Add(new("medic.seek_picks", _seekPicks));
            into.Add(new("medic.pop_sends", _popSends));
            into.Add(new("medic.beam_drops", _beamDrops));
        }

        public override void Died() => MedicDied();
    }
}

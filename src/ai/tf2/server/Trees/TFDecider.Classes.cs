using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The class subtrees (plan_bots_teams_tf2 C.3, P7): each class's own play is a <see cref="ClassPlay"/>
/// nested in the decider, in that class's file (<c>src/ai/tf2/server/Classes/TF&lt;Class&gt;.cs</c>), so it reaches
/// the decider's parts (its target, its goal, the hands, the follower's ways) while its own fields stay its own:
/// nine lanes can each write one class at once without a name of theirs meeting another's. The tree calls the
/// play at fixed points every decide (<see cref="ClassPlay"/>'s members); a play that overrides none of them
/// plays as every bot did before P7: the general fight with whatever it holds, the objectives and the team layer.
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>The branch of the root a class's own place is weighed by (<see cref="ClassPlay.PlaceUtility"/>).</summary>
    public const string ClassBranch = "Class";

    /// <summary>
    /// What a class does that another does not, at the points the tree asks (plan_bots_teams_tf2 C.3): the
    /// hands' weapon choice, the fire, the fight's footwork, the one-shots, its own place, and its say in the
    /// objective branches and the team role. Every member's base is the general play; a class overrides what
    /// it plays differently. One object per class for the decider's life, so what a class keeps between lives
    /// (a sticky count, a sentry's place) is its own fields. <see cref="Archetype"/> is the way of playing the
    /// bot drew for the class (C.3.1), or a pinned one.
    /// </summary>
    internal abstract class ClassPlay(TFDecider d, TFClass playerClass)
    {
        /// <summary>The decider whose parts the play drives.</summary>
        protected TFDecider D { get; } = d;

        /// <summary>The class this play is.</summary>
        public TFClass Class { get; } = playerClass;

        /// <summary>The archetype the bot plays this class as (C.3.1): drawn by its seed, or pinned.</summary>
        public string Archetype { get; set; } = "";

        /// <summary>The subtree's name in the doing record and the trace: the class's.</summary>
        public virtual string Name => Class.ToString();

        /// <summary>The weapon choice beside the fire (a switch, a reload in a lull): the general hands by default.</summary>
        public virtual void Hands() => D.GeneralHands();

        /// <summary>The aim and the trigger on a target in sight: the general fire by default.</summary>
        public virtual void Engage() => D.GeneralEngage();

        /// <summary>The fight's footwork with the target in sight (the "fight at range" leaf): the general range-keeping by default.</summary>
        public virtual bool FightInSight() => D.FightAtRange();

        /// <summary>The class's one-shots after the hands (an ability row: <see cref="Knowledge.TFAbilities"/>): none by default.</summary>
        public virtual void OneShots()
        {
        }

        /// <summary>A factor on the score of root branch <paramref name="branch"/>: the class's say in the objectives (1: as the general play).</summary>
        public virtual float Weigh(string branch) => 1f;

        /// <summary>The team role the class plays when its team dealt it <paramref name="dealt"/>: the dealt one by default.</summary>
        public virtual TFTeamRole Role(TFTeamRole dealt) => dealt;

        /// <summary>The score of going to the class's own place (<see cref="ClassBranch"/>: a sentry spot, a sight line); 0, never, by default.</summary>
        public virtual float PlaceUtility() => 0f;

        /// <summary>Goes to the class's own place when its branch won; false when it has none now.</summary>
        public virtual bool GoPlace() => false;

        /// <summary>
        /// The class's terms for the shared ammo errand (round 2's S5, TFDecider.Ammo.cs): its floor and its utility. Off by
        /// default: a class that names none never goes for ammo on its own account.
        /// </summary>
        public virtual TFAmmoErrand Ammo => TFAmmoErrand.Off;

        /// <summary>
        /// The rounds the errand's floor is weighed against (<see cref="Ammo"/>): the reserve of the weapon in hand by default;
        /// under 0, not known (no errand). A class whose pool is one weapon's (the Heavy's minigun) answers that weapon's.
        /// </summary>
        public virtual int AmmoPool => D._self.Reserve;

        /// <summary>
        /// The general play's weight for answering a sapped friendly building, every class's default (round 2's S7): over the run
        /// for a far flag (0.7) and a far fight (0.6), under a near fight (0.8), a heal (0.82) and the run for a near flag (0.9).
        /// Ours: a Spy at a sentry is the nearest threat to the team's hold. Each class lane rules on it for its class by
        /// overriding <see cref="AnswersSapped"/> (a Medic leaving his patient, a Demoman leaving a sentry kill).
        /// </summary>
        public const float SappedAnswerWeight = 0.75f;

        /// <summary>
        /// The score of the general play's answer to a sapped friendly building in reach (TFDecider.Sapped.cs): going to stand by
        /// it. <see cref="SappedAnswerWeight"/> by default; 0, never (a class whose own place answers it).
        /// </summary>
        public virtual float AnswersSapped => SappedAnswerWeight;

        /// <summary>Whether the class keeps out of a sentry's range (the general play's: yes); a class that kills or saps one says no.</summary>
        public virtual bool AvoidsSentries => true;

        /// <summary>
        /// Whether the bot backs off and loads when its primary is dry with the target in sight (the shared fight's first
        /// leaf, "launcher dry"): yes by default. A class with a loaded secondary, or a target within a swing, says no and
        /// fights on with its own footwork while its hands swap (round 2's S10: the Scout).
        /// </summary>
        public virtual bool BacksOffDry => true;

        /// <summary>
        /// What the class asks of the hands its rocket jump's program lends it in flight (round 2's S1, <see cref="Jump.JumpHands"/>):
        /// asked each decide the bot is in the air of <paramref name="flight"/>, its program's; null, nothing: the program flies the
        /// jump exactly as it would with no class at all. None by default.
        /// </summary>
        public virtual Jump.AirAsk? InFlight(Jump.AirFlight flight) => null;

        /// <summary>Whether the bot carries out the reflex <paramref name="verdict"/> names (a dodge, backing off an Über, a sentry's range): yes by default; a Pyro may shove the Über instead.</summary>
        public virtual bool TakesReflex(string verdict) => true;

        /// <summary>The class's bonus to a target's priority (a Spy's to a Medic, a Sniper's to a Sniper): 0 by default.</summary>
        public virtual float TargetBonus(KnownActor target, bool sighted, float distance) => 0f;

        /// <summary>The share of its health under which the bot in a lull goes to heal (<see cref="TFDecider.HurtShare"/> by default).</summary>
        public virtual float HurtShare => TFDecider.HurtShare;

        /// <summary>The least time between two weapon changes or lull reloads of the hands (<see cref="TFDecider.HandsEverySeconds"/> by default; a combo wants less).</summary>
        public virtual float SwitchSeconds => TFDecider.HandsEverySeconds;

        /// <summary>
        /// Holds the trigger at teammate <paramref name="mate"/> on purpose (the Medic's beam, a Pyro's spy-check puff), aimed at
        /// <paramref name="at"/>: the friend gate lets it go this decide. Call it from <see cref="Engage"/> or <see cref="OneShots"/>.
        /// Every friend-fire is a hold: a one-shot this decide keeps the trigger down; a decide that does not call it lets go.
        /// </summary>
        protected void FireAtFriend(ActorId mate, AiVector at) => D.FireAtFriend(mate, at);

        /// <summary>
        /// The class's own counters for the A/B harness (<c>/ai/classes</c>, <c>tools/ai-tf-class</c>): ubers popped, sentry
        /// seconds up, backstabs. Add each as a name and a count; none by default.
        /// </summary>
        public virtual void Count(List<KeyValuePair<string, long>> into)
        {
        }

        /// <summary>The bot died: what the class forgets with a life.</summary>
        public virtual void Died()
        {
        }
    }

    /// <summary>A class with no play of its own (a class the mod adds, the civilian): the general play, named by its class.</summary>
    private sealed class GeneralPlay(TFDecider d, TFClass playerClass) : ClassPlay(d, playerClass);

    private readonly ClassPlay?[] _plays = new ClassPlay?[(int)TFClass.CountAll];

    /// <summary>A fact's own plays, asked before the classes' files (null, or an answer of null: the class's own).</summary>
    internal Func<TFDecider, TFClass, ClassPlay?>? Plays { get; init; }
    private ClassPlay _classPlay = null!;

    /// <summary>The play of <paramref name="playerClass"/>, made at its first life as it and kept for the decider's.</summary>
    private ClassPlay PlayOf(TFClass playerClass)
    {
        int i = (int)playerClass;
        if ((uint)i >= (uint)_plays.Length)
            return new GeneralPlay(this, playerClass);
        if (_plays[i] is { } made)
            return made;
        ClassPlay play = Plays?.Invoke(this, playerClass) ?? playerClass switch
        {
            TFClass.Scout => new ScoutPlay(this),
            TFClass.Soldier => new SoldierPlay(this),
            TFClass.Pyro => new PyroPlay(this),
            TFClass.Demoman => new DemomanPlay(this),
            TFClass.HeavyWeapons => new HeavyPlay(this),
            TFClass.Engineer => new EngineerPlay(this),
            TFClass.Medic => new MedicPlay(this),
            TFClass.Sniper => new SniperPlay(this),
            TFClass.Spy => new SpyPlay(this),
            _ => new GeneralPlay(this, playerClass),
        };
        play.Archetype = ArchetypeOf(playerClass);
        return _plays[i] = play;
    }

    /// <summary>What the bot was pinned to on <c>ai_bot_add</c> (a class, an archetype), or null: its archetypes are its seed's draw.</summary>
    public TFBotPin? Pin { get; init; }

    /// <summary>
    /// The archetype the bot plays <paramref name="playerClass"/> as (C.3.1): its pin's, else its seed's draw from the class's
    /// file, the draw its items followed (<see cref="TFArchetypeItems"/>); "" for a class with none.
    /// </summary>
    private string ArchetypeOf(TFClass playerClass)
        => (Pin is { } pin ? pin.ArchetypeFor(playerClass) : TFClassSpecs.Of(playerClass)?.Ways.Draw(_seed))?.Name ?? "";

    private readonly int[] _abilityUses = new int[AbilityCall.MaxRow + 1];

    /// <summary>The ability one-shots the bot has sent, by row of the table, for the A/B harness (<c>/ai/classes</c>).</summary>
    public ReadOnlySpan<int> AbilityUses => _abilityUses;

    /// <summary>
    /// The counters of every class the bot has played (<see cref="ClassPlay.Count"/>), each name once, for the A/B harness; then
    /// the shared hooks' (round 2), each once it has counted: <c>shared.ammo_trips</c>, <c>shared.sapped_answer_decides</c>,
    /// <c>shared.teleporter_trips</c>, <c>shared.teleporter_give_ups</c>, <c>shared.teleporter_weighs</c> (with the last one's
    /// <c>shared.teleporter_last_saving_u</c>), <c>shared.friendly_building_sightings</c>, <c>shared.ready_entrance_sightings</c>,
    /// the weighs' skips (<c>shared.teleporter_skips_far|no_exit|no_walk</c>) and their work (<c>shared.teleporter_searches</c>,
    /// <c>_expanded</c>, <c>_search_us</c>, <c>_weigh_most_us</c>).
    /// </summary>
    public List<KeyValuePair<string, long>> ClassCounters()
    {
        var into = new List<KeyValuePair<string, long>>();
        foreach (ClassPlay? play in _plays)
            play?.Count(into);
        // The general play's: decides whose team place read a teammate by his respawn glow (round 2's S11), once there is one.
        if (_glowDecides > 0)
            into.Add(new("team.glow_decides", _glowDecides));
        CountDash(into);
        Shared(into, "shared.ammo_trips", AmmoTrips);
        Shared(into, "shared.sapped_answer_decides", SappedAnswerDecides);
        Shared(into, "shared.teleporter_trips", TeleporterTrips);
        Shared(into, "shared.teleporter_give_ups", TeleporterGiveUps);
        Shared(into, "shared.friendly_building_sightings", FriendlyBuildingSightings);
        Shared(into, "shared.ready_entrance_sightings", ReadyEntranceSightings);
        Shared(into, "shared.teleporter_weighs", TeleporterWeighs);
        Shared(into, "shared.teleporter_skips_far", TeleporterSkipsFar);
        Shared(into, "shared.teleporter_skips_no_exit", TeleporterSkipsNoExit);
        Shared(into, "shared.teleporter_skips_no_walk", TeleporterSkipsNoWalk);
        SharedLong(into, "shared.teleporter_searches", TeleporterSearches);
        SharedLong(into, "shared.teleporter_expanded", TeleporterExpanded);
        SharedLong(into, "shared.teleporter_search_us", TeleporterSearchMicros);
        SharedLong(into, "shared.teleporter_weigh_most_us", TeleporterWeighMostMicros);
        if (TeleporterWeighs > 0)
            into.Add(new("shared.teleporter_last_saving_u", float.IsFinite(TeleporterLastSaving) ? (long)TeleporterLastSaving : long.MinValue));
        return into;

        static void Shared(List<KeyValuePair<string, long>> into, string name, int count) => SharedLong(into, name, count);

        static void SharedLong(List<KeyValuePair<string, long>> into, string name, long count)
        {
            if (count > 0)
                into.Add(new(name, count));
        }
    }

    /// <summary>The archetype the bot plays its class as now ("" for none), for the routes and the facts.</summary>
    public string Archetype => _classPlay?.Archetype ?? "";

    /// <summary>What the class branch goes to: the class's own place, or a shared errand the class asks for (round 2's hooks).</summary>
    private enum ClassErrand : byte
    {
        Own = 0,
        Ammo,
        Sapped,
    }

    private ClassErrand _classErrand;

    /// <summary>
    /// The class branch's score, weighed as every branch is: the best of the class's own place (<see cref="ClassPlay.PlaceUtility"/>)
    /// and the shared errands it asks for: the ammo errand (<see cref="ClassPlay.Ammo"/>) and the answer to a sapped friendly
    /// building (<see cref="ClassPlay.AnswersSapped"/>). The class's own place wins a tie, then the ammo.
    /// </summary>
    private float ClassUtility()
    {
        float own = _classPlay.PlaceUtility(), ammo = AmmoErrandUtility(), sapped = SappedUtility();
        _classErrand = sapped > own && sapped > ammo ? ClassErrand.Sapped : ammo > own ? ClassErrand.Ammo : ClassErrand.Own;
        return MathF.Max(own, MathF.Max(ammo, sapped));
    }

    /// <summary>To what won the class branch's score; the others, in the tie's order, when that has nothing now.</summary>
    private bool GoClassPlace() => _classErrand switch
    {
        ClassErrand.Sapped => GoSapped() || (_classPlay.PlaceUtility() > 0f && _classPlay.GoPlace()) || (AmmoErrandUtility() > 0f && GoAmmo()),
        ClassErrand.Ammo => GoAmmo() || (_classPlay.PlaceUtility() > 0f && _classPlay.GoPlace()) || (SappedUtility() > 0f && GoSapped()),
        _ => _classPlay.GoPlace() || (AmmoErrandUtility() > 0f && GoAmmo()) || (SappedUtility() > 0f && GoSapped()),
    };
}

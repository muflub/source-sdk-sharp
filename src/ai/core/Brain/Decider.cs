namespace SourceSharp.Ai;

/// <summary>What a decider is made from: the bot, its seed, its memory and its skills' parameters (§10).</summary>
public sealed record BrainSetup(ActorId Bot, ulong Seed, BotMemory Memory, DecisionParams Params, float TickSeconds);

/// <summary>
/// What the brain is doing and planning (§7.2's third part), as of its last
/// decide: the chosen branch, the goal, the opponent it is about, the
/// push / hold / retreat verdict and its window arithmetic, the reason terms.
/// The strings are the tree's node names (constants): nothing is built per decide.
/// </summary>
public readonly record struct DoingRecord(
    string Branch,
    string Leaf,
    bool HasGoal,
    AiVector Goal,
    ActorId Target,
    string Verdict,
    float PEmpty,
    float PReloading,
    float WindowSeconds,
    float ArriveSeconds,
    float ScoreboardTerm,
    float AmmoTerm,
    Tick Expires,
    float GrudgeTerm = 0f,
    bool PreAiming = false,
    AiVector HabitSpot = default,
    int MomentSeq = 0,
    string MomentKind = "",
    ActorId MomentOther = default,
    int LineAsk = 0,
    AiVector LineAt = default,
    string Prefire = "",
    AiVector PrefireAt = default,
    string Streak = "",
    float StreakAimErrorDeg = 0f,
    float StreakReactionMs = 0f,
    float StreakRecognizeSeconds = 0f);

/// <summary>
/// A game's decision layer for one bot (plan_hl2mp_ai §9 Phase 5 step 3):
/// one decide from the bot's view and memory to an intent, with the tree's
/// trace and what it is doing. The core holds the interface; a game's binding
/// builds the trees (HL2MP's: SourceSharp.Ai.Hl2mp.Trees).
/// </summary>
public interface IDecider
{
    IntentCommand Decide(Tick tick, PerceivedView view, int seq);

    BtTrace Trace { get; }

    DoingRecord Doing { get; }

    /// <summary>The tree's node names by id, for the trace's readers.</summary>
    IReadOnlyList<string> NodeNames { get; }

    /// <summary>Each node's branch by id (<see cref="BehaviorTree{T}.Branches"/>), for a recording to name a leaf's branch.</summary>
    IReadOnlyList<int> NodeBranches { get; }
}

//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

namespace SourceSharp.Shared;

/// <summary>
/// WHEN A BODY GOES, AND WHETHER A MAP SHOWS BODIES AT ALL.
///
/// <para>
/// This is <c>game/shared/ragdoll_shared.cpp</c>'s two policy classes —
/// <c>CRagdollLowViolenceManager</c> (lines 46–82) and
/// <c>CRagdollLRURetirement</c> (lines 729–1112) — as decisions rather than as
/// a game system. Step 4d's handover named both by name as
/// <i>"policy that is still native"</i>: <i>how many ragdolls to keep
/// (<c>CRagdollLRURetirement</c>) and which maps are low-violence are decisions
/// about what the game does, and belong in managed code"</i>. §6 of
/// plan_entity.md keeps only <i>the ragdoll builder</i> in C++ — the solver
/// calls, the constraint setup, the bone snapshot — and none of that is here.
/// </para>
///
/// <para>
/// PURE, AND DELIBERATELY SO. Nothing in this file asks the engine anything.
/// Every input arrives as an argument, which is what lets the same rules be
/// checked against stock's own source text in a test rather than only against
/// a live game — and what lets the CLIENT use them unchanged when its half of
/// <c>ragdoll_shared.cpp</c> is taken (<c>c_baseanimating.cpp</c> is the only
/// other writer of the list, at line 356 and 4845).
/// </para>
/// </summary>
public static class RagdollViolencePolicy
{
    /// <summary>
    /// The four legacy violence ConVars, in stock's own order.
    ///
    /// <para>
    /// <c>util_shared.cpp</c>'s <c>UTIL_IsLowViolence</c> — this tree's copy is
    /// <c>sourcesharp/game/shared/util_shared_library.cpp:177</c> — answers
    /// <c>true</c> if ANY of the four is off, and otherwise defers to
    /// <c>engine-&gt;IsLowViolence()</c>. Its own comment says the four are
    /// legacy and kept "for testing low violence when the engine is in normal
    /// violence mode", which is exactly what makes them readable policy: they
    /// are ConVars, and managed code reads a ConVar by name.
    /// </para>
    /// </summary>
    public static readonly ImmutableArray<string> LegacyViolenceConVars =
    [
        "violence_hblood", "violence_ablood", "violence_hgibs", "violence_agibs",
    ];

    /// <summary>
    /// The maps that force violence back ON in an episodic build.
    ///
    /// <para>
    /// Stock's reason, quoted from the site: <i>"Turn the low violence ragdoll
    /// stuff off if we're in the HL2 Citadel maps because the player has the
    /// super gravity gun and fading ragdolls will break things."</i> A puzzle
    /// that needs a body to stay put is a GAME rule, which is why the list is
    /// here and not in the engine.
    /// </para>
    /// </summary>
    public static readonly ImmutableArray<string> EpisodicFullViolenceMaps =
    [
        "ep1_citadel_02", "ep1_citadel_02b", "ep1_citadel_03",
    ];

    /// <summary>The same list for a non-episodic build.</summary>
    public static readonly ImmutableArray<string> FullViolenceMaps =
    [
        "d3_citadel_03", "d3_citadel_04", "d3_citadel_05", "d3_breen_01",
    ];

    /// <summary>
    /// <c>UTIL_IsLowViolence</c>'s legacy half: is any of the four ConVars off?
    ///
    /// <para>
    /// THE OTHER HALF IS NOT HERE AND THAT IS MEASURED RATHER THAN GLOSSED.
    /// <c>engine-&gt;IsLowViolence()</c> is an engine call with no managed door
    /// on the server: <c>ClientIsLowViolence</c> exists
    /// (<c>sourcesharp.h:6312</c>) and <c>sourcesharp_gamesystem.cpp</c>'s own
    /// comment says it is client-only on purpose — <i>"a violence setting … is
    /// a question about this side's convars … The server answers none of them
    /// for this side."</i> The server's own answer wants a slot of its own,
    /// <c>ServerIsLowViolence</c>, taking nothing and returning an
    /// <c>int32_t</c>, wired beside <c>SurfacePropGameMaterial</c> as the
    /// seventh "one slot, two implementations".
    /// </para>
    ///
    /// <para>
    /// It is DESIGNED AND NOT REQUESTED, for step 6e's reason about the ragdoll
    /// identity slot: it has no caller that behaves differently today. Every
    /// non-German retail build answers <c>false</c>, so the composed answer
    /// below is exact wherever the four ConVars are the only thing set — and
    /// where it is not, <see cref="IsLowViolence"/> takes the engine's answer as
    /// an argument so the caller that finally has the slot needs no change here.
    /// </para>
    /// </summary>
    public static bool AnyLegacyConVarOff(bool humanBlood, bool alienBlood,
                                          bool humanGibs, bool alienGibs)
        => !humanBlood || !alienBlood || !humanGibs || !alienGibs;

    /// <summary><c>UTIL_IsLowViolence</c>, whole, with the engine's answer supplied.</summary>
    public static bool IsLowViolence(bool humanBlood, bool alienBlood, bool humanGibs,
                                     bool alienGibs, bool engineLowViolence)
        => AnyLegacyConVarOff(humanBlood, alienBlood, humanGibs, alienGibs) || engineLowViolence;

    /// <summary>
    /// <c>CRagdollLowViolenceManager::SetLowViolence</c>, as a decision.
    ///
    /// <para>
    /// The three rules in stock's own order, because the order is the rule:
    /// the engine/ConVar answer first, then the multiplayer override (SERVER
    /// ONLY — stock's <c>#if !defined( CLIENT_DLL )</c>), then the map list.
    /// A map on the list wins over everything, including multiplayer, because
    /// it is applied last.
    /// </para>
    ///
    /// <para>
    /// <paramref name="isServer"/> IS A PARAMETER AND NOT AN ASSUMPTION. The
    /// multiplayer clause is inside stock's server-only guard, so a client
    /// asking this question about a multiplayer map must still get
    /// <c>true</c> if the engine says so — the two sides genuinely disagree
    /// here and a shared rule that quietly picked one would be a divergence
    /// nothing could see.
    /// </para>
    /// </summary>
    public static bool LowViolenceForMap(bool baseLowViolence, bool isServer,
                                         bool isMultiplayer, bool episodic, string? mapName)
    {
        bool low = baseLowViolence;

        // "the server doesn't worry about low violence during multiplayer games"
        if (isServer && isMultiplayer)
            low = false;

        if (IsFullViolenceMap(episodic, mapName))
            low = false;

        return low;
    }

    /// <summary>
    /// Whether this map forces violence on. Case-insensitive, as stock's
    /// <c>Q_stricmp</c> is.
    /// </summary>
    public static bool IsFullViolenceMap(bool episodic, string? mapName)
    {
        if (string.IsNullOrEmpty(mapName))
            return false;

        ImmutableArray<string> list = episodic ? EpisodicFullViolenceMaps : FullViolenceMaps;

        foreach (string name in list)
        {
            if (string.Equals(name, mapName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

/// <summary>
/// How many bodies may stand, and which one goes when there is one too many.
///
/// <para>
/// <c>CRagdollLRURetirement</c>'s decisions, without its list of entity
/// pointers. The caller owns the bodies and their order; this owns the rules.
/// </para>
/// </summary>
public static class RagdollRetirementPolicy
{
    /// <summary>
    /// <c>g_ragdoll_maxcount</c>'s default in stock's <c>ragdoll_shared.cpp</c>.
    /// Four on Xbox, eight everywhere else, and this build is everywhere else.
    /// </summary>
    public const int DefaultMaxCount = 8;

    /// <summary>
    /// <c>g_ragdoll_important_maxcount</c>'s default
    /// (stock's <c>ragdoll_shared.cpp</c>).
    /// </summary>
    public const int DefaultImportantMaxCount = 2;

    /// <summary>
    /// The cap actually in force.
    ///
    /// <para>
    /// LOW VIOLENCE IS A CAP OF ZERO AND NOT A SEPARATE SWITCH — stock's
    /// <c>if ( g_RagdollLVManager.IsLowViolence() ) iMaxRagdollCount = 0;</c>.
    /// Written the same way here because the difference matters at the edge:
    /// a zero cap still runs the retirement loop, so bodies that are mid-effect
    /// are still skipped by <see cref="Retire"/> rather than being yanked.
    /// </para>
    ///
    /// <para>
    /// <paramref name="managerMaxCount"/> is <c>m_iMaxRagdolls</c>, which
    /// <c>ragdoll_manager.cpp</c> sets from a map entity. <c>-1</c> means "no
    /// manager has spoken", and then the ConVar decides. That file is NOT in
    /// this build's server project (<c>grep -c ragdoll_manager
    /// server_csharp.vpc</c> = 0), so <c>-1</c> is the only value that occurs
    /// today; the parameter exists because the rule is the rule.
    /// </para>
    /// </summary>
    public static int EffectiveCap(int managerMaxCount, int conVarMaxCount, bool lowViolence)
    {
        if (lowViolence)
            return 0;

        return managerMaxCount == -1 ? conVarMaxCount : managerMaxCount;
    }

    /// <summary>
    /// <c>ShouldRemoveThisRagdoll</c>'s SERVER arm (stock's <c>ragdoll_shared.cpp</c>),
    /// as a decision over three facts the caller looks up.
    ///
    /// <para>
    /// "Retire what nobody is looking at first" is a rule about how the game
    /// looks, which is why it is here; <i>whether</i> a body is in a player's
    /// PVS or view cone is a world query and stays where the world is.
    /// </para>
    ///
    /// <para>
    /// LOW VIOLENCE ANSWERS FIRST AND UNCONDITIONALLY, before either
    /// visibility test — so on a low-violence map every body is a candidate
    /// even the one the player is staring at. That is stock's order and it is
    /// the difference between "bodies fade quickly" and "bodies do not exist".
    /// </para>
    /// </summary>
    public static bool ShouldRemove(bool lowViolence, bool inAnyClientPvs, bool inPlayerViewCone)
    {
        if (lowViolence)
            return true;

        if (!inAnyClientPvs)
            return true;

        return !inPlayerViewCone;
    }

    /// <summary>
    /// <c>CRagdollLRURetirement::Update</c>'s FIRST pass: at most ONE body, the
    /// oldest that nobody can see, and only while the list is over its cap.
    /// Returns its index, or <c>-1</c>.
    ///
    /// <para>
    /// AT MOST ONE PER FRAME, and stock says so with a <c>return</c> out of
    /// <c>Update</c> rather than a <c>break</c> — so when this pass fires, the
    /// sweep below DOES NOT RUN AT ALL that frame. A version that ran both
    /// would retire the invisible body and then immediately sweep the visible
    /// ones down to the cap in the same frame, which is a pile of corpses
    /// vanishing at once in front of a player instead of one at a time behind
    /// them. <see cref="RetireThisFrame"/> is the pair, in stock's order.
    /// </para>
    /// </summary>
    public static int RetireOneUnseen(IReadOnlyList<RagdollFacts> bodies, int cap)
    {
        if (bodies.Count <= cap)
            return -1;

        for (int i = 0; i < bodies.Count; i++)
        {
            if (ShouldRemove(bodies[i].LowViolence, bodies[i].InAnyClientPvs, bodies[i].InPlayerViewCone))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// The whole of <c>Update</c>: the unseen pass, and — only if it retired
    /// nothing — the oldest-first sweep down to the cap.
    /// </summary>
    public static List<int> RetireThisFrame(IReadOnlyList<RagdollFacts> bodies, int cap)
    {
        int unseen = RetireOneUnseen(bodies, cap);

        if (unseen >= 0)
            return new List<int> { unseen };

        var effected = new bool[bodies.Count];

        for (int i = 0; i < bodies.Count; i++)
            effected[i] = bodies[i].StillEffected;

        return Retire(effected, cap);
    }

    /// <summary>
    /// Which bodies retire this frame, as indexes into <paramref name="stillEffected"/>
    /// — which is the LRU in order, OLDEST FIRST, one flag per body saying
    /// whether an effect entity still owns it (stock's
    /// <c>pRagdoll-&gt;GetEffectEntity()</c>: burning, or dissolving).
    ///
    /// <para>
    /// STOCK'S SECOND LOOP, which is the one that does the work in a
    /// non-episodic build. It walks oldest-first, SKIPS a body that is still
    /// burning or dissolving — <i>"Just ignore it until we're done
    /// burning/dissolving"</i> — and stops the moment the count is inside the
    /// cap. The skip does not consume the budget: a skipped body stays on the
    /// list and the loop keeps looking, which is why the answer can be shorter
    /// than <c>count - cap</c> and can even be empty.
    /// </para>
    ///
    /// <para>
    /// THE COUNT IT COMPARES IS THE WHOLE LIST'S AND NOT THE SURVIVORS'.
    /// <c>m_LRU.Count()</c> falls with each <c>Remove</c>, so the loop's guard
    /// sees the shrinking list; a version that compared "how many are left that
    /// are not skipped" would retire a different number whenever anything was
    /// mid-effect. Written the way stock wrote it, and
    /// <c>ABodyMidEffectDoesNotSpendTheBudget</c> is the fact that separates
    /// the two.
    /// </para>
    /// </summary>
    public static List<int> Retire(IReadOnlyList<bool> stillEffected, int cap)
    {
        var going = new List<int>();

        if (stillEffected.Count == 0)
            return going;

        int count = stillEffected.Count;

        for (int i = 0; i < stillEffected.Count; i++)
        {
            if (count <= cap)
                break;

            if (stillEffected[i])
                continue;

            going.Add(i);
            count--;
        }

        return going;
    }

    /// <summary>
    /// <c>MoveToTopOfLRU</c>'s non-important arm: the body goes to the TAIL,
    /// and an entry it already had is removed first.
    ///
    /// <para>
    /// THE REMOVE-FIRST IS THE WHOLE POINT and it is easy to lose. Without it a
    /// body that is touched twice occupies two slots, so the cap counts it
    /// twice and the oldest LIVE body is retired to make room for a duplicate
    /// of a body that is already there. Stock's loop breaks after the first
    /// match, so exactly one duplicate is removed; this does the same by
    /// removing the first match only.
    /// </para>
    /// </summary>
    public static void MoveToTop<T>(List<T> lru, T body)
    {
        int at = lru.IndexOf(body);

        if (at >= 0)
            lru.RemoveAt(at);

        lru.Add(body);
    }

    /// <summary>
    /// <c>MoveToTopOfLRU</c>'s IMPORTANT arm, which is a different rule and not
    /// a flag on the same one: the body is appended without a duplicate check,
    /// and if the list is now over its own cap the HEAD retires — one, this
    /// call, and never a sweep. Returns the index that retires, or <c>-1</c>.
    /// </summary>
    public static int ImportantOverflow(int importantCount, int importantCap)
        => importantCount > importantCap ? 0 : -1;
}

/// <summary>
/// The three facts <c>CRagdollLRURetirement::Update</c> looks up about one
/// body before it decides. Named rather than passed as three parallel arrays,
/// because the bug those invite is an off-by-one that lines a body's PVS
/// answer up with a different body's effect entity.
/// </summary>
public readonly struct RagdollFacts
{
    /// <summary>Is the map low-violence right now.</summary>
    public bool LowViolence { get; init; }

    /// <summary>
    /// <c>UTIL_FindClientInPVS( pRagdoll-&gt;edict() )</c> — is any client's
    /// potentially-visible set carrying this body.
    /// </summary>
    public bool InAnyClientPvs { get; init; }

    /// <summary><c>pPlayer-&gt;FInViewCone( pRagdoll )</c>.</summary>
    public bool InPlayerViewCone { get; init; }

    /// <summary>
    /// <c>pRagdoll-&gt;GetEffectEntity()</c> — an effect entity still owns
    /// this body: it is burning, or it is dissolving.
    /// </summary>
    public bool StillEffected { get; init; }
}

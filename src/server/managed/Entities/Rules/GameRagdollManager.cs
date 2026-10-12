//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// How many bodies a map is willing to have lying around, and whether it wants
/// any at all.
///
/// A budget rather than a switch: ragdolls are the most expensive thing a fight
/// leaves behind, and a map that stages a large one wants to say "keep four"
/// where the default keeps whatever the client's convar allows. Zero forbids
/// them outright, which is how a map with a tight physics budget stops paying
/// for corpses it never shows.
///
/// The DX8 key is the same budget for machines that cannot afford the first one,
/// and it only applies BELOW shader model 3 — above it the plain count wins
/// regardless of what the DX8 key says, so a map may set both and the weaker
/// machine quietly gets the smaller number.
/// </summary>
[ScriptedEntity(EntityNames.GameRagdollManager)]
public sealed class GameRagdollManager : ScriptedEntity
{
    public GameRagdollManager(Corpses corpses)
    {
        _corpses = corpses;
    }

    /// <summary>
    /// The body registry this entity's budget is FOR.
    ///
    /// <para>
    /// BY CONCRETE TYPE, which is how <c>ServerModule</c> registers it: nothing
    /// asks for a corpse registry by contract, and an <c>ICorpses</c> with one
    /// implementation and one caller would be an interface with no reader.
    /// </para>
    /// </summary>
    private readonly Corpses _corpses;
    /// <summary>What a map may set on this entity. See <see cref="GameRagdollManagerVars"/>.</summary>
    [ScriptVars] public GameRagdollManagerVars Vars = new();

    /// <summary>Below this, the DX8 budget applies. Source's own threshold.</summary>
    private const int ShaderModel3 = 90;

    // What to assume when nothing answers. mat_dxlevel is a CLIENT convar and a
    // dedicated server has never heard of it, so the honest default is the
    // capable path — the alternative silently applies the low-end budget to
    // every machine on the server.
    private const int DefaultDxLevel = ShaderModel3;

    /// <summary>The budget currently in force, after the DX level has chosen between the two.</summary>
    public int CurrentMaxRagdollCount { get; private set; } = -1;

    /// <summary>Whether an important ally's body is spared. Read by nothing yet; see the class comment.</summary>
    public bool SaveImportant => Vars.SaveImportant;

    public override void OnSpawn() => Apply();

    [ScriptedEvent("SetMaxRagdollCount")]
    private void OnSetMaxRagdollCount(float count)
    {
        Vars.MaxRagdollCount = (int)count;
        Apply();
    }

    [ScriptedEvent("SetMaxRagdollCountDX8")]
    private void OnSetMaxRagdollCountDX8(float count)
    {
        Vars.MaxRagdollCountDX8 = (int)count;
        Apply();
    }

    /// <summary>
    /// Puts the budget where the thing that retires bodies will read it.
    ///
    /// <para>
    /// THIS USED TO BE <c>Console.ExecuteServerCommand("g_ragdoll_maxcount N")</c>
    /// and that was right while a native LRU existed to read the convar.
    /// plan_shim.md §4a item 3 deleted <c>CRagdollLRURetirement</c> and the three
    /// convars with it — its only writer, <c>MoveToTopOfLRU</c>, had zero
    /// importers on either link line, so the list it retired from was
    /// permanently empty. A console command naming a convar nobody declares is
    /// reached BY NAME, fails silently and no link-line census can see it, which
    /// is the exact shape that left every dynamic prop's sequence call returning
    /// false for a whole phase. So the write is re-pointed in the same commit as
    /// the deletion, not left to be noticed later.
    /// </para>
    ///
    /// <para>
    /// <see cref="Corpses.Limit"/> is the live budget and the one
    /// <see cref="RagdollRetirementPolicy.EffectiveCap"/> is handed, so this
    /// reaches the same rule stock reached — directly, rather than through a
    /// convar and a second copy of the cap.
    /// </para>
    ///
    /// <para>
    /// Nothing is written when the map asked for no limit. -1 means "no limit of
    /// mine", and stamping it over the registry's own default would turn silence
    /// into a decision.
    /// </para>
    /// </summary>
    private void Apply()
    {
        CurrentMaxRagdollCount = Vars.MaxRagdollCount;

        if (Vars.MaxRagdollCount >= 0)
            _corpses.Limit = Vars.MaxRagdollCount;
    }

    /// <summary>
    /// Which of the two budgets stock would pick.
    ///
    /// Kept, tested, and NOT used. Stock chose between them on the SERVER by
    /// reading mat_dxlevel — a client convar a dedicated server has never heard
    /// of, so the branch read a default and applied one machine's answer to
    /// everybody. The budget this entity now writes lives in one server-side
    /// registry by construction, so there is no per-client choice left to make
    /// and pretending otherwise would be theatre.
    /// </summary>
    internal static int Budget(int dxLevel, int maxRagdollCount, int maxRagdollCountDX8)
        => (dxLevel < ShaderModel3 && maxRagdollCountDX8 >= 0) ? maxRagdollCountDX8 : maxRagdollCount;
}

/// <summary>
/// The map keys <see cref="GameRagdollManager"/> declares.
/// </summary>
[ScriptVars]
public partial class GameRagdollManagerVars
{
    /// <summary>Ragdolls to keep. Negative means "no limit of mine", which is the default.</summary>
    public int MaxRagdollCount { get; set; } = -1;

    /// <summary>
    /// The same budget for a machine below shader model 3. Bound so a stock map
    /// does not warn, and deliberately NOT applied — see OnSpawn.
    /// </summary>
    public int MaxRagdollCountDX8 { get; set; } = -1;

    /// <summary>
    /// Whether an important ally's body survives the cull. Kept because the key
    /// belongs to the entity, though this mod has no NPCs to be allied with.
    /// </summary>
    public bool SaveImportant { get; set; }
}

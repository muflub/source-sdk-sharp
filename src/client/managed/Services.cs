using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// A level coming and going on this client, as a hook: implement it on a
/// client <c>[SideService]</c> and the module calls it. The client's
/// counterpart to the server's <c>IGameLifecycle</c> level steps, for mods.
/// </summary>
public interface IClientLevelHook
{
    /// <summary>The level is up: after the engine's LevelInitPostEntity.</summary>
    void OnLevelInit(string mapName) { }

    /// <summary>The level is going, before the module forgets its state.</summary>
    void OnLevelShutdown() { }
}

/// <summary>
/// What a map has asked to say on screen.
///
/// <para>
/// The client's counterpart to the server's rules: a standalone entity that
/// wants to put something in front of the player asks for this rather than
/// reaching for the module's field. Which is the whole reason the client has a
/// container at all — the client hierarchy creates its entities the same way
/// the server does, on demand, and until now a client entity had no way to
/// reach anything the module owned.
/// </para>
///
/// <para>
/// <c>Paint</c> and <c>Subscribe</c> are off it, matching the server's rule:
/// they are the module's, once a frame and once at load. What is on it is what
/// can be asked at any moment.
/// </para>
/// </summary>
public interface IHudMessages
{
    /// <summary>Puts a message on one of the numbered channels.</summary>
    void Show(HudMessageArgs args, float now);

    /// <summary>Puts one resolved titles.txt entry on screen.</summary>
    void ShowTitle(string name, TextMessage entry, float now);

    /// <summary>The same, with the entry already unpacked.</summary>
    void ShowTitle(string name, string text, string clears,
                   float x, float y, Color32 color,
                   float fadeIn, float fadeOut, float hold, float now);

    /// <summary>How many channel messages are showing.</summary>
    int ActiveCount(float now);

    /// <summary>How many titles.txt messages are showing.</summary>
    int ActiveTitleCount(float now);
}

/// <summary>
/// What a map has asked to instruct, via <c>env_hudhint</c> — the lines that
/// name a key rather than describe one.
/// </summary>
public interface IHudHints
{
    /// <summary>The hint lines currently up, already resolved to bound keys.</summary>
    IReadOnlyList<string> Lines { get; }
}

/// <summary>
/// The HUD itself, and the two things a map can put on it.
///
/// <para>
/// A facade rather than a fourth service, because the two below are reached
/// through it in every existing caller and splitting them apart at the
/// container would leave three registrations where the code has one object.
/// Each is also registered in its own right, so an entity that only wants to
/// say something asks for <see cref="IHudMessages"/> and nothing more.
/// </para>
/// </summary>
public interface IHud
{
    /// <summary>What a map has asked to say.</summary>
    IHudMessages Messages { get; }

    /// <summary>What a map has asked to instruct.</summary>
    IHudHints Hints { get; }

    /// <summary>How faded the damage indicator is. 1 is a fresh hit, 0 is gone.</summary>
    float DamageFraction(float now);
}

/// <summary>
/// The dlights this client is keeping alive.
///
/// <para>
/// The other shape a service takes: an entity asks for this not to QUERY it but
/// to put itself IN it. A light has to be refreshed every frame whether or not
/// anything can see it — a lamp round a corner still lights the wall you can —
/// so it cannot drive itself from its own draw call, and something has to hold
/// the list of what to refresh.
/// </para>
///
/// <para>
/// <c>Frame</c> and <c>Reset</c> are off the interface, matching every other
/// service here: the module drives those, once a frame and once a level, and an
/// entity able to call Frame from inside its own update would re-enter the walk
/// it is standing in.
/// </para>
/// </summary>
public interface IDynamicLights
{
    /// <summary>Starts refreshing a source's lights every frame.</summary>
    void Add(IDynamicLightSource source);

    /// <summary>Stops, and releases what it was holding.</summary>
    void Remove(IDynamicLightSource source);

    /// <summary>
    /// Asks the pool for a light, or moves the one this key already has.
    ///
    /// <para>
    /// THROUGH THE REGISTRY RATHER THAN STRAIGHT AT THE ENGINE, because the
    /// registry is what knows which keys are lit — and a source that crossed on
    /// its own would leave that record guessing. One author, one ledger.
    /// </para>
    /// </summary>
    void Refresh(in DynamicLight light);

    /// <summary>
    /// Puts a key's light out — and does nothing at all if this side never lit
    /// one under that key. See <see cref="Refresh"/>.
    /// </summary>
    void Release(int key);

    /// <summary>
    /// Whether the registry believes a key currently holds a pool slot. For
    /// tests and the divergence check; nothing in the render path asks.
    /// </summary>
    bool IsLit(int key);

    /// <summary>How many keys are lit right now. Diagnostic.</summary>
    int LitCount { get; }

    /// <summary>
    /// How many times <see cref="Refresh"/> has crossed since the level loaded.
    /// Diagnostic: this one is expected to be one per lit source per frame.
    /// </summary>
    long Refreshes { get; }

    /// <summary>
    /// How many times <see cref="Release"/> has actually CROSSED since the
    /// level loaded — not how many times it was called.
    ///
    /// <para>
    /// The number the whole slot is about. Called once per dark source per
    /// frame; it should climb only when a light genuinely goes out, so a gate
    /// that switches one light off should move it by exactly one and then leave
    /// it still.
    /// </para>
    /// </summary>
    long Releases { get; }
}

/// <summary>
/// Something that wants a dlight kept alive for it.
///
/// <para>
/// Public rather than internal, which it was while the registry was a static.
/// A public interface cannot take an internal type in its signature, and the
/// alternative — an internal service interface with internal entity
/// constructors — would put the whole of this side's injection out of reach of
/// the reflection that performs it.
/// </para>
/// </summary>
public interface IDynamicLightSource
{
    /// <summary>Refresh, or ask for, this frame's lights.</summary>
    void UpdateLights();

    /// <summary>Give them back.</summary>
    void ReleaseLights();
}

/// <summary>
/// The colour-correction lookups this client is holding.
///
/// <para>
/// The same shape as <see cref="IDynamicLights"/> and for the same reason: a
/// correction is re-weighted every frame because the thing its weight depends
/// on — where the player is standing — moves on its own, and a correction round
/// a corner still tints the wall you can see.
/// </para>
/// </summary>
public interface IColorCorrections
{
    /// <summary>Starts re-weighting a correction every frame.</summary>
    void Add(IWeightedCorrection correction);

    /// <summary>Stops.</summary>
    void Remove(IWeightedCorrection correction);
}

/// <summary>
/// A colour correction whose weight depends on where the local player is
/// standing this frame.
///
/// <para>
/// Two classes qualify and they weigh differently — <c>color_correction</c> by
/// distance falloff, <c>color_correction_volume</c> by containment and a
/// crossing fade — which is why the service holds this and not either class:
/// what they share is only that the answer moves when the player does.
/// </para>
/// </summary>
public interface IWeightedCorrection
{
    /// <summary>Recomputes and applies this correction's weight for the frame.</summary>
    void Reweigh(Vector playerOrigin);
}

/// <summary>
/// What the local player is holding, as this client knows it — the HUD's feed.
///
/// <para>
/// Under the standalone player the inventory authority is the server's
/// Arsenal, and what the client sees of it is the CARRIED weapon's networked
/// state: one ss_weapon entity per armed player, whose WeaponTypeId moves on
/// a switch. The old HUD read C_BaseCombatWeapon, which is out of both
/// builds; this is the managed answer to the same three questions. The owned
/// LIST is deliberately not on it — the server does not network the
/// inventory behind the active weapon, and an interface that offered it
/// would have to invent one.
/// </para>
/// </summary>
public interface ILocalArsenal
{
    /// <summary>
    /// The weapon in the local player's hands: its classname, rounds loaded
    /// (-1 for a weapon with no clip — a crowbar) and the pool behind them.
    /// False when there is no local player, no carried weapon entity, or the
    /// hands are empty — the HUD draws nothing rather than a zeroed readout.
    /// </summary>
    bool TryGetActive(out string name, out int clip, out int reserve);

    /// <summary>
    /// The active weapon's alt-fire pool and its carry cap (SMG grenades,
    /// AR2 balls), as found by the last <see cref="TryGetActive"/>; false for a
    /// weapon without one.
    /// </summary>
    bool TryGetSecondary(out int current, out int max)
    {
        (current, max) = (-1, 0);
        return false;
    }
}

/// <summary>
/// Which standalone entities this client is holding right now.
///
/// <para>
/// A read-only view, and deliberately the only thing on it. The registry itself
/// creates, removes and hot-reloads entities and none of that is anybody else's
/// business; what a diagnostic — or a class that has to sweep its neighbours —
/// needs is the list, and an interface that offers only the list cannot be used
/// to reach past it.
/// </para>
/// </summary>
public interface IClientStandalones
{
    /// <summary>Every live client entity, as a snapshot. See the registry.</summary>
    IReadOnlyList<Standalone.ClientStandaloneEntity> Live();

    /// <summary>
    /// The managed client entity at an ENTITY INDEX, or null if that index is
    /// not one of ours.
    ///
    /// <para>
    /// BY INDEX AND NOT BY HANDLE, because a trace answers with an index: this
    /// is what makes the legacy tempent pool's collision arm answerable in C#
    /// (behaviour ledger row C4). Reading a client handle back through
    /// <c>CBaseHandle::Get()</c> is row C8, so the index never becomes one.
    /// </para>
    /// </summary>
    Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex);

    /// <summary>
    /// Every live client entity of a classname, which may end in '*' —
    /// <c>plan_entity.md</c> §3.5's find-by-class.
    ///
    /// <para>
    /// ON THE CONTRACT AND NOT ONLY ON THE REGISTRY, for the reason
    /// <c>IEntityDirectory.OfClass</c> is on the server's: two callers want it
    /// and they must not answer differently. Before this, every caller that
    /// wanted "the entities of this class" walked <see cref="Live"/> and
    /// compared classnames at its own call site, and the comparisons genuinely
    /// diverged — some exact, some a case-insensitive SUBSTRING, which is not
    /// what a Source classname lookup has ever meant.
    /// </para>
    ///
    /// <para>
    /// It is also the line §3.3 draws: this question is the managed
    /// directory's, and the native client slot table must never grow an arm
    /// that answers it.
    /// </para>
    /// </summary>
    /// <para>
    /// A DEFAULT IMPLEMENTATION, over <see cref="Live"/> and <c>SDK</c>
    /// matching rules, so that a view holding a plain list of entities answers
    /// this correctly without an index. <see cref="Standalone.ClientStandaloneRegistry"/>
    /// — the only implementation the game actually runs — overrides it with
    /// the directory's classname index, which costs the answer rather than the
    /// population. The default exists for the dozen test doubles and view
    /// objects that hold a list and nothing else; a caller cannot tell them
    /// apart, which is the point.
    /// </para>
    IEnumerable<Standalone.ClientStandaloneEntity> OfClass(string? className)
        => string.IsNullOrEmpty(className)
            ? Array.Empty<Standalone.ClientStandaloneEntity>()
            : Live().Where(e => Abi.EntityNameMatch.Match(className, e.ClassName));

    /// <summary>
    /// Every live client entity answering to a targetname, which may end in
    /// '*' — §3.5's find-by-name.
    ///
    /// <para>
    /// Empty on a live client today, because targetnames are not networked and
    /// nothing on this side sets one. That is the answer rather than a gap; see
    /// the registry.
    /// </para>
    /// </summary>
    /// <para>
    /// Defaulted to EMPTY rather than to a walk, and that is the difference
    /// between the two: a client entity's classname is on the object, so any
    /// implementation can answer <see cref="OfClass"/>; its targetname is held
    /// only by the directory, so an implementation without one has nothing to
    /// walk and must say so rather than invent an answer.
    /// </para>
    IEnumerable<Standalone.ClientStandaloneEntity> OfName(string? name)
        => Array.Empty<Standalone.ClientStandaloneEntity>();
}

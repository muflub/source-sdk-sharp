using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// One handle's "is this a hierarchy player", as the registry has it and as the
/// <c>EntityGetClassName</c> ABI slot answers it.
/// </summary>
public readonly struct PlayerMirrorComparison
{
    public PlayerMirrorComparison(uint handle, string slotClassName, bool registry, bool slot)
    {
        Handle = handle;
        SlotClassName = slotClassName;
        Registry = registry;
        Slot = slot;
    }

    public uint Handle { get; }
    public string SlotClassName { get; }
    public bool Registry { get; }
    public bool Slot { get; }

    public override string ToString()
        => $"handle {Handle} (slot says '{SlotClassName}'): "
         + $"registry={(Registry ? "player" : "not")} slot={(Slot ? "player" : "not")}";
}

/// <summary>
/// <c>cs_sv_player_mirror</c> — every entity's "is it an <c>ss_player</c>",
/// held against the ABI slot the answer replaced.
///
/// <para>
/// <b>The divergence gate for D19's <c>EntityGetClassName</c> row.</b>
/// <see cref="Arsenal.IsStandalonePlayer"/> used to ask the shell for a
/// classname once per touch — measured at <b>6.000 crossings per server
/// frame</b> while a player stands on an <c>item_ammo_*</c>, the largest of the
/// fourteen unowned A12 inversion rows. It is a registry lookup now, and a
/// registry lookup's failure mode is not staleness: it is a lifetime path
/// nobody pushed from, which answers confidently forever with no log line and
/// no crash.
/// </para>
///
/// <para>
/// <b>The slot is the oracle BECAUSE it was kept.</b>
/// <see cref="IEngineEntities.GetClassName"/> still goes straight to
/// <c>SourceSharp_EntityGetClassName</c>, which reads
/// <c>CStandaloneEntity::GetClassname</c> — the string the engine's own entity
/// reporting reads. Nothing managed wrote it. A gate that compared the registry
/// against something else managed had also written would agree with itself
/// whatever the shells did, which is the shape this project has been fooled by
/// more than forty times.
/// </para>
///
/// <para>
/// <b>BOTH DIRECTIONS, and the second is the one that matters.</b> Walking the
/// world catches a spawn push that never fired: an entity the engine calls
/// <c>ss_player</c> that the registry does not hold. It cannot catch the
/// opposite — a registry instance whose entity is gone or was re-classed —
/// because the walk is over the engine's list. So pass two walks
/// <c>OfClass(ss_player)</c> and asks the slot about each one. That is the
/// direction in which this inversion answers "yes, a player" for something that
/// is not one.
/// </para>
///
/// <para>
/// <b>At a gate, never per frame.</b> It crosses once per live entity, so
/// running it every frame puts back far more than the inversion removed —
/// 473 crossings against 6. It is a command, run when someone asks, and
/// <c>tools/d19-gate</c> is what asks: once on a fresh map, and again after
/// <c>cs_sv_reload</c>, which is the forced transition. A mirror that never
/// updates passes a gate that never fires.
/// </para>
/// </summary>
[ConCommand("cs_sv_player_mirror",
            Help = "Compares the managed registry's answer to 'is this a hierarchy player' "
                 + "against the EntityGetClassName ABI slot it replaced, in both directions. "
                 + "The divergence gate for D19's EntityGetClassName inversion.")]
public sealed class PlayerMirrorCheckCommand : IConCommand
{
    private readonly IEngineEntities _entities;
    private readonly IEngineConsole _console;
    private readonly IEntityDirectory _directory;

    public PlayerMirrorCheckCommand(IEngineEntities entities, IEngineConsole console,
                                    IEntityDirectory directory)
    {
        _entities = entities;
        _console = console;
        _directory = directory;
    }

    /// <summary>
    /// One handle, compared. Static and engine-free because it is the unit worth
    /// testing: a test drives it with a stubbed slot answer and asserts what it
    /// reports, with no map anywhere near it.
    /// </summary>
    /// <returns>true when the two agree, false when they diverge.</returns>
    public static bool Compare(uint handle, string slotClassName, bool registrySaysPlayer,
                               System.Action<PlayerMirrorComparison> report)
    {
        // The SLOT's own rule, spelled out here rather than borrowed: stock
        // compares classnames with stricmp, so a case difference between the
        // shell's string and EntityNames.SsPlayer is a divergence this gate
        // must SEE rather than normalise away. Ordinal, deliberately.
        bool slotSaysPlayer = slotClassName == EntityNames.SsPlayer;

        if (registrySaysPlayer == slotSaysPlayer)
            return true;

        report(new PlayerMirrorComparison(handle, slotClassName, registrySaysPlayer, slotSaysPlayer));
        return false;
    }

    public void Execute(string args)
    {
        var divergences = new List<PlayerMirrorComparison>();
        int walked = 0;
        int registryPlayers = 0;

        // Pass one: the world's live entities. Catches a spawn push that never
        // fired — an entity the engine calls ss_player that the registry has no
        // instance for.
        foreach (EntityHandle entity in _entities.FindAllByClassname("*"))
        {
            walked++;

            bool registrySaysPlayer = _directory.Get(entity) is SsPlayer;

            if (registrySaysPlayer)
                registryPlayers++;

            Compare(entity.Value, _entities.GetClassName(entity), registrySaysPlayer,
                    divergences.Add);
        }

        // Pass two: everything the registry holds as a player. Catches the
        // direction pass one structurally cannot see — an instance whose entity
        // the engine no longer calls ss_player, which is the case where this
        // inversion answers "player" for something that is not one.
        // Materialised first: GetClassName crosses the boundary and nothing may
        // mutate the registry under an open enumerator.
        var held = new List<EntityHandle>();

        foreach (ScriptedEntity instance in _directory.OfClass(EntityNames.SsPlayer))
            held.Add(instance.Handle);

        int orphaned = 0;

        foreach (EntityHandle entity in held)
        {
            string slotClassName = _entities.GetClassName(entity);

            if (slotClassName == EntityNames.SsPlayer)
                continue;

            orphaned++;
            divergences.Add(new PlayerMirrorComparison(entity.Value, slotClassName, true, false));
        }

        _console.Msg($"[playermirror] walked={walked} registryPlayers={registryPlayers} "
                     + $"held={held.Count} orphaned={orphaned}\n");

        foreach (PlayerMirrorComparison d in divergences)
            _console.Msg($"[playermirror] DIVERGED {d}\n");

        _console.Msg(divergences.Count == 0
                         ? "[playermirror] VERDICT ok\n"
                         : $"[playermirror] VERDICT {divergences.Count} DIVERGED\n");
    }
}

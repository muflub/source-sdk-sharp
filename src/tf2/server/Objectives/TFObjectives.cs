using SourceSharp.Abi;

using TF2.Shared;

namespace TF2.Server.Objectives;

/// <summary>What kind of objective a <see cref="TFObjective"/> row is.</summary>
public enum TFObjectiveKind : byte
{
    Flag,
    CaptureZone,
    ControlPoint,
    Cart,
    KothPoint,
}

/// <summary>The one-word state an AI can decide on.</summary>
public enum TFObjectiveState : byte
{
    Locked,
    Open,
    Capturing,
    Carried,
    Dropped,
    Home,
    Moving,
    RollingBack,
    Won,
}

/// <summary>
/// One objective as every client's HUD shows it. <paramref name="Progress"/> is
/// 0..1: the capture percentage, the cart's path fraction, or the flag's
/// home/carried 0-or-1. <paramref name="MayAct"/> of
/// <see cref="TFTeam.Unassigned"/> means both teams may act on it.
/// </summary>
public readonly record struct TFObjective(
    int Id, TFObjectiveKind Kind, TFTeam Owner, TFTeam MayAct,
    TFObjectiveState State, Vector Position, float Progress,
    int CarrierIndex, float UnlockIn, float TimerLeft);

/// <summary>
/// A read-only feed of the map's objectives, refreshed by a server tick. The
/// AI reads <see cref="LastProgressTick"/> to decide a team is stuck.
/// </summary>
public interface ITFObjectives
{
    ReadOnlySpan<TFObjective> All { get; }

    /// <summary>The tick that team last gained: a capture, a cart advance, a flag pickup or capture.</summary>
    int LastProgressTick(TFTeam team);

    /// <summary>Bumps when <see cref="All"/>' contents change.</summary>
    int Version { get; }
}

/// <summary>One chain member of the feed: it appends its kind's records, in its own order.</summary>
public interface ITFObjectiveSource
{
    void Collect(List<TFObjective> into);
}

/// <summary>TF2's slots in the core's unique-order sequences (<see cref="StageUniqueness"/>).</summary>
public static class TFObjectivesOrders
{
    /// <summary>TF2's objectives band: the free 999_4xx of TF2's thousand.</summary>
    private const int Band = ModStages.First - 1_000 + 400;

    /// <summary>The feed's frame slot (<see cref="TFObjectives"/>).</summary>
    public const int Feed = Band;

    /// <summary>The CTF chain member (<see cref="TF2CtfObjectiveSource"/>).</summary>
    public const int CtfSource = Band + 10;

    /// <summary>The payload chain member (<see cref="TF2PayloadObjectiveSource"/>).</summary>
    public const int PayloadSource = Band + 20;

    /// <summary>The control-point chain member (<see cref="TF2CpObjectiveSource"/>).</summary>
    public const int ControlPointSource = Band + 30;

    /// <summary>The koth chain member (<see cref="TF2KothObjectiveSource"/>).</summary>
    public const int KothSource = Band + 40;
}

/// <summary>
/// The feed itself (<see cref="ITFObjectives"/>): refills its record array each
/// frame from the registered <see cref="ITFObjectiveSource"/> chain (one per
/// kind) and bumps <see cref="ITFObjectives.Version"/> only when a record
/// differs from last frame's. A team's last-progress tick moves when one of its
/// records gains: rising progress, or a carrier or state change. Which team a
/// record's gain belongs to is <see cref="TFObjective.MayAct"/> when it is set
/// (the flag's enemy carries it home), else its owner; when neither is a game
/// team, both teams took the gain.
/// </summary>
/// <remarks>
/// Refills in the frame's think, after the entities have thought, so a moved
/// cart or a picked-up flag is already on the record this frame.
/// </remarks>
[SideService(Side.Server, As = [typeof(ITFObjectives)])]
public sealed class TFObjectives(IHooks<ITFObjectiveSource>? hooks = null) : ITFObjectives, IFrameLifecycle
{
    /// <summary>The feed's order in TF2's objectives band.</summary>
    public const int Order = TFObjectivesOrders.Feed;

    /// <summary>The team slots: one per team ordinal below <see cref="TFTeam.Count"/>.</summary>
    private const int TeamSlots = (int)TFTeam.Count;

    private readonly IHooks<ITFObjectiveSource>? _hooks = hooks;

    private readonly List<TFObjective> _scratch = [];
    private Action<ITFObjectiveSource>? _collect;
    private readonly int[] _lastProgress = new int[TeamSlots];

    private TFObjective[] _records = [];
    private int _version;
    private int _tick;

    /// <inheritdoc />
    int IFrameLifecycle.FrameOrder => Order;

    /// <inheritdoc />
    void IFrameLifecycle.OnThink(float frameTime) => Refresh();

    /// <inheritdoc />
    public ReadOnlySpan<TFObjective> All => _records;

    /// <inheritdoc />
    public int Version => _version;

    /// <inheritdoc />
    public int LastProgressTick(TFTeam team)
    {
        int slot = (int)team;
        return slot is >= 0 and < TeamSlots ? _lastProgress[slot] : 0;
    }

    /// <summary>One refresh: collect, diff against last frame, credit the gains.</summary>
    public void Refresh()
    {
        _scratch.Clear();
        _hooks?.Each("Collect", _collect ??= source => source.Collect(_scratch));

        TFObjective[] previous = _records;

        bool changed = _scratch.Count != previous.Length;
        for (int i = 0; !changed && i < previous.Length; i++)
            changed = _scratch[i] != previous[i];

        _tick++;

        // Only a record that was already there can move a team's tick: the
        // map's first fill states the starting line, it is not a gain.
        foreach (TFObjective record in _scratch)
        {
            if (!TryFind(previous, record.Id, out TFObjective before))
                continue;

            if (RecordGained(before, record))
                NoteGain(record, _tick);
        }

        // A frame nothing moved in keeps its array: this runs every frame of
        // every map, with or without a bot to read it.
        if (changed)
        {
            _version++;
            _records = _scratch.ToArray();
        }
    }

    private static bool TryFind(TFObjective[] records, int id, out TFObjective found)
    {
        foreach (TFObjective record in records)
        {
            if (record.Id == id)
            {
                found = record;
                return true;
            }
        }

        found = default;
        return false;
    }

    private static bool RecordGained(in TFObjective before, in TFObjective now)
        => now.Progress > before.Progress
           || now.CarrierIndex != before.CarrierIndex
           || now.State != before.State;

    private void NoteGain(in TFObjective record, int tick)
    {
        TFTeam acted = record.MayAct != TFTeam.Unassigned ? record.MayAct : record.Owner;

        if (acted != TFTeam.Unassigned)
        {
            Book(acted, tick);
            return;
        }

        // MayAct Unassigned is both teams: whoever of them pushed gained.
        Book(TFTeam.Red, tick);
        Book(TFTeam.Blue, tick);
    }

    private void Book(TFTeam team, int tick)
    {
        int slot = (int)team;
        if (slot is >= 0 and < TeamSlots)
            _lastProgress[slot] = tick;
    }
}

/// <summary>The feed's chain (<see cref="ITFObjectiveSource"/>), which only a registrar can name.</summary>
[SideService(Side.Server)]
public sealed class TF2ObjectivesRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<ITFObjectiveSource>();
}

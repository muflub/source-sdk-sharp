using SourceSharp.Abi;

namespace TF2.Shared;

/// <summary>The crit convars as a roll reads them. A single owner per side: <see cref="TFCritConVars"/>.</summary>
public interface ITFCritSettings
{
    TFCritSettings Current { get; }
}

/// <summary>The crit convars, read when a roll asks (they are replicated, so both sides read one value).</summary>
[SideService(Side.Server, As = [typeof(ITFCritSettings)])]
[SideService(Side.Client, As = [typeof(ITFCritSettings)])]
public sealed class TFCritConVars(
    TFWeaponCriticalsConVar criticals,
    TFWeaponCriticalsMeleeConVar melee,
    TFWeaponCriticalsBucketCapConVar cap,
    TFWeaponCriticalsBucketBottomConVar bottom,
    TFWeaponCriticalsBucketDefaultConVar initial) : ITFCritSettings
{
    public TFCritSettings Current =>
        new(criticals.Value, melee.Value, cap.Value, bottom.Value, initial.Value);
}

/// <summary>
/// One weapon's crit state on one side: its <see cref="CritBucket"/> and what
/// it answered the recent commands it rolled for. Stock's bucket is not
/// predicted, so a client's replays would pay for a crit again (and be
/// refused what the first prediction was granted); here a replay answers what
/// the command's first roll answered and touches the bucket no more, so the
/// client's bucket changes once per command, as the server's does.
/// </summary>
public sealed class TFCritState(float initialTokens)
{
    /// <summary>How many rolled commands are remembered: more than a client replays.</summary>
    public const int Remembered = 64;

    private readonly (int Seed, TFCritResult Result, TFCritClocks Clocks)[] _recent = new (int, TFCritResult, TFCritClocks)[Remembered];
    private int _next;
    private int _count;

    public CritBucket Bucket { get; } = new(initialTokens);

    /// <summary>How many first rolls this weapon made (a replay's recall is not one).</summary>
    public int Rolls { get; private set; }

    /// <summary>How many of them crit.</summary>
    public int Crits { get; private set; }

    /// <summary>The last first roll's answer.</summary>
    public TFCritResult Last { get; private set; }

    /// <summary>Remembers a first roll's answer and the clocks it left.</summary>
    public void Remember(int seed, TFCritResult result, TFCritClocks clocks)
    {
        Rolls++;
        if (result.Critical)
            Crits++;
        Last = result;
        _recent[_next] = (seed, result, clocks);
        _next = (_next + 1) % Remembered;
        _count = Math.Min(_count + 1, Remembered);
    }

    /// <summary>The answer the command with <paramref name="seed"/> was given, newest first.</summary>
    public bool Recall(int seed, out TFCritResult result, out TFCritClocks clocks)
    {
        for (int i = 1; i <= _count; i++)
        {
            var entry = _recent[(_next - i + Remembered) % Remembered];
            if (entry.Seed == seed)
            {
                result = entry.Result;
                clocks = entry.Clocks;
                return true;
            }
        }

        result = default;
        clocks = default;
        return false;
    }
}

/// <summary>
/// Each weapon's crit state on this side (stock's buckets are weapon members
/// no one networks): one per weapon entity index and weapon row, its bucket
/// made full of the default on first ask, as a weapon's constructor fills it.
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFCritBuckets
{
    private readonly Dictionary<(int Weapon, int Type), TFCritState> _states = new();

    /// <summary>The crit state of weapon row <paramref name="typeId"/> in the hands of weapon entity <paramref name="weaponIndex"/>.</summary>
    public TFCritState StateFor(int weaponIndex, int typeId, float initialTokens)
    {
        if (!_states.TryGetValue((weaponIndex, typeId), out var state))
            _states[(weaponIndex, typeId)] = state = new TFCritState(initialTokens);
        return state;
    }

    /// <summary>The bucket of weapon row <paramref name="typeId"/> in the hands of weapon entity <paramref name="weaponIndex"/>.</summary>
    public CritBucket For(int weaponIndex, int typeId, float initialTokens) =>
        StateFor(weaponIndex, typeId, initialTokens).Bucket;

    /// <summary>Forgets a weapon entity's buckets, as a fresh weapon starts a fresh bucket (a respawn's new loadout).</summary>
    public void Reset(int weaponIndex)
    {
        foreach (var key in _states.Keys.Where(k => k.Weapon == weaponIndex).ToArray())
            _states.Remove(key);
    }

    /// <summary>How many buckets this side holds.</summary>
    public int Count => _states.Count;

    /// <summary>The crit states of weapon entity <paramref name="weaponIndex"/>, by weapon row.</summary>
    public IEnumerable<(int TypeId, TFCritState State)> Of(int weaponIndex)
    {
        foreach (var (key, state) in _states)
        {
            if (key.Weapon == weaponIndex)
                yield return (key.Type, state);
        }
    }
}

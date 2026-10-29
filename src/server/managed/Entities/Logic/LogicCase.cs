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
/// Routes an incoming value to one of sixteen outputs by matching it against
/// sixteen configured cases, or picks one at random.
/// </summary>
[ScriptedEntity(EntityNames.LogicCase)]
[ClaimsKeys("Case", 1, 16)]
public sealed class LogicCase : ScriptedEntity
{
    /// <summary>
    /// The side's seed source, from which this case's own stream is seeded:
    /// explicitly and differently per instance, so two cases never replay the
    /// same picks.
    /// </summary>
    public LogicCase(IRandomSeeds seeds)
    {
        _random = new Random(seeds.Next());
    }

    public readonly Output OnCase01 = new();
    public readonly Output OnCase02 = new();
    public readonly Output OnCase03 = new();
    public readonly Output OnCase04 = new();
    public readonly Output OnCase05 = new();
    public readonly Output OnCase06 = new();
    public readonly Output OnCase07 = new();
    public readonly Output OnCase08 = new();
    public readonly Output OnCase09 = new();
    public readonly Output OnCase10 = new();
    public readonly Output OnCase11 = new();
    public readonly Output OnCase12 = new();
    public readonly Output OnCase13 = new();
    public readonly Output OnCase14 = new();
    public readonly Output OnCase15 = new();
    public readonly Output OnCase16 = new();
    public readonly Output OnDefault = new();

    private const int CaseCount = 16;

    private readonly string?[] _cases = new string?[CaseCount];
    private Output[] _caseOutputs = Array.Empty<Output>();

    // Shuffle deals out every case before repeating, so a map gets variety
    // rather than a run of the same result.
    private readonly List<int> _shuffleBag = new();
    // This case's own stream, seeded from the side's IRandomSeeds. A static
    // one shared by every logic_case in the process was mutable global state.
    private readonly Random _random;

    public override void OnSpawn()
    {
        _caseOutputs = new[]
        {
            OnCase01, OnCase02, OnCase03, OnCase04, OnCase05, OnCase06, OnCase07, OnCase08,
            OnCase09, OnCase10, OnCase11, OnCase12, OnCase13, OnCase14, OnCase15, OnCase16,
        };
    }

    /// <summary>
    /// The sixteen "Case01".."Case16" keys. Sixteen [ScriptVars] keys would say
    /// the same thing far less clearly, and the index is what the code needs.
    /// </summary>
    public override bool OnKeyValue(string key, string value)
    {
        if (key.Length == 6 && key.StartsWith("case", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key.AsSpan(4), out int index)
            && index >= 1 && index <= CaseCount)
        {
            _cases[index - 1] = value;
            return true;
        }

        return false;
    }

    [ScriptedEvent] private void OnInValue(string value)
    {
        for (int i = 0; i < CaseCount; i++)
        {
            if (_cases[i] is not null && string.Equals(_cases[i], value, StringComparison.OrdinalIgnoreCase))
            {
                _caseOutputs[i].Fire();
                return;
            }
        }

        OnDefault.Fire();
    }

    [ScriptedEvent] private void OnPickRandom() => FireRandom(shuffle: false);

    [ScriptedEvent] private void OnPickRandomShuffle() => FireRandom(shuffle: true);

    private void FireRandom(bool shuffle)
    {
        // Only configured cases are candidates — picking an unset one would look
        // like the entity doing nothing at all.
        var candidates = new List<int>();
        for (int i = 0; i < CaseCount; i++)
            if (_cases[i] is not null)
                candidates.Add(i);

        if (candidates.Count == 0)
        {
            OnDefault.Fire();
            return;
        }

        if (!shuffle)
        {
            _caseOutputs[candidates[_random.Next(candidates.Count)]].Fire();
            return;
        }

        if (_shuffleBag.Count == 0)
            _shuffleBag.AddRange(candidates);

        int pick = _random.Next(_shuffleBag.Count);
        int chosen = _shuffleBag[pick];
        _shuffleBag.RemoveAt(pick);
        _caseOutputs[chosen].Fire();
    }
}

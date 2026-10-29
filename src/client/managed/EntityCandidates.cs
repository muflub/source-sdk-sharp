using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// The entity indices a predicted trace clips against: every index from 1 but
/// the tracer's own. The client's shells are in no spatial partition, so no
/// engine trace here can stop on one, and the clips
/// (<see cref="IEngineWorldQuery.ClientClipRayToEntities"/>,
/// <see cref="IEngineWorldQuery.ClientTraceHullEntities"/>) are handed this
/// list instead. Built once per tracer and kept, because the list only changes
/// with who traces.
/// </summary>
internal sealed class EntityCandidates
{
    private readonly int[] _indices = new int[EngineLimits.MaxEdicts - 2];
    private int _builtFor = -1;

    /// <summary>Every entity index from 1 but <paramref name="tracerIndex"/>.</summary>
    public ReadOnlySpan<int> AllBut(int tracerIndex)
    {
        if (_builtFor != tracerIndex)
        {
            int count = 0;
            for (int index = 1; index < EngineLimits.MaxEdicts && count < _indices.Length; index++)
            {
                if (index != tracerIndex)
                    _indices[count++] = index;
            }

            _builtFor = tracerIndex;
        }

        return _indices;
    }
}

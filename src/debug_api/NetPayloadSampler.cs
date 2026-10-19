using Debug.Api.Server.Interfaces;

namespace Debug.Api;

/// <summary>
/// The last blob seen for each watched entity, so the next read can say what
/// moved.
///
/// <para>
/// This is the third of Part 13's three views and the one that catches an offset
/// bug. A field reading the wrong bytes does not look wrong on its own — the
/// value it shows is a perfectly plausible number. It looks wrong when its
/// NEIGHBOUR is written and it moves, and that is only visible against a
/// previous sample. So the diff is kept here rather than left to whoever is
/// reading the page to hold in their head.
/// </para>
///
/// <para>
/// KEYED BY HANDLE, not by index. An index is reused the moment an entity is
/// freed, and diffing a new entity's blob against the one that used to hold its
/// slot would report a change in every field at once — which reads exactly like
/// the failure this is here to find.
/// </para>
///
/// <para>
/// Held per server rather than per request because HTTP has no session and the
/// CLI's <c>--watch</c> is a loop of separate requests. Bounded, because a map
/// full of entities polled one at a time would otherwise keep a copy of every
/// blob forever.
/// </para>
/// </summary>
internal sealed class NetPayloadSampler
{
    /// <summary>
    /// How many entities are remembered at once. Well past what anybody watches
    /// by hand, and small enough that the memory is not worth thinking about.
    /// </summary>
    private const int MaxWatched = 64;

    private readonly Dictionary<uint, Sample> _samples = new();

    private readonly record struct Sample(byte[] Blob, DateTime At);

    /// <summary>What changed between the previous sample of an entity and this one.</summary>
    /// <param name="First">
    /// No previous sample. NOT the same as "nothing changed", and reported apart
    /// from it: a still entity and a newly watched one would otherwise look
    /// identical, and only one of those means the value is not moving.
    /// </param>
    /// <param name="Resized">
    /// The blob is a different length than last time. Only possible if the
    /// entity in the slot changed or native re-sized the hierarchy's blob, and
    /// both make a byte-for-byte diff meaningless — so the comparison stops at
    /// the shorter length and this says why.
    /// </param>
    internal readonly record struct Diff(
        bool First,
        bool Resized,
        double SinceSeconds,
        int ChangedBytes,
        IReadOnlyList<(int Offset, int Length)> Runs)
    {
        public static Diff FirstSample()
            => new(true, false, 0, 0, Array.Empty<(int, int)>());
    }

    /// <summary>
    /// Records a blob and reports what it changed since the last one.
    ///
    /// Not thread-safe by itself; called from the request thread only, after the
    /// game thread has handed the bytes back.
    /// </summary>
    public Diff Observe(DebugNetPayload read, DateTime now)
    {
        if (!read.Found)
            return Diff.FirstSample();

        byte[] blob = read.Blob;

        if (!_samples.TryGetValue(read.Handle, out var previous))
        {
            Remember(read.Handle, blob, now);
            return Diff.FirstSample();
        }

        var runs = PayloadBytes.ChangedRuns(previous.Blob, blob);

        int changedBytes = 0;
        foreach (var (_, length) in runs)
            changedBytes += length;

        var diff = new Diff(
            First: false,
            Resized: previous.Blob.Length != blob.Length,
            SinceSeconds: Math.Max(0, (now - previous.At).TotalSeconds),
            ChangedBytes: changedBytes,
            Runs: runs);

        Remember(read.Handle, blob, now);
        return diff;
    }

    /// <summary>Forgets everything, which a level change makes worth doing.</summary>
    public void Clear() => _samples.Clear();

    private void Remember(uint handle, byte[] blob, DateTime now)
    {
        // Oldest out first, and only when a NEW entity would push past the cap —
        // re-observing one already held must never evict anything, or a watch
        // loop on one entity would keep throwing away its own history.
        if (!_samples.ContainsKey(handle) && _samples.Count >= MaxWatched)
        {
            uint oldest = 0;
            var oldestAt = DateTime.MaxValue;

            foreach (var (key, sample) in _samples)
            {
                if (sample.At >= oldestAt)
                    continue;

                oldest = key;
                oldestAt = sample.At;
            }

            _samples.Remove(oldest);
        }

        _samples[handle] = new Sample(blob, now);
    }
}

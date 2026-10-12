using System;
using System.Collections.Generic;

namespace SourceSharp.Abi;

/// <summary>
/// ONE CROSSING PER MODEL PER LEVEL, and the cache that makes it one.
///
/// <para>
/// <see cref="ModelDescriptor"/> says why the crossing exists; this is where
/// the answer is kept. A model's sequence table, attachment names and bone
/// count are facts of a file on disk: they cannot change while the level is
/// loaded, so asking twice is asking the same question twice.
/// </para>
///
/// <para>
/// <b>KEYED BY MODEL NAME, ONE INSTANCE PER SIDE.</b> Per side because ONE
/// managed runtime serves BOTH DLLs on a listen server, so a single dictionary
/// would hand the client's answer to the server and the other way round. They
/// would usually agree, and "usually" is how this kind of bug survives review.
/// Each side's engine owns its own (<c>EngineSide.Models</c>, plan_abi_hotreload
/// D8): the render binding reads it through the side it is bound to, and the
/// module hands it to the rest by constructor. It used to be one static class
/// whose two tables were chosen by the thread's active side, so two game
/// instances in one process shared them. By NAME rather than by index because a
/// model index is the side's own string-table slot and the name is the thing
/// both sides have; the index is asked for once, on the miss, from that side's
/// own <c>IVModelInfo</c>.
/// </para>
///
/// <para>
/// <b>CLEARED ON LEVEL SHUTDOWN, by each side's own level-shutdown crossing.</b>
/// Model indices are per-level, the mdl cache is flushed between levels, and a
/// descriptor kept across a map change would answer for whatever model now
/// holds that name — or, worse, still answer correctly on the first map and
/// wrongly on a RELOAD of it, which is the failure mode that hides from a gate
/// that loads one map.
/// </para>
///
/// <para>
/// <see cref="Crossings"/> and <see cref="Bytes"/> are the measurement the
/// design was argued from and are not diagnostics-only: the question "is one
/// descriptor cheaper than eight per-call entry points" is answered by how many
/// models managed actually asks about, which is not the number a level
/// precaches.
/// </para>
/// </summary>
public sealed class ModelDescriptors
{
    private readonly Dictionary<string, ModelDescriptor> _table =
        new(StringComparer.OrdinalIgnoreCase);

    // The side's three engine reads: a model's index by name, the descriptor
    // blob at an index, and an entity's model name.
    private readonly Func<string, int> _modelIndex;
    private readonly Func<int, byte[]?> _blob;
    private readonly Func<EntityHandle, string> _modelName;

    private int _crossings;
    private long _bytes;

    // Where each sequence's default duration comes from: the module that owns
    // the side's managed studio headers attaches it (AttachTiming).
    private IModelSequenceTiming? _timing;

    // Where a model this side cannot describe is said (statics to zero).
    private readonly ILog _log;

    /// <summary>The cache of one side, over that side's engine.</summary>
    internal ModelDescriptors(EngineSide side)
        : this(model => EngineBound.ModelInfo(side).ModelIndex(model),
               side.ModelDescriptorBlob,
               side.EntityGetModelName,
               side.Log)
    {
    }

    /// <summary>
    /// The same over the three reads given: what the side's constructor
    /// passes, and what a fact passes instead of an engine.
    /// </summary>
    internal ModelDescriptors(Func<string, int> modelIndex,
                              Func<int, byte[]?> blob,
                              Func<EntityHandle, string> modelName,
                              ILog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _modelIndex = modelIndex ?? throw new ArgumentNullException(nameof(modelIndex));
        _blob = blob ?? throw new ArgumentNullException(nameof(blob));
        _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
    }

    /// <summary>How many descriptor blobs have been fetched since the last
    /// <see cref="Clear"/> — one per model managed has asked about, not one per
    /// model the level precached.</summary>
    public int Crossings => _crossings;

    /// <summary>How many bytes those blobs came to.</summary>
    public long Bytes => _bytes;

    /// <summary>How many models this side has cached.</summary>
    public int CachedCount => _table.Count;

    /// <summary>
    /// The descriptor for an entity's model, or <see cref="ModelDescriptor.Empty"/>
    /// for an entity with no model or no studio data.
    /// </summary>
    public ModelDescriptor For(EntityHandle entity)
        => ForModel(_modelName(entity));

    /// <summary>
    /// The descriptor for a named model. The name is the one
    /// <c>IVModelInfo::GetModelName</c> answers — <c>models/weapons/v_smg1.mdl</c>.
    /// </summary>
    public ModelDescriptor ForModel(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName))
            return ModelDescriptor.Empty;

        if (_table.TryGetValue(modelName, out ModelDescriptor? cached))
            return cached;

        ModelDescriptor fetched = Fetch(modelName);

        // THE MISS IS CACHED TOO. A model with no studio data — a sprite, a
        // brush model — answers Empty every time, and a cache that only stored
        // hits would cross for it on every call, which is the shape this type
        // exists to remove.
        _table[modelName] = fetched;
        return fetched;
    }

    /// <summary>
    /// Takes each sequence's default duration from <paramref name="timing"/>
    /// until the returned attachment is disposed. The module that owns the
    /// side's managed studio headers attaches at composition and disposes in
    /// Shutdown, so a reloaded module's reader is never held across its unload.
    /// Descriptors already cached keep the durations they were built with.
    /// </summary>
    public IDisposable AttachTiming(IModelSequenceTiming timing)
    {
        ArgumentNullException.ThrowIfNull(timing);

        IModelSequenceTiming? previous = _timing;
        _timing = timing;
        return new TimingAttachment(this, timing, previous);
    }

    private sealed class TimingAttachment(ModelDescriptors owner, IModelSequenceTiming timing,
                                          IModelSequenceTiming? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (ReferenceEquals(owner._timing, timing))
                owner._timing = previous;
        }
    }

    /// <summary>
    /// Forget everything. Called from this side's level-shutdown crossing.
    /// </summary>
    public void Clear()
    {
        _table.Clear();
        _crossings = 0;
        _bytes = 0;
    }

    private ModelDescriptor Fetch(string modelName)
    {
        int index = _modelIndex(modelName);

        if (index <= 0)
            return ModelDescriptor.Empty;

        byte[]? blob = _blob(index);

        if (blob is null || blob.Length == 0)
            return ModelDescriptor.Empty;

        _crossings++;
        _bytes += blob.Length;

        ModelDescriptor d = ModelDescriptor.Decode(blob, out string why);

        if (why.Length > 0)
        {
            // LOUD, because the alternative is a model that resolves every
            // sequence name to -1 and animates as nothing with nothing in the
            // log — which is the defect the descriptor replaced an eight-slot
            // crossing to make visible in the first place.
            _log.Msg($"[SourceSharp] ModelDescriptor({modelName}): {why}\n");
            return d;
        }

        if (d.Sequences.Count == 0)
            return d;

        // THE DURATIONS ARE MANAGED: the blob carries none (v3). A side with no
        // reader attached, or a header the reader cannot open, answers 0 for
        // every sequence -- what SequenceDuration answers for a sequence the
        // model does not have -- and says so, since a 0 duration plays a
        // sequence in one frame.
        float[]? durations = _timing?.DefaultDurations(index);

        if (durations is null || durations.Length != d.Sequences.Count)
        {
            string cause = durations is null
                ? _timing is null ? "no sequence timing attached" : "no studio header for its sequence timing"
                : $"sequence timing has {durations.Length} sequence(s), the descriptor {d.Sequences.Count}";
            _log.Msg($"[SourceSharp] ModelDescriptor({modelName}): {cause}; every duration answers 0\n");
            return d;
        }

        return d.WithDurations(durations);
    }
}

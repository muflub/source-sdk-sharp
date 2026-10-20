using System;
using System.Collections.Generic;

namespace SourceSharp.Abi;

/// <summary>
/// ONE READ PER MODEL PER LEVEL, and the cache that makes it one.
///
/// <para>
/// <see cref="ModelDescriptor"/> says what a descriptor answers; this is where
/// the answer is kept. A model's sequence table, attachment names and bone
/// count are facts of a file on disk: they cannot change while the level is
/// loaded, so asking twice is asking the same question twice. The answer is
/// read from the side's managed studio header through the attached
/// <see cref="IModelDescriptorSource"/> (studio.cpp's eviction: it used to be
/// a blob that model_descriptor.cpp's native walk crossed).
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
/// <see cref="Reads"/> is how many models managed actually asks about, which
/// is not the number a level precaches.
/// </para>
/// </summary>
public sealed class ModelDescriptors
{
    private readonly Dictionary<string, ModelDescriptor> _table =
        new(StringComparer.OrdinalIgnoreCase);

    // The side's two engine reads: a model's index by name, and an entity's
    // model name.
    private readonly Func<string, int> _modelIndex;
    private readonly Func<EntityHandle, string> _modelName;

    private int _reads;

    // Where each descriptor is read from: the module that owns the side's
    // managed studio headers attaches it (AttachSource).
    private IModelDescriptorSource? _source;

    // Where a model this side cannot describe is said (statics to zero).
    private readonly ILog _log;

    /// <summary>
    /// The same over the two reads given: what the side's constructor
    /// passes, and what a fact passes instead of an engine.
    /// </summary>
    internal ModelDescriptors(Func<string, int> modelIndex,
                              Func<EntityHandle, string> modelName,
                              ILog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _modelIndex = modelIndex ?? throw new ArgumentNullException(nameof(modelIndex));
        _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
    }

    /// <summary>How many descriptors have been read from the attached source
    /// since the last <see cref="Clear"/> — one per model managed has asked
    /// about, not one per model the level precached.</summary>
    public int Reads => _reads;

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
        // hits would ask again on every call.
        _table[modelName] = fetched;
        return fetched;
    }

    /// <summary>
    /// Reads every descriptor from <paramref name="source"/> until the
    /// returned attachment is disposed. The module that owns the side's
    /// managed studio headers attaches at composition and disposes in
    /// Shutdown, so a reloaded module's reader is never held across its
    /// unload. Descriptors already cached are kept.
    /// </summary>
    public IDisposable AttachSource(IModelDescriptorSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        IModelDescriptorSource? previous = _source;
        _source = source;
        return new SourceAttachment(this, source, previous);
    }

    private sealed class SourceAttachment(ModelDescriptors owner, IModelDescriptorSource source,
                                          IModelDescriptorSource? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (ReferenceEquals(owner._source, source))
                owner._source = previous;
        }
    }

    /// <summary>
    /// Forget everything. Called from this side's level-shutdown crossing.
    /// </summary>
    public void Clear()
    {
        _table.Clear();
        _reads = 0;
    }

    private ModelDescriptor Fetch(string modelName)
    {
        int index = _modelIndex(modelName);

        if (index <= 0)
            return ModelDescriptor.Empty;

        if (_source is not { } source)
        {
            // LOUD, because the alternative is a model that resolves every
            // sequence name to -1 and animates as nothing with nothing in the
            // log.
            _log.Msg($"[SourceSharp] ModelDescriptor({modelName}): no descriptor source attached; it answers nothing\n");
            return ModelDescriptor.Empty;
        }

        // Null is a model with no studio header to open (a sprite, a brush
        // model), which the native walk answered with no blob: Empty, quietly.
        ModelDescriptor? d = source.Describe(index);

        if (d is null)
            return ModelDescriptor.Empty;

        _reads++;
        return d;
    }
}

namespace SourceSharp.Server;

/// <summary>
/// The live entities of one class, kept by the entities themselves: each adds
/// itself on spawn and removes itself on removal.
///
/// <para>
/// For the classes stock found with a <c>gEntList</c> walk or a native registry
/// and that answer a question asked from outside any one of them — the fires in
/// a radius, the magnet a body falls towards, the triggers a surface change is
/// handed to. Each such list was a static on the class; it is one roster per
/// server module now, registered in its container and taken through the
/// constructor by the entities that join it and by whatever asks.
/// </para>
///
/// <para>
/// Emptied by the level change itself: every map entity is removed then, and
/// removal is how an entity leaves.
/// </para>
/// </summary>
public interface IEntityRoster<T> : IReadOnlyList<T> where T : class
{
    /// <summary>An entity has spawned.</summary>
    void Add(T entity);

    /// <summary>An entity has been removed.</summary>
    void Remove(T entity);

    /// <summary>Forgets every entity: for a roster its owner drops with the map.</summary>
    void Clear();
}

/// <inheritdoc cref="IEntityRoster{T}"/>
public sealed class EntityRoster<T> : IEntityRoster<T> where T : class
{
    private readonly List<T> _live = new();

    /// <inheritdoc/>
    public int Count => _live.Count;

    /// <inheritdoc/>
    public T this[int index] => _live[index];

    /// <inheritdoc/>
    public void Add(T entity) => _live.Add(entity);

    /// <inheritdoc/>
    public void Remove(T entity) => _live.Remove(entity);

    /// <inheritdoc/>
    public void Clear() => _live.Clear();

    /// <summary>The list's own enumerator: the walks here are per death and per think.</summary>
    public List<T>.Enumerator GetEnumerator() => _live.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => _live.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _live.GetEnumerator();
}

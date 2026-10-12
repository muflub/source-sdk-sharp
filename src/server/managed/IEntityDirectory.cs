namespace SourceSharp.Abi;

/// <summary>
/// Finding the managed half of another entity.
///
/// <para>
/// The one dependency nearly every entity of any complexity has. A trigger asks
/// the filter that decides who it responds to; a door asks the button that
/// opened it; a path corner asks the next corner along. Until now that went
/// through <c>ScriptedEntity.Owner</c> — a back-reference the registry pushed
/// onto every instance it made — which works, but only for a
/// <see cref="ScriptedEntity"/> and only for one that the registry created. A
/// service anything can ask for is the same lookup without either condition.
/// </para>
///
/// <para>
/// Every lookup returns null on a miss rather than throwing, and the reason is
/// the map: a mapper naming an entity that is not there is a mistake in the
/// map, which is a thing to report and carry on from, not a thing to take the
/// level down over.
/// </para>
/// </summary>
public interface IEntityDirectory
{
    /// <summary>The instance driving an entity, or null if nothing is.</summary>
    ScriptedEntity? Get(EntityHandle handle);

    /// <summary>
    /// The instance a mapper named, or null.
    ///
    /// Null covers all three ways this fails — no such name, a name belonging
    /// to something that is not one of ours, and a name belonging to a
    /// different kind of entity than the caller wanted.
    /// </summary>
    ScriptedEntity? FindByName(string name);

    /// <summary>
    /// Every live instance of a classname.
    ///
    /// <para>
    /// The managed answer to <c>gEntList.FindEntityByClassname</c>, which cannot
    /// see the parallel hierarchy: it walks the engine's own entity list, where
    /// every managed entity appears as one of a handful of shell classes rather
    /// than under the name a map wrote. Two managed entities that have to find
    /// each other by classname have to do it here.
    /// </para>
    /// </summary>
    IEnumerable<ScriptedEntity> OfClass(string className);

    /// <summary>Whether a classname was declared by a <c>[ScriptedEntity]</c>.</summary>
    bool IsRegistered(string className);

    /// <summary>
    /// Every declared class, with what a map can send it and what it sends back.
    ///
    /// <para>
    /// On the CONTRACT rather than only on the registry because two callers want
    /// it and they must not answer differently: <c>./debug-api classes</c> lists it
    /// as JSON, and <c>ent_dump</c> prints one class's half of it as text. The
    /// registry reads the registration back rather than reflecting again — see
    /// ScriptedEntityRegistry.DescribeClasses — so what this reports is what was
    /// actually registered, which is the one thing a debugging view must never
    /// get wrong.
    /// </para>
    /// </summary>
    IReadOnlyList<ScriptedClassInfo> DescribeClasses();
}

/// <summary>Typed lookups over <see cref="IEntityDirectory"/>.</summary>
public static class EntityDirectoryExtensions
{
    /// <summary>The instance driving an entity, if it is of the type asked for.</summary>
    public static T? Get<T>(this IEntityDirectory directory, EntityHandle handle)
        where T : ScriptedEntity
        => directory.Get(handle) as T;

    /// <summary>The instance a mapper named, if it is of the type asked for.</summary>
    public static T? FindByName<T>(this IEntityDirectory directory, string name)
        where T : ScriptedEntity
        => directory.FindByName(name) as T;
}

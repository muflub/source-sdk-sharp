using SourceSharp.Server.Interface;

namespace Debug.Api;

/// <summary>
/// One entity, named the way a caller has it: by <c>index</c>, by
/// <c>handle</c>, or by <c>name</c> (its targetname). Exactly one of the three.
///
/// <para>
/// A HANDLE IS WHAT A WAIT HOLDS. An index is reused the moment its entity is
/// freed and a name can be given to the next entity spawned, so a wait or a
/// read resolves the selector to a handle once and follows that handle --
/// otherwise "wait until it is gone" would be satisfied by, or blind to, the
/// next entity to take the slot.
/// </para>
/// </summary>
public sealed record EntitySelector(int? Index, uint? Handle, string? Name)
{
    /// <summary>What the selector says, for a reply.</summary>
    public override string ToString()
        => Handle is { } h ? $"handle {h}" : Index is { } i ? $"index {i}" : $"name '{Name}'";

    /// <summary>
    /// The selector a query names. Null with no error when it names none, so a
    /// route can treat "no entity named" as its own case.
    /// </summary>
    public static EntitySelector? Parse(Func<string, string?> get, out string? error)
    {
        error = null;
        string? indexText = get("index"), handleText = get("handle"), name = get("name");

        int given = (string.IsNullOrEmpty(indexText) ? 0 : 1)
                  + (string.IsNullOrEmpty(handleText) ? 0 : 1)
                  + (string.IsNullOrEmpty(name) ? 0 : 1);

        if (given == 0)
            return null;

        if (given > 1)
        {
            error = "name one entity: index, handle or name, not more than one";
            return null;
        }

        if (!string.IsNullOrEmpty(indexText))
        {
            if (!int.TryParse(indexText, out int index) || index < 0)
            {
                error = $"index '{indexText}' is not an entity index";
                return null;
            }

            return new EntitySelector(index, null, null);
        }

        if (!string.IsNullOrEmpty(handleText))
        {
            if (!uint.TryParse(handleText, out uint handle))
            {
                error = $"handle '{handleText}' is not a number";
                return null;
            }

            return new EntitySelector(null, handle, null);
        }

        return new EntitySelector(null, null, name);
    }

    /// <summary>
    /// The live entity this names, or <see cref="IDebugWorld.NoEntity"/>. Game thread.
    /// An index is found by walking the world, which costs a pass over every
    /// entity: a caller resolves once and holds the handle.
    /// </summary>
    public uint Resolve(IDebugWorld world)
    {
        if (Handle is { } handle)
            return world.IsValid(handle) ? handle : IDebugWorld.NoEntity;

        if (Index is { } index)
        {
            foreach (uint entity in world.FindAllByClassname("*"))
            {
                if (world.GetIndex(entity) == index)
                    return entity;
            }

            return IDebugWorld.NoEntity;
        }

        uint found = world.FindByName(Name!);
        return world.IsValid(found) ? found : IDebugWorld.NoEntity;
    }
}

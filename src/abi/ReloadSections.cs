namespace SourceSharp.Abi;

/// <summary>
/// One side's named byte sections of the reload store, as a service.
///
/// <para>
/// What a module hands its next build across <c>cs_cl_reload</c> or
/// <c>cs_sv_reload</c> that is not an entity: the old module puts bytes under
/// a section name before it goes, and the reloaded one takes them back. Bytes
/// only, for <see cref="ReloadStore"/>'s reason (D6): the store lives in the
/// side root's load context, and an object of a side's would pin that side's
/// collectible context.
/// </para>
///
/// <para>
/// An interface so new code is handed it by its side's composition rather than
/// reaching the store itself, and so a test can stand in for it.
/// </para>
/// </summary>
public interface IReloadSections
{
    /// <summary>Stores <paramref name="bytes"/> under <paramref name="section"/>; null or empty removes it.</summary>
    void Put(string section, byte[]? bytes);

    /// <summary>Takes the section's bytes and removes them, or null when there are none.</summary>
    byte[]? Take(string section);
}

/// <summary><see cref="IReloadSections"/> over one side's <see cref="ReloadStore"/>.</summary>
public sealed class ReloadSections(ReloadStore store) : IReloadSections
{
    public void Put(string section, byte[]? bytes) => store.Put(section, bytes, 1);

    public byte[]? Take(string section) => store.Take(section);
}

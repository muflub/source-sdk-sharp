namespace SourceSharp.Abi;

/// <summary>
/// An <see cref="IHooks{T}"/> for a fact: the implementations it is handed,
/// in that order, and none when it is handed none. A consumer takes its hooks
/// by constructor and never checks them for null, so a test that registers
/// no hook hands it an empty one, as a side whose mods register none does.
/// <see cref="Each"/> isolates a throw as <see cref="Hooks{T}"/> does, so a
/// fact sees the same dispatch the side's container gives.
/// </summary>
public sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
{
    /// <summary>Every call <see cref="Each"/> caught, in order.</summary>
    public List<Exception> Thrown { get; } = new();

    public IReadOnlyList<T> All => hooks;

    public void Each(string what, Action<T> call)
    {
        foreach (T hook in hooks)
        {
            try
            {
                call(hook);
            }
            catch (Exception e)
            {
                Thrown.Add(e);
            }
        }
    }
}

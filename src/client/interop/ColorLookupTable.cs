namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's colour-correction lookups, by the small handles managed code
/// names them with (lane M1-A: shell_worldrender.cpp kept this table natively).
/// Each handle stands for the render context's opaque
/// <c>ColorCorrectionHandle_t</c>; handles count up from 1 and are never
/// reused, and 0 is "no lookup".
/// </summary>
public sealed class ColorLookupTable
{
    private readonly Dictionary<int, nint> _lookups = new();
    private int _next = 1;

    /// <summary>The lookups held.</summary>
    public int Count => _lookups.Count;

    /// <summary>A new handle for a loaded lookup; 0 for none (a lookup that would not load).</summary>
    public int Register(nint lookup)
    {
        if (lookup == 0)
            return 0;
        int handle = _next++;
        _lookups[handle] = lookup;
        return handle;
    }

    /// <summary>The lookup a handle names, while it is held.</summary>
    public bool TryGet(int handle, out nint lookup) => _lookups.TryGetValue(handle, out lookup);

    /// <summary>Lets a handle go, answering the lookup it named.</summary>
    public bool Forget(int handle, out nint lookup) => _lookups.Remove(handle, out lookup);
}

namespace SourceSharp.Abi;

/// <summary>
/// The size tiers native registered, as anything that has to fit inside one
/// sees them.
///
/// <para>
/// An interface rather than a static because the table is STATE — read from the
/// engine once and cached — and a cache nobody owns is a cache every test
/// shares. That is not theoretical here: three tests in this tree were passing
/// on a table some other suite had left behind, and the fixed order of the old
/// runner hid it completely.
/// </para>
///
/// <para>
/// <c>Forget</c> and <c>SupplyForTests</c> are deliberately NOT on it. They are
/// how a test arranges its own table, so they belong to the concrete class a
/// test constructs; a service handed to game code should have no way to empty
/// the tier list underneath it.
/// </para>
/// </summary>
public interface INetTiers
{
    /// <summary>Every tier registered for a shell, smallest first. Empty with no engine.</summary>
    IReadOnlyList<NetTierInfo> For(NetShellKind shell);

    /// <summary>The tier sizes, smallest first.</summary>
    IReadOnlyList<int> Sizes { get; }

    /// <summary>The smallest tier a layout fits in, or -1 if none does.</summary>
    int Ceiling(int layoutBytes);

    /// <summary>How full of its tier a layout is, from 0 to 1.</summary>
    double Fill(int layoutBytes);

    /// <summary>Whether a layout is close enough to its ceiling to be worth saying so.</summary>
    bool NearCeiling(int layoutBytes);
}

/// <summary>
/// No tiers at all.
///
/// <para>
/// For the publish paths, which run before any tier could have been registered
/// — a ServerClass may only be constructed before the engine collects the class
/// list, so at publish time the answer is genuinely "none" rather than "not
/// looked up yet". Naming that explicitly is better than passing null and
/// better than reaching for a static table that would be empty anyway.
/// </para>
/// </summary>
public sealed class EmptyTiers : INetTiers
{
    /// <summary>The one instance. It holds nothing, so one is enough.</summary>
    public static readonly EmptyTiers Instance = new();

    private EmptyTiers() { }

    /// <inheritdoc/>
    public IReadOnlyList<NetTierInfo> For(NetShellKind shell) => Array.Empty<NetTierInfo>();

    /// <inheritdoc/>
    public IReadOnlyList<int> Sizes => Array.Empty<int>();

    /// <inheritdoc/>
    public int Ceiling(int layoutBytes) => -1;

    /// <inheritdoc/>
    public double Fill(int layoutBytes) => 0d;

    /// <inheritdoc/>
    public bool NearCeiling(int layoutBytes) => false;
}

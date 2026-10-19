namespace SourceSharp.Abi;

/// <summary>
/// An effect name as the index the wire carries for it: the server's half of
/// the EffectDispatch network string table (plan_server_native_v2 T4b, which
/// retired IShimEffectsServer::EffectDispatchIndex).
/// </summary>
public interface IEffectDispatchNames
{
    /// <summary>The name's index, added if the table did not hold it; -1 when there is no table or it is full.</summary>
    int IndexOf(string name);
}

/// <summary>
/// <see cref="IEffectDispatchNames"/> over the server's string table area:
/// AddString on the table shell_stringtables.cpp creates, which answers the
/// index a name already has, so one name is one number for the level. The
/// native TE_DispatchEffect forward asks the same table the same question.
/// </summary>
public sealed class EffectDispatchNames : IEffectDispatchNames
{
    /// <summary>The table's name (shell_stringtables.cpp).</summary>
    public const string Table = "EffectDispatch";

    private readonly IEngineStringTables _tables;

    internal EffectDispatchNames(IEngineStringTables tables)
        => _tables = tables ?? throw new ArgumentNullException(nameof(tables));

    /// <inheritdoc/>
    public int IndexOf(string name)
    {
        int table = _tables.Find(Table);
        return table < 0 ? -1 : _tables.AddString(table, name);
    }

    /// <summary>The server's, over its engine area.</summary>
    public static IEffectDispatchNames For(ServiceContainer services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new EffectDispatchNames(services.Require<IEngineStringTables>());
    }
}

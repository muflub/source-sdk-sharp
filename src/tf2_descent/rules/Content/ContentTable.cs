using System.Text.Json;

namespace TF2.Descent.Rules.Content;

/// <summary>A row of a table keyed by id.</summary>
public interface IContentRow
{
    string Id { get; }
}

/// <summary>A loaded row table seen without its row type: what a rule that reads another lane's table by interface needs.</summary>
public interface IRowSource
{
    IEnumerable<IContentRow> UntypedRows { get; }
}

/// <summary>
/// A row of the item-base table (plan_tf2_rpg 2b): its id and display name. The skill
/// validator reads every loaded table whose rows are these (R17: no skill names, collides
/// with or references an item base), so the items table needs only to declare its row type so.
/// </summary>
public interface IItemBaseRow : IContentRow
{
    string Name { get; }
}

/// <summary>A loaded table's rows, in file order, and by id.</summary>
public sealed class Table<TRow> : IRowSource where TRow : IContentRow
{
    IEnumerable<IContentRow> IRowSource.UntypedRows => Rows.Cast<IContentRow>();

    private readonly Dictionary<string, TRow> _byId;

    public Table(string name, IReadOnlyList<TRow> rows)
    {
        Name = name;
        Rows = rows;
        _byId = new Dictionary<string, TRow>(StringComparer.Ordinal);
        foreach (var r in rows)
            _byId.TryAdd(r.Id, r);
    }

    public string Name { get; }
    public IReadOnlyList<TRow> Rows { get; }
    public int Count => Rows.Count;

    public bool Contains(string id) => _byId.ContainsKey(id);

    public bool TryGet(string id, out TRow row) => _byId.TryGetValue(id, out row!);

    public TRow this[string id] =>
        _byId.TryGetValue(id, out var row) ? row : throw new KeyNotFoundException($"no row '{id}' in table '{Name}'");
}

/// <summary>
/// One content table: its file name, how its JSON becomes typed records, and the rules
/// that make a bad row a load error. A later package adds a table by adding its JSON file
/// under Content/, a subclass of this, and a line in <see cref="ContentTables.All"/>.
/// </summary>
public abstract class ContentTable
{
    /// <summary>The table's name: its file is Content/&lt;Name&gt;.json.</summary>
    public abstract string Name { get; }

    /// <summary>A required table that is missing is a load error (<see cref="ContentRule.MissingTable"/>).</summary>
    public virtual bool Required => true;

    /// <summary>The typed value of the table, or null when its JSON is unusable (the errors say why).</summary>
    public abstract object? Parse(JsonElement root, ContentErrors errors);

    /// <summary>The rules that need other tables: run once every table has parsed. A table that did not parse is absent.</summary>
    public abstract void Validate(DescentContent content, ContentErrors errors);
}

/// <summary>A table whose JSON is an array of rows, each with a unique string "id".</summary>
public abstract class RowTable<TRow> : ContentTable where TRow : class, IContentRow
{
    /// <summary>One row from its fields; read every field through <paramref name="f"/> so unknown ones are reported.</summary>
    protected abstract TRow ReadRow(JsonFields f);

    /// <summary>A row's own rules against the rest of the content.</summary>
    protected virtual void ValidateRow(DescentContent content, TRow row, ContentErrors errors) { }

    /// <summary>Rules over the whole table (counts, coverage).</summary>
    protected virtual void ValidateTable(DescentContent content, Table<TRow> table, ContentErrors errors) { }

    public sealed override object? Parse(JsonElement root, ContentErrors errors)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Name, null, ContentRule.Malformed, $"expected an array of rows, found {root.ValueKind}");
            return null;
        }
        var rows = new List<TRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (var element in root.EnumerateArray())
        {
            string? id = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("id", out var idv)
                         && idv.ValueKind == JsonValueKind.String ? idv.GetString() : null;
            var f = new JsonFields(element, Name, id ?? "#" + index, errors);
            var row = ReadRow(f);
            f.End();
            index++;
            if (string.IsNullOrEmpty(row.Id))
            {
                errors.Add(Name, "#" + (index - 1), ContentRule.Malformed, "a row has no id");
                continue;
            }
            if (!seen.Add(row.Id))
            {
                errors.Add(Name, row.Id, ContentRule.DuplicateId, $"id '{row.Id}' appears more than once");
                continue;
            }
            // A malformed row stays, so what names it still resolves: its own error fails the
            // load, and one bad field does not cascade into an error at every reference.
            rows.Add(row);
        }
        return new Table<TRow>(Name, rows);
    }

    public sealed override void Validate(DescentContent content, ContentErrors errors)
    {
        if (!content.TryGet<Table<TRow>>(Name, out var table)) return;
        foreach (var row in table.Rows)
            ValidateRow(content, row, errors);
        ValidateTable(content, table, errors);
    }
}

/// <summary>A table whose JSON is one object (a block of settings).</summary>
public abstract class SingleTable<T> : ContentTable where T : class
{
    protected abstract T Read(JsonFields f);

    protected abstract void Validate(DescentContent content, T value, ContentErrors errors);

    public sealed override object? Parse(JsonElement root, ContentErrors errors)
    {
        var f = new JsonFields(root, Name, null, errors);
        var value = Read(f);
        f.End();
        return f.Ok ? value : null;
    }

    public sealed override void Validate(DescentContent content, ContentErrors errors)
    {
        if (content.TryGet<T>(Name, out var value))
            Validate(content, value, errors);
    }
}

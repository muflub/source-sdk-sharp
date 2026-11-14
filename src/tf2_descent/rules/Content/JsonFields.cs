using System.Text.Json;

namespace TF2.Descent.Rules.Content;

/// <summary>
/// Reads one JSON object of a table into a record by hand: a missing required field, a
/// field of the wrong kind and a field nobody reads (a typo) are each a
/// <see cref="ContentRule.Malformed"/> error naming the field, never a silent default.
/// Hand-written over <see cref="JsonElement"/> because the source-generated serializer
/// emits a static field this assembly may not hold (SS0010), and reflection-based JSON is
/// an open cost (docs/todo.md).
/// </summary>
public sealed class JsonFields
{
    private readonly JsonElement _obj;
    private readonly ContentErrors _errors;
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);

    public JsonFields(JsonElement obj, string table, string? row, ContentErrors errors)
    {
        _obj = obj;
        Table = table;
        Row = row;
        _errors = errors;
        if (obj.ValueKind != JsonValueKind.Object)
            Fail($"expected an object, found {obj.ValueKind}");
    }

    public string Table { get; }
    public string? Row { get; }

    /// <summary>Where this object's errors go; a nested object's reader reports into the same collector.</summary>
    public ContentErrors Errors => _errors;

    /// <summary>False once any field of this object was malformed.</summary>
    public bool Ok { get; private set; } = true;

    private void Fail(string message)
    {
        Ok = false;
        _errors.Add(Table, Row, ContentRule.Malformed, message);
    }

    private bool TryGet(string name, bool required, out JsonElement value)
    {
        _read.Add(name);
        if (_obj.ValueKind == JsonValueKind.Object && _obj.TryGetProperty(name, out value))
            return true;
        if (required && _obj.ValueKind == JsonValueKind.Object)
            Fail($"'{name}' is required");
        value = default;
        return false;
    }

    public string Str(string name) => StrOr(name, null, required: true) ?? "";

    public string? StrOr(string name, string? fallback) => StrOr(name, fallback, required: false);

    private string? StrOr(string name, string? fallback, bool required)
    {
        if (!TryGet(name, required, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        Fail($"'{name}' must be a string");
        return fallback;
    }

    public double Num(string name) => NumOr(name, 0, required: true);

    public double NumOr(string name, double fallback) => NumOr(name, fallback, required: false);

    private double NumOr(string name, double fallback, bool required)
    {
        if (!TryGet(name, required, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d)) return d;
        Fail($"'{name}' must be a number");
        return fallback;
    }

    public int Int(string name) => IntOr(name, 0, required: true);

    public int IntOr(string name, int fallback) => IntOr(name, fallback, required: false);

    private int IntOr(string name, int fallback, bool required)
    {
        if (!TryGet(name, required, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        Fail($"'{name}' must be a whole number");
        return fallback;
    }

    public long Long(string name)
    {
        if (!TryGet(name, true, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l;
        Fail($"'{name}' must be a whole number");
        return 0;
    }

    public bool Bool(string name)
    {
        if (!TryGet(name, true, out var v)) return false;
        if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
        Fail($"'{name}' must be true or false");
        return false;
    }

    /// <summary>The elements of an array field; empty (and an error if required) when absent or not an array.</summary>
    public IReadOnlyList<JsonElement> Array(string name, bool required = true)
    {
        if (!TryGet(name, required, out var v)) return [];
        if (v.ValueKind == JsonValueKind.Array) return [.. v.EnumerateArray()];
        Fail($"'{name}' must be an array");
        return [];
    }

    /// <summary>A field's raw value for a reader of its own (a rank curve), or null when absent (an error if required).</summary>
    public JsonElement? Element(string name, bool required = true) => TryGet(name, required, out var v) ? v : null;

    /// <summary>An optional array of strings; a non-string element is an error naming the field.</summary>
    public IReadOnlyList<string> Strings(string name, bool required = false)
    {
        var list = new List<string>();
        foreach (var e in Array(name, required))
        {
            if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString()!);
            else Fail($"'{name}' must hold only strings");
        }
        return list;
    }

    /// <summary>A nested object field, read under the same table and row.</summary>
    public JsonFields Obj(string name)
    {
        TryGet(name, true, out var v);
        return new JsonFields(v.ValueKind == JsonValueKind.Undefined ? EmptyObject() : v, Table, Row, _errors);
    }

    /// <summary>The names of an object field's properties, for a map keyed by id.</summary>
    public IReadOnlyList<(string Key, JsonElement Value)> Map(string name)
    {
        if (!TryGet(name, true, out var v)) return [];
        if (v.ValueKind == JsonValueKind.Object) return [.. v.EnumerateObject().Select(p => (p.Name, p.Value))];
        Fail($"'{name}' must be an object");
        return [];
    }

    /// <summary>Reports every field of the object that no reader asked for.</summary>
    public void End()
    {
        if (_obj.ValueKind != JsonValueKind.Object) return;
        foreach (var p in _obj.EnumerateObject())
            if (!_read.Contains(p.Name))
                Fail($"'{p.Name}' is not a field of this table");
    }

    private static JsonElement EmptyObject()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }
}

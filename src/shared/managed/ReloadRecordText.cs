using System;
using System.Collections.Generic;
using System.Globalization;

namespace SourceSharp.Abi;

/// <summary>
/// A <see cref="ReloadRecord"/> as flat key/value pairs, and back.
///
/// <para>
/// <b>WHY A RECORD HAS TO FLATTEN AT ALL.</b> Two of the three places a record
/// has to live are already flat string bags that other code owns:
/// <see cref="SavedEntity"/>'s local store, which the reload and the
/// <c>.sav</c> codec both already carry, and the codec's own on-disk form. A
/// record that could only live in <see cref="ReloadStore"/> would have meant a
/// second mechanism for the savegame path — and the whole argument for deriving
/// save/restore from one declaration is that a second mechanism is where the
/// two drift.
/// </para>
///
/// <para>
/// So the custom tier writes a record, and this turns it into keys an existing
/// carrier already knows how to move. One shape, three carriers.
/// </para>
///
/// <para>
/// <b>THE KEY SHAPE IS THE CONTRACT</b>, so it is written down here rather than
/// spread over a writer and a reader that can disagree:
/// </para>
///
/// <list type="bullet">
/// <item><c>&lt;prefix&gt;s.&lt;key&gt;</c> — one scalar.</item>
/// <item><c>&lt;prefix&gt;#</c> — how many rows.</item>
/// <item><c>&lt;prefix&gt;r.&lt;i&gt;.&lt;key&gt;</c> — one field of row <c>i</c>.</item>
/// </list>
///
/// <para>
/// The row COUNT is written even when it is zero, because "no rows" and "this
/// record was never written" are different answers and the reader must be able
/// to tell them apart.
/// </para>
/// </summary>
public static class ReloadRecordText
{
    /// <summary>The scalar segment. See the class remarks.</summary>
    public const string ScalarMark = "s.";

    /// <summary>The row-count key. See the class remarks.</summary>
    public const string CountMark = "#";

    /// <summary>The row segment. See the class remarks.</summary>
    public const string RowMark = "r.";

    /// <summary>
    /// Writes a record out through <paramref name="set"/>, which is whatever
    /// bag is carrying it.
    /// </summary>
    public static void Flatten(ReloadRecord record, string prefix, Action<string, string> set)
    {
        foreach (var (key, value) in record.Scalars)
            set(prefix + ScalarMark + key, value);

        set(prefix + CountMark,
            record.Rows.Count.ToString(CultureInfo.InvariantCulture));

        for (int i = 0; i < record.Rows.Count; i++)
        {
            string head = prefix + RowMark + i.ToString(CultureInfo.InvariantCulture) + ".";

            foreach (var (key, value) in record.Rows[i].Fields)
                set(head + key, value);
        }
    }

    /// <summary>
    /// Reads a record back out of a flat bag. Returns an EMPTY record when the
    /// bag holds nothing under the prefix, never null — see
    /// <see cref="ISaveCustom.OnRestore"/> for why the difference between
    /// "empty" and "absent" is the caller's to decide and not this method's.
    /// </summary>
    public static ReloadRecord Unflatten(string owner, string prefix,
                                         IEnumerable<KeyValuePair<string, string>> bag)
    {
        var record = new ReloadRecord(owner);
        var rows = new SortedDictionary<int, ReloadRow>();

        foreach (var (key, value) in bag)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            string rest = key.Substring(prefix.Length);

            if (rest.StartsWith(ScalarMark, StringComparison.Ordinal))
            {
                record.Set(rest.Substring(ScalarMark.Length), value);
                continue;
            }

            if (!rest.StartsWith(RowMark, StringComparison.Ordinal))
                continue;

            string tail = rest.Substring(RowMark.Length);
            int dot = tail.IndexOf('.');
            if (dot <= 0)
                continue;

            if (!int.TryParse(tail.Substring(0, dot), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out int index))
            {
                continue;
            }

            if (!rows.TryGetValue(index, out var row))
                rows[index] = row = new ReloadRow();

            row.Set(tail.Substring(dot + 1), value);
        }

        // SORTED BY INDEX RATHER THAN IN BAG ORDER. The bag is a dictionary, so
        // its order is hashing; a subsystem whose rows are a schedule or a
        // sequence would come back in an order nobody wrote.
        foreach (var (_, row) in rows)
            record.AddRow(row);

        return record;
    }
}

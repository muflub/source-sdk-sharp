//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Econ;

/// <summary>
/// A definition's keys after its prefabs are merged in: the <c>KeyValues</c>
/// copy <c>CEconItemDefinition::BInitFromKV</c> builds (<c>m_pKVItem</c>,
/// econ_item_schema.cpp:3023-3024) and every later read goes through.
///
/// <para>
/// A block is a node of its own, because a merge writes into it; a leaf is
/// the parsed <see cref="KeyValuesText"/> node itself, shared rather than
/// copied (nothing writes to a leaf: a later layer replaces it), so the
/// typed reads are KeyValuesText's, which are tier1's.
/// </para>
/// </summary>
public sealed class MergedKeys
{
    private readonly List<MergedKeys> _children = [];

    private MergedKeys(string name, KeyValuesText? leaf)
    {
        Name = name;
        Leaf = leaf;
    }

    /// <summary>An empty block named <paramref name="name"/>: <c>new KeyValues( name )</c>.</summary>
    public static MergedKeys Block(string name) => new(name, null);

    /// <summary><c>KeyValues::MakeCopy</c> of a parsed block.</summary>
    public static MergedKeys CopyOf(KeyValuesText block)
    {
        var copy = Block(block.Name);
        copy.InheritFrom(block);
        return copy;
    }

    public string Name { get; }

    /// <summary>The parsed leaf this key's value is, or null for a block.</summary>
    public KeyValuesText? Leaf { get; private set; }

    public bool IsBlock => Leaf is null;

    public IReadOnlyList<MergedKeys> Children => _children;

    /// <summary><c>FindKey</c>: the first child of that name, case-insensitively.</summary>
    public MergedKeys? Find(string name)
    {
        foreach (var child in _children)
        {
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    /// <summary><c>KeyValues::GetString( key, default )</c>; a block answers the default.</summary>
    public string? GetString(string key, string? defaultValue) =>
        Find(key) is { Leaf: { } leaf } ? leaf.AsString(defaultValue) : defaultValue;

    /// <summary><c>KeyValues::GetInt( key, default )</c>; a block reads 0, as stock's does.</summary>
    public int GetInt(string key, int defaultValue) =>
        Find(key) is { } k ? k.Leaf?.AsInt() ?? 0 : defaultValue;

    /// <summary><c>KeyValues::GetBool( key )</c>: <c>GetInt( key, 0 ) != 0</c>.</summary>
    public bool GetBool(string key) => GetInt(key, 0) != 0;

    /// <summary>This leaf's own value, <c>GetString()</c>; empty for a block.</summary>
    public string Value => Leaf?.AsString() ?? string.Empty;

    /// <summary>
    /// <c>RecursiveInheritKeyValues</c> (econ_item_schema.cpp:2897-2938): every
    /// key of <paramref name="source"/> is written into this one, a leaf
    /// replacing the first key of its name (<c>SetString</c> on
    /// <c>FindKey( name, true )</c>), a block merging into the first key of
    /// its name or a new block at the end.
    /// </summary>
    public void InheritFrom(KeyValuesText source)
    {
        foreach (var sub in source.Children)
        {
            if (!sub.IsBlock)
            {
                if (Find(sub.Name) is { } existing)
                {
                    // SetString on an existing key keeps its place in the list.
                    existing.Leaf = sub;
                    existing._children.Clear();
                }
                else
                {
                    _children.Add(new MergedKeys(sub.Name, sub));
                }
                continue;
            }

            // The first key of the name, block or not, takes the block's keys
            // in place (FindKey, then recurse into it); a new block goes last.
            var into = Find(sub.Name);
            if (into is null)
            {
                into = Block(sub.Name);
                _children.Add(into);
            }
            into.Leaf = null;
            into.InheritFrom(sub);
        }
    }
}

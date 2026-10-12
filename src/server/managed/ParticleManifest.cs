//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The names a particle config file (<c>.pcf</c>) defines, read straight out of
/// its binary DMX — plan_server_novalve.md P2.1.
///
/// <para>
/// <b>WHY THE SERVER NEEDS SO LITTLE.</b> server.so linked particles.a and
/// dmxloader.a so <c>CParticleSystemMgr</c> could load every definition in the
/// manifest, and then used exactly one thing from them: the NAMES of the
/// systems marked always-precache, which it adds to the ParticleEffectNames
/// string table. Nothing on the server simulates, renders or looks a
/// definition up. So this reads the names and nothing else.
/// </para>
///
/// <para>
/// <b>WHICH NAMES.</b> <c>ReadParticleConfigFile</c> registers the elements the
/// file's root element lists in its <c>particleSystemDefinitions</c> array —
/// not every <c>DmeParticleSystemDefinition</c> element in the file — so this
/// reads the root's attributes, follows that array, and answers those
/// elements' names in array order.
/// </para>
///
/// <para>
/// <b>THE ENCODING, AND ONLY ONE.</b> <c>&lt;!-- dmx encoding binary 2 format
/// pcf 1 --&gt;</c>, a newline and a NUL; every file the game ships is binary 2
/// (measured over the eleven in hl2_misc.vpk). Then an int16 string-table
/// count and that many NUL-terminated strings, which element TYPES and
/// attribute NAMES index with an int16; element names and string VALUES are
/// inline. Then an int32 element count and, per element, its type, its name
/// and a 16-byte GUID; then, per element in the same order, an int32
/// attribute count and each attribute's name, a type byte and its value. An
/// element-valued attribute is an int32 index into the element list. Any other
/// encoding is refused: its layout was never measured here, and a reader that
/// guessed would hand the precache a wrong list in silence.
/// </para>
/// </summary>
public static class PcfNames
{
    /// <summary>The one binary DMX encoding this reader walks.</summary>
    public const int Encoding = 2;

    /// <summary>
    /// The definition names <paramref name="file"/> registers, in the order the
    /// root lists them. Throws <see cref="FormatException"/> on anything that is
    /// not a binary-2 DMX with a definitions array -- never a partial list.
    /// </summary>
    public static List<string> Read(ReadOnlySpan<byte> file)
    {
        var r = new Reader(file);
        r.Header();
        r.StringTable();

        int count = r.Int32();
        if (count <= 0) throw new FormatException("no elements");
        var names = new string[count];
        for (int e = 0; e < count; e++)
        {
            r.Indexed();            // type
            names[e] = r.CString(); // name, inline in binary 2
            r.Skip(16);             // GUID
        }

        // Element 0 is the root. Only its attributes are read: the definitions
        // array is one of them, and every other element's block follows it.
        int attributes = r.Int32();
        for (int a = 0; a < attributes; a++)
        {
            string attrName = r.Indexed();
            byte type = r.Byte();
            if (attrName == "particleSystemDefinitions" && type == ElementArray)
            {
                int n = r.Int32();
                if (n < 0) throw new FormatException("negative definitions count");
                var result = new List<string>(n);
                for (int i = 0; i < n; i++)
                {
                    int idx = r.Int32();
                    if (idx < 0 || idx >= count)
                        throw new FormatException("definition index " + idx + " outside " + count + " elements");
                    result.Add(names[idx]);
                }
                return result;
            }
            r.SkipValue(type);
        }

        throw new FormatException("the root element has no particleSystemDefinitions array");
    }

    // DmAttributeType_t: the scalars 1..14, their arrays 15..28 in the same order.
    private const byte StringType = 5;
    private const byte BinaryType = 6;
    private const byte ElementArray = 15;

    private static int ScalarSize(byte t) => t switch
    {
        1 => 4,             // element index
        2 => 4,             // int
        3 => 4,             // float
        4 => 1,             // bool
        7 => 4,             // time
        8 => 4,             // color
        9 => 8,             // Vector2D
        10 => 12,           // Vector
        11 => 16,           // Vector4D
        12 => 12,           // QAngle
        13 => 16,           // Quaternion
        14 => 64,           // VMatrix
        _ => throw new FormatException("attribute type " + t + " has no fixed size"),
    };

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _d;
        private int _p;
        private string[] _table;

        public Reader(ReadOnlySpan<byte> d)
        {
            _d = d;
            _p = 0;
            _table = Array.Empty<string>();
        }

        public void Header()
        {
            string head = CString();
            const string prefix = "<!-- dmx encoding binary ";
            if (!head.StartsWith(prefix, StringComparison.Ordinal))
                throw new FormatException("not a binary DMX header: " + head.Trim());
            int end = head.IndexOf(' ', prefix.Length);
            if (end < 0 || !int.TryParse(head.AsSpan(prefix.Length, end - prefix.Length), out int encoding))
                throw new FormatException("unreadable encoding version in: " + head.Trim());
            if (encoding != Encoding)
                throw new FormatException("binary DMX encoding " + encoding + " is not " + Encoding);
        }

        public void StringTable()
        {
            int n = Int16();
            if (n < 0) throw new FormatException("negative string table count");
            _table = new string[n];
            for (int i = 0; i < n; i++)
                _table[i] = CString();
        }

        public string Indexed()
        {
            int idx = Int16();
            if ((uint)idx >= (uint)_table.Length)
                throw new FormatException("string index " + idx + " outside a table of " + _table.Length);
            return _table[idx];
        }

        public byte Byte()
        {
            Need(1);
            return _d[_p++];
        }

        public int Int16()
        {
            Need(2);
            int v = (short)(_d[_p] | (_d[_p + 1] << 8));
            _p += 2;
            return v;
        }

        public int Int32()
        {
            Need(4);
            int v = _d[_p] | (_d[_p + 1] << 8) | (_d[_p + 2] << 16) | (_d[_p + 3] << 24);
            _p += 4;
            return v;
        }

        public string CString()
        {
            int end = _d.Slice(_p).IndexOf((byte)0);
            if (end < 0) throw new FormatException("unterminated string at byte " + _p);
            string s = System.Text.Encoding.UTF8.GetString(_d.Slice(_p, end));
            _p += end + 1;
            return s;
        }

        public void Skip(int n)
        {
            Need(n);
            _p += n;
        }

        public void SkipValue(byte type)
        {
            if (type >= 1 && type <= 14)
            {
                SkipOne(type);
                return;
            }
            if (type >= 15 && type <= 28)
            {
                int n = Int32();
                if (n < 0) throw new FormatException("negative array length");
                for (int i = 0; i < n; i++)
                    SkipOne((byte)(type - 14));
                return;
            }
            throw new FormatException("unknown attribute type " + type);
        }

        private void SkipOne(byte type)
        {
            if (type == StringType)
            {
                CString();
                return;
            }
            if (type == BinaryType)
            {
                int n = Int32();
                if (n < 0) throw new FormatException("negative binary length");
                Skip(n);
                return;
            }
            Skip(ScalarSize(type));
        }

        private void Need(int n)
        {
            if (n < 0 || _p + n > _d.Length)
                throw new FormatException("truncated at byte " + _p);
        }
    }
}

/// <summary>
/// The particle manifests, read in C# — plan_server_novalve.md P2.1, porting
/// <c>ParseParticleEffects</c>, <c>ParseParticleEffectsMap</c> and
/// <c>PrecacheStandardParticleSystems</c> (particle_manifest.cpp) together with
/// the one fact the server took from <c>CParticleSystemMgr</c>: which systems
/// are always precached.
///
/// <para>
/// <b>THE RULE, AS STOCK HAS IT.</b> A manifest entry is <c>"file" "path"</c>;
/// a path written with a leading <c>!</c> marks every system that file defines
/// as always-precache. The game manifest is read without that flag otherwise;
/// a map's own manifest is read WITH it, for every file it names. A system a
/// later file defines again takes the later file's flag, as a redefinition
/// replaces the earlier definition.
/// </para>
///
/// <para>
/// <b>ONE DIVERGENCE, IN ORDER ONLY.</b> Stock precached the set from
/// <c>CWorld::Precache</c>; this precaches it right after the two parse steps
/// of the same level load. The names in ParticleEffectNames are the same; the
/// string indices they land on can differ, and nothing reads a particle index
/// except by name except index 0, "error", which the table is created with.
/// </para>
/// </summary>
public sealed class ParticleManifest
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public const string GameManifest = ParticleManifestFiles.GameManifest;
    public const int MaxMapEntries = ParticleManifestFiles.MaxMapEntries;

    private readonly ILevelFiles _level;

    // Name -> always-precache, in first-definition order.
    private readonly Dictionary<string, bool> _always = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    public ParticleManifest(ILog log, ILevelFiles files) 

    {

        _log = log;

        _level = files;

    }

    /// <summary>Every system read so far, in first-definition order.</summary>
    public IReadOnlyList<string> Definitions => _order;

    /// <summary>The systems to precache, in first-definition order.</summary>
    public List<string> AlwaysPrecache()
    {
        var list = new List<string>();
        foreach (string n in _order)
            if (_always[n]) list.Add(n);
        return list;
    }

    /// <summary>Level shutdown: stock uncached the manager's definitions.</summary>
    public void Clear()
    {
        _always.Clear();
        _order.Clear();
    }

    /// <summary><c>ParseParticleEffects( false, false )</c>.</summary>
    public void ParseGame()
    {
        foreach (string file in Files.Game())
            ReadConfigFile(file, precache: false);
    }

    /// <summary><c>ParseParticleEffectsMap( mapName, false )</c>.</summary>
    public void ParseMap(string mapName)
    {
        if (Files.Map(mapName) is { } files)
            foreach (string f in files)
                ReadConfigFile(f, precache: true);
    }

    // Which files the manifests name: the shared reader both sides use.
    private ParticleManifestFiles Files => new(_log, _level.ReadFileOnPath);

    private void ReadConfigFile(string entry, bool precache)
    {
        string path = entry;
        if (path.StartsWith('!'))
        {
            precache = true;
            path = path.Substring(1);
        }

        byte[]? bytes = _level.ReadFileOnPath(path, "GAME");
        if (bytes == null)
        {
            _log.Warning($"Particle config file '{path}' could not be read\n");
            return;
        }

        List<string> names;
        try
        {
            names = PcfNames.Read(bytes);
        }
        catch (FormatException e)
        {
            _log.Warning($"Particle config file '{path}' is not a particle file this server can read: {e.Message}\n");
            return;
        }

        foreach (string n in names)
        {
            if (!_always.ContainsKey(n))
                _order.Add(n);
            _always[n] = precache;
        }
    }
}

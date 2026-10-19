using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Guards the one thing the compiler cannot: that the managed mirror of the ABI
/// still agrees with the native header.
///
/// Both failures this catches have actually happened. The version constant was
/// bumped in the header and not in Abi.cs, so native refused to load managed
/// code at all and every symptom appeared somewhere else. And two adjacent
/// slots traded places between the two files, so calling one function invoked
/// the other — a string pointer arrived where an entity handle was expected,
/// and the crash was three frames away from anything that looked wrong.
///
/// The third is quieter than either and is what a batch of new slots invites: a
/// slot declared in BOTH files and never assigned in the gamesystem. Both sides
/// agree, the version matches, the layout is right — and the pointer is null, so
/// every guarded call in Engine.cs skips silently and the feature simply does
/// nothing. Nothing crashes and nothing is logged. That check went with the
/// engine API struct (plan_client_vtables CC-last): its slots are shim-area
/// methods now, and <see cref="EngineApiAreasTests"/> holds each one to a
/// header method on the side that serves it.
/// </summary>
public partial class AbiParityTests
{
    /// <summary>
    /// plan_client_vtables CC-last (2c) deleted the engine API struct, and
    /// CE-last the managed one: every former slot of either is a method of a
    /// shim area or a managed-built area (docs/engineapi-areas.tsv,
    /// docs/managed-interfaces.tsv), so the facts that compared each struct's
    /// two halves have no subject. What holds instead is the absence, in
    /// compiled metadata and in the header's code, each read with a control
    /// that it saw something.
    /// </summary>
    [Fact]
    public void NeitherFunctionPointerStructRemains()
    {
        var abi = typeof(ShimRootFwd).Assembly;
        // And the engine surface that left Abi for Shared.Interop in P1 run 3.
        var names = abi.GetTypes().Concat(typeof(EngineServices).Assembly.GetTypes())
                       .Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ShimRootFwd", names);   // the control: the reflection read the ABI assembly
        Assert.DoesNotContain("EngineApi", names);
        Assert.DoesNotContain("ManagedApi", names);

        string path = RepoLayout.Repo("src", "host", "native", "sourcesharp.h");
        Assert.True(File.Exists(path), $"the ABI header at {path}");
        string text = CodeOnly(File.ReadAllText(path));

        Assert.Contains("} SourceSharpMapLoad;", text);   // the control: the header's structs are read
        Assert.DoesNotContain("SourceSharpEngineApi", text);
        Assert.DoesNotContain("SourceSharpManagedApi", text);
    }

    /// <summary>The ABI header, in the compat farm.</summary>
    private static string? FindHeader() => RepoLayout.FindSdkFile(Path.Combine("host", "sourcesharp.h"));

    /// <summary>
    /// That the DATA struct crossing the ABI still has the shape the header
    /// gives it.
    ///
    /// <para>
    /// <see cref="Covers"/> checks function pointers, which is where a swap has
    /// actually happened; it says nothing about a struct passed BY POINTER
    /// through one of them. <c>SourceSharpBreakPiece</c> is filled by native
    /// code and read field by field in C#, and both halves compile happily
    /// against their own idea of the layout: a float inserted on one side only
    /// shifts every field after it, and the symptom is debris at plausible but
    /// wrong offsets with a fade time read out of a collision group.
    /// </para>
    ///
    /// <para>
    /// Names AND width, in order. A float and an int32 are both one word, so
    /// width alone would not catch a retype — but a retype keeps the name, and
    /// the two together do.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBreakPieceStructMatchesTheHeader()
    {
        string? header = FindHeader();

        if (header is null)
        {
            Assert.True(true, "ABI header found for the break piece layout (skipped, not found)");
            return;
        }

        var native = PlainFields(File.ReadAllText(header), "SourceSharpBreakPiece");

        Assert.True(native.Count > 0, "the header declares SourceSharpBreakPiece");

        // Vector is three floats laid out to match Source's, so it stands in for
        // a float[3] on the native side. Everything else is one word.
        var managed = typeof(BreakPiece)
            .GetFields(System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.Instance)
            .OrderBy(f => f.MetadataToken)
            .Select(f => (Name: f.Name, Words: f.FieldType == typeof(Vector) ? 3 : 1))
            .ToList();

        Assert.True(native.Count == managed.Count,
            $"break piece field counts match (native {native.Count}, managed {managed.Count})");

        for (int i = 0; i < Math.Min(native.Count, managed.Count); i++)
        {
            // The names differ by the suffix the header uses to say "three
            // floats" — OffsetXYZ against Offset — so the managed name cannot
            // simply be required to be EQUAL to the native one. See SameField
            // for why it is not a bare prefix test either.
            if (!SameField(native[i], managed[i]))
            {
                Assert.Fail($"break piece field {i} matches "
                          + $"(native '{native[i].Name}' x{native[i].Words}, "
                          + $"managed '{managed[i].Name}' x{managed[i].Words})");
                return;
            }
        }

        int words = managed.Sum(f => f.Words);

        Assert.True(Unsafe.SizeOf<BreakPiece>() == words * 4,
            $"the managed break piece is {words * 4} bytes, with no padding the header does not have");
    }

    /// <summary>
    /// The same check for <see cref="EffectData"/>, which is the THIRD struct
    /// crossing this ABI by pointer and the widest of the three.
    ///
    /// <para>
    /// It goes the same way <see cref="GibShot"/> does — C# fills it and
    /// <c>SourceSharp_EffectRegistrationRun</c> reads it field by field into a
    /// <c>CEffectData</c> — and that direction is the worse one. Twenty-one
    /// fields, seven of them vectors, and a field inserted on one side only
    /// shifts everything after it with nothing failing to compile. The symptom
    /// is a dispatched effect drawn at a plausible but wrong scale, on the
    /// wrong surface, with a radius read out of a damage type.
    /// </para>
    ///
    /// <para>
    /// This is also the one struct whose header half has a NAME that stops
    /// being obviously wrong when it drifts: every field of
    /// <c>CEffectData</c> has a plausible neighbour of the same width.
    /// </para>
    /// </summary>
    [Fact]
    public void TheEffectDataStructMatchesTheHeader()
    {
        string? header = FindHeader();

        if (header is null)
        {
            Assert.True(true, "ABI header found for the effect data layout (skipped, not found)");
            return;
        }

        var native = PlainFields(File.ReadAllText(header), "SourceSharpEffectData_t");

        Assert.True(native.Count > 0, "the header declares SourceSharpEffectData_t");

        var managed = typeof(EffectData)
            .GetFields(System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.Instance)
            .OrderBy(f => f.MetadataToken)
            .Select(f => (Name: f.Name, Words: f.FieldType == typeof(Vector) ? 3 : 1))
            .ToList();

        Assert.True(native.Count == managed.Count,
            $"effect data field counts match (native {native.Count}, managed {managed.Count})");

        for (int i = 0; i < Math.Min(native.Count, managed.Count); i++)
        {
            // Same rule as its two neighbours: OriginXYZ against Origin,
            // AnglesPYR against Angles. See SameField.
            if (!SameField(native[i], managed[i]))
            {
                Assert.Fail($"effect data field {i} matches "
                          + $"(native '{native[i].Name}' x{native[i].Words}, "
                          + $"managed '{managed[i].Name}' x{managed[i].Words})");
                return;
            }
        }

        int effectWords = managed.Sum(f => f.Words);

        Assert.True(Unsafe.SizeOf<EffectData>() == effectWords * 4,
            $"the managed effect data is {effectWords * 4} bytes, with no padding the header does not have");
    }

    /// <summary>
    /// Whether one native field and one managed field are the same field.
    ///
    /// <para>
    /// NOT A BARE PREFIX TEST, and it used to be. The header spells a float[3]
    /// with a suffix that says so — <c>OffsetXYZ</c> against <c>Offset</c>,
    /// <c>OriginXYZ</c> against <c>Origin</c>, <c>AnglesPYR</c> against
    /// <c>Angles</c> — so equality alone is too strict. But
    /// <c>StartsWith</c> alone is too LOOSE in a way that matters: any rename
    /// that EXTENDS the old name passes it. Renaming the header's
    /// <c>Skin</c> to <c>SkinRenamed</c> satisfied
    /// <c>"SkinRenamed".StartsWith("Skin")</c> and kept its width, so the check
    /// stayed green through a one-sided rename — the exact drift it exists to
    /// catch. Found by mutating it.
    /// </para>
    ///
    /// <para>
    /// So: the names must be EQUAL, or differ by exactly the suffix that means
    /// "three floats" — and in that case the field really has to be three
    /// words. A one-word field whose native name merely starts with the managed
    /// one is a rename, and fails.
    /// </para>
    /// </summary>
    private static bool SameField((string Name, int Words) native, (string Name, int Words) managed)
    {
        if (native.Words != managed.Words)
            return false;

        if (native.Name == managed.Name)
            return true;

        // The only licensed difference, and only for a vector.
        if (native.Words != 3 || !native.Name.StartsWith(managed.Name, StringComparison.Ordinal))
            return false;

        string suffix = native.Name.Substring(managed.Name.Length);
        return suffix is "XYZ" or "PYR";
    }

    /// <summary>
    /// That <see cref="MapLoadRaw"/> still has the shape <c>SourceSharpMapLoad</c>
    /// gives it.
    ///
    /// <para>
    /// It is the FOURTH struct crossing this ABI by pointer and the first made
    /// entirely of pointers, which is what makes it worse than the three that
    /// came before: every field is the same width and the same type, so a
    /// transposition between the two files is invisible to the compiler on both
    /// sides and to every width check. The symptom would be managed code
    /// parsing the landmark name as the entity lump — a map that loads with no
    /// entities in it and nothing anywhere saying why.
    /// </para>
    ///
    /// <para>
    /// <see cref="PlainFields"/> cannot be reused: it knows float, int32_t and
    /// uint32_t, and this struct has none of them.
    /// </para>
    /// </summary>
    [Fact]
    public void TheMapLoadStructMatchesTheHeader()
    {
        string? header = FindHeader();

        if (header is null)
        {
            Assert.True(true, "ABI header found for the map load layout (skipped, not found)");
            return;
        }

        var native = PointerFields(CodeOnly(File.ReadAllText(header)), "SourceSharpMapLoad");

        Assert.True(native.Count > 0, "the header declares SourceSharpMapLoad");

        var managed = typeof(MapLoadRaw)
            .GetFields(System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.Instance)
            .OrderBy(f => f.MetadataToken)
            .Select(f => f.Name)
            .ToList();

        Assert.True(native.Count == managed.Count,
            $"map load field counts match (native {native.Count}, managed {managed.Count})");

        for (int i = 0; i < Math.Min(native.Count, managed.Count); i++)
        {
            // The header's Hungarian p-prefix is the only permitted difference,
            // and it is required rather than optional: allowing a bare prefix
            // match would let pMapName stand against Map.
            if (native[i] != "p" + managed[i])
            {
                Assert.Fail($"map load field {i} matches "
                          + $"(native '{native[i]}', managed '{managed[i]}')");
                return;
            }
        }

        // Four pointers and nothing else. A managed field of a non-pointer type
        // would still be one field with the right name and a different width.
        Assert.True(Unsafe.SizeOf<MapLoadRaw>() == managed.Count * IntPtr.Size,
            $"the managed map load is {managed.Count} pointers wide, with no padding the header does not have");
    }

    /// <summary>
    /// That the surface trace's out-parameters are read back into
    /// <see cref="SurfaceTraceResult"/> in the order the header writes them
    /// (tf2 stage 0.8).
    ///
    /// <para>
    /// The three trailing outs — surfaceprop, <c>SURF_*</c> flags, hitgroup —
    /// are all <c>int32_t *</c>, so the signature check in
    /// <see cref="EngineApiAreasTests"/> passes a transposition of any two of
    /// them. The symptom would be a bullet whose hitgroup is a surfaceprop
    /// index: a headshot read as "concrete". So the NAMES are compared, in
    /// order, across the three places that spell them: the header's parameter
    /// list, the managed call's arguments, and the result's positional members.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSurfaceTraceOutsReachTheResultInHeaderOrder()
    {
        string header = CodeOnly(File.ReadAllText(RepoLayout.Repo("src", "shared", "native", "ishim_areas.h")));
        var decl = Regex.Match(header, @"virtual\s+int32_t\s+TraceLineSurface\s*\((?<p>[^)]*)\)");
        Assert.True(decl.Success, "ishim_areas.h declares IShimWorldServer::TraceLineSurface");
        var native = decl.Groups["p"].Value.Split(',')
                         .Select(p => Regex.Match(p, @"(\w+)\s*$").Groups[1].Value)
                         .SkipWhile(n => n != "pOutHit").Skip(1)
                         .Select(n => n.StartsWith("pOut", StringComparison.Ordinal) ? n[4..] : n)
                         .ToList();

        string side = File.ReadAllText(RepoLayout.Repo("src", "shared", "interop", "EngineSide.cs"));
        var call = Regex.Match(side, @"ServerShims\.TraceLineSurface\((?<a>[^;]*)\);");
        Assert.True(call.Success, "EngineSide calls the TraceLineSurface slot");
        var passed = call.Groups["a"].Value.Split(',')
                         .Select(a => a.Trim())
                         .SkipWhile(a => a != "&hit").Skip(1)
                         .Select(a => a.TrimStart('&'))
                         .ToList();

        var members = typeof(SurfaceTraceResult).GetConstructors()
                          .Single(c => c.GetParameters().Length > 1)
                          .GetParameters()
                          .Select(p => p.Name!)
                          .SkipWhile(n => n != "Entity").Skip(1)
                          .ToList();

        Assert.Equal(["SurfaceProps", "SurfaceFlags", "Hitgroup"], native);
        Assert.Equal(native.Select(n => n.ToLowerInvariant()),
                     passed.Select(a => a.ToLowerInvariant()));
        Assert.Equal(native.Select(n => n.ToLowerInvariant()),
                     members.Select(m => m.ToLowerInvariant()));
    }

    /// <summary>
    /// The <c>const char *</c> fields of a struct in the header, in declaration
    /// order.
    /// </summary>
    private static List<string> PointerFields(string text, string structName)
    {
        var fields = new List<string>();

        int end = text.IndexOf($"}} {structName};", StringComparison.Ordinal);
        if (end < 0)
            return fields;

        int start = text.LastIndexOf("typedef struct", end, StringComparison.Ordinal);
        if (start < 0)
            return fields;

        foreach (Match m in Regex.Matches(text[start..end], @"\bconst\s+char\s*\*\s*(\w+)\s*;"))
            fields.Add(m.Groups[1].Value);

        return fields;
    }

    /// <summary>
    /// The plain (non-function-pointer) fields of a header struct, in order,
    /// each with how many 4-byte words it occupies.
    ///
    /// <para>
    /// Comments are stripped first, for the reason the native scanners record:
    /// the prose above these fields names types and field names, and an
    /// unstripped scan reads the explanation as a declaration.
    /// </para>
    /// </summary>
    private static List<(string Name, int Words)> PlainFields(string text, string structName)
    {
        var fields = new List<(string, int)>();

        int end = text.IndexOf($"}} {structName};", StringComparison.Ordinal);
        if (end < 0)
            return fields;

        int start = text.LastIndexOf("typedef struct", end, StringComparison.Ordinal);
        if (start < 0)
            return fields;

        string body = Regex.Replace(text[start..end], @"//[^\n]*", string.Empty);

        foreach (Match m in Regex.Matches(body, @"\b(float|int32_t|uint32_t)\s+(\w+)\s*(?:\[\s*(\d+)\s*\])?\s*;"))
        {
            int words = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 1;
            fields.Add((m.Groups[2].Value, words));
        }

        return fields;
    }

}

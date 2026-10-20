//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.Shadows;

// D9: the debug overlays (DrawRenderToTextureDebugInfo, DebugDrawFrustum),
// the console commands (r_shadowdir, r_shadowangles, r_shadowcolor,
// r_shadowdist, r_shadowblobbycutoff, r_shadowbreak), the fork's
// cs_cl_shadow_report, the proxies' view of the manager, and the hot reload.
public sealed partial class ShadowManager
{
    private void Line(in Vector start, in Vector end) => _debugOverlay.AddLineOverlay(start, end, 255, 0, 0, true, (float)0.01);

    /// <summary><c>DrawRenderToTextureDebugInfo</c>: the caster's box in red, and its entity's index (the fork's text).</summary>
    private void DrawRenderToTextureDebugInfo(nint renderable, in Vector mins, in Vector maxs)
    {
        Span<Vector> vec = stackalloc Vector[3];
        Basis(renderable, vec);

        Vector size = new(maxs.X - mins.X, maxs.Y - mins.Y, maxs.Z - mins.Z);
        Vector origin = _renderables.RenderOrigin(renderable);

        Vector start = ShadowMath.MA(origin, mins.X, vec[0]);
        start = ShadowMath.MA(start, mins.Y, vec[1]);
        start = ShadowMath.MA(start, mins.Z, vec[2]);

        Vector end = ShadowMath.MA(start, size.X, vec[0]);
        Vector end2 = ShadowMath.MA(end, size.Z, vec[2]);
        Line(start, end);
        Line(end2, end);

        end = ShadowMath.MA(start, size.Y, vec[1]);
        end2 = ShadowMath.MA(end, size.Z, vec[2]);
        Line(start, end);
        Line(end2, end);

        end = ShadowMath.MA(start, size.Z, vec[2]);
        Line(start, end);

        start = end;
        end = ShadowMath.MA(start, size.X, vec[0]);
        Line(start, end);

        end = ShadowMath.MA(start, size.Y, vec[1]);
        Line(start, end);

        start = ShadowMath.MA(end, size.X, vec[0]);
        end = ShadowMath.MA(start, -size.X, vec[0]);
        Line(start, end);

        end = ShadowMath.MA(start, -size.Y, vec[1]);
        Line(start, end);

        end = ShadowMath.MA(start, -size.Z, vec[2]);
        Line(start, end);

        start = end;
        end = ShadowMath.MA(start, -size.X, vec[0]);
        Line(start, end);

        end = ShadowMath.MA(start, -size.Y, vec[1]);
        Line(start, end);

        int entIndex = _renderables.NetworkableEntIndex(renderable);
        string text = entIndex >= 0
            ? entIndex.ToString(CultureInfo.InvariantCulture)
            : unchecked((ulong)renderable).ToString("X", CultureInfo.InvariantCulture);
        _debugOverlay.AddTextOverlay(origin, 0, text);
    }

    private void LineDrawHelper(in Vector startShadowSpace, in Vector endShadowSpace, in ShadowMatrix shadowToWorld, int r, int g, int b)
    {
        Vector startWorld = ShadowMath.MultiplyPositionProjective(shadowToWorld, startShadowSpace);
        Vector endWorld = ShadowMath.MultiplyPositionProjective(shadowToWorld, endShadowSpace);
        _debugOverlay.AddLineOverlay(new Vector(startWorld.X + 0.0f, startWorld.Y + 0.0f, startWorld.Z + 1.0f),
                                     new Vector(endWorld.X + 0.0f, endWorld.Y + 0.0f, endWorld.Z + 1.0f), r, g, b, false, -1);
    }

    /// <summary><c>DebugDrawFrustum</c>: the flashlight's frustum's twelve edges and an RGB triad at its front.</summary>
    private void DebugDrawFrustum(in ShadowMatrix worldToFlashlight)
    {
        ShadowMatrix flashlightToWorld = default;
        ShadowMath.InverseGeneral(worldToFlashlight, ref flashlightToWorld);

        LineDrawHelper(new(0, 0, 0), new(0, 0, 1), flashlightToWorld, 255, 255, 255);
        LineDrawHelper(new(0, 0, 1), new(0, 1, 1), flashlightToWorld, 255, 255, 255);
        LineDrawHelper(new(0, 1, 1), new(0, 1, 0), flashlightToWorld, 255, 255, 255);
        LineDrawHelper(new(0, 1, 0), new(0, 0, 0), flashlightToWorld, 255, 255, 255);
        LineDrawHelper(new(1, 0, 0), new(1, 0, 1), flashlightToWorld, 255, 255, 255);
        LineDrawHelper(new(1, 0, 1), new(1, 1, 1), flashlightToWorld, 255, 255, 255);
        LineDrawHelper(new(1, 1, 1), new(1, 1, 0), flashlightToWorld, 255, 255, 255);
        LineDrawHelper(new(1, 1, 0), new(1, 0, 0), flashlightToWorld, 255, 255, 255);
        LineDrawHelper(new(0, 0, 0), new(1, 0, 0), flashlightToWorld, 255, 255, 255);
        LineDrawHelper(new(0, 0, 1), new(1, 0, 1), flashlightToWorld, 255, 255, 255);
        LineDrawHelper(new(0, 1, 1), new(1, 1, 1), flashlightToWorld, 255, 255, 255);
        LineDrawHelper(new(0, 1, 0), new(1, 1, 0), flashlightToWorld, 255, 255, 255);

        LineDrawHelper(new(0.5f, 0.5f, 0.0f), new(1.0f, 0.5f, 0.0f), flashlightToWorld, 255, 0, 0);
        LineDrawHelper(new(0.5f, 0.5f, 0.0f), new(0.5f, 1.0f, 0.0f), flashlightToWorld, 0, 255, 0);
        LineDrawHelper(new(0.5f, 0.5f, 0.0f), new(0.5f, 0.5f, 0.35f), flashlightToWorld, 0, 0, 255);
    }

    //-------------------------------------------------------------------------
    // The console commands, as stock's CON_COMMAND_Fs read their arguments.
    //-------------------------------------------------------------------------

    private static string F2(float f) => ((double)f).ToString("F2", CultureInfo.InvariantCulture);

    /// <summary><c>r_shadowdir [x y z]</c>.</summary>
    public void CommandShadowDir(IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            Vector dir = GetShadowDirection();
            _console.Msg($"{F2(dir.X)} {F2(dir.Y)} {F2(dir.Z)}");
            return;
        }
        if (args.Count == 4)
            SetShadowDirection(new Vector((float)Atof(args[1]), (float)Atof(args[2]), (float)Atof(args[3])));
    }

    /// <summary><c>r_shadowangles [pitch yaw roll]</c>.</summary>
    public void CommandShadowAngles(IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            Vector angles = Detail.DetailMath.VectorAngles(GetShadowDirection());
            _console.Msg($"{F2(angles.X)} {F2(angles.Y)} {F2(angles.Z)}");
            return;
        }
        if (args.Count == 4)
        {
            var angles = new QAngle((float)Atof(args[1]), (float)Atof(args[2]), (float)Atof(args[3]));
            ShadowMath.AngleVectors(angles, out Vector dir, out _, out _);
            SetShadowDirection(dir);
        }
    }

    /// <summary><c>r_shadowcolor [r g b]</c>.</summary>
    public void CommandShadowColor(IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            _console.Msg($"Shadow color {_ambientR} {_ambientG} {_ambientB}");
            return;
        }
        if (args.Count == 4)
            SetShadowColor(unchecked((byte)Atoi(args[1])), unchecked((byte)Atoi(args[2])), unchecked((byte)Atoi(args[3])));
    }

    /// <summary><c>r_shadowdist [d]</c>.</summary>
    public void CommandShadowDist(IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            _console.Msg($"Shadow distance {F2(_shadowCastDist)}");
            return;
        }
        if (args.Count == 2)
            SetShadowDistance((float)Atof(args[1]));
    }

    /// <summary><c>r_shadowblobbycutoff [area]</c>.</summary>
    public void CommandShadowBlobbyCutoff(IReadOnlyList<string> args)
    {
        if (args.Count == 1)
        {
            _console.Msg($"Cutoff area {F2(_minShadowArea)}");
            return;
        }
        if (args.Count == 2)
            SetShadowBlobbyCutoffArea((float)Atof(args[1]));
    }

    /// <summary><c>r_shadowbreak</c> (_DEBUG): arms a breakpoint flag the next shadow update clears.</summary>
    public void CommandShadowBreak() => _break = true;

    // atof/atoi: the longest number at the front, else 0.
    private static double Atof(string s)
    {
        int end = 0;
        while (end < s.Length && (char.IsWhiteSpace(s[end])))
            end++;
        int start = end;
        if (end < s.Length && (s[end] == '+' || s[end] == '-'))
            end++;
        while (end < s.Length && (char.IsAsciiDigit(s[end]) || s[end] == '.'))
            end++;
        if (end < s.Length && (s[end] == 'e' || s[end] == 'E'))
        {
            int e = end + 1;
            if (e < s.Length && (s[e] == '+' || s[e] == '-'))
                e++;
            if (e < s.Length && char.IsAsciiDigit(s[e]))
            {
                end = e;
                while (end < s.Length && char.IsAsciiDigit(s[end]))
                    end++;
            }
        }
        return double.TryParse(s.AsSpan(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;
    }

    private static int Atoi(string s) => MaterialProxies.ProxyVarName.StrToL(s);

    /// <summary>
    /// The fork's <c>cs_cl_shadow_report</c>: each shadow (its entity, where
    /// it was last projected from and its renderable's render origin, its
    /// kind, its render frame) and the totals. The fork's resolve line counts
    /// SourceSharp_RenderableFromHandle's asks, which native keeps; S2 decides
    /// what the managed report prints there.
    /// </summary>
    public void PrintShadowReport(bool resetCounters)
    {
        int held = 0, rtt = 0, simple = 0, depth = 0, none = 0, lost = 0;
        for (int h = _shadowHandles.Head; h != Invalid; h = _shadowHandles.Next(h))
        {
            ++held;
            ref ClientShadow shadow = ref _shadows[h];
            nint renderable = _renderables.RenderableOf(shadow.Entity);
            if (renderable == 0)
                ++lost;
            int type = GetActualShadowCastType(h);
            switch (type)
            {
                case ShadowTypes.RenderToTexture: ++rtt; break;
                case ShadowTypes.Simple: ++simple; break;
                case ShadowTypes.RenderToDepthTexture: ++depth; break;
                default: ++none; break;
            }
            Vector render = renderable != 0 ? _renderables.RenderOrigin(renderable) : Vector.Zero;
            string F1(float f) => ((double)f).ToString("F1", CultureInfo.InvariantCulture);
            // CBaseHandle's entry and serial (const.h: the low 16 bits and the high 16).
            int entry = (int)(shadow.Entity & 0xFFFF);
            int serial = (int)(shadow.Entity >> 16);
            _console.Msg($"[SourceSharp] shadow {h}: entry {entry} serial {serial}  projectedFrom ({F1(shadow.LastOrigin.X)} {F1(shadow.LastOrigin.Y)} {F1(shadow.LastOrigin.Z)})  " +
                         $"renderOrigin ({F1(render.X)} {F1(render.Y)} {F1(render.Z)})  type {type}  renderFrame {shadow.RenderFrame}  " +
                         (renderable != 0 ? "resolved" : "UNRESOLVED"));
        }
        _console.Msg($"[SourceSharp] shadow report: held {held}  rtt {rtt}  simple {simple}  depth {depth}  none {none}  " +
                     $"lostRenderable {lost}  texturesDrawn {_rttShadowTexturesDrawn}  rttActive {(_renderToTextureActive ? 1 : 0)}  r_shadows {_settings.Shadows}");
        if (held == 0)
            _console.Msg("[SourceSharp] shadow report: THE MANAGER HOLDS NO SHADOWS -- every number above is about an empty list and none of them is evidence that shadows work");
        if (resetCounters)
            _rttShadowTexturesDrawn = 0;
    }

    //-------------------------------------------------------------------------
    // The proxies' view (Q1: their OnBind is managed).
    //-------------------------------------------------------------------------

    /// <inheritdoc/>
    public int AtlasPage => _allocator.Page;

    /// <inheritdoc/>
    public bool TryGetShadowInfo(int clientShadowHandle, out ShadowEngineInfo info)
    {
        if (!_shadowHandles.IsValid(clientShadowHandle))
        {
            info = default;
            return false;
        }
        _engine.GetInfo(_shadows[clientShadowHandle].ShadowHandle, out info);
        return true;
    }

    //-------------------------------------------------------------------------
    // Hot reload: the manager outlives the module.
    //-------------------------------------------------------------------------

    private const int SaveFormat = 1;

    private static void Write(ReloadWriter w, in Vector v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static Vector ReadVector(ReloadReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());

    private static void Write(ReloadWriter w, in FlashlightState s)
    {
        ReadOnlySpan<byte> bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<FlashlightState>(in s));
        w.Write(bytes);
    }

    private static FlashlightState ReadState(ReloadReader r)
    {
        byte[] bytes = r.ReadBytes();
        return System.Runtime.InteropServices.MemoryMarshal.Read<FlashlightState>(bytes);
    }

    /// <inheritdoc/>
    public void Save(ReloadWriter writer)
    {
        writer.Write(SaveFormat);
        Write(writer, _simpleShadowDir);
        writer.Write(_ambientR | (_ambientG << 8) | (_ambientB << 16));
        writer.Write(_simpleShadow);
        writer.Write(_renderShadow);
        writer.Write(_renderModelShadow);
        writer.Write(_dummyColorTexture);
        writer.Write(_renderToTextureActive);
        writer.Write(_renderTargetNeedsClear);
        writer.Write(_updatingDirtyShadows);
        writer.Write(_shadowCastDist);
        writer.Write(_minShadowArea);
        writer.Write(_depthTextureActive);
        writer.Write(_depthTextureResolution);
        writer.Write(_maxDepthTextureShadows);
        writer.Write(_depthOverflowWarned);
        writer.Write(_shadowMapWarnings);
        writer.Write(_break);
        writer.Write(_rttShadowTexturesDrawn);
        writer.Write(_depthTextureCache.Count);
        for (int i = 0; i < _depthTextureCache.Count; i++)
        {
            writer.Write(_depthTextureCache[i]);
            writer.Write(_depthTextureCacheLocks[i]);
        }
        _dirtyShadows.Save(writer);
        writer.Write(_transparentShadows.Count);
        foreach (int h in _transparentShadows)
            writer.Write(h);
        _shadowHandles.Save(writer);
        for (int h = 0; h <= _shadowHandles.LastAlloc; h++)
        {
            ref ClientShadow s = ref _shadows[h];
            writer.Write(unchecked((int)s.Entity));
            writer.Write(s.ShadowHandle);
            writer.Write(s.LeafShadow);
            writer.Write(s.Flags);
            for (int i = 0; i < 16; i++)
                writer.Write(s.WorldToShadow[i]);
            writer.Write(s.WorldSizeX);
            writer.Write(s.WorldSizeY);
            Write(writer, s.LastOrigin);
            writer.Write(s.LastAngles.Pitch);
            writer.Write(s.LastAngles.Yaw);
            writer.Write(s.LastAngles.Roll);
            writer.Write(s.ShadowTexture);
            writer.Write(s.RenderFrame);
            writer.Write(unchecked((int)s.TargetEntity));
            writer.Write(s.HasState);
            Write(writer, s.State);
        }
        _allocator.Save(writer);
    }

    /// <inheritdoc/>
    public void Restore(ReloadReader reader)
    {
        if (reader.ReadInt() != SaveFormat)
            throw new ReloadFormatException("the shadow manager's format is not " + SaveFormat);
        _simpleShadowDir = ReadVector(reader);
        int ambient = reader.ReadInt();
        _ambientR = (byte)ambient;
        _ambientG = (byte)(ambient >> 8);
        _ambientB = (byte)(ambient >> 16);
        _simpleShadow = reader.ReadInt();
        _renderShadow = reader.ReadInt();
        _renderModelShadow = reader.ReadInt();
        _dummyColorTexture = reader.ReadInt();
        _renderToTextureActive = reader.ReadBool();
        _renderTargetNeedsClear = reader.ReadBool();
        _updatingDirtyShadows = reader.ReadBool();
        _shadowCastDist = reader.ReadFloat();
        _minShadowArea = reader.ReadFloat();
        _depthTextureActive = reader.ReadBool();
        _depthTextureResolution = reader.ReadInt();
        _maxDepthTextureShadows = reader.ReadInt();
        _depthOverflowWarned = reader.ReadBool();
        _shadowMapWarnings = reader.ReadInt();
        _break = reader.ReadBool();
        _rttShadowTexturesDrawn = reader.ReadInt();
        _depthTextureCache.Clear();
        _depthTextureCacheLocks.Clear();
        int depthCount = reader.ReadInt();
        for (int i = 0; i < depthCount; i++)
        {
            _depthTextureCache.Add(reader.ReadInt());
            _depthTextureCacheLocks.Add(reader.ReadBool());
        }
        _dirtyShadows.Restore(reader);
        _transparentShadows.Clear();
        int transparent = reader.ReadInt();
        for (int i = 0; i < transparent; i++)
            _transparentShadows.Add(reader.ReadInt());
        _shadowHandles.Restore(reader);
        _shadows = new ClientShadow[Math.Max(64, _shadowHandles.LastAlloc + 1)];
        for (int h = 0; h <= _shadowHandles.LastAlloc; h++)
        {
            ref ClientShadow s = ref _shadows[h];
            s.Entity = unchecked((uint)reader.ReadInt());
            s.ShadowHandle = reader.ReadInt();
            s.LeafShadow = reader.ReadInt();
            s.Flags = reader.ReadInt();
            for (int i = 0; i < 16; i++)
                s.WorldToShadow[i] = reader.ReadFloat();
            s.WorldSizeX = reader.ReadFloat();
            s.WorldSizeY = reader.ReadFloat();
            s.LastOrigin = ReadVector(reader);
            s.LastAngles = new QAngle(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
            s.ShadowTexture = reader.ReadInt();
            s.RenderFrame = reader.ReadInt();
            s.TargetEntity = unchecked((uint)reader.ReadInt());
            s.HasState = reader.ReadBool();
            s.State = ReadState(reader);
        }
        _allocator.Restore(reader);
    }
}

/// <summary>What the Shadow and ShadowModel proxies read of the manager (Q1: their OnBind is managed).</summary>
public interface IShadowProxySource
{
    /// <summary>The atlas render target's id, 0 while there is none (stock's GetShadowTexture answers NULL then).</summary>
    int AtlasPage { get; }

    /// <summary>The engine's ShadowInfo_t for a client shadow; false for a handle that names none.</summary>
    bool TryGetShadowInfo(int clientShadowHandle, out ShadowEngineInfo info);
}

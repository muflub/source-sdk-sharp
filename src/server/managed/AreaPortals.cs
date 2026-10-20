//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The area portal records func_areaportal and func_areaportalwindow keep
/// (plan_server_native_v2 T4d): what areaportal_list.h's CAreaPortal and
/// shell_areaportal.cpp's four slots were.
/// </summary>
public interface IAreaPortals
{
    /// <summary>A record for vbsp's portal number, opened in the engine; its token, or 0 for a negative number.</summary>
    uint Create(int portalNumber, int portalVersion);

    /// <summary>Reopens the portal in the engine and forgets the record.</summary>
    void Destroy(uint portal);

    /// <summary>Opens or shuts the portal, in the record and in the engine.</summary>
    void SetOpen(uint portal, bool open);

    /// <summary>
    /// Makes the portal a window over a world-space volume that closes beyond
    /// <paramref name="fadeDistance"/> (plus a buffer) from the viewer.
    /// </summary>
    void SetWindow(uint portal, Vector worldMins, Vector worldMaxs, float fadeDistance);
}

/// <summary>
/// The records, and the per-client walk over them that ClientSetupVisibility
/// asks for once per client per frame (plan_server_native_v2 T4d).
///
/// <para>
/// Ported from stock's CFuncAreaPortalBase/CAreaPortal (func_areaportalbase.cpp,
/// func_areaportal.cpp, func_areaportalwindow.cpp) by way of the native shim
/// that carried them: a portal is open on the server when its record is open,
/// a window's viewer is inside its fade distance, and the viewer is in front
/// of the portal's plane; it is open on the CLIENT unless the record or the
/// window shut it. The engine is told the server's answer for every portal,
/// in creation order, 512 at a time.
/// </para>
///
/// <para>
/// <b>Carried across a hot reload</b> (ISaveCustom): the entities keep their
/// tokens, and the engine's own portal states outlive the assembly, so the
/// records must too or every portal would drop out of the walk.
/// </para>
/// </summary>
public sealed class AreaPortals : IAreaPortals, IGameLifecycle, ISaveCustom
{
    /// <summary>AREAPORTAL_VIEWER_PADDING: how far behind a portal's plane the viewer may stand and still see through it.</summary>
    internal const float ViewerPadding = 80f;

    /// <summary>AREAPORTAL_FADE_DIST_BUFFER: the slack past a window's fade distance before it shuts.</summary>
    internal const float FadeDistanceBuffer = 10f;

    /// <summary>The engine call's batch: ClientSetupVisibility's two 512-entry arrays.</summary>
    internal const int Batch = 512;

    private sealed class Record
    {
        public uint Token;
        public int Number;
        public int Version;
        public bool Open = true;
        public bool Window;
        public float FadeDistance;
        public Vector Mins;
        public Vector Maxs;
    }

    private readonly IAreaPortalEngine _engine;
    private readonly List<Record> _records = new();
    private readonly Dictionary<uint, Record> _byToken = new();
    private uint _lastToken;

    /// <param name="engine">IVEngineServer's portal calls: the side's registered ones, acquired at their first call.</param>
    public AreaPortals(IAreaPortalEngine engine) => _engine = engine;

    /// <summary>How many records there are; the debug readout's and the facts' count.</summary>
    public int Count => _records.Count;

    /// <inheritdoc/>
    public uint Create(int portalNumber, int portalVersion)
    {
        if (portalNumber < 0)
            return 0;

        _lastToken = TokenAfter(_lastToken);
        var record = new Record { Token = _lastToken, Number = portalNumber, Version = portalVersion };
        _records.Add(record);
        _byToken[record.Token] = record;

        _engine.SetAreaPortalState(portalNumber, true);
        return record.Token;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// OPEN, then gone: a portal removed while shut would stay shut in the
    /// engine with nothing left to reopen it, and everything beyond it would
    /// stop being sent for the rest of the map.
    /// </remarks>
    public void Destroy(uint portal)
    {
        if (!_byToken.Remove(portal, out Record? record))
            return;

        _engine.SetAreaPortalState(record.Number, true);
        _records.Remove(record);
    }

    /// <inheritdoc/>
    public void SetOpen(uint portal, bool open)
    {
        if (!_byToken.TryGetValue(portal, out Record? record))
            return;

        record.Open = open;
        _engine.SetAreaPortalState(record.Number, open);
    }

    /// <inheritdoc/>
    public void SetWindow(uint portal, Vector worldMins, Vector worldMaxs, float fadeDistance)
    {
        if (!_byToken.TryGetValue(portal, out Record? record))
            return;

        record.FadeDistance = fadeDistance;
        record.Mins = worldMins;
        record.Maxs = worldMaxs;
        record.Window = fadeDistance > 0f;
    }

    /// <summary>
    /// One client's pass: every record's server answer to the engine, and the
    /// client's area portal bits into <paramref name="portalBits"/> (which the
    /// caller zeroed). -1, or the portal number too large for the bits, which
    /// is an Error the caller raises.
    /// </summary>
    public int Visibility(Vector origin, float fovDistanceAdjustFactor, Span<byte> portalBits)
    {
        IAreaPortalEngine engine = _engine;
        Span<int> numbers = stackalloc int[Batch];
        Span<int> open = stackalloc int[Batch];
        int n = 0;

        foreach (Record record in _records)
        {
            bool openOnClient = true;

            numbers[n] = record.Number;
            open[n] = UpdateVisibility(engine, record, origin, fovDistanceAdjustFactor, ref openOnClient) ? 1 : 0;

            if (++n >= Batch)
            {
                engine.SetAreaPortalStates(numbers, open);
                n = 0;
            }

            // Version 0 portals (shipping Half-Life 2) are always open in the
            // client's bits.
            if (record.Version == 0)
                openOnClient = true;

            if (openOnClient)
            {
                if (record.Number >= portalBits.Length * 8)
                    return record.Number;

                portalBits[record.Number >> 3] |= (byte)(1 << (record.Number & 7));
            }
        }

        engine.SetAreaPortalStates(numbers[..n], open[..n]);
        return -1;
    }

    /// <summary>CAreaPortal::UpdateVisibility: whether the server sends what is beyond it.</summary>
    private static bool UpdateVisibility(IAreaPortalEngine engine, Record record, Vector origin,
                                         float fovDistanceAdjustFactor, ref bool openOnClient)
    {
        if (ClosedByState(record, origin, fovDistanceAdjustFactor, ref openOnClient))
            return false;

        // Left open when the engine has no plane for it.
        if (!engine.GetAreaPortalPlane(origin, record.Number, out Vector normal, out float distance))
            return true;

        // VPlane::DistTo: the dot product, then the distance.
        float distTo = origin.X * normal.X + origin.Y * normal.Y + origin.Z * normal.Z - distance;
        return distTo + ViewerPadding > 0f;
    }

    /// <summary>The record's own say: shut, or a window the viewer is too far from.</summary>
    private static bool ClosedByState(Record record, Vector origin, float fovDistanceAdjustFactor, ref bool openOnClient)
    {
        if (!record.Open)
        {
            openOnClient = false;
            return true;
        }

        if (record.Window)
        {
            float distance = DistanceToBox(record.Mins, record.Maxs, origin) * fovDistanceAdjustFactor;

            if (distance > record.FadeDistance + FadeDistanceBuffer)
            {
                openOnClient = false;
                return true;
            }
        }

        return false;
    }

    /// <summary>The next token after <paramref name="token"/>: 0 is "no portal", so the last wraps to 1.</summary>
    internal static uint TokenAfter(uint token) => token == uint.MaxValue ? 1u : token + 1u;

    /// <summary>mathlib's CalcDistanceToAABB: FastSqrt of CalcSqrDistanceToAABB, axis by axis.</summary>
    internal static float DistanceToBox(Vector mins, Vector maxs, Vector point)
    {
        float distSqr = 0f;
        distSqr += AxisSqr(point.X, mins.X, maxs.X);
        distSqr += AxisSqr(point.Y, mins.Y, maxs.Y);
        distSqr += AxisSqr(point.Z, mins.Z, maxs.Z);
        return MathF.Sqrt(distSqr);

        static float AxisSqr(float p, float lo, float hi)
        {
            float delta;

            if (p < lo)
                delta = lo - p;
            else if (p > hi)
                delta = p - hi;
            else
                return 0f;

            return delta * delta;
        }
    }

    /// <summary>
    /// The records, one line each, for cs_sv_areaportals: by position in the walk
    /// (creation order), in the native readout's words.
    /// </summary>
    public IEnumerable<string> Describe()
    {
        int element = 0;

        foreach (Record r in _records)
        {
            int open = r.Open ? 1 : 0;
            int window = r.Window ? 1 : 0;
            yield return FormattableString.Invariant(
                $"element {element++}: number={r.Number} version={r.Version} open={open} window={window} fade={r.FadeDistance:0} token={r.Token}");
        }
    }

    // ---- lifetime ------------------------------------------------------------

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.AreaPortals;

    /// <summary>The level's records go with it; a new map's portals create their own.</summary>
    void IGameLifecycle.OnLevelShutdown() => Clear();

    private void Clear()
    {
        _records.Clear();
        _byToken.Clear();
    }

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        writer.Write("last", unchecked((int)_lastToken));

        foreach (Record r in _records)
        {
            writer.AddRow()
                  .Set("token", unchecked((int)r.Token))
                  .Set("number", r.Number)
                  .Set("version", r.Version)
                  .Set("open", r.Open)
                  .Set("window", r.Window)
                  .Set("fade", r.FadeDistance)
                  .Set("mins", r.Mins)
                  .Set("maxs", r.Maxs);
        }
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        Clear();
        _lastToken = unchecked((uint)reader.ReadInt("last"));

        foreach (ReloadRow row in reader.Rows)
        {
            var record = new Record
            {
                Token = unchecked((uint)row.GetInt("token")),
                Number = row.GetInt("number"),
                Version = row.GetInt("version"),
                Open = row.GetBool("open"),
                Window = row.GetBool("window"),
                FadeDistance = row.GetFloat("fade"),
                Mins = row.GetVector("mins"),
                Maxs = row.GetVector("maxs"),
            };

            if (record.Token == 0 || !_byToken.TryAdd(record.Token, record))
                continue;

            _records.Add(record);
        }
    }
}

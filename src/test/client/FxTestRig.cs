using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// The client-side effects' native half, as a test answers it: materials by
/// name with their reference counts, every quad mesh drawn, a scripted random
/// stream (each draw checked against the range stock drew from), the view, the
/// listener, the clock, sounds and particle dispatches recorded.
/// </summary>
internal sealed class FxTestRig : IFxRender, IFxTracerSound, IEngineHost
{
    public readonly Dictionary<string, int> Ids = new(StringComparer.Ordinal);
    public readonly Dictionary<int, string> Names = [];
    public readonly Dictionary<int, int> Refs = [];
    public readonly List<(int Material, FxVertex[] Vertices, bool ByteColor)> Draws = [];
    public readonly Queue<(float Lo, float Hi, float Value)> Randoms = new();
    public readonly List<string> Wrong = [];
    public readonly List<(string Name, Vector Start, Vector Dir)> Sounds = [];
    public FxView ViewAnswer;
    public Vector Listener;
    public float Now;

    public int Id(string name)
    {
        if (!Ids.TryGetValue(name, out int id))
        {
            id = Ids.Count + 1;
            Ids[name] = id;
            Names[id] = name;
            Refs[id] = 0;
        }
        return id;
    }

    public int MaterialFind(string name)
    {
        int id = Id(name);
        Refs[id]++;
        return id;
    }

    public void MaterialAddRef(int material) => Refs[material]++;

    public void MaterialRelease(int material) => Refs[material]--;

    public string MaterialName(int material) => Names.TryGetValue(material, out string? n) ? n : "";

    public void DrawQuads(int material, ReadOnlySpan<FxVertex> vertices, bool byteColor)
        => Draws.Add((material, vertices.ToArray(), byteColor));

    public FxView View() => ViewAnswer;

    public Vector MainViewOrigin() => Listener;

    public bool EmitTracerSound(string soundName, Vector start, Vector direction)
    {
        Sounds.Add((soundName, start, direction));
        return true;
    }

    public int LocalPlayerAnswer = 1;
    public QAngle ViewAnglesAnswer;
    public int AttachmentAnswer = -1;
    public Vector AttachmentOrigin;
    public readonly List<(int System, Vector Start, Vector End, QAngle Angles, int Ent)> Particles = [];

    public int LocalPlayer() => LocalPlayerAnswer;

    public QAngle ViewAngles() => ViewAnglesAnswer;

    public int EntityAttachment(int entIndex, int attachment, out Vector origin, out string model)
    {
        origin = AttachmentOrigin;
        model = "models/test.mdl";
        return AttachmentAnswer;
    }

    public void DispatchParticleEffect(int systemIndex, Vector start, Vector end, QAngle angles, int entIndex)
        => Particles.Add((systemIndex, start, end, angles, entIndex));

    public bool RenderAnglesAnswer;
    public QAngle RenderAngles;
    public readonly List<(Vector Origin, QAngle Angles, QAngle GunAngles, int Type)> Brass = [];

    public bool EntityRenderAngles(int entIndex, out QAngle angles)
    {
        angles = RenderAngles;
        return RenderAnglesAnswer;
    }

    public void EjectBrass(Vector origin, QAngle angles, QAngle gunAngles, int type) => Brass.Add((origin, angles, gunAngles, type));

    /// <summary>
    /// Recorded draws of either kind in stock's order (a probe's `fxr f|i lo hi
    /// value` rows): a float draw takes from here first when anything is here.
    /// </summary>
    public readonly Queue<(bool IsInt, float Lo, float Hi, float Value)> RecordedDraws = new();

    /// <summary>Queue one `fxr` row.</summary>
    public void Enqueue(string[] fxr)
    {
        static float P(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        RecordedDraws.Enqueue((fxr[1] == "i", P(fxr[2]), P(fxr[3]), P(fxr[4])));
    }

    /// <summary>Recorded draws not yet taken.</summary>
    public int Pending => RecordedDraws.Count + Randoms.Count;

    public void ClearDraws()
    {
        RecordedDraws.Clear();
        Randoms.Clear();
    }

    public float RandomFloat(float min, float max)
    {
        if (RecordedDraws.Count > 0)
        {
            var (isInt, dlo, dhi, dvalue) = RecordedDraws.Dequeue();
            if (isInt || BitConverter.SingleToInt32Bits(dlo) != BitConverter.SingleToInt32Bits(min)
                || BitConverter.SingleToInt32Bits(dhi) != BitConverter.SingleToInt32Bits(max))
                Wrong.Add($"managed drew RandomFloat({min}, {max}) where stock drew {(isInt ? "RandomInt" : "RandomFloat")}({dlo}, {dhi})");
            return dvalue;
        }
        if (Randoms.Count == 0)
        {
            Wrong.Add($"managed drew RandomFloat({min}, {max}) that stock did not");
            return 0f;
        }
        var (lo, hi, value) = Randoms.Dequeue();
        if (BitConverter.SingleToInt32Bits(lo) != BitConverter.SingleToInt32Bits(min)
            || BitConverter.SingleToInt32Bits(hi) != BitConverter.SingleToInt32Bits(max))
            Wrong.Add($"managed drew RandomFloat({min}, {max}) where stock drew ({lo}, {hi})");
        return value;
    }

    public float CurrentTime => Now;

    // Not read by the effect list.
    public Side ActiveSide => throw new NotSupportedException();
    public void EndRound() => throw new NotSupportedException();
    public bool IsInitialized => throw new NotSupportedException();
    public string ManagedDirectory => throw new NotSupportedException();
    public int RandomInt(int min, int max)
    {
        if (RecordedDraws.Count == 0)
        {
            Wrong.Add($"managed drew RandomInt({min}, {max}) that stock did not");
            return min;
        }
        var (isInt, lo, hi, value) = RecordedDraws.Dequeue();
        if (!isInt || (int)lo != min || (int)hi != max)
            Wrong.Add($"managed drew RandomInt({min}, {max}) where stock drew {(isInt ? "RandomInt" : "RandomFloat")}({lo}, {hi})");
        return (int)value;
    }
    public bool IsDedicatedServer => throw new NotSupportedException();
    public bool SteamGameServerReacquire() => throw new NotSupportedException();
    public void SteamGameServerClear() => throw new NotSupportedException();
    public void SteamGameServerRequestGameplayStats() => throw new NotSupportedException();
    public bool SteamGameServerLoggedOn() => throw new NotSupportedException();
}

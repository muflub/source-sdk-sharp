//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Buffers.Binary;
using System.Text;

namespace SourceSharp.Client.LipSync;

/// <summary>One weight of a flex setting: a key local to its file, and how much.</summary>
public readonly record struct FlexWeight(int Key, float Weight);

/// <summary>
/// A phoneme class file (a .vfe image, studio.h's flexsettinghdr_t) read in
/// place: settings looked up by phoneme code through the file's index, each
/// setting's weights, and the names of the file's local keys. Offsets are the
/// layout studio.h declares (header 108 bytes, flexsetting_t 24, flexweight_t
/// 12); a reference that leaves the image reads as absent.
/// </summary>
public sealed class FlexSettingFile
{
    private const int HeaderSize = 108;
    private const int SettingSize = 24;
    private const int WeightSize = 12;

    private readonly byte[] _image;

    public FlexSettingFile(byte[] image)
    {
        _image = image;
    }

    private int Int(int at) => at >= 0 && at + 4 <= _image.Length ? BinaryPrimitives.ReadInt32LittleEndian(_image.AsSpan(at)) : 0;

    private float Float(int at) => BitConverter.Int32BitsToSingle(Int(at));

    private int NumSettings => Int(76);
    private int SettingsAt => Int(80);
    private int NumIndexes => Int(88);
    private int IndexAt => Int(92);

    /// <summary>The number of local keys (flex controller names) the file maps.</summary>
    public int NumKeys => _image.Length >= HeaderSize ? Int(96) : 0;

    /// <summary>The setting the index gives this phoneme code, or -1: a code outside the index, or an entry of -1.</summary>
    public int IndexedSetting(int phoneme)
    {
        if (_image.Length < HeaderSize || phoneme < 0 || phoneme >= NumIndexes)
            return -1;
        int setting = Int(IndexAt + phoneme * 4);
        return setting >= 0 && setting < NumSettings ? setting : -1;
    }

    /// <summary>A setting's weights (flexsetting_t::psetting from its first: the array starts settingindex past the setting itself).</summary>
    public IReadOnlyList<FlexWeight> Weights(int setting)
    {
        int at = SettingsAt + setting * SettingSize;
        int count = Int(at + 8);
        int first = at + Int(at + 20);
        var weights = new FlexWeight[Math.Max(0, count)];
        for (int i = 0; i < weights.Length; i++)
            weights[i] = new FlexWeight(Int(first + i * WeightSize), Float(first + i * WeightSize + 4));
        return weights;
    }

    /// <summary>The name of local key <paramref name="key"/> (flexsettinghdr_t::pLocalName).</summary>
    public string LocalName(int key)
    {
        int at = Int(Int(100) + key * 4);
        if (at <= 0 || at >= _image.Length)
            return "";
        int end = Array.IndexOf(_image, (byte)0, at);
        return Encoding.ASCII.GetString(_image, at, (end < 0 ? _image.Length : end) - at);
    }
}

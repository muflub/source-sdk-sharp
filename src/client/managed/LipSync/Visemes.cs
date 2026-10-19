//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.LipSync;

/// <summary>Finds a phoneme class file by name (C_BaseFlex::FindSceneFile); null when it is not there (or not loaded yet).</summary>
public interface IPhonemeFileSource
{
    FlexSettingFile? Find(string name);
}

/// <summary>
/// One of a speaking entity's voice sources this frame (mouthinfo.h's
/// CVoiceData, with what the engine answers for its sound): whether it
/// ignores phonemes, its sentence (null when the engine has none), the
/// sentence's length and the time since the sound started.
/// </summary>
public readonly record struct VoiceSource(bool IgnorePhonemes, Sentence? Sentence, float SentenceLength, float Elapsed);

/// <summary>What the pass reads of the model: whether there is one, its studiohdr flags, and the entity's accumulated bone mask.</summary>
public readonly record struct VisemeModel(bool Present, int StudioFlags, int AccumulatedBoneMask);

/// <summary>
/// The pass's three ConVars, c_baseflex.cpp's phonemedelay (sound latency
/// subtracted from the elapsed time), phonemefilter (the box filter's length)
/// and phonemesnap (the vertex LOD below which two phonemes always blend).
/// </summary>
public readonly record struct VisemeSettings(float PhonemeDelay, float PhonemeFilter, int PhonemeSnap)
{
    /// <summary>Stock's defaults: "0", "0.08", "2".</summary>
    public static VisemeSettings Stock => new(0.0f, 0.08f, 2);
}

/// <summary>
/// C_BaseFlex's viseme pass, per speaking entity: each voice's runtime
/// phonemes, box-filtered around the current time, add the phoneme class
/// files' flex settings into the global flex weights, the normal class
/// crossfaded with the weak or strong class by the sentence's emphasis.
/// Stock's ProcessVisemes, AddVisemesForSentence, AddViseme,
/// SetupEmphasisBlend, ComputeBlendedSetting and SetupMappings, held to the
/// pristine code by <c>LipSyncOracleTests</c>.
/// </summary>
public sealed class Visemes
{
    public const int ClassWeak = 0;
    public const int ClassNormal = 1;
    public const int ClassStrong = 2;
    public const int ClassCount = 3;

    /// <summary>studio.h's STUDIOHDR_FLAGS_FORCE_PHONEME_CROSSFADE.</summary>
    public const int ForcePhonemeCrossfade = 0x1000;

    private const int BoneUsedByVertexLod0 = 0x400;
    private const int BoneUsedByVertexLod1 = 0x800;
    private const int BoneUsedByVertexLod2 = 0x1000;
    private const int BoneUsedByVertexLod3 = 0x2000;

    private const float StrongCrossfadeStart = 0.60f;
    private const float WeakCrossfadeStart = 0.40f;

    private sealed class PhonemeClass
    {
        public string Name = "";
        public bool Required;
        public bool Looked;            // basechecked: the file is looked up once
        public FlexSettingFile? File;
        public int Setting = -1;       // exp: this phoneme's setting in File
        public bool Valid;
        public float Amount;
    }

    private readonly PhonemeClass[] _classes = [new(), new(), new()];
    private readonly IPhonemeFileSource _files;
    private readonly FlexControllerRegistry _registry;
    private readonly Dictionary<FlexSettingFile, int[]> _translations = new(ReferenceEqualityComparer.Instance);

    public Visemes(IPhonemeFileSource files, FlexControllerRegistry registry, string fileRoot = "phonemes")
    {
        _files = files;
        _registry = registry;
        SetupMappings(fileRoot);
    }

    /// <summary>The classes' files: <c>root</c> (required), <c>root_weak</c>, <c>root_strong</c>, none looked up yet.</summary>
    public void SetupMappings(string fileRoot)
    {
        for (int i = 0; i < ClassCount; i++)
            _classes[i] = new PhonemeClass();
        _classes[ClassNormal].Name = fileRoot;
        _classes[ClassNormal].Required = true;
        _classes[ClassWeak].Name = fileRoot + "_weak";
        _classes[ClassStrong].Name = fileRoot + "_strong";
    }

    /// <summary>
    /// ProcessVisemes: for each voice that has phonemes to follow, a sentence,
    /// and has not run two seconds past its sentence's end, the visemes at the
    /// voice's time less the delay, filtered over phonemefilter seconds.
    /// </summary>
    public void Process(ReadOnlySpan<VoiceSource> voices, in VisemeModel model, in VisemeSettings settings, Span<float> flexWeights)
    {
        foreach (VoiceSource voice in voices)
        {
            if (voice.IgnorePhonemes || voice.Sentence is null)
                continue;
            if (voice.Elapsed >= voice.SentenceLength + 2.0f)
                continue;

            float t = voice.Elapsed - settings.PhonemeDelay;
            float dt = settings.PhonemeFilter;
            float emphasis = voice.Sentence.Intensity(t, voice.SentenceLength);
            AddVisemesForSentence(emphasis, voice.Sentence, t, dt, model, settings.PhonemeSnap, flexWeights);
        }
    }

    // A phoneme under the time widens the filter, when it crossfades, to reach
    // the next phoneme (its end when they touch, its start across a gap) but no
    // further than its own length; the widened filter stays for the phonemes
    // after it. Each phoneme the filter overlaps adds its visemes scaled by the
    // overlap. (Stock's arm for a phoneme with no next one is unreachable: the
    // last phoneme takes neither branch.)
    private void AddVisemesForSentence(float emphasis, Sentence sentence, float t, float dt, in VisemeModel model, int snap, Span<float> flexWeights)
    {
        if (!model.Present)
            return;

        IReadOnlyList<RuntimePhoneme> phonemes = sentence.Phonemes;
        int count = phonemes.Count;
        for (int k = 0; k < count; k++)
        {
            RuntimePhoneme phoneme = phonemes[k];
            if (t > phoneme.Start && t < phoneme.End && Crossfades(model, snap) && k < count - 1)
            {
                RuntimePhoneme next = phonemes[k + 1];
                float reach = next.Start == phoneme.End ? next.End - t : next.Start - t;
                float length = phoneme.End - phoneme.Start;
                float widened = reach < length ? reach : length;
                dt = dt > widened ? dt : widened;
            }

            float t1 = (phoneme.Start - t) / dt;
            float t2 = (phoneme.End - t) / dt;
            if (t1 < 1.0 && t2 > 0)
            {
                if (t2 > 1)
                    t2 = 1;
                if (t1 < 0)
                    t1 = 0;
                AddViseme(emphasis, phoneme.Code, t2 - t1, flexWeights);
            }
        }
    }

    // Forced by the model, or by the finest vertex LOD the entity's bones are
    // used at: LOD n crossfades while phonemesnap > n; no vertex LOD, never.
    private static bool Crossfades(in VisemeModel model, int snap)
    {
        if ((model.StudioFlags & ForcePhonemeCrossfade) != 0)
            return true;
        int mask = model.AccumulatedBoneMask;
        if ((mask & BoneUsedByVertexLod0) != 0)
            return snap > 0;
        if ((mask & BoneUsedByVertexLod1) != 0)
            return snap > 1;
        if ((mask & BoneUsedByVertexLod2) != 0)
            return snap > 2;
        if ((mask & BoneUsedByVertexLod3) != 0)
            return snap > 3;
        return false;
    }

    private void AddViseme(float emphasis, int phoneme, float scale, Span<float> flexWeights)
    {
        if (SetupEmphasisBlend(phoneme))
            return;
        ComputeBlendedSetting(emphasis);

        for (int type = 0; type < ClassCount; type++)
        {
            PhonemeClass info = _classes[type];
            if (!info.Valid || info.Amount == 0.0f)
                continue;
            FlexSettingFile file = info.File!;
            int setting = file.IndexedSetting(phoneme);
            if (setting < 0)
                continue;
            int[] map = _translations[file];
            foreach (FlexWeight weight in file.Weights(setting))
            {
                if (weight.Key < 0 || weight.Key >= map.Length)
                    continue;   // a malformed file: stock would read past its map
                int global = map[weight.Key];
                if (global >= 0 && global < flexWeights.Length)
                    flexWeights[global] += info.Amount * scale * weight.Weight;
            }
        }
    }

    // Resets each class, finds its file the first time, and marks it valid when
    // the file has this phoneme. A required class without it skips the phoneme
    // (the classes after it are left as they were).
    private bool SetupEmphasisBlend(int phoneme)
    {
        for (int i = 0; i < ClassCount; i++)
        {
            PhonemeClass info = _classes[i];
            info.Valid = false;
            info.Amount = 0.0f;
            if (!info.Looked)
            {
                info.Looked = true;
                info.File = Find(info.Name);
            }
            info.Setting = info.File is null ? -1 : info.File.IndexedSetting(phoneme);
            if (info.Required && info.Setting < 0)
                return true;
            if (info.Setting >= 0)
                info.Valid = true;
        }
        return false;
    }

    // The scene file lookup maps a file's local keys to global controllers the
    // first time the file is found (C_BaseFlex::EnsureTranslations: every key,
    // in order).
    private FlexSettingFile? Find(string name)
    {
        FlexSettingFile? file = _files.Find(name);
        if (file is not null && !_translations.ContainsKey(file))
        {
            var map = new int[Math.Max(0, file.NumKeys)];
            for (int i = 0; i < map.Length; i++)
                map[i] = _registry.Add(file.LocalName(i));
            _translations[file] = map;
        }
        return file;
    }

    // 0.5 is the normal class at 100%. Above STRONG the strong class fades in
    // (or, without one, the normal class stops growing at 2 x STRONG); below
    // WEAK the weak class fades in (or the normal class stops shrinking at
    // 2 x WEAK).
    private void ComputeBlendedSetting(float emphasis)
    {
        bool hasWeak = _classes[ClassWeak].Valid;
        bool hasStrong = _classes[ClassStrong].Valid;
        PhonemeClass normal = _classes[ClassNormal];

        if (emphasis > StrongCrossfadeStart)
        {
            if (hasStrong)
            {
                float frac = (1.0f - emphasis) / (1.0f - StrongCrossfadeStart);
                normal.Amount = frac * 2.0f * StrongCrossfadeStart;
                _classes[ClassStrong].Amount = 1.0f - frac;
            }
            else
            {
                emphasis = emphasis < StrongCrossfadeStart ? emphasis : StrongCrossfadeStart;
                normal.Amount = 2.0f * emphasis;
            }
        }
        else if (emphasis < WeakCrossfadeStart)
        {
            if (hasWeak)
            {
                float frac = (WeakCrossfadeStart - emphasis) / WeakCrossfadeStart;
                normal.Amount = (1.0f - frac) * 2.0f * WeakCrossfadeStart;
                _classes[ClassWeak].Amount = frac;
            }
            else
            {
                emphasis = emphasis > WeakCrossfadeStart ? emphasis : WeakCrossfadeStart;
                normal.Amount = 2.0f * emphasis;
            }
        }
        else
        {
            normal.Amount = 2.0f * emphasis;
        }
    }
}

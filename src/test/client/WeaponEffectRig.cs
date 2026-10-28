using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

namespace SourceSharp.Tests;

/// <summary>
/// What a weapon's draw effect reads and draws through, as a test answers it:
/// attachments by entity and index (and names by entity), every quad drawn
/// with its material's name, the view, a scripted random stream, the beams
/// made, and a carried weapon with the local viewmodel beside it.
/// </summary>
internal sealed class WeaponEffectRig
{
    public const int Local = 1;

    public readonly AttachmentModels Models = new();
    public readonly QuadRecorder Quads = new();
    public readonly ViewSlots View = new();
    public readonly ScriptedRandom Random = new();
    public readonly RecordingEngine Engine;
    public readonly RecordingBeams Beams;
    public readonly ClientSsWeapon Weapon;
    public readonly ClientSsViewModel ViewModel;
    public float Now { get; set; }

    public WeaponEffectRig(string weaponName)
    {
        Engine = TestServices.Mock();
        Engine.Strict = false;
        Engine.Returns("Hud.LocalPlayerIndex", Local);
        Beams = new RecordingBeams(Engine);

        Weapon = new ClientSsWeapon(Engine) { Handle = new EntityHandle(0x0001_0041u) };
        Weapon.State.BindLocal();
        Weapon.State.OwnerIndex = Local;
        Weapon.State.WeaponTypeId = WeaponCatalog.Stock().IndexOf(weaponName);

        ViewModel = new ClientSsViewModel(Engine, Engine) { Handle = new EntityHandle(0x0001_0042u) };
        ViewModel.State.BindLocal();
        ViewModel.State.OwnerIndex = Local;
        ViewModel.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
    }

    /// <summary>The first person: the weapon drawn through the local viewmodel.</summary>
    public WeaponDrawContext FirstPerson() => new(Weapon, ViewModel, FirstPerson: true, Now);

    /// <summary>The third person: the carried world model drawn.</summary>
    public WeaponDrawContext ThirdPerson() => new(Weapon, null, FirstPerson: false, Now);

    /// <summary>The quads drawn with a material, in order.</summary>
    public IReadOnlyList<FxVertex[]> Drawn(string material)
        => Quads.Draws.Where(d => Quads.Names[d.Material] == material).Select(d => d.Vertices).ToList();

    /// <summary>The beams made through CreateBeamEntPoint(BeamInfo), in order.</summary>
    public IReadOnlyList<BeamInfo> BeamsMade(string member = "Beams.CreateBeamEntPoint(BeamInfo)")
        => Engine.Calls.Where(c => c.Member == member).Select(c => (BeamInfo)c.Args[0]!).ToList();
}

/// <summary>A quad's centre: the mean of its four corners.</summary>
internal static class QuadMath
{
    public static Vector Centre(FxVertex[] quad)
    {
        Vector c = (quad[0].Position + quad[1].Position + quad[2].Position + quad[3].Position) * 0.25f;
        return new Vector(MathF.Round(c.X, 3), MathF.Round(c.Y, 3), MathF.Round(c.Z, 3));
    }

    /// <summary>The quad's width, corner 0 to corner 3.</summary>
    public static float Width(FxVertex[] quad) => (quad[3].Position - quad[0].Position).Length;
}

internal sealed class QuadRecorder : IFxRender
{
    public readonly Dictionary<string, int> Ids = new(StringComparer.Ordinal);
    public readonly Dictionary<int, string> Names = [];
    public readonly List<(int Material, FxVertex[] Vertices, bool ByteColor)> Draws = [];

    public int MaterialFind(string name)
    {
        if (!Ids.TryGetValue(name, out int id))
        {
            id = Ids.Count + 1;
            Ids[name] = id;
            Names[id] = name;
        }
        return id;
    }

    public void MaterialAddRef(int material) { }
    public void MaterialRelease(int material) { }
    public string MaterialName(int material) => Names.GetValueOrDefault(material, "");

    public void DrawQuads(int material, ReadOnlySpan<FxVertex> vertices, bool byteColor)
        => Draws.Add((material, vertices.ToArray(), byteColor));

    public FxView View() => throw new InvalidOperationException("the effect read IFxRender.View");
    public Vector MainViewOrigin() => throw new InvalidOperationException("the effect read MainViewOrigin");
    public int LocalPlayer() => WeaponEffectRig.Local;
    public QAngle ViewAngles() => throw new InvalidOperationException("the effect read ViewAngles");
    public int EntityAttachment(int entIndex, int attachment, out Vector origin, out string model)
        => throw new InvalidOperationException("the effect read EntityAttachment");
    public void DispatchParticleEffect(int systemIndex, Vector start, Vector end, QAngle angles, int entIndex)
        => throw new InvalidOperationException("the effect dispatched a particle");
    public bool EntityRenderAngles(int entIndex, out QAngle angles)
        => throw new InvalidOperationException("the effect read EntityRenderAngles");
    public void EjectBrass(Vector origin, QAngle angles, QAngle gunAngles, int type)
        => throw new InvalidOperationException("the effect ejected brass");
}

/// <summary>The current view: an eye 100 units back along -X looking down +X.</summary>
internal sealed class ViewSlots : IPixelVisSlots
{
    public CurrentViewState Answer = new(0, new Vector(-100, 0, 0), new Vector(1, 0, 0), new Vector(0, -1, 0),
                                         new Vector(0, 0, 1));

    public CurrentViewState View() => Answer;

    public int QueryCreate() => throw new InvalidOperationException();
    public void QueryDestroy(int query) => throw new InvalidOperationException();
    public void QueryReset(int query) => throw new InvalidOperationException();
    public void QueryPixels(int visible, int count, out int visiblePixels, out int countPixels)
        => throw new InvalidOperationException();
    public void Diameters(ReadOnlySpan<float> spheres, Span<float> diameters) => throw new InvalidOperationException();
}

/// <summary>
/// The engine's uniform stream, scripted: each draw answers the next queued
/// value, or the middle of the range asked; every draw is recorded with its range.
/// </summary>
internal sealed class ScriptedRandom : IUniformRandom
{
    public readonly Queue<float> Values = new();
    public readonly List<(bool IsInt, float Lo, float Hi)> Asked = [];

    public float RandomFloat(float min, float max)
    {
        Asked.Add((false, min, max));
        return Values.Count > 0 ? Values.Dequeue() : (min + max) * 0.5f;
    }

    public int RandomInt(int min, int max)
    {
        Asked.Add((true, min, max));
        return Values.Count > 0 ? (int)Values.Dequeue() : (min + max) / 2;
    }
}

/// <summary>
/// Models as their attachment tables: a point and angles per (entity,
/// attachment), names per (entity, name), pose parameters and activities by
/// name. Everything else an effect has no business reading throws.
/// </summary>
internal sealed class AttachmentModels : IEngineEntityRender
{
    public readonly Dictionary<(uint Entity, int Attachment), (Vector Origin, Vector Angles)> Points = [];
    public readonly Dictionary<(uint Entity, string Name), int> Names = [];
    public readonly Dictionary<(uint Entity, string Activity), int> Activities = [];
    public readonly List<(uint Entity, int Attachment)> Asked = [];

    public AttachmentModels At(EntityHandle entity, int attachment, Vector origin, Vector angles = default)
    {
        Points[(entity.Value, attachment)] = (origin, angles);
        return this;
    }

    public AttachmentModels Named(EntityHandle entity, string name, int attachment)
    {
        Names[(entity.Value, name)] = attachment;
        return this;
    }

    public bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out Vector angles)
    {
        Asked.Add((entity.Value, attachment));
        if (Points.TryGetValue((entity.Value, attachment), out var p))
        {
            (origin, angles) = p;
            return true;
        }

        origin = default;
        angles = default;
        return false;
    }

    public int LookupAttachment(EntityHandle entity, string name) => Names.GetValueOrDefault((entity.Value, name), 0);

    public int LookupSequenceByActivity(EntityHandle entity, string activityName)
        => Activities.GetValueOrDefault((entity.Value, activityName), -1);

    public string? GetModelKeyValueText(string modelName) => null;

    private static Exception Refuse(string member) => new InvalidOperationException($"the effect read {member}");

    public string GetModelName(EntityHandle entity) => throw Refuse(nameof(GetModelName));
    public int GetTextureFrame(EntityHandle entity) => throw Refuse(nameof(GetTextureFrame));
    public int LookupSequence(EntityHandle entity, string name) => throw Refuse(nameof(LookupSequence));
    public float SequenceDuration(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceDuration));
    public float SequenceGroundSpeed(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceGroundSpeed));
    public int AttachmentCount(EntityHandle entity) => throw Refuse(nameof(AttachmentCount));
    public string AttachmentName(EntityHandle entity, int attachment) => throw Refuse(nameof(AttachmentName));
    public int BoneCount(EntityHandle entity, out int cached) => throw Refuse(nameof(BoneCount));
    public StandaloneBones? BoneMatrices(EntityHandle entity) => throw Refuse(nameof(BoneMatrices));
    public bool IsPoseable(EntityHandle entity) => throw Refuse(nameof(IsPoseable));
    public string StudioName(EntityHandle entity) => throw Refuse(nameof(StudioName));
    public int PoseParameterCount(EntityHandle entity) => throw Refuse(nameof(PoseParameterCount));
    public int LookupPoseParameter(EntityHandle entity, string name) => throw Refuse(nameof(LookupPoseParameter));
    public string PoseParameterName(EntityHandle entity, int parameter) => throw Refuse(nameof(PoseParameterName));
    public bool PoseParameterRange(EntityHandle entity, int parameter, out float start, out float end) => throw Refuse(nameof(PoseParameterRange));
    public float SetPoseParameter(EntityHandle entity, int parameter, float value) => throw Refuse(nameof(SetPoseParameter));
    public float GetPoseParameter(EntityHandle entity, int parameter) => throw Refuse(nameof(GetPoseParameter));
    public bool IsDormant(EntityHandle entity) => throw Refuse(nameof(IsDormant));
    public int SequenceEventCount(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceEventCount));
    public bool SequenceState(EntityHandle entity, out int sequence, out float cycle, out float duration, out bool looping, out int starts) => throw Refuse(nameof(SequenceState));
    public int ProceduralBones(EntityHandle entity, out int computed, out int jiggle, out int ikChains) => throw Refuse(nameof(ProceduralBones));
    public void SetAnimLayer(EntityHandle entity, int slot, int sequence, float weight, float playbackRate, int order, bool restart) => throw Refuse(nameof(SetAnimLayer));
    public int AnimLayer(EntityHandle entity, int slot, out int sequence, out float weight, out int order, out float cycle) => throw Refuse(nameof(AnimLayer));
    public bool SequenceEvent(EntityHandle entity, int sequence, int index, out float cycle, out int eventId, out int type, out string name, out string options) => throw Refuse(nameof(SequenceEvent));
    public void SetCustomDraw(EntityHandle entity, Vector mins, Vector maxs) => throw Refuse(nameof(SetCustomDraw));
    public void SetBlending(EntityHandle entity, bool translucent, bool ignoreZ, int alpha) => throw Refuse(nameof(SetBlending));
    public ShadowCast ShadowCast(EntityHandle entity, out ShadowCastChoice asked) => throw Refuse(nameof(ShadowCast));
    public void SetFade(EntityHandle entity, FadeStyle style, float startDistance, float endDistance, float minimumAlpha = 0f) => throw Refuse(nameof(SetFade));
    public void SetModel(EntityHandle entity, string model) => throw Refuse(nameof(SetModel));
    public void SetOverlayModel(EntityHandle entity, int modelIndex) => throw Refuse(nameof(SetOverlayModel));
    public void SetModelScale(EntityHandle entity, float scale) => throw Refuse(nameof(SetModelScale));
    public void SetBoneMergeSource(EntityHandle entity, int sourceEntityIndex) => throw Refuse(nameof(SetBoneMergeSource));
    public void SetSequence(EntityHandle entity, int sequence) => throw Refuse(nameof(SetSequence));
    public void SetViewModel(EntityHandle entity, bool enable) => throw Refuse(nameof(SetViewModel));
    public void PlaySequenceAt(EntityHandle entity, int sequence, float playbackRate, float cycle) => throw Refuse(nameof(PlaySequenceAt));
    public void PlaySequence(EntityHandle entity, int sequence, float playbackRate, bool restart) => throw Refuse(nameof(PlaySequence));
    public void SetCycle(EntityHandle entity, float cycle) => throw Refuse(nameof(SetCycle));
    public void SetSkin(EntityHandle entity, int skin) => throw Refuse(nameof(SetSkin));
    public void SetTextureFrame(EntityHandle entity, int frame) => throw Refuse(nameof(SetTextureFrame));
    public void SetTransmitAlways(EntityHandle entity, bool always) => throw Refuse(nameof(SetTransmitAlways));
    public bool DetectInSkybox(EntityHandle entity) => throw Refuse(nameof(DetectInSkybox));
    public void SetVisible(EntityHandle entity, bool visible) => throw Refuse(nameof(SetVisible));
}

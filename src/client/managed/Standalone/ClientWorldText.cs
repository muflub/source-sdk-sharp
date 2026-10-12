using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of <c>point_worldtext</c>: it owns the text's layout
/// (<see cref="WorldTextLayout"/>, lane c2-sims) and lays out each draw the
/// native text renderer asks for.
///
/// <para>
/// A managed entity is never on the wire as <c>CPointWorldText</c>, so the
/// engine never builds stock's class; shell_worldtext_render.cpp keeps a leaf
/// renderable for each text, made on the first update with the font page and
/// the bounds measured here, and asks <see cref="IManagedWorldTextSource.Draw"/>
/// for the vertices it builds.
/// </para>
///
/// <para>
/// The place is not passed: native follows this entity's origin and angles
/// every frame in a <c>PreRender</c> sweep (origin and angles are props outside
/// the blob, so text that only moved would deliver no update) and hands them to
/// the draw with the view's basis and the clock.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.PointWorldtext)]
public sealed class ClientWorldText : ClientStandaloneEntity, IManagedWorldTextSource, ISaveCustom
{
    public ClientWorldText(IEngineEntityRender render, IClientWorldTextMeshes meshes, IEngineConsole console)
        : base(render)
    {
        _meshes = meshes;
        _console = console;
    }

    private readonly IClientWorldTextMeshes _meshes;
    private readonly IEngineConsole _console;
    private WorldTextVertex[] _scratch = new WorldTextVertex[256];
    private bool _created;

    public override string ClassName => EntityNames.PointWorldtext;

    [NetState] public WorldTextNetState State = new();

    public WorldTextLayout Layout { get; } = new();

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Layout.Update(State.Text ?? string.Empty, State.TextSize, State.SpacingX, State.SpacingY,
                      State.Color, (int)State.Orientation, (int)State.Font, State.Rainbow);
        _created = _meshes.Update(Handle, WorldTextLayout.MaterialOf(Layout.Font), Layout.BoundsMin, Layout.BoundsMax);
    }

    public override void OnRemoved()
    {
        if (_created)
            _meshes.Destroy(Handle);
        _created = false;
    }

    /// <summary>
    /// The two rainbow speeds, read as the engine reads them (<c>atof</c>):
    /// their defaults carry an 'f' inside the string, stock's to the character.
    /// </summary>
    private float Speed(string name) => VariantParse.Atof(_console.FindConVarValue(name));

    int IManagedWorldTextSource.Draw(in WorldTextView view, Span<WorldTextVertexRaw> verticesOut)
    {
        float time = 0, chars = 0;
        if (Layout.Rainbow)
        {
            time = Speed("pointworldtext_rainbowspeed_time");
            chars = Speed("pointworldtext_rainbowspeed_char");
        }

        int n = Layout.Layout(view.Origin, view.Angles, view.Right, view.Up, view.CurTime, time, chars, _scratch);
        if (n > _scratch.Length)
        {
            _scratch = new WorldTextVertex[n];
            n = Layout.Layout(view.Origin, view.Angles, view.Right, view.Up, view.CurTime, time, chars, _scratch);
        }

        if (n > verticesOut.Length)
            return n;
        for (int i = 0; i < n; i++)
        {
            var v = _scratch[i];
            verticesOut[i] = new WorldTextVertexRaw
            {
                Position = v.Position, S = v.S, T = v.T,
                Color = v.R | ((uint)v.G << 8) | ((uint)v.B << 16) | ((uint)v.A << 24),
            };
        }

        return n;
    }

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        using var buffer = new MemoryStream();
        Layout.Save(new BinaryWriter(buffer));
        writer.Write("worldtext.layout", Convert.ToBase64String(buffer.ToArray()));
        writer.Write("worldtext.created", _created);
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        string carried = reader.ReadString("worldtext.layout");
        if (carried.Length == 0)
            return;
        Layout.Restore(new BinaryReader(new MemoryStream(Convert.FromBase64String(carried))));
        _created = reader.ReadBool("worldtext.created");
    }
}

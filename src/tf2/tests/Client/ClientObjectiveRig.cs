extern alias client;

using SourceSharp.Abi;

using Engine = client::SourceSharp.Abi.RecordingEngine;
using Entity = client::TF2.Client.Entities.ClientTFObjectiveResource;
using Settings = client::TF2.Client.Entities.ITFObjectiveSettings;
using Source = client::TF2.Client.Entities.TFClientObjectiveResources;
using State = client::TF2.Shared.Resources.ObjectiveResourceNetState;
using Sounds = client::SourceSharp.Client.Sounds.IClientEntitySounds;
using Local = TF2.Client.Ui.ITFLocalPlayer;

namespace TF2.Tests.Client;

/// <summary>The convars stock's client cap model reads, as a record a fact can choose.</summary>
internal sealed class SettingsFake : Settings
{
    public int CapStyle { get; init; } = 1;

    public float CapDeteriorateTime { get; init; } = 90f;

    public bool CapLinear { get; init; } = true;

    public int BlockStyle { get; init; } = 1;
}

/// <summary>
/// One client objective resource over a bound source state: the facts write the
/// state the server writes, snapshot it, and hand the packet to the entity the
/// way the registry does (decode, then the data-changed call).
/// </summary>
internal sealed class ObjectiveRig
{
    public readonly Engine Engine = Engine.Default();

    public readonly Source Source = new();

    public readonly State State = new();

    public readonly ContestedSounds Sounds = new();

    public readonly LocalPlayer Local = new();

    public readonly Entity Entity;

    public ObjectiveRig(Settings? settings = null)
    {
        State.BindLocal();
        Entity = new Entity(Engine, Source, settings ?? new SettingsFake(), Engine, Sounds, Local);
        Entity.State.BindLocal();
    }

    /// <summary>Writes, snapshots and delivers one packet at <paramref name="now"/>.</summary>
    public void Arrive(Action<State>? write = null, float now = 0f)
    {
        write?.Invoke(State);

        Engine.Returns("Clock.Now", now);

        byte[] blob = State.Snapshot();
        Entity.State.Decode(blob);
        Entity.OnDataChanged(blob, DataUpdateType.DataTableChanged);
    }
}

/// <summary>The sounds the resource emits, recorded.</summary>
internal sealed class ContestedSounds : Sounds
{
    public readonly List<(string Name, Vector? Origin)> Emitted = [];

    public void EmitFrom(EntityHandle entity, string soundName, Vector? origin = null)
        => Emitted.Add((soundName, origin));

    public void StopFrom(EntityHandle entity, string soundName) { }
}

/// <summary>The local player, whose team a fact chooses.</summary>
internal sealed class LocalPlayer : Local
{
    public int Team { get; set; }

    public string ClassName => "";
    public int Health => 100;
    public int MaxHealth => 125;
    public int MaxBuffedHealth => 125;
    public int Overheal => 0;
    public bool ShowsAmmo => false;
    public int Clip => -1;
    public int Reserve => 0;
    public int MaxTotalAmmo => 0;
}

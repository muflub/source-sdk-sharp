using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>A server entity that does each thing the two-instance fact drives.</summary>
[ScriptedEntity("two_instance_probe")]
public sealed class TwoInstanceProbe : ScriptedEntity
{
    public readonly Output OnPoked = new();

    public int Thought;
    public int Pokes;

    public override void OnThink()
    {
        Thought++;
        Log.Msg($"probe {Handle.Value:x} thought {Thought}\n");
    }

    [ScriptedEvent] public void Poke()
    {
        Pokes++;
        OnPoked.Fire();
    }

    public override void SaveState(SavedEntity state)
    {
        base.SaveState(state);
        state.Set("pokes", Pokes);
    }
}

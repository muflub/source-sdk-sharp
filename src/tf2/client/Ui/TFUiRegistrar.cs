using SourceSharp.Abi;

using TF2.Shared.Ctf;
using TF2.Shared.Econ;

namespace TF2.Client.Ui;

/// <summary>The TF2 client's hook seams: the user messages its adapters read, and the text window's map info gate.</summary>
[SideService(Side.Client)]
public sealed class TFUiRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry)
    {
        registry.AddHooks<ITFLoadoutReplyHook>();
        registry.AddHooks<ITFHudNotifyHook>();
        registry.AddHooks<TF2.Shared.Combat.ITFKillerNameHook>();
        registry.AddHooks<ITFMapInfoGate>();
        registry.Add<TFVoiceMenus, TFVoiceMenus>();
    }
}

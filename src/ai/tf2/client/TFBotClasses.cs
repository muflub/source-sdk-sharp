using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai.Ui;

namespace SourceSharp.Ai.TF2.Client;

/// <summary>
/// The classes TF2's bots play, for the bots page (plan_bots_teams_tf2 C.4): the classes whose
/// file says they are ready (P7), in the classes' own order. A class joins <see cref="Playable"/>
/// when its bots play it, and the page offers it then.
/// </summary>
[SideService(Side.Client, As = [typeof(IBotClassSource)])]
public sealed class TFBotClasses : IBotClassSource
{
    /// <summary>Each class a bot plays, by the mod's raw name (TFClassNames.Raw), with its name's token.</summary>
    public ImmutableArray<(string Raw, string Token)> Playable { get; } = [("scout", "#TF_Class_Name_Scout"), ("soldier", "#TF_Class_Name_Soldier"), ("demoman", "#TF_Class_Name_Demoman"), ("heavyweapons", "#TF_Class_Name_HWGuy"), ("pyro", "#TF_Class_Name_Pyro"), ("spy", "#TF_Class_Name_Spy")];

    public int ClassCount => Playable.Length;

    public string ClassToken(int i) => Playable[i].Token;

    /// <summary>The page's first choice: the first playable class.</summary>
    public int ChosenClass => 0;
}

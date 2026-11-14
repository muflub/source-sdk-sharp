using SourceSharp.Abi;

namespace TF2.Client.Buildings;

/// <summary>
/// The client's answer to the Wrangler's word
/// (<see cref="TF2.Shared.ITFSentryWrangle"/>): the sentries and their laser
/// dots are the server's, so the replay's asks — the aim, the held fire, the
/// rockets, the release — all hold nothing.
/// </summary>
[SideService(Side.Client, As = [typeof(TF2.Shared.ITFSentryWrangle)])]
public sealed class TFClientSentryWrangle : TF2.Shared.ITFSentryWrangle
{
    public void Aim(int ownerIndex) { }

    public void Fire(int ownerIndex) { }

    public void FireRockets(int ownerIndex) { }

    public void Release(int ownerIndex) { }
}

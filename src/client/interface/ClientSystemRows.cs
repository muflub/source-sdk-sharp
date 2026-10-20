namespace SourceSharp.Client.Interop;

/// <summary>
/// The kClientSystems[] rows whose system is managed's
/// (IManagedClientSystemsClient::Stage): one stage of the row named
/// <paramref name="name"/>.
/// </summary>
public interface IClientSystemRows
{
    /// <summary>Init's answer (1 or 0), 1 for another stage, -1 for an unknown row or stage.</summary>
    int Stage(string name, int stage);
}

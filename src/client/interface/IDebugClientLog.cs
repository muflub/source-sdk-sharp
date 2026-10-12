namespace SourceSharp.Client.Interface;

/// <summary>The client's console, as its dev API reaches it (P3).</summary>
public interface IDebugClientLog
{
    void Msg(string message);

    void Warning(string message);
}

using System.Globalization;
using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Vgui;

/// <summary>Which of the remembered lists: Steam's own flags (k_unFavoriteFlagFavorite, k_unFavoriteFlagHistory).</summary>
public enum ServerListKind : uint
{
    Favourites = 1,
    History = 2,
}

/// <summary>A remembered server: where to query it and where to connect.</summary>
public readonly record struct StoredServer(ServerAddress Query, ushort GamePort);

/// <summary>The favourite and history servers.</summary>
public interface IServerFavourites
{
    IReadOnlyList<StoredServer> List(ServerListKind kind);

    void Add(ServerListKind kind, StoredServer server);

    void Remove(ServerListKind kind, StoredServer server);
}

/// <summary>Steam's favourites and history for this game (stock's browser kept them there).</summary>
public sealed class SteamServerFavourites(ISteamServerBrowser steam) : IServerFavourites
{
    public IReadOnlyList<StoredServer> List(ServerListKind kind)
    {
        var list = new List<StoredServer>();
        int count = steam.FavouriteCount;
        for (int i = 0; i < count; i++)
            if (steam.TryGetFavourite(i, out var f) && (f.Flags & (uint)kind) != 0)
                list.Add(new StoredServer(new ServerAddress(f.Ip, f.QueryPort), f.ConnectionPort));
        return list;
    }

    public void Add(ServerListKind kind, StoredServer server)
        => steam.AddFavourite(server.Query.Ip, server.GamePort, server.Query.Port, (uint)kind);

    public void Remove(ServerListKind kind, StoredServer server)
        => steam.RemoveFavourite(server.Query.Ip, server.GamePort, server.Query.Port, (uint)kind);
}

/// <summary>
/// The favourites and history without Steam: cfg/serverbrowser.txt in the
/// mod, a line per server, <c>favourites|history a.b.c.d:query gameport</c>,
/// newest history last and at most <see cref="HistoryLimit"/> of it.
/// </summary>
public sealed class FileServerFavourites(IOptionsFiles files) : IServerFavourites
{
    public const string FileName = "cfg/serverbrowser.txt";
    public const int HistoryLimit = 100;

    private List<(ServerListKind Kind, StoredServer Server)>? _all;

    public IReadOnlyList<StoredServer> List(ServerListKind kind)
    {
        var list = new List<StoredServer>();
        foreach (var (k, s) in All())
            if (k == kind)
                list.Add(s);
        return list;
    }

    public void Add(ServerListKind kind, StoredServer server)
    {
        var all = All();
        all.RemoveAll(e => e.Kind == kind && e.Server.Query == server.Query);
        all.Add((kind, server));
        int history = all.Count(e => e.Kind == ServerListKind.History);
        for (int i = 0; history > HistoryLimit && i < all.Count;)
        {
            if (all[i].Kind == ServerListKind.History)
            {
                all.RemoveAt(i);
                history--;
            }
            else
                i++;
        }
        Save(all);
    }

    public void Remove(ServerListKind kind, StoredServer server)
    {
        var all = All();
        if (all.RemoveAll(e => e.Kind == kind && e.Server.Query == server.Query) > 0)
            Save(all);
    }

    private List<(ServerListKind Kind, StoredServer Server)> All()
    {
        if (_all is not null)
            return _all;
        _all = [];
        if (files.ReadAll(FileName) is not { } bytes)
            return _all;
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !ServerAddress.TryParse(parts[1], out var query))
                continue;
            ServerListKind? kind = parts[0] switch { "favourites" => ServerListKind.Favourites, "history" => ServerListKind.History, _ => null };
            if (kind is null)
                continue;
            ushort game = parts.Length > 2 && ushort.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var g) ? g : query.Port;
            _all.Add((kind.Value, new StoredServer(query, game)));
        }
        return _all;
    }

    private void Save(List<(ServerListKind Kind, StoredServer Server)> all)
    {
        var text = new StringBuilder();
        foreach (var (kind, s) in all)
            text.Append(kind == ServerListKind.Favourites ? "favourites " : "history ").Append(s.Query).Append(' ')
                .Append(s.GamePort.ToString(CultureInfo.InvariantCulture)).Append('\n');
        files.WriteAll(FileName, Encoding.UTF8.GetBytes(text.ToString()));
    }
}

/// <summary>Steam's lists while Steam is up, the file's otherwise (a -hushsteam launch).</summary>
public sealed class ServerFavourites(ISteamServerBrowser steam, SteamServerFavourites steamStore, FileServerFavourites fileStore)
    : IServerFavourites
{
    private IServerFavourites Store => steam.Available ? steamStore : fileStore;

    public IReadOnlyList<StoredServer> List(ServerListKind kind) => Store.List(kind);

    public void Add(ServerListKind kind, StoredServer server) => Store.Add(kind, server);

    public void Remove(ServerListKind kind, StoredServer server) => Store.Remove(kind, server);
}

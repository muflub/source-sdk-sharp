using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using Debug.Api.Server.Interfaces;

namespace Debug.Api;

/// <summary>One entity to bring into the world, as a saved group holds it.</summary>
public sealed record SpawnEntity(
    string ClassName,
    string TargetName,
    float[] Origin,
    float[] Angles,
    IReadOnlyList<MapKeyValue> KeyValues,
    IReadOnlyList<MapConnection> Connections);

/// <summary>Where a group should land.</summary>
public enum Placement
{
    /// <summary>Each entity at the position it was saved from.</summary>
    Origin,

    /// <summary>The group as a unit, its base at whatever the player is looking at.</summary>
    Look,
}

/// <summary>What happened to one entity.</summary>
public readonly record struct SpawnResult(
    string ClassName,
    string RequestedName,
    string ActualName,
    int Index,
    bool Created,
    string Error,
    uint Handle = IDebugWorld.NoEntity);

/// <summary>
/// Brings a saved group of entities back into the running game.
///
/// Everything goes through ent_create, because creating an entity properly means
/// setting its keyvalues BEFORE it spawns -- a func_door handed its speed after
/// DispatchSpawn has already decided how fast it moves is not the entity that was
/// saved. ent_create is the only path from here that gets that order right.
///
/// It comes with a real limitation, and this class reports it rather than hiding
/// it: ent_create refuses to run without a command client, so a game where no
/// player has issued a console command creates nothing. Every creation is
/// therefore checked by looking the entity up again, and a failure says which
/// entity and why instead of leaving an empty world and a cheerful reply.
/// </summary>
public static class EntitySpawner
{
    /// <summary>Keys the spawner sets itself; taking them from the saved data as well would fight it.</summary>
    private static readonly FrozenSet<string> Reserved =
        new[] { "classname", "targetname", "origin", "angles" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    //-------------------------------------------------------------------------
    // Parsing the request.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Reads a spawn request.
    ///
    /// JSON in, unlike everything else here, because a group is a nested
    /// structure and squeezing one through a query string would mean inventing
    /// an encoding for keyvalues that can contain any character at all.
    /// System.Text.Json is part of the shared framework, so this is a parser
    /// rather than a dependency.
    /// </summary>
    public static (Placement Placement, int Player, string Name, List<SpawnEntity> Entities) Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var placement = Text(root, "placement").Equals("look", StringComparison.OrdinalIgnoreCase)
            ? Placement.Look
            : Placement.Origin;

        int player = root.TryGetProperty("player", out var p) && p.TryGetInt32(out int index) ? index : 1;
        string name = Text(root, "name");

        var entities = new List<SpawnEntity>();

        if (root.TryGetProperty("entities", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in list.EnumerateArray())
                entities.Add(ReadEntity(element));
        }

        return (placement, player, name, entities);
    }

    private static SpawnEntity ReadEntity(JsonElement element)
    {
        var keyValues = new List<MapKeyValue>();

        if (element.TryGetProperty("keyvalues", out var keys) && keys.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in keys.EnumerateObject())
            {
                if (Reserved.Contains(key.Name))
                    continue;

                keyValues.Add(new MapKeyValue(key.Name, Scalar(key.Value)));
            }
        }

        var connections = new List<MapConnection>();

        if (element.TryGetProperty("connections", out var wired) && wired.ValueKind == JsonValueKind.Array)
        {
            foreach (var connection in wired.EnumerateArray())
            {
                connections.Add(new MapConnection(
                    Text(connection, "output"),
                    Text(connection, "target"),
                    Text(connection, "input"),
                    Text(connection, "param"),
                    Number(connection, "delay"),
                    (int)Number(connection, "times", -1)));
            }
        }

        return new SpawnEntity(
            Text(element, "classname"),
            Text(element, "targetname"),
            Vector(element, "origin"),
            Vector(element, "angles"),
            keyValues,
            connections);
    }

    private static string Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? Scalar(value) : "";

    /// <summary>
    /// A JSON value as the console will see it.
    ///
    /// A map value is always a string, but a saved group may have been through a
    /// YAML round trip that turned "0" into a number -- so numbers and booleans
    /// are rendered back rather than rejected.
    /// </summary>
    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True   => "1",
        JsonValueKind.False  => "0",
        _ => "",
    };

    private static float Number(JsonElement element, string name, float fallback = 0f)
        => element.TryGetProperty(name, out var value) && value.TryGetDouble(out double number)
            ? (float)number
            : fallback;

    private static float[] Vector(JsonElement element, string name)
    {
        var result = new float[3];

        if (!element.TryGetProperty(name, out var value))
            return result;

        // Either [x, y, z] or the map's own "x y z". A group can arrive from a
        // hand-edited YAML file, where both are natural to write.
        if (value.ValueKind == JsonValueKind.String)
            return BspFile.ParseVector(value.GetString() ?? "");

        if (value.ValueKind != JsonValueKind.Array)
            return result;

        int i = 0;
        foreach (var component in value.EnumerateArray())
        {
            if (i >= 3) break;
            if (component.TryGetDouble(out double number)) result[i] = (float)number;
            i++;
        }

        return result;
    }

    /// <summary>
    /// Reads an edited entity list, as the map file holds one.
    ///
    /// Shares the shapes the spawner accepts, because the two arrive from the
    /// same place: a group and an edited map entity are the same object with a
    /// different destination. Keyvalues may come as a list of pairs or as an
    /// object -- a list preserves order and duplicates, which a map file is
    /// entitled to have, so it is what the editor sends.
    /// </summary>
    public static List<MapEntity> ParseEntities(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var element = root.ValueKind == JsonValueKind.Array ? root
            : root.TryGetProperty("entities", out var list) ? list
            : default;

        var entities = new List<MapEntity>();

        if (element.ValueKind != JsonValueKind.Array)
            return entities;

        int id = 0;

        foreach (var item in element.EnumerateArray())
            entities.Add(ReadMapEntity(item, id++));

        return entities;
    }

    private static MapEntity ReadMapEntity(JsonElement element, int id)
    {
        var keyValues = new List<MapKeyValue>();

        if (element.TryGetProperty("keyvalues", out var keys))
        {
            if (keys.ValueKind == JsonValueKind.Array)
            {
                foreach (var pair in keys.EnumerateArray())
                    keyValues.Add(new MapKeyValue(Text(pair, "key"), Text(pair, "value")));
            }
            else if (keys.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in keys.EnumerateObject())
                    keyValues.Add(new MapKeyValue(key.Name, Scalar(key.Value)));
            }
        }

        string className = Text(element, "classname");
        string targetName = Text(element, "targetname");
        string model = Text(element, "model");
        var origin = Vector(element, "origin");
        var angles = Vector(element, "angles");

        // The four keys that are also fields are written from the fields, so an
        // edit to one of them cannot disagree with the copy in the keyvalue
        // list -- which is the bug this shape invites.
        keyValues.RemoveAll(kv => Reserved.Contains(kv.Key) || kv.Key.Equals("model", StringComparison.OrdinalIgnoreCase));

        if (className.Length > 0) keyValues.Insert(0, new MapKeyValue("classname", className));
        if (targetName.Length > 0) keyValues.Add(new MapKeyValue("targetname", targetName));
        if (model.Length > 0) keyValues.Add(new MapKeyValue("model", model));

        keyValues.Add(new MapKeyValue("origin", Join(origin)));

        // Angles are only written when they mean something. A great many map
        // entities have none, and adding "0 0 0" to every one of them would make
        // a diff of the saved map unreadable.
        if (angles[0] != 0f || angles[1] != 0f || angles[2] != 0f)
            keyValues.Add(new MapKeyValue("angles", Join(angles)));

        var connections = new List<MapConnection>();

        if (element.TryGetProperty("connections", out var wired) && wired.ValueKind == JsonValueKind.Array)
        {
            foreach (var connection in wired.EnumerateArray())
            {
                connections.Add(new MapConnection(
                    Text(connection, "output"),
                    Text(connection, "target"),
                    Text(connection, "input"),
                    Text(connection, "param"),
                    Number(connection, "delay"),
                    (int)Number(connection, "times", -1)));
            }
        }

        return new MapEntity(id, className, targetName, origin, angles, model, keyValues, connections);
    }

    private static string Join(float[] vector)
        => string.Join(' ', vector.Select(v =>
            v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>An AddOutput value: what the engine wants a connection to look like.</summary>
    internal static string Wire(MapConnection connection)
        => $"{connection.Output} {connection.Target}:{connection.Input}:{connection.Parameter}:" +
           $"{connection.Delay.ToString(System.Globalization.CultureInfo.InvariantCulture)}:" +
           $"{connection.TimesToFire}";

    /// <summary>
    /// A console argument.
    ///
    /// The console splits on whitespace, so anything containing a space has to
    /// arrive quoted or it becomes two arguments -- and for ent_create that
    /// means a keyvalue silently taking the wrong value and shifting every pair
    /// after it.
    /// </summary>
    internal static string Quote(string value)
        => value.Length > 0 && !value.Any(char.IsWhiteSpace) && !value.Contains('"')
            ? value
            : $"\"{value.Replace("\"", "")}\"";
}

/// <summary>
/// Brings a saved group back into the running game. See <see cref="EntitySpawner"/>
/// for why everything goes through <c>ent_create</c>.
/// </summary>
public interface IEntityGroupSpawner
{
    /// <summary>Creates a group, positions it, and wires it back up.</summary>
    IReadOnlyList<SpawnResult> Spawn(Placement placement, int playerIndex,
                                     IReadOnlyList<SpawnEntity> entities);
}

/// <summary>
/// The spawning half of <see cref="EntitySpawner"/>: the engine areas it drives
/// and the counter its temporary names come from.
///
/// <para>
/// The counter was a static, one per process. It is one per spawner now, and the
/// dev API server that owns the spawner is one per server module and lives in
/// that module's load context, so it restarts exactly when the static did: at a
/// managed reload, which makes a new context.
/// </para>
/// </summary>
public sealed class EntityGroupSpawner : IEntityGroupSpawner
{
    private readonly Action<string> _execute;
    private readonly IDebugWorld _world;

    /// <summary>The engine areas a spawn drives, and the server console it creates through.</summary>
    public EntityGroupSpawner(Action<string> execute, IDebugWorld world)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// The name an entity is created under before it is put where it belongs.
    ///
    /// Unique per spawn, because positioning it means finding it again by name
    /// and a group frequently contains several entities sharing one name --
    /// which is legal, and which would otherwise all resolve to the first.
    /// </summary>
    private const string TempPrefix = "__ss_spawn_";

    private int _counter;

    //-------------------------------------------------------------------------
    // Spawning. Game thread only -- every line of it touches the engine.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Creates a group, positions it, and wires it back up.
    ///
    /// Connections are a second pass over the whole group, because a connection
    /// between two members can only be made once both exist -- doing it inline
    /// would silently drop every edge that pointed forwards.
    /// </summary>
    public IReadOnlyList<SpawnResult> Spawn(
        Placement placement, int playerIndex, IReadOnlyList<SpawnEntity> entities)
    {
        var results = new List<SpawnResult>(entities.Count);

        if (entities.Count == 0)
            return results;

        var offset = OffsetFor(placement, playerIndex, entities);

        // Where each entity ended up under, which is what the connection pass
        // addresses them by. An entity that failed to spawn is absent, so a
        // connection to it is skipped rather than fired at nothing.
        var names = new string[entities.Count];

        for (int i = 0; i < entities.Count; i++)
            results.Add(Create(entities[i], offset, out names[i]));

        for (int i = 0; i < entities.Count; i++)
        {
            if (names[i].Length == 0)
                continue;

            foreach (var connection in entities[i].Connections)
            {
                _execute(
                    $"ent_fire {EntitySpawner.Quote(names[i])} AddOutput {EntitySpawner.Quote(EntitySpawner.Wire(connection))}");
            }
        }

        return results;
    }

    /// <summary>
    /// How far the group has to move from where it was saved.
    ///
    /// For a look placement the anchor is the group's footprint centre -- middle
    /// in x and y, bottom in z -- so a group lands standing on the surface being
    /// pointed at rather than half sunk into it or hovering above.
    /// </summary>
    private DebugVec3 OffsetFor(Placement placement, int playerIndex, IReadOnlyList<SpawnEntity> entities)
    {
        if (placement != Placement.Look)
            return default;

        uint player = _world.PlayerHandle(playerIndex);

        if (!_world.IsValid(player))
            return default;

        _world.TraceAim(player, 8192f, out var hit);

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue;

        foreach (var entity in entities)
        {
            minX = Math.Min(minX, entity.Origin[0]); maxX = Math.Max(maxX, entity.Origin[0]);
            minY = Math.Min(minY, entity.Origin[1]); maxY = Math.Max(maxY, entity.Origin[1]);
            minZ = Math.Min(minZ, entity.Origin[2]);
        }

        var anchor = new DebugVec3((minX + maxX) / 2f, (minY + maxY) / 2f, minZ);

        return new DebugVec3(hit.X - anchor.X, hit.Y - anchor.Y, hit.Z - anchor.Z);
    }

    private SpawnResult Create(SpawnEntity entity, DebugVec3 offset, out string name)
    {
        name = "";

        if (entity.ClassName.Length == 0)
            return new SpawnResult("", entity.TargetName, "", -1, false, "no classname");

        string temp = TempPrefix + (++_counter);

        var command = new StringBuilder("ent_create ")
            .Append(EntitySpawner.Quote(entity.ClassName))
            .Append(" targetname ").Append(temp);

        foreach (var kv in entity.KeyValues)
            command.Append(' ').Append(EntitySpawner.Quote(kv.Key)).Append(' ').Append(EntitySpawner.Quote(kv.Value));

        _execute(command.ToString());

        // Looked up rather than assumed. ent_create returns nothing and fails
        // quietly for a class the engine does not know, and for a game with no
        // command client it fails quietly for everything.
        uint handle = _world.FindByName(temp);

        if (!_world.IsValid(handle))
        {
            return new SpawnResult(entity.ClassName, entity.TargetName, "", -1, false,
                "the engine created nothing — either there is no such entity class, or " +
                "ent_create had no command client (a player must have run a console command first)");
        }

        _world.Teleport(handle,
            new DebugVec3(entity.Origin[0] + offset.X, entity.Origin[1] + offset.Y, entity.Origin[2] + offset.Z),
            new DebugVec3(entity.Angles[0], entity.Angles[1], entity.Angles[2]),
            zeroVelocity: true);

        // Renamed last. The temporary name is what made it findable, and taking
        // it away before it has been placed would leave it unreachable.
        name = temp;

        if (entity.TargetName.Length > 0)
        {
            _execute(
                $"ent_fire {temp} AddOutput {EntitySpawner.Quote("targetname " + entity.TargetName)}");
            name = entity.TargetName;
        }

        return new SpawnResult(entity.ClassName, entity.TargetName, name, _world.GetIndex(handle), true, "", handle);
    }
}

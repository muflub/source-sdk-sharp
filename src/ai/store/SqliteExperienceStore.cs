using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SourceSharp.Ai.Store;

/// <summary>
/// The experience store in a SQLite file (plan_bots_teams_tf2 Part G, G.3):
/// one file a server, shared by every game process pointed at it. A merge is
/// one transaction of additive upserts, each fading the row it finds to the
/// map's round count before adding (SQLite calls <see cref="ExperienceDecay"/>
/// through the <c>fade</c> function), so two processes writing one file sum,
/// whatever the order. A load reads one consistent snapshot and fades it in
/// passing; nothing is rewritten on a timer, and rows past forgetting go when
/// a merge counts new rounds.
///
/// <para>It never throws into its caller. A file that cannot be opened, is no
/// database, is another's database or a newer build's is a
/// <see cref="Refusal"/>, as is one that turns out corrupt later: the store
/// then loads the empty experience and merges nothing, and leaves the file as
/// it found it. A file another process holds locked past the busy timeout
/// fails that one call and is no refusal.</para>
/// </summary>
public sealed class SqliteExperienceStore : IExperienceStore
{
    private const int Busy = 5, Locked = 6;

    private readonly Lock _gate = new();
    private SqliteConnection? _connection;
    private string? _refusal;

    private SqliteExperienceStore(string path, SqliteConnection? connection, string? refusal)
    {
        Path = path;
        _connection = connection;
        _refusal = refusal;
    }

    /// <summary>The store at <paramref name="path"/>, its file and directory created if absent and its schema brought up to this build's.</summary>
    public static SqliteExperienceStore Open(string path) => Open(path, SqliteSchema.Migrations, SqliteFile.BusyTimeoutMs);

    internal static SqliteExperienceStore Open(string path, IReadOnlyList<Migration> migrations, int busyTimeoutMs = SqliteFile.BusyTimeoutMs)
    {
        SqliteConnection? connection = null;
        try
        {
            if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) is { Length: > 0 } directory)
                Directory.CreateDirectory(directory);
            connection = SqliteFile.Open(path, busyTimeoutMs);
            connection.CreateFunction("fade", (long from, long to, double halfLife) => ExperienceDecay.Factor(to - from, halfLife), isDeterministic: true);
            if (SqliteSchema.Migrate(connection, migrations) is not { } refused)
                return new SqliteExperienceStore(path, connection, null);
            connection.Dispose();
            return new SqliteExperienceStore(path, null, $"{path} is not used as the experience store: {refused}");
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                      or DllNotFoundException or TypeInitializationException or BadImageFormatException)
        {
            connection?.Dispose();
            return new SqliteExperienceStore(path, null, $"{path} cannot be used as the experience store: {OneLine(e)}");
        }
    }

    /// <summary>The file.</summary>
    public string Path { get; }

    public string? Refusal
    {
        get
        {
            lock (_gate)
                return _refusal;
        }
    }

    /// <summary>Calls that gave up on a file another process held locked.</summary>
    public long BusyFailures { get; private set; }

    public MapExperience Load(ExperienceKey key, double halfLife)
    {
        lock (_gate)
        {
            if (_connection is not { } db)
                return new MapExperience(key, 0, new ExperienceDelta());
            try
            {
                // Deferred: a read, and one snapshot of the file for all of it.
                using SqliteTransaction transaction = db.BeginTransaction(deferred: true);
                var rows = new ExperienceDelta();
                long rounds = Read(db, transaction, key, halfLife, rows);
                return new MapExperience(key, rounds, rows);
            }
            catch (SqliteException e)
            {
                Failed(e);
                return new MapExperience(key, 0, new ExperienceDelta());
            }
        }
    }

    public bool Merge(ExperienceKey key, ExperienceDelta delta, double halfLife)
    {
        ArgumentNullException.ThrowIfNull(delta);
        lock (_gate)
        {
            if (_connection is not { } db)
                return false;
            try
            {
                using SqliteTransaction transaction = db.BeginTransaction();
                Write(db, transaction, key, delta, halfLife);
                transaction.Commit();
                return true;
            }
            catch (SqliteException e)
            {
                Failed(e);
                return false;
            }
        }
    }

    public ExperienceCounts Counts(ExperienceKey key)
    {
        lock (_gate)
        {
            if (_connection is not { } db)
                return default;
            try
            {
                using SqliteTransaction transaction = db.BeginTransaction(deferred: true);
                using var find = new Statement(db, transaction, "SELECT id, rounds FROM map WHERE name = $0 AND checksum = $1 AND mode = $2");
                using SqliteDataReader map = find.Read(key.Map, key.Checksum, key.Mode);
                if (!map.Read())
                    return default;
                long id = map.GetInt64(0), rounds = map.GetInt64(1);
                long InContext(string table) => Count(db, transaction, $"SELECT COUNT(*) FROM {table} t JOIN context c ON c.id = t.context_id WHERE c.map_id = $0", id);
                return new ExperienceCounts(rounds, InContext("cell_stat"), InContext("sight_line"), InContext("look"), InContext("spot"),
                                            Count(db, transaction, "SELECT COUNT(*) FROM link WHERE map_id = $0", id), InContext("outcome"),
                                            Count(db, transaction, "SELECT COUNT(*) FROM round WHERE map_id = $0", id));
            }
            catch (SqliteException e)
            {
                Failed(e);
                return default;
            }
        }
    }

    /// <summary>
    /// The keys the file holds rows of (lane G8: the practice tool finds the mode a map was played in): every map row's
    /// name, checksum and mode, in the order they were first written. None for a refused store or a failed read.
    /// </summary>
    public IReadOnlyList<ExperienceKey> Keys()
    {
        lock (_gate)
        {
            if (_connection is not { } db)
                return [];
            try
            {
                using SqliteTransaction transaction = db.BeginTransaction(deferred: true);
                using var all = new Statement(db, transaction, "SELECT name, checksum, mode FROM map ORDER BY id");
                using SqliteDataReader r = all.Read();
                var keys = new List<ExperienceKey>();
                while (r.Read())
                    keys.Add(new ExperienceKey(r.GetString(0), r.GetString(1), r.GetString(2)));
                return keys;
            }
            catch (SqliteException e)
            {
                Failed(e);
                return [];
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }

    /// <summary>A failed call: a lock held too long is counted and passes; anything else (a corrupt file, a full disk) refuses the store from here on.</summary>
    private void Failed(SqliteException e)
    {
        if (e.SqliteErrorCode is Busy or Locked)
        {
            BusyFailures++;
            return;
        }

        _refusal = $"{Path} stopped working as the experience store: {OneLine(e)}";
        _connection?.Dispose();
        _connection = null;
    }

    private static string OneLine(Exception e)
    {
        Exception inner = e is TypeInitializationException { InnerException: { } cause } ? cause : e;
        return $"{inner.GetType().Name}: {inner.Message.ReplaceLineEndings(" ")}";
    }

    private static long Count(SqliteConnection db, SqliteTransaction transaction, string sql, long map)
    {
        using var count = new Statement(db, transaction, sql);
        return count.Scalar(map);
    }

    private static void Write(SqliteConnection db, SqliteTransaction transaction, ExperienceKey key, ExperienceDelta delta, double halfLife)
    {
        long map, now;
        using (var count = new Statement(db, transaction,
                   "INSERT INTO map (name, checksum, mode, rounds) VALUES ($0, $1, $2, $3) "
                   + "ON CONFLICT (name, checksum, mode) DO UPDATE SET rounds = rounds + excluded.rounds RETURNING id, rounds"))
        using (SqliteDataReader row = count.Read(key.Map, key.Checksum, key.Mode, (long)delta.Rounds.Count))
        {
            row.Read();
            map = row.GetInt64(0);
            now = row.GetInt64(1);
        }

        using (var round = new Statement(db, transaction,
                   "INSERT INTO round (map_id, round_no, seconds, winning_role, humans, bots, composition) VALUES ($0, $1, $2, $3, $4, $5, $6)"))
        {
            long number = now - delta.Rounds.Count;
            foreach (RoundRow r in delta.Rounds)
                round.Run(map, ++number, r.Seconds, (long)r.WinningRole, r.Humans, r.Bots, r.Composition);
        }

        var contexts = new Dictionary<ExperienceContext, long>();
        using var context = new Statement(db, transaction,
            "INSERT INTO context (map_id, stage, role, class) VALUES ($0, $1, $2, $3) "
            + "ON CONFLICT (map_id, stage, role, class) DO UPDATE SET stage = excluded.stage RETURNING id");
        long Of(ExperienceContext c)
        {
            if (!contexts.TryGetValue(c, out long id))
                contexts[c] = id = context.Scalar(map, (long)c.Stage, (long)c.Role, (long)c.Class);
            return id;
        }

        // $n is the round count the rows are written at, $h the half-life: each
        // SET reads the row as it was, so every column fades by the old round_no.
        using (var cell = new Statement(db, transaction,
                   "INSERT INTO cell_stat (context_id, bin, source, seconds, deaths, kills, damage_taken, damage_dealt, round_no) "
                   + "VALUES ($0, $1, $2, $3, $4, $5, $6, $7, $8) ON CONFLICT (context_id, bin, source) DO UPDATE SET "
                   + Fading("seconds", "deaths", "kills", "damage_taken", "damage_dealt")))
        {
            foreach ((CellKey k, CellStat v) in delta.Cells)
                cell.Run(Of(k.Context), k.Bin.Packed, (long)k.Source, v.Seconds, v.Deaths, v.Kills, v.DamageTaken, v.DamageDealt, now, halfLife);
        }

        using (var sight = new Statement(db, transaction,
                   "INSERT INTO sight_line (context_id, victim_bin, killer_bin, weapon_class, count, round_no) "
                   + "VALUES ($0, $1, $2, $3, $4, $5) ON CONFLICT (context_id, victim_bin, killer_bin, weapon_class) DO UPDATE SET " + Fading("count")))
        {
            foreach ((SightKey k, Tally v) in delta.Sights)
                sight.Run(Of(k.Context), k.Victim.Packed, k.Killer.Packed, (long)k.WeaponClass, v.Count, now, halfLife);
        }

        using (var look = new Statement(db, transaction,
                   "INSERT INTO look (context_id, bin, yaw, pitch, seconds, round_no) "
                   + "VALUES ($0, $1, $2, $3, $4, $5) ON CONFLICT (context_id, bin, yaw, pitch) DO UPDATE SET " + Fading("seconds")))
        {
            foreach ((LookKey k, Tally v) in delta.Looks)
                look.Run(Of(k.Context), k.Bin.Packed, (long)k.Yaw, (long)k.Pitch, v.Count, now, halfLife);
        }

        using (var spot = new Statement(db, transaction,
                   "INSERT INTO spot (context_id, kind, bin, yaw, uses, seconds_held, kills, deaths, round_no) "
                   + "VALUES ($0, $1, $2, $3, $4, $5, $6, $7, $8) ON CONFLICT (context_id, kind, bin, yaw) DO UPDATE SET "
                   + Fading("uses", "seconds_held", "kills", "deaths")))
        {
            foreach ((SpotKey k, SpotStat v) in delta.Spots)
                spot.Run(Of(k.Context), (long)k.Kind, k.Bin.Packed, (long)k.Yaw, v.Uses, v.SecondsHeld, v.Kills, v.Deaths, now, halfLife);
        }

        using (var link = new Statement(db, transaction,
                   "INSERT INTO link (map_id, class, from_bin, to_bin, kind, source, recipe, tries, successes, seconds, health_cost, round_no) "
                   + "VALUES ($0, $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11) "
                   + "ON CONFLICT (map_id, class, from_bin, to_bin, kind, source, recipe) DO UPDATE SET "
                   + Fading("tries", "successes", "seconds", "health_cost")))
        {
            foreach ((LinkKey k, LinkStat v) in delta.Links)
            {
                link.Run(map, (long)k.Class, k.From.Packed, k.To.Packed, (long)k.Kind, (long)k.Source, k.Recipe.Bytes.ToArray(),
                         v.Tries, v.Successes, v.Seconds, v.HealthCost, now, halfLife);
            }
        }

        using (var outcome = new Statement(db, transaction,
                   "INSERT INTO outcome (context_id, kind, choice, tries, score, round_no) "
                   + "VALUES ($0, $1, $2, $3, $4, $5) ON CONFLICT (context_id, kind, choice) DO UPDATE SET " + Fading("tries", "score")))
        {
            foreach ((OutcomeKey k, OutcomeStat v) in delta.Outcomes)
                outcome.Run(Of(k.Context), (long)k.Kind, k.Choice, v.Tries, v.Score, now, halfLife);
        }

        // Rows past forgetting go when rounds were counted: only then can a row have aged.
        if (delta.Rounds.Count > 0 && halfLife > 0d)
            Forget(db, transaction, map, now, halfLife);
    }

    /// <summary>
    /// The SET list of an upsert over <paramref name="columns"/>: each is the
    /// row's old value faded from its old round to the new, plus the new
    /// value. The round count and the half-life are the statement's last two
    /// parameters, the first of them the inserted row's <c>round_no</c>.
    /// </summary>
    private static string Fading(params string[] columns)
        => string.Join(", ", columns.Select(c => $"{c} = {c} * fade(round_no, excluded.round_no, $h) + excluded.{c}")) + ", round_no = excluded.round_no";

    private static void Forget(SqliteConnection db, SqliteTransaction transaction, long map, long now, double halfLife)
    {
        long oldest = now - (long)Math.Floor(halfLife * ExperienceDecay.ForgetHalfLives);
        foreach (string table in new[] { "cell_stat", "sight_line", "look", "spot", "outcome" })
        {
            using var forget = new Statement(db, transaction, $"DELETE FROM {table} WHERE round_no < $1 AND context_id IN (SELECT id FROM context WHERE map_id = $0)");
            forget.Run(map, oldest);
        }

        foreach (string table in new[] { "link", "round" })
        {
            using var forget = new Statement(db, transaction, $"DELETE FROM {table} WHERE map_id = $0 AND round_no < $1");
            forget.Run(map, oldest);
        }
    }

    /// <summary>Reads <paramref name="key"/>'s rows into <paramref name="rows"/>, each faded to the map's round count, which it answers (0 for a key never written).</summary>
    private static long Read(SqliteConnection db, SqliteTransaction transaction, ExperienceKey key, double halfLife, ExperienceDelta rows)
    {
        long map, now;
        using (var find = new Statement(db, transaction, "SELECT id, rounds FROM map WHERE name = $0 AND checksum = $1 AND mode = $2"))
        using (SqliteDataReader row = find.Read(key.Map, key.Checksum, key.Mode))
        {
            if (!row.Read())
                return 0;
            map = row.GetInt64(0);
            now = row.GetInt64(1);
        }

        var contexts = new Dictionary<long, ExperienceContext>();
        using (var context = new Statement(db, transaction, "SELECT id, stage, role, class FROM context WHERE map_id = $0"))
        using (SqliteDataReader r = context.Read(map))
        {
            while (r.Read())
                contexts[r.GetInt64(0)] = new ExperienceContext(r.GetInt32(1), r.GetInt32(2), r.GetInt32(3));
        }

        // Each query's last column is round_no: what is left of the row, or nothing once it is forgotten.
        bool Left(SqliteDataReader r, out double factor)
        {
            long age = now - r.GetInt64(r.FieldCount - 1);
            factor = ExperienceDecay.Factor(age, halfLife);
            return !ExperienceDecay.Forgotten(age, halfLife);
        }

        const string InMap = " t JOIN context c ON c.id = t.context_id WHERE c.map_id = $0";
        using (var cell = new Statement(db, transaction, "SELECT t.context_id, bin, source, seconds, deaths, kills, damage_taken, damage_dealt, round_no FROM cell_stat" + InMap))
        using (SqliteDataReader r = cell.Read(map))
        {
            while (r.Read())
            {
                if (Left(r, out double f))
                {
                    rows.Add(new CellKey(contexts[r.GetInt64(0)], WorldBin.Unpack(r.GetInt64(1)), (ExperienceSource)r.GetInt32(2)),
                             new CellStat(r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetDouble(7)).Scaled(f));
                }
            }
        }

        using (var sight = new Statement(db, transaction, "SELECT t.context_id, victim_bin, killer_bin, weapon_class, count, round_no FROM sight_line" + InMap))
        using (SqliteDataReader r = sight.Read(map))
        {
            while (r.Read())
            {
                if (Left(r, out double f))
                    rows.Add(new SightKey(contexts[r.GetInt64(0)], WorldBin.Unpack(r.GetInt64(1)), WorldBin.Unpack(r.GetInt64(2)), r.GetInt32(3)), new Tally(r.GetDouble(4)).Scaled(f));
            }
        }

        using (var look = new Statement(db, transaction, "SELECT t.context_id, bin, yaw, pitch, seconds, round_no FROM look" + InMap))
        using (SqliteDataReader r = look.Read(map))
        {
            while (r.Read())
            {
                if (Left(r, out double f))
                    rows.Add(new LookKey(contexts[r.GetInt64(0)], WorldBin.Unpack(r.GetInt64(1)), r.GetInt32(2), r.GetInt32(3)), new Tally(r.GetDouble(4)).Scaled(f));
            }
        }

        using (var spot = new Statement(db, transaction, "SELECT t.context_id, kind, bin, yaw, uses, seconds_held, kills, deaths, round_no FROM spot" + InMap))
        using (SqliteDataReader r = spot.Read(map))
        {
            while (r.Read())
            {
                if (Left(r, out double f))
                {
                    rows.Add(new SpotKey(contexts[r.GetInt64(0)], r.GetInt32(1), WorldBin.Unpack(r.GetInt64(2)), r.GetInt32(3)),
                             new SpotStat(r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetDouble(7)).Scaled(f));
                }
            }
        }

        using (var link = new Statement(db, transaction,
                   "SELECT class, from_bin, to_bin, kind, source, recipe, tries, successes, seconds, health_cost, round_no FROM link WHERE map_id = $0"))
        using (SqliteDataReader r = link.Read(map))
        {
            while (r.Read())
            {
                if (Left(r, out double f))
                {
                    var recipe = new LinkRecipe(ImmutableArray.Create(r.GetFieldValue<byte[]>(5)));
                    rows.Add(new LinkKey(r.GetInt32(0), WorldBin.Unpack(r.GetInt64(1)), WorldBin.Unpack(r.GetInt64(2)), r.GetInt32(3), (LinkSource)r.GetInt32(4), recipe),
                             new LinkStat(r.GetDouble(6), r.GetDouble(7), r.GetDouble(8), r.GetDouble(9)).Scaled(f));
                }
            }
        }

        using (var outcome = new Statement(db, transaction, "SELECT t.context_id, kind, choice, tries, score, round_no FROM outcome" + InMap))
        using (SqliteDataReader r = outcome.Read(map))
        {
            while (r.Read())
            {
                if (Left(r, out double f))
                    rows.Add(new OutcomeKey(contexts[r.GetInt64(0)], r.GetInt32(1), r.GetInt64(2)), new OutcomeStat(r.GetDouble(3), r.GetDouble(4)).Scaled(f));
            }
        }

        using (var round = new Statement(db, transaction,
                   "SELECT seconds, winning_role, humans, bots, composition, round_no FROM round WHERE map_id = $0 ORDER BY round_no"))
        using (SqliteDataReader r = round.Read(map))
        {
            while (r.Read())
            {
                if (Left(r, out double f))
                    rows.Add(new RoundRow(r.GetDouble(0), r.GetInt32(1), r.GetDouble(2), r.GetDouble(3), r.GetString(4), f));
            }
        }

        return now;
    }

    /// <summary>
    /// A prepared statement run many times: its parameters are <c>$0</c>,
    /// <c>$1</c>, ... in the order the values are given, and an upsert's last
    /// two values are the round count (also its last numbered parameter) and
    /// the half-life (<c>$h</c>).
    /// </summary>
    private sealed class Statement : IDisposable
    {
        private readonly SqliteCommand _command;

        public Statement(SqliteConnection db, SqliteTransaction transaction, string sql)
        {
            _command = db.CreateCommand();
            _command.Transaction = transaction;
            _command.CommandText = sql;
        }

        private void Bind(object[] values)
        {
            bool fades = _command.CommandText.Contains("$h", StringComparison.Ordinal);
            int numbered = fades ? values.Length - 1 : values.Length;
            if (_command.Parameters.Count == 0)
            {
                for (int i = 0; i < numbered; i++)
                    _command.Parameters.Add(new SqliteParameter("$" + i.ToString(CultureInfo.InvariantCulture), values[i]));
                if (fades)
                    _command.Parameters.Add(new SqliteParameter("$h", values[^1]));
                return;
            }

            for (int i = 0; i < values.Length; i++)
                _command.Parameters[i].Value = values[i];
        }

        public void Run(params object[] values)
        {
            Bind(values);
            _command.ExecuteNonQuery();
        }

        public long Scalar(params object[] values)
        {
            Bind(values);
            return Convert.ToInt64(_command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        public SqliteDataReader Read(params object[] values)
        {
            Bind(values);
            return _command.ExecuteReader();
        }

        public void Dispose() => _command.Dispose();
    }
}

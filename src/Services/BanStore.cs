using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Sws2Cs2Admin.Models;

namespace Sws2Cs2Admin.Services;

internal enum BanStatus { Active, Permanent, Temporary, Expired, Revoked, All }
internal sealed record BanQuery(string Text = "", BanStatus Status = BanStatus.Active,
    string Admin = "", DateTimeOffset? From = null, DateTimeOffset? To = null, ulong? SteamId = null);
internal sealed record BanPage(IReadOnlyList<BanRecord> Records, int Total, int Page, int PageSize);

/// <summary>SQLite history with a small cache for join enforcement. All runtime I/O runs on a worker.</summary>
internal sealed class BanStore
{
    private readonly string _connectionString;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly Dictionary<ulong, List<BanRecord>> _active = [];
    private const string Columns = "id,steam_id,player_name,ip_address,reason,admin_steam_id,admin_name,created_at,expires_at,revoked_at,revoked_by,revoke_reason";

    public BanStore(string dataDirectory, ILogger logger)
    {
        Directory.CreateDirectory(dataDirectory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "bans.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
        using var db = Open();
        using var schema = db.CreateCommand();
        schema.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS bans(
              id TEXT PRIMARY KEY, steam_id TEXT NOT NULL, player_name TEXT NOT NULL,
              ip_address TEXT, reason TEXT NOT NULL, admin_steam_id TEXT NOT NULL, admin_name TEXT NOT NULL,
              created_at INTEGER NOT NULL, expires_at INTEGER, revoked_at INTEGER, revoked_by TEXT, revoke_reason TEXT,
              search_name TEXT NOT NULL, search_admin TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS idx_bans_steam ON bans(steam_id,created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_bans_state ON bans(revoked_at,expires_at,created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_bans_created ON bans(created_at DESC,id);
            CREATE INDEX IF NOT EXISTS idx_bans_admin ON bans(admin_steam_id,created_at DESC);
            """;
        schema.ExecuteNonQuery();
        ImportLegacy(db, dataDirectory, logger);
        using var command = db.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM bans WHERE revoked_at IS NULL AND (expires_at IS NULL OR expires_at > $now) ORDER BY created_at DESC,id";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using var reader = command.ExecuteReader();
        while (reader.Read()) Cache(Read(reader));
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous=FULL;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static void ImportLegacy(SqliteConnection db, string directory, ILogger logger)
    {
        using var check = db.CreateCommand();
        check.CommandText = "SELECT value FROM metadata WHERE key='legacy_json_imported'";
        if (check.ExecuteScalar() is not null) return;
        var source = Path.Combine(directory, "bans.json");
        var records = new List<BanRecord>();
        if (File.Exists(source))
        {
            // Invalid input aborts startup rather than silently dropping existing bans.
            var json = File.ReadAllText(source);
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object || !parsed.RootElement.EnumerateObject()
                .Any(property => property.Name.Equals("Bans", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Array))
                throw new InvalidDataException("bans.json has no Bans array; migration stopped.");
            var document = JsonSerializer.Deserialize<BanDocument>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("bans.json has no ban document.");
            records = document.Bans ?? throw new InvalidDataException("bans.json has no Bans array.");
            var backup = source + ".pre-sqlite.bak";
            if (!File.Exists(backup)) File.Copy(source, backup, overwrite: false);
            else if (!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(backup)))
                throw new InvalidDataException("Legacy JSON changed after an interrupted migration; preserve and review both files before retrying.");
        }
        using var transaction = db.BeginTransaction();
        foreach (var record in records)
        {
            if (record.Id == Guid.Empty || record.SteamId == 0)
                throw new InvalidDataException("Legacy ban has an invalid ID or SteamID; migration stopped.");
            Insert(db, transaction, record);
        }
        using var marker = db.CreateCommand();
        marker.Transaction = transaction;
        marker.CommandText = "INSERT INTO metadata(key,value) VALUES('legacy_json_imported',$value)";
        marker.Parameters.AddWithValue("$value", DateTimeOffset.UtcNow.ToString("O"));
        marker.ExecuteNonQuery();
        transaction.Commit();
        logger.LogInformation("[sws2-cs2admin] SQLite initialized; imported {Count} legacy ban records. Original JSON retained.", records.Count);
    }

    // Only called during plugin shutdown. Runtime operations never wait for disk on the game thread.
    public void DrainWrites()
    {
        _writes.Wait();
        _writes.Release();
    }

    public BanRecord? FindActive(ulong steamId)
    {
        lock (_sync)
        {
            if (!_active.TryGetValue(steamId, out var bans)) return null;
            var now = DateTimeOffset.UtcNow;
            bans.RemoveAll(ban => !ban.IsActive(now));
            if (bans.Count == 0) { _active.Remove(steamId); return null; }
            return bans.OrderByDescending(ban => ban.CreatedAt).First();
        }
    }

    private void Cache(BanRecord ban)
    {
        lock (_sync)
        {
            if (!_active.TryGetValue(ban.SteamId, out var records)) _active[ban.SteamId] = records = [];
            records.Add(ban);
        }
    }

    public async Task<bool> AddAsync(BanRecord ban)
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                using var db = Open();
                using var transaction = db.BeginTransaction();
                using var check = db.CreateCommand();
                check.Transaction = transaction;
                check.CommandText = "SELECT 1 FROM bans WHERE steam_id=$steam AND revoked_at IS NULL AND (expires_at IS NULL OR expires_at>$now) LIMIT 1";
                check.Parameters.AddWithValue("$steam", ban.SteamId.ToString(CultureInfo.InvariantCulture));
                check.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (check.ExecuteScalar() is not null) return false;
                Insert(db, transaction, ban);
                transaction.Commit();
                Cache(ban);
                return true;
            }).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }

    public async Task<BanRecord?> RevokeAsync(ulong steamId, string revokedBy, string reason, Guid? expectedId = null)
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var current = FindActive(steamId);
                if (current is null || (expectedId.HasValue && current.Id != expectedId)) return null;
                var now = DateTimeOffset.UtcNow;
                using var db = Open();
                using var command = db.CreateCommand();
                command.CommandText = "UPDATE bans SET revoked_at=$now,revoked_by=$by,revoke_reason=$reason WHERE steam_id=$steam AND revoked_at IS NULL AND (expires_at IS NULL OR expires_at>$now)";
                command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue("$by", revokedBy);
                command.Parameters.AddWithValue("$reason", reason);
                command.Parameters.AddWithValue("$steam", steamId.ToString(CultureInfo.InvariantCulture));
                if (command.ExecuteNonQuery() == 0) return null;
                lock (_sync) _active.Remove(steamId);
                return current;
            }).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }

    public Task<BanPage> QueryAsync(BanQuery query, int page = 0, int pageSize = 10) => Task.Run(() =>
    {
        using var db = Open();
        using var transaction = db.BeginTransaction(deferred: true);
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        var conditions = new List<string>();
        conditions.Add(query.Status switch
        {
            BanStatus.Active => "revoked_at IS NULL AND (expires_at IS NULL OR expires_at>$now)",
            BanStatus.Permanent => "revoked_at IS NULL AND expires_at IS NULL",
            BanStatus.Temporary => "revoked_at IS NULL AND expires_at>$now",
            BanStatus.Expired => "revoked_at IS NULL AND expires_at<=$now",
            BanStatus.Revoked => "revoked_at IS NOT NULL",
            _ => "1=1"
        });
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (query.SteamId.HasValue)
        {
            conditions.Add("steam_id=$steam");
            command.Parameters.AddWithValue("$steam", query.SteamId.Value.ToString(CultureInfo.InvariantCulture));
        }
        else if (!string.IsNullOrWhiteSpace(query.Text))
        {
            conditions.Add("instr(search_name,$text)>0");
            command.Parameters.AddWithValue("$text", query.Text.Trim().ToUpperInvariant());
        }
        if (!string.IsNullOrWhiteSpace(query.Admin))
        {
            conditions.Add("(admin_steam_id=$admin OR instr(search_admin,$admin)>0)");
            command.Parameters.AddWithValue("$admin", query.Admin.Trim().ToUpperInvariant());
        }
        if (query.From.HasValue)
        {
            conditions.Add("created_at >= $from");
            command.Parameters.AddWithValue("$from", query.From.Value.ToUnixTimeMilliseconds());
        }
        if (query.To.HasValue)
        {
            conditions.Add("created_at < $to");
            command.Parameters.AddWithValue("$to", query.To.Value.ToUnixTimeMilliseconds());
        }
        var where = string.Join(" AND ", conditions);
        command.CommandText = "SELECT COUNT(*) FROM bans WHERE " + where;
        var total = checked((int)(long)command.ExecuteScalar()!);
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Clamp(page, 0, Math.Max(0, (total - 1) / pageSize));
        command.CommandText = $"SELECT {Columns} FROM bans WHERE {where} ORDER BY created_at DESC,id LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", page * pageSize);
        var records = new List<BanRecord>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) records.Add(Read(reader));
        transaction.Commit();
        return new BanPage(records, total, page, pageSize);
    });

    private static void Insert(SqliteConnection db, SqliteTransaction transaction, BanRecord record)
    {
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO bans({Columns},search_name,search_admin) VALUES($id,$steam,$name,$ip,$reason,$admin,$adminName,$created,$expires,$revoked,$by,$revokeReason,$searchName,$searchAdmin)";
        var values = new (string, object?)[]
        {
            ("$id",record.Id.ToString()), ("$steam",record.SteamId.ToString(CultureInfo.InvariantCulture)),
            ("$name",record.PlayerName), ("$ip",record.IpAddress), ("$reason",record.Reason),
            ("$admin",record.AdminSteamId.ToString(CultureInfo.InvariantCulture)), ("$adminName",record.AdminName),
            ("$created",record.CreatedAt.ToUnixTimeMilliseconds()), ("$expires",record.ExpiresAt?.ToUnixTimeMilliseconds()),
            ("$revoked",record.RevokedAt?.ToUnixTimeMilliseconds()), ("$by",record.RevokedBy), ("$revokeReason",record.RevokeReason),
            ("$searchName",record.PlayerName.ToUpperInvariant()), ("$searchAdmin",record.AdminName.ToUpperInvariant())
        };
        foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static BanRecord Read(SqliteDataReader reader)
    {
        string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
        DateTimeOffset? Date(int i) => reader.IsDBNull(i) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(i));
        return new BanRecord
        {
            Id = Guid.Parse(reader.GetString(0)),
            SteamId = ulong.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
            PlayerName = reader.GetString(2),
            IpAddress = Text(3),
            Reason = reader.GetString(4),
            AdminSteamId = ulong.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            AdminName = reader.GetString(6),
            CreatedAt = Date(7)!.Value,
            ExpiresAt = Date(8),
            RevokedAt = Date(9),
            RevokedBy = Text(10),
            RevokeReason = Text(11)
        };
    }
}

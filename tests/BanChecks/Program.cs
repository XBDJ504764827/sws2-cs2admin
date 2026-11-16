using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Sws2Cs2Admin.Models;
using Sws2Cs2Admin.Services;

var checks = 0;
var directory = Path.Combine(Path.GetTempPath(), "sws2-bans-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var now = DateTimeOffset.UtcNow;
    BanRecord Ban(ulong steam, string name, DateTimeOffset? expiry = null) => new()
    {
        SteamId = steam,
        PlayerName = name,
        Reason = "test",
        CreatedAt = now.AddDays(-2),
        AdminSteamId = 42,
        AdminName = "管理员 Alice",
        ExpiresAt = expiry
    };
    var permanent = Ban(76561198000000001, "测试玩家 Alice");
    var temporary = Ban(76561198000000002, "temporary", now.AddDays(1));
    var expired = Ban(76561198000000003, "expired", now.AddDays(-1));
    var lifted = Ban(76561198000000004, "lifted");
    lifted.RevokedAt = now.AddHours(-1); lifted.RevokedBy = "Legacy admin"; lifted.RevokeReason = "legacy appeal";
    var legacy = Path.Combine(directory, "bans.json");
    File.WriteAllText(legacy, JsonSerializer.Serialize(new BanDocument { Bans = [permanent, temporary, expired, lifted] }));
    var original = File.ReadAllBytes(legacy);
    var store = new BanStore(directory, NullLogger.Instance);
    Check(File.Exists(Path.Combine(directory, "bans.db")), "SQLite database created");
    Check(File.ReadAllBytes(legacy).SequenceEqual(original) && File.ReadAllBytes(legacy + ".pre-sqlite.bak").SequenceEqual(original), "Original and backup preserved exactly");
    Check((await store.QueryAsync(new(Status: BanStatus.All))).Total == 4, "All legacy records imported");
    Check(store.FindActive(permanent.SteamId)?.Id == permanent.Id && store.FindActive(expired.SteamId) is null, "Join cache enforces permanent bans and excludes expired bans");
    Check((await store.QueryAsync(new(Status: BanStatus.Revoked))).Records.Single().RevokeReason == "legacy appeal", "Migration preserves previous unban attribution");
    store = new BanStore(directory, NullLogger.Instance);
    Check((await store.QueryAsync(new(Status: BanStatus.All))).Total == 4, "Restart cannot reimport legacy records");
    Check((await store.QueryAsync(new(Text: "alice"))).Records.Single().Id == permanent.Id, "Offline Unicode/case-insensitive name search");
    Check((await store.QueryAsync(new(SteamId: permanent.SteamId))).Records.Single().Id == permanent.Id, "SteamID exact lookup");
    Check((await store.QueryAsync(new(Text: "%' OR 1=1 --", Status: BanStatus.All))).Total == 0, "Search is parameterized and treats wildcard/query characters literally");
    Check((await store.QueryAsync(new(Admin: "ALICE", From: now.AddDays(-3), To: now.AddDays(-1)))).Total == 2, "Admin and UTC date filters compose with active state");
    Check((await store.QueryAsync(new(From: now.AddDays(-1)))).Total == 0, "Date range excludes older records");
    foreach (var (status, count) in new[] { (BanStatus.Active, 2), (BanStatus.Permanent, 1), (BanStatus.Temporary, 1), (BanStatus.Expired, 1), (BanStatus.Revoked, 1) })
        Check((await store.QueryAsync(new(Status: status))).Total == count, $"Correct {status} filter");
    var pages = await store.QueryAsync(new(Status: BanStatus.All), 999, 3);
    Check(pages.Page == 1 && pages.Records.Count == 1, "Out-of-range page clamps to last bounded page");
    Check(await store.RevokeAsync(permanent.SteamId, "Admin", "Appeal approved", Guid.NewGuid()) is null, "Stale confirmation cannot revoke a different ban");
    Check(await store.RevokeAsync(permanent.SteamId, "Admin", "Appeal approved", permanent.Id) is not null, "Permanent ban revoked after persistence");
    Check(store.FindActive(permanent.SteamId) is null, "Unban updates join cache");
    Check(await store.RevokeAsync(permanent.SteamId, "Other", "again") is null, "Repeated unban reports no active record");
    var changed = (await store.QueryAsync(new(Status: BanStatus.Revoked, SteamId: permanent.SteamId))).Records.Single();
    Check(changed.RevokedBy == "Admin" && changed.RevokeReason == "Appeal approved" && changed.RevokedAt.HasValue, "Unban preserves reason/admin/time and ban history");
    var replacement = Ban(permanent.SteamId, "renamed");
    Check(await store.AddAsync(replacement), "Can reban after a previous unban");
    Check(await store.RevokeAsync(permanent.SteamId, "Admin", "stale", permanent.Id) is null, "Old detail page cannot lift a newly issued ban for the same SteamID");
    var same = Enumerable.Range(0, 8).Select(_ => store.AddAsync(Ban(76561198000000005, "concurrent"))).ToArray();
    var results = await Task.WhenAll(same);
    Check(results.Count(value => value) == 1, "Concurrent ban requests create only one active ban");
    var many = Enumerable.Range(0, 120).Select(i => store.AddAsync(Ban((ulong)(900000 + i), "record-" + i))).ToArray();
    Check((await Task.WhenAll(many)).All(value => value), "Many independent writes persist");
    var resultPage = await store.QueryAsync(new(Status: BanStatus.All), 12, 3);
    Check(resultPage.Total == 126 && resultPage.Records.Count == 3, "Large history returns only a bounded database page");
    store = new BanStore(directory, NullLogger.Instance);
    Check(store.FindActive(replacement.SteamId)?.Id == replacement.Id, "Restart preserves the replacement active ban");
    var connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "bans.db"), Pooling = false }.ToString();
    using (var db = new SqliteConnection(connectionString))
    {
        db.Open();
        using var integrity = db.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check";
        Check((string)integrity.ExecuteScalar()! == "ok", "SQLite integrity check");
        using var fail = db.CreateCommand(); fail.CommandText = "CREATE TRIGGER reject_new BEFORE INSERT ON bans BEGIN SELECT RAISE(ABORT,'simulated disk failure'); END"; fail.ExecuteNonQuery();
    }
    try { await store.AddAsync(Ban(9999999, "failed")); throw new Exception("Expected a persistence exception"); }
    catch (SqliteException) { Check(store.FindActive(9999999) is null, "Failed persistence never changes enforcement cache or reports success"); }

    using (var db = new SqliteConnection(connectionString))
    {
        db.Open(); using var fail = db.CreateCommand();
        fail.CommandText = "CREATE TRIGGER reject_revoke BEFORE UPDATE ON bans BEGIN SELECT RAISE(ABORT,'simulated unban failure'); END";
        fail.ExecuteNonQuery();
    }
    try { await store.RevokeAsync(replacement.SteamId, "Admin", "failed"); throw new Exception("Expected an unban persistence exception"); }
    catch (SqliteException) { Check(store.FindActive(replacement.SteamId)?.Id == replacement.Id, "Failed unban keeps enforcement active"); }

    // A failed migration is rolled back and remains retryable with the preserved backup.
    var failedDir = Path.Combine(directory, "failed-migration"); Directory.CreateDirectory(failedDir);
    var duplicate = Ban(42, "duplicate");
    var failedJson = Path.Combine(failedDir, "bans.json");
    File.WriteAllText(failedJson, JsonSerializer.Serialize(new BanDocument { Bans = [duplicate, duplicate] }));
    try { _ = new BanStore(failedDir, NullLogger.Instance); throw new Exception("Duplicate migration should fail"); }
    catch (SqliteException) { Check(File.Exists(failedJson + ".pre-sqlite.bak"), "Failed import retains original backup"); }
    using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(failedDir, "bans.db"), Pooling = false }.ToString()))
    {
        db.Open(); using var count = db.CreateCommand(); count.CommandText = "SELECT count(*) FROM bans";
        Check((long)count.ExecuteScalar()! == 0, "Failed import rolls back all rows");
    }
    var corruptDir = Path.Combine(directory, "corrupt"); Directory.CreateDirectory(corruptDir);
    File.WriteAllText(Path.Combine(corruptDir, "bans.json"), "{malformed");
    try { _ = new BanStore(corruptDir, NullLogger.Instance); throw new Exception("Corrupt JSON should fail"); }
    catch (JsonException) { Check(File.ReadAllText(Path.Combine(corruptDir, "bans.json")) == "{malformed", "Corrupt legacy file fails startup instead of silently losing bans"); }
    var absentDir = Path.Combine(directory, "missing-array"); Directory.CreateDirectory(absentDir);
    File.WriteAllText(Path.Combine(absentDir, "bans.json"), "{}");
    try { _ = new BanStore(absentDir, NullLogger.Instance); throw new Exception("Missing array should fail"); }
    catch (InvalidDataException) { Check(true, "Missing Bans array cannot silently import zero records"); }
    var interruptedDir = Path.Combine(directory, "interrupted"); Directory.CreateDirectory(interruptedDir);
    var interruptedJson = Path.Combine(interruptedDir, "bans.json");
    File.WriteAllText(interruptedJson, JsonSerializer.Serialize(new BanDocument { Bans = [permanent] }));
    File.Copy(interruptedJson, interruptedJson + ".pre-sqlite.bak");
    var resumed = new BanStore(interruptedDir, NullLogger.Instance);
    Check(resumed.FindActive(permanent.SteamId)?.Id == permanent.Id, "Migration resumes safely with an identical preexisting backup");
    var changedDir = Path.Combine(directory, "changed-backup"); Directory.CreateDirectory(changedDir);
    File.Copy(interruptedJson, Path.Combine(changedDir, "bans.json"));
    File.WriteAllText(Path.Combine(changedDir, "bans.json.pre-sqlite.bak"), "different original");
    try { _ = new BanStore(changedDir, NullLogger.Instance); throw new Exception("Changed backup should fail"); }
    catch (InvalidDataException) { Check(true, "Interrupted migration never overwrites a conflicting backup"); }
    Console.WriteLine($"Passed {checks} SQLite ban regression checks.");
}
finally { Directory.Delete(directory, recursive: true); }

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++; Console.WriteLine("PASS: " + message);
}

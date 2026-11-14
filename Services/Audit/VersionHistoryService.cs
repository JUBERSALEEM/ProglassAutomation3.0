// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Audit/VersionHistoryService.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — entity version history (§24)
//
// Append-only version chain per record (EntityType, EntityId). Each
// save stores the document snapshot + who/when/session, so previous
// versions can be listed and restored.
//
// Restoring is itself audited — history is never silently erased.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Audit
{
    /// <summary>
    /// Shared round-trip parser for the ISO-8601 ("o") timestamps this
    /// module writes into audit.db.
    ///
    /// AUDIT T-1 — WHY THIS EXISTS
    /// ───────────────────────────
    /// Values are written with DateTime.UtcNow.ToString("o") and were read
    /// back with a bare DateTime.Parse(). That overload uses the CURRENT
    /// CULTURE and returns a DateTime whose Kind is Local, not Utc, so the
    /// clock reading was reinterpreted as if it had been local time all
    /// along. Every consumer then calls .ToLocalTime() on it
    /// (SectionActivityViewModel, SystemCenterViewModel, VersionsDialog,
    /// ActivityPanel), which shifts it a SECOND time.
    ///
    /// The visible result is version history and record metadata stamped
    /// hours away from when the change actually happened — one UTC offset
    /// for every read, in the wrong direction. On UTC+4 (this app's home
    /// market) a save at 09:00 displays as 17:00. For an audit trail that
    /// exists precisely to answer "who changed this, and when", a silently
    /// wrong timestamp is worse than no timestamp.
    ///
    /// Parsing with RoundtripKind honours the trailing "Z" and yields a
    /// DateTime with Kind = Utc, so the single .ToLocalTime() the UI
    /// already performs is correct. InvariantCulture removes the
    /// dependency on the operator's regional settings.
    /// </summary>
    internal static class AuditTimestamp
    {
        /// <summary>
        /// Parses a stored ISO-8601 timestamp as UTC. Unparseable or
        /// legacy values fall back to a UTC-kinded best effort rather than
        /// throwing, so one bad row cannot blank an entire history list.
        /// </summary>
        internal static DateTime ParseUtc(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DateTime.MinValue;

            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind |
                    DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed.Kind == DateTimeKind.Utc
                    ? parsed
                    : DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }

            return DateTime.MinValue;
        }
    }

    /// <summary>One stored version of a record.</summary>
    public class RecordVersion
    {
        public int Id { get; set; }
        public string EntityType { get; set; } = "";
        public string EntityId { get; set; } = "";
        public int VersionNo { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string Username { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string Summary { get; set; } = "";
        public string SnapshotJson { get; set; } = "";
    }

    /// <summary>Append-only version store (audit.db → RecordVersions).</summary>
    public static class VersionHistoryService
    {
        /// <summary>Stores a new version; version number auto-increments.</summary>
        public static async Task<int> AddVersionAsync(
            string entityType, string entityId,
            string summary, string snapshotJson)
        {
            try
            {
                using var connection = AuditDbMigrations.Open();

                int next = 1;
                using (var max = connection.CreateCommand())
                {
                    max.CommandText = @"
                        SELECT COALESCE(MAX(VersionNo), 0)
                        FROM RecordVersions
                        WHERE EntityType = $type AND EntityId = $id;";
                    max.Parameters.AddWithValue("$type", entityType);
                    max.Parameters.AddWithValue("$id", entityId);

                    var result = await max.ExecuteScalarAsync();
                    next = Convert.ToInt32(result) + 1;
                }

                using var insert = connection.CreateCommand();
                insert.CommandText = @"
                    INSERT INTO RecordVersions
                        (EntityType, EntityId, VersionNo, TimestampUtc,
                         Username, SessionId, Summary, SnapshotJson)
                    VALUES
                        ($type, $id, $ver, $now, $user, $session,
                         $summary, $snap);";

                insert.Parameters.AddWithValue("$type", entityType);
                insert.Parameters.AddWithValue("$id", entityId);
                insert.Parameters.AddWithValue("$ver", next);
                insert.Parameters.AddWithValue(
                    "$now", DateTime.UtcNow.ToString("o"));
                insert.Parameters.AddWithValue(
                    "$user", AuditService.CurrentUsername);
                insert.Parameters.AddWithValue(
                    "$session", AuditService.SessionId);
                insert.Parameters.AddWithValue("$summary", summary ?? "");
                insert.Parameters.AddWithValue(
                    "$snap", snapshotJson ?? "");

                await insert.ExecuteNonQueryAsync();
                return next;
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    $"[VersionHistory] Add failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>All versions of a record, newest first.</summary>
        public static async Task<List<RecordVersion>> GetVersionsAsync(
            string entityType, string entityId,
            bool includeSnapshots = false)
        {
            var list = new List<RecordVersion>();

            try
            {
                using var connection = AuditDbMigrations.Open();
                using var command = connection.CreateCommand();

                command.CommandText = includeSnapshots
                    ? @"SELECT Id, EntityType, EntityId, VersionNo,
                               TimestampUtc, Username, SessionId,
                               Summary, SnapshotJson
                        FROM RecordVersions
                        WHERE EntityType = $type AND EntityId = $id
                        ORDER BY VersionNo DESC;"
                    : @"SELECT Id, EntityType, EntityId, VersionNo,
                               TimestampUtc, Username, SessionId,
                               Summary, ''
                        FROM RecordVersions
                        WHERE EntityType = $type AND EntityId = $id
                        ORDER BY VersionNo DESC;";

                command.Parameters.AddWithValue("$type", entityType);
                command.Parameters.AddWithValue("$id", entityId);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new RecordVersion
                    {
                        Id = reader.GetInt32(0),
                        EntityType = reader.GetString(1),
                        EntityId = reader.GetString(2),
                        VersionNo = reader.GetInt32(3),
                        TimestampUtc = AuditTimestamp.ParseUtc(reader.GetString(4)),
                        Username = reader.GetString(5),
                        SessionId = reader.GetString(6),
                        Summary = reader.GetString(7),
                        SnapshotJson = reader.GetString(8)
                    });
                }
            }
            catch (Exception ex) { AppLogService.Warning($"Suppressed exception: {ex.Message}"); }

            return list;
        }

        /// <summary>Loads one version including its snapshot.</summary>
        public static async Task<RecordVersion?> GetByIdAsync(int versionId)
        {
            try
            {
                using var connection = AuditDbMigrations.Open();
                using var command = connection.CreateCommand();

                command.CommandText = @"
                    SELECT Id, EntityType, EntityId, VersionNo,
                           TimestampUtc, Username, SessionId,
                           Summary, SnapshotJson
                    FROM RecordVersions WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", versionId);

                using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return null;

                return new RecordVersion
                {
                    Id = reader.GetInt32(0),
                    EntityType = reader.GetString(1),
                    EntityId = reader.GetString(2),
                    VersionNo = reader.GetInt32(3),
                    TimestampUtc = AuditTimestamp.ParseUtc(reader.GetString(4)),
                    Username = reader.GetString(5),
                    SessionId = reader.GetString(6),
                    Summary = reader.GetString(7),
                    SnapshotJson = reader.GetString(8)
                };
            }
            catch
            {
                return null;
            }
        }
    }
}

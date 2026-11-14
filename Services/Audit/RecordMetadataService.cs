// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Audit/RecordMetadataService.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — automatic created/updated record metadata (§16)
//
// Stores WHO created/updated each business record and in which session
// WITHOUT adding columns to the nine existing business databases (that
// would risk user data — §41). Metadata lives centrally in audit.db,
// keyed by (EntityType, EntityId).
//
// Rules enforced here:
//   • StampCreated sets the created block ONCE — later edits never
//     change it.
//   • StampUpdated only replaces the updated block + bumps the counter.
//   • Users never type these values; they come from the session.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using System;
using System.Threading.Tasks;
using ProGlassAutomation.Services.Logging;

namespace ProGlassAutomation.Services.Audit
{
    /// <summary>Created/updated metadata for any business record.</summary>
    public class RecordMetadata
    {
        public string EntityType { get; set; } = "";
        public string EntityId { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public string CreatedByUser { get; set; } = "";
        public string CreatedSession { get; set; } = "";
        public DateTime UpdatedAtUtc { get; set; }
        public string UpdatedByUser { get; set; } = "";
        public string UpdatedSession { get; set; } = "";
        public int UpdateCount { get; set; }
    }

    /// <summary>Central metadata store (audit.db → RecordMetadata).</summary>
    public static class RecordMetadataService
    {
        /// <summary>Records creation. Ignored when metadata already exists.</summary>
        public static async Task StampCreatedAsync(
            string entityType, string entityId)
        {
            try
            {
                using var connection = AuditDbMigrations.Open();
                using var command = connection.CreateCommand();

                command.CommandText = @"
                    INSERT OR IGNORE INTO RecordMetadata
                        (EntityType, EntityId, CreatedAtUtc, CreatedByUser,
                         CreatedSession, UpdatedAtUtc, UpdatedByUser,
                         UpdatedSession, UpdateCount)
                    VALUES
                        ($type, $id, $now, $user, $session,
                         $now, $user, $session, 0);";

                var now = DateTime.UtcNow.ToString("o");
                command.Parameters.AddWithValue("$type", entityType);
                command.Parameters.AddWithValue("$id", entityId);
                command.Parameters.AddWithValue("$now", now);
                command.Parameters.AddWithValue(
                    "$user", AuditService.CurrentUsername);
                command.Parameters.AddWithValue(
                    "$session", AuditService.SessionId);

                await command.ExecuteNonQueryAsync();
            }
            catch { /* metadata must never break the business flow */ }
        }

        /// <summary>Replaces the updated block; never touches the created block.</summary>
        public static async Task StampUpdatedAsync(
            string entityType, string entityId)
        {
            try
            {
                using var connection = AuditDbMigrations.Open();
                using var command = connection.CreateCommand();

                command.CommandText = @"
                    UPDATE RecordMetadata
                    SET UpdatedAtUtc   = $now,
                        UpdatedByUser  = $user,
                        UpdatedSession = $session,
                        UpdateCount    = UpdateCount + 1
                    WHERE EntityType = $type AND EntityId = $id;";

                command.Parameters.AddWithValue(
                    "$now", DateTime.UtcNow.ToString("o"));
                command.Parameters.AddWithValue(
                    "$user", AuditService.CurrentUsername);
                command.Parameters.AddWithValue(
                    "$session", AuditService.SessionId);
                command.Parameters.AddWithValue("$type", entityType);
                command.Parameters.AddWithValue("$id", entityId);

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                // S-5: was a silent catch. A swallowed failure here
                // meant the created/updated-by trail silently stopped recording.
                AppLogService.Warning(
                    $"StampUpdated failed: {ex.Message}",
                    new LogContext
                    {
                        Module = "Audit",
                        Operation = "StampUpdated"
                    });
            }
        }

        /// <summary>Reads metadata for one record (null when absent).</summary>
        public static async Task<RecordMetadata?> GetAsync(
            string entityType, string entityId)
        {
            try
            {
                using var connection = AuditDbMigrations.Open();
                using var command = connection.CreateCommand();

                command.CommandText = @"
                    SELECT EntityType, EntityId, CreatedAtUtc, CreatedByUser,
                           CreatedSession, UpdatedAtUtc, UpdatedByUser,
                           UpdatedSession, UpdateCount
                    FROM RecordMetadata
                    WHERE EntityType = $type AND EntityId = $id;";

                command.Parameters.AddWithValue("$type", entityType);
                command.Parameters.AddWithValue("$id", entityId);

                using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return null;

                return new RecordMetadata
                {
                    EntityType = reader.GetString(0),
                    EntityId = reader.GetString(1),
                    // T-1: parsed as UTC, not current-culture Local. See
                    // AuditTimestamp for why a bare DateTime.Parse here
                    // double-shifted every "created/updated by" timestamp.
                    CreatedAtUtc = AuditTimestamp.ParseUtc(reader.GetString(2)),
                    CreatedByUser = reader.GetString(3),
                    CreatedSession = reader.GetString(4),
                    UpdatedAtUtc = AuditTimestamp.ParseUtc(reader.GetString(5)),
                    UpdatedByUser = reader.GetString(6),
                    UpdatedSession = reader.GetString(7),
                    UpdateCount = reader.GetInt32(8)
                };
            }
            catch
            {
                return null;
            }
        }
    }
}

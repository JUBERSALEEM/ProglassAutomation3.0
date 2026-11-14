// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/OptimizationSessionRepository.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — session persistence (optimization.db)
//
// Thin CRUD over OptimizationDbContext so saved optimization sessions
// (pieces + Step 4 stock pool + margins + run metadata) can be saved,
// listed, reopened and deleted. Mirrors the InquiryRepository house
// pattern: one short-lived DbContext per operation, no tracking on reads.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Optimization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Optimization
{
    public sealed class OptimizationSessionRepository
    {
        /// <summary>Creates the table on first use (idempotent).</summary>
        public async Task EnsureCreatedAsync(CancellationToken ct = default)
        {
            await using var db = new OptimizationDbContext();
            await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The reserved latest-run row for one document, or null when that
        /// document has never had a run persisted. An empty key addresses the
        /// legacy global row.
        /// </summary>
        public async Task<SavedOptimizationSession?> GetLatestRunAsync(
            string? documentKey, CancellationToken ct = default)
        {
            // PHASE 3 / finding 2: canonicalised, so a run stamped with a job
            // order id and a lookup by proforma id address the SAME reserved row
            // instead of two rows that can never find each other.
            string id = SavedOptimizationSession.LatestRunIdFor(
                OptimizationDocumentKey.Canonicalize(documentKey));

            await using var db = new OptimizationDbContext();
            return await db.Sessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, ct)
                .ConfigureAwait(false);
        }

        public async Task<List<SavedOptimizationSession>> ListAsync(CancellationToken ct = default)
        {
            await using var db = new OptimizationDbContext();
            return await db.Sessions
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedUtc)
                .Take(100)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        public async Task<SavedOptimizationSession?> GetAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            await using var db = new OptimizationDbContext();
            return await db.Sessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, ct)
                .ConfigureAwait(false);
        }

        public async Task InsertAsync(SavedOptimizationSession session, CancellationToken ct = default)
        {
            await using var db = new OptimizationDbContext();
            db.Sessions.Add(session);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes the latest run into the one reserved row, replacing whatever was
        /// there. Insert-or-update rather than insert: the row has a fixed id, so
        /// a second run would otherwise collide on the primary key.
        /// </summary>
        public async Task UpsertLatestRunAsync(
            SavedOptimizationSession session, string? documentKey = null,
            CancellationToken ct = default)
        {
            if (session == null) return;

            // Stamped with the document the run was produced for, so a quote
            // reopened later reads its OWN plan and not whichever run happened
            // to be newest globally.
            session.Id = SavedOptimizationSession.LatestRunIdFor(
                OptimizationDocumentKey.Canonicalize(documentKey));

            // PHASE 7 / finding 15 (2026-09-20): this was a read-then-write with
            // no transaction. Two runs finishing near-simultaneously — the Step 5
            // headless host and the module, or a double-clicked RUN — both read
            // existing == null, both called Add with the SAME primary key, and
            // the second threw DbUpdateException. WAL and busy_timeout (applied
            // by SqliteResilience) handle lock CONTENTION, not this logical race.
            // The caller logged the failure at Debug, so the lost save was
            // invisible: the operator's run simply was not there next time.
            //
            // Now wrapped in a transaction with a bounded retry. A retry is the
            // correct response to a PK collision because the next attempt reads
            // the row the other writer just committed and updates it instead of
            // inserting.
            const int maxAttempts = 3;

            for (int attempt = 1; ; attempt++)
            {
                await using var db = new OptimizationDbContext();
                try
                {
                    await using var tx = await db.Database
                        .BeginTransactionAsync(ct).ConfigureAwait(false);

                    var existing = await db.Sessions
                        .FirstOrDefaultAsync(s => s.Id == session.Id, ct)
                        .ConfigureAwait(false);

                    if (existing == null)
                        db.Sessions.Add(session);
                    else
                        db.Entry(existing).CurrentValues.SetValues(session);

                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                    return;
                }
                catch (DbUpdateException) when (attempt < maxAttempts && !ct.IsCancellationRequested)
                {
                    await Task.Delay(50 * attempt, ct).ConfigureAwait(false);
                }
            }
        }

        // ── PHASE 6 / finding 14: retention ──────────────────────────────
        //
        // Every document that ever ran an optimization left a permanent reserved
        // row here, each carrying full PiecesJson, StocksJson, ResultJson and
        // MarginsJson, and ListAsync only ever shows the newest 100 — so the
        // accumulation was both unbounded and invisible. Phase 3 made it worse
        // before it made it better: a quote that ran from both Step 5 and the
        // JOBS tab had been writing TWO rows under the PI- and JOB- spellings.
        //
        // Pruning touches RESERVED LATEST-RUN ROWS ONLY. A named session is a
        // deliberate act by an operator pressing SAVE and is never removed here;
        // SavedOptimizationSession.IsReservedLatestRunId is the same predicate
        // that keeps those rows out of the Saved Sessions list, so the two views
        // of "which rows are plumbing" cannot disagree.

        /// <summary>
        /// Default age after which a reserved latest-run row is pruned. Half a
        /// year: long enough that reopening any live quote still finds its plan,
        /// short enough that the file stops growing forever.
        /// </summary>
        public const int DefaultLatestRunRetentionDays = 180;

        /// <summary>
        /// Deletes reserved latest-run rows older than <paramref name="olderThanDays"/>.
        /// Returns how many were removed. Never touches a named session.
        /// </summary>
        public async Task<int> PruneStaleLatestRunsAsync(
            int olderThanDays = DefaultLatestRunRetentionDays, CancellationToken ct = default)
        {
            if (olderThanDays <= 0) return 0;

            var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);

            await using var db = new OptimizationDbContext();

            // Candidate set is read first and filtered in memory by the reserved
            // predicate, because that predicate is a C# string test and cannot be
            // translated to SQL. Reserved rows are a small minority of the table,
            // so this reads the id/updated pairs only.
            var candidates = await db.Sessions
                .AsNoTracking()
                .Where(s => s.UpdatedUtc < cutoff)
                .Select(s => new { s.Id, s.UpdatedUtc })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var stale = candidates
                .Where(c => SavedOptimizationSession.IsReservedLatestRunId(c.Id))
                .Select(c => c.Id)
                .ToList();

            if (stale.Count == 0) return 0;

            var rows = await db.Sessions
                .Where(s => stale.Contains(s.Id))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            db.Sessions.RemoveRange(rows);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return rows.Count;
        }

        // ── PHASE 7 / finding 7c: EnsureCreated creates but never migrates ──
        //
        // EnsureCreatedAsync is called on every persist and every show. It
        // creates the table if absent and otherwise does NOTHING — so if
        // SavedOptimizationSession ever gains a column, an existing
        // optimization.db keeps the old schema and every insert fails. Combined
        // with the Debug-level catch in PersistLatestRunAsync that was a
        // permanently silent failure. This probe costs one PRAGMA per call and
        // turns it into a named, actionable warning.

        /// <summary>Columns the current entity model expects on the Sessions table.</summary>
        private static readonly string[] ExpectedColumns =
        {
            "Id", "Name", "JobOrderRefs", "EngineStage", "EffortLevel", "RotationEnabled",
            "PieceCount", "SheetsUsed", "UnplacedCount", "UtilizationPct",
            "MarginsJson", "StocksJson", "PiecesJson", "ResultJson", "CreatedUtc", "UpdatedUtc"
        };

        /// <summary>
        /// Returns the columns the stored Sessions table is MISSING relative to
        /// the current model, or empty when the schema matches. Reads
        /// <c>PRAGMA table_info</c> directly rather than going through EF, so it
        /// reports drift even when EF would simply fail.
        /// </summary>
        public async Task<List<string>> DetectSchemaDriftAsync(CancellationToken ct = default)
        {
            var missing = new List<string>();

            try
            {
                await using var db = new OptimizationDbContext();
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync(ct).ConfigureAwait(false);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(Sessions);";

                var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        // PRAGMA table_info columns: cid, name, type, notnull, dflt_value, pk
                        if (reader.FieldCount > 1 && !reader.IsDBNull(1))
                            present.Add(reader.GetString(1));
                    }
                }

                if (present.Count == 0) return missing;   // no table yet — EnsureCreated will make it

                foreach (var expected in ExpectedColumns)
                    if (!present.Contains(expected))
                        missing.Add(expected);
            }
            catch
            {
                // A probe that cannot run must not break the operation it was
                // protecting. Absence of evidence is not evidence of drift.
            }

            return missing;
        }

        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            await using var db = new OptimizationDbContext();
            var row = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
            if (row == null) return;
            db.Sessions.Remove(row);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.b.4.ProjectRepository.cs
// MODULE: step5 — project record persistence.
//
// Behaviour
//   • InitializeAsync asks the established ERP initializer to bring the shared
//     inquiries.db to the current schema (which now includes migration 1017 →
//     Projects and the two inquiry link columns). One chain, one runner, one
//     EnsureCreated owner; no inline DDL from this file.
//   • Every write normalizes through ProjectRules, then stamps UTC timestamps
//     and the operator name from the module that owns operator identity.
//   • Writes go through SqliteResilience so a transient SQLITE_BUSY is retried
//     rather than surfaced to the operator as a failure.
//   • Failures return an operator-facing message; the UI never sees a raw
//     exception for an ordinary save failure.
//   • Nothing here deletes a project, and nothing here touches an inquiry's own
//     columns. The link half lives in step5.b.5.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Diagnostics;
using ProGlassAutomation.Models.Projects;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Inquiry;

namespace ProGlassAutomation.Services.Projects
{
    /// <summary>EF Core implementation of <see cref="IProjectRepository"/>.</summary>
    public sealed partial class ProjectRepository : IProjectRepository
    {
        private readonly Func<ProjectDbContext> _createContext;
        private bool _initialized;

        public ProjectRepository() : this(null) { }

        /// <summary>
        /// Test seam: an alternate context factory. Production callers use the
        /// parameterless constructor; the verification executable points the same
        /// code at an isolated file through AppDataRoot.TestFolderOverride.
        /// </summary>
        internal ProjectRepository(Func<ProjectDbContext>? contextFactory)
        {
            _createContext = contextFactory ?? (() => new ProjectDbContext());
        }

        private ProjectDbContext CreateContext() => _createContext();

        // ── Initialization ───────────────────────────────────────────────

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (_initialized) return;

            // The ERP file is owned by the Inquiry initializer: it creates a fresh
            // file and applies the single version chain (which now includes 1017).
            // Running it here means Projects can never be created before the base
            // tables exist, which would make EF's "database is empty" check skip
            // the Inquiry schema.
            await new InquiryRepository().InitializeAsync(ct).ConfigureAwait(false);

            if (!await IsSchemaReadyAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException(
                    "The project table (migration 1017) is missing after initialization. "
                    + "Schema upgrade, if any, reported: "
                    + (string.IsNullOrWhiteSpace(SchemaMigrationRunner.FailureSummary)
                        ? "no failure recorded."
                        : SchemaMigrationRunner.FailureSummary));

            _initialized = true;
        }

        public async Task<bool> IsSchemaReadyAsync(CancellationToken ct = default)
        {
            try
            {
                await using var db = CreateContext();
                var connection = db.Database.GetDbConnection();
                await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
                if (connection.State != System.Data.ConnectionState.Open)
                    await connection.OpenAsync(ct).ConfigureAwait(false);

                foreach (string column in new[]
                         {
                             ProjectSchema.InquiryClientColumn,
                             ProjectSchema.InquiryProjectColumn
                         })
                {
                    using var check = connection.CreateCommand();
                    check.CommandText =
                        "SELECT COUNT(*) FROM pragma_table_info('" + ProjectSchema.InquiriesTable
                        + "') WHERE name = $name";
                    check.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$name", column));
                    object? found = check.ExecuteScalar();
                    if (found == null || Convert.ToInt64(found) == 0) return false;
                }

                using var table = connection.CreateCommand();
                table.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
                table.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$name", ProjectSchema.TableName));
                object? exists = table.ExecuteScalar();
                return exists != null && Convert.ToInt64(exists) > 0;
            }
            catch (Exception error) when (error is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
            {
                AppLog.Error("step5.projects.schema-probe", error, "the project schema could not be probed");
                return false;
            }
        }

        // ── Writes ───────────────────────────────────────────────────────

        public async Task<ProjectSaveResult> CreateAsync(ProjectDraft draft, CancellationToken ct = default)
        {
            if (draft == null) return ProjectSaveResult.Fail("No project data was supplied.");

            // A blank number means "allocate one". The editor shows the suggestion,
            // but the allocation is decided here, at the moment of the write, so
            // two open editors cannot both be handed the same number by a stale
            // form; the UNIQUE index refuses the loser and the message says so.
            ProjectDraft working = draft;
            if (string.IsNullOrWhiteSpace(working.ProjectNumber))
            {
                working = new ProjectDraft
                {
                    ProjectNumber = await SuggestNumberAsync(ct).ConfigureAwait(false),
                    ProjectName = draft.ProjectName,
                    ClientId = draft.ClientId,
                    ClientName = draft.ClientName,
                    Location = draft.Location,
                    Description = draft.Description,
                    Status = draft.Status
                };
            }

            var errors = ProjectRules.Validate(working);
            if (errors.Count > 0) return ProjectSaveResult.Fail(string.Join(" ", errors.Values));

            ProjectDraft normalized = ProjectRules.Normalize(working);
            string? clientProblem = await ValidateClientAsync(normalized.ClientId, ct).ConfigureAwait(false);
            if (clientProblem != null) return ProjectSaveResult.Fail(clientProblem);

            var now = DateTime.UtcNow;
            string actor = CurrentOperator.DisplayName;

            var project = new Project
            {
                ProjectNumber = normalized.ProjectNumber,
                ProjectName = normalized.ProjectName,
                ClientId = normalized.ClientId,
                ClientName = normalized.ClientName,
                Location = normalized.Location,
                Description = normalized.Description,
                Status = normalized.Status,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = actor,
                UpdatedBy = actor
            };

            try
            {
                await using var db = CreateContext();
                db.Projects.Add(project);
                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                return ProjectSaveResult.Ok(project);
            }
            catch (Exception error)
            {
                return ProjectSaveResult.Fail(DescribeWriteFailure("The project could not be saved", error));
            }
        }

        public async Task<ProjectSaveResult> UpdateAsync(
            int id, ProjectDraft draft, CancellationToken ct = default)
        {
            if (id <= 0) return ProjectSaveResult.Fail("No project was selected.");
            if (draft == null) return ProjectSaveResult.Fail("No project data was supplied.");

            var errors = ProjectRules.Validate(draft);
            if (errors.Count > 0) return ProjectSaveResult.Fail(string.Join(" ", errors.Values));

            ProjectDraft normalized = ProjectRules.Normalize(draft);
            string? clientProblem = await ValidateClientAsync(normalized.ClientId, ct).ConfigureAwait(false);
            if (clientProblem != null) return ProjectSaveResult.Fail(clientProblem);

            try
            {
                await using var db = CreateContext();
                Project? project = await db.Projects.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
                if (project == null)
                    return ProjectSaveResult.Fail(
                        "That project no longer exists in the register. Refresh the list and try again.");

                // The number is editable only while the project is still current:
                // a closed project's number is a historical reference, and renaming
                // it later would break every document that quotes it.
                if (!string.IsNullOrWhiteSpace(normalized.ProjectNumber)
                    && !string.Equals(normalized.ProjectNumber, project.ProjectNumber, StringComparison.Ordinal))
                {
                    if (project.IsClosed)
                        return ProjectSaveResult.Fail(
                            "A closed project keeps its number: it is the reference other records quote.");
                    project.ProjectNumber = normalized.ProjectNumber;
                }

                project.ProjectName = normalized.ProjectName;
                project.ClientId = normalized.ClientId;
                project.ClientName = normalized.ClientName;
                project.Location = normalized.Location;
                project.Description = normalized.Description;

                project.UpdatedAt = DateTime.UtcNow;
                project.UpdatedBy = CurrentOperator.DisplayName;

                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                return ProjectSaveResult.Ok(project);
            }
            catch (Exception error)
            {
                return ProjectSaveResult.Fail(DescribeWriteFailure("The project could not be saved", error));
            }
        }

        public async Task<ProjectSaveResult> SetStatusAsync(
            int id, string status, CancellationToken ct = default)
        {
            if (id <= 0) return ProjectSaveResult.Fail("No project was selected.");
            if (!ProjectStatus.IsDefined(status))
                return ProjectSaveResult.Fail(
                    $"The status must be {ProjectStatus.Active} or {ProjectStatus.Closed}.");

            try
            {
                await using var db = CreateContext();
                Project? project = await db.Projects.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
                if (project == null) return ProjectSaveResult.Fail("That project no longer exists.");

                project.Status = ProjectStatus.Normalize(status);
                project.UpdatedAt = DateTime.UtcNow;
                project.UpdatedBy = CurrentOperator.DisplayName;

                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                return ProjectSaveResult.Ok(project);
            }
            catch (Exception error)
            {
                return ProjectSaveResult.Fail(DescribeWriteFailure("The project status could not be changed", error));
            }
        }

        // ── Shared ───────────────────────────────────────────────────────

        /// <summary>
        /// A client reference must be a real client. Null is allowed — a project
        /// may belong to a one-off customer — but an id that names nothing is a
        /// bug in the caller, not something to store.
        /// </summary>
        private async Task<string?> ValidateClientAsync(int? clientId, CancellationToken ct)
        {
            if (clientId == null) return null;
            if (clientId <= 0) return "The selected client is not valid.";

            await using var db = CreateContext();
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT COUNT(*) FROM ""Clients"" WHERE ""Id"" = $id;";
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$id", clientId.Value));
            object? found = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return found != null && Convert.ToInt64(found) > 0
                ? null
                : "The selected client is no longer in the directory. Choose another, or leave the client empty.";
        }

        internal static DateTime ParseStamp(string? text) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
                ? parsed
                : default;

        /// <summary>
        /// Turns a write failure into something an operator can act on, and logs
        /// the exception itself. A duplicate number is the one failure that is
        /// expected in normal use, so it gets a specific sentence.
        /// </summary>
        internal static string DescribeWriteFailure(string prefix, Exception error)
        {
            AppLog.Error("step5.projects.write-failure", error, prefix);
            string message = error.GetBaseException().Message;
            if (message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                && message.Contains("ProjectNumber", StringComparison.OrdinalIgnoreCase))
            {
                return "Another project already uses that number. Accept the suggested number, or use a "
                       + "different one.";
            }
            if (message.Contains("CHECK", StringComparison.OrdinalIgnoreCase))
                return prefix + ": the status is not one this database accepts.";
            return prefix + ". " + message;
        }
    }
}

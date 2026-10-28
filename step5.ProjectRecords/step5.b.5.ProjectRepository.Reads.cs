// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.b.5.ProjectRepository.Reads.cs
// MODULE: step5 — project record reads.
//
// Split from step5.b.4 so no file carries more than one job: that file owns
// initialization and the writes, this owns the four SELECTs the register and
// the editor need.
//
// The register's read is one statement: the linked-inquiry count is a scalar
// subquery, so a project with no inquiries still appears — a LEFT JOIN written
// as an inner join would silently drop exactly the projects an operator creates
// first.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Projects;
using ProGlassAutomation.Services.Database;

namespace ProGlassAutomation.Services.Projects
{
    public sealed partial class ProjectRepository
    {
        // ── Reads ────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<ProjectListRow>> QueryAsync(
            ProjectQuery query, CancellationToken ct = default)
        {
            query ??= new ProjectQuery();

            await using var db = CreateContext();
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

            // The linked-inquiry count is a subquery, so the register shows every
            // project whether or not it has inquiries yet. A LEFT JOIN with a
            // GROUP BY would also work and would drop the empty projects if it
            // were ever mistyped as an inner join; this shape cannot.
            var sql = @"SELECT p.""Id"", p.""ProjectNumber"", p.""ProjectName"", p.""ClientName"",
                               p.""Location"", p.""Status"", p.""UpdatedAt"",
                               (SELECT COUNT(*) FROM ""Inquiries"" i
                                  WHERE i.""" + ProjectSchema.InquiryProjectColumn + @""" = p.""Id"") AS ""Linked""
                        FROM ""Projects"" p
                        WHERE ($search = '' OR
                               p.""ProjectNumber"" LIKE $like OR
                               p.""ProjectName""   LIKE $like OR
                               p.""ClientName""    LIKE $like OR
                               p.""Location""      LIKE $like)
                          AND ($includeClosed = 1 OR p.""Status"" = 'Active')
                          AND ($clientId IS NULL OR p.""ClientId"" = $clientId)
                        ORDER BY CASE p.""Status"" WHEN 'Active' THEN 0 ELSE 1 END,
                                 p.""ProjectNumber""
                        LIMIT $maxRows;";

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            string search = (query.Search ?? "").Trim();
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$search", search));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$like", "%" + search + "%"));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$includeClosed", query.IncludeClosed ? 1 : 0));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter(
                "$clientId", (object?)query.ScopeToClientId ?? DBNull.Value));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$maxRows", query.MaxRows < 1 ? 1 : query.MaxRows));

            var rows = new List<ProjectListRow>();
            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(new ProjectListRow
                {
                    Id = reader.GetInt32(0),
                    ProjectNumber = reader.GetString(1),
                    ProjectName = reader.GetString(2),
                    ClientName = reader.GetString(3),
                    Location = reader.GetString(4),
                    Status = reader.GetString(5),
                    UpdatedAt = ParseStamp(reader.GetString(6)),
                    LinkedInquiries = reader.GetInt32(7)
                });
            }
            return rows;
        }

        public async Task<Project?> GetAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0) return null;
            await using var db = CreateContext();
            return await db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<ProjectClientChoice>> GetClientChoicesAsync(
            CancellationToken ct = default)
        {
            await using var db = CreateContext();
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

            // Only active clients are offered: a deactivated client must not gain
            // new work, but existing projects linked to it keep working.
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT ""Id"", ""ClientName"" FROM ""Clients""
                                    WHERE ""IsActive"" = 1
                                    ORDER BY ""ClientName"";";
            var choices = new List<ProjectClientChoice>();
            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                choices.Add(new ProjectClientChoice
                {
                    Id = reader.GetInt32(0),
                    DisplayName = reader.GetString(1)
                });
            }
            return choices;
        }

        public async Task<string> SuggestNumberAsync(CancellationToken ct = default)
        {
            await using var db = CreateContext();
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT ""ProjectNumber"" FROM ""Projects"";";
            var numbers = new List<string>();
            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                numbers.Add(reader.GetString(0));

            return ProjectRules.NextNumber(numbers);
        }
    }
}

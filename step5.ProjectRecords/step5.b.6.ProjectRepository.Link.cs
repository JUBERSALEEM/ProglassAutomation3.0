// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.b.5.ProjectRepository.Link.cs
// MODULE: step5 — the inquiry link (attach / detach / read).
//
// This is the "& Inquiry Integration" half of the module, and it is deliberately
// the smallest write surface in the product:
//
//   • Attaching an inquiry is ONE parameterised UPDATE of ONE column
//     (`ProjectRecordId`). It cannot insert an inquiry, cannot delete one, and
//     cannot rewrite the inquiry's number, customer, text, status or timestamps.
//     The inquiry's own `UpdatedAt` is left alone on purpose: linking a project
//     is not the customer editing their inquiry, and stamping it would make the
//     register claim an edit that never happened.
//
//   • Detaching is the same UPDATE with NULL.
//
//   • Reading the candidates is a SELECT. An inquiry is offered for linking only
//     when its link column is NULL, so an inquiry can never be silently moved
//     from one project to another by attaching it twice — the caller has to
//     detach it first, which is a decision, not an accident.
//
// The legacy placeholders (`Inquiries.CustomerId`, `Inquiries.ProjectId`) are
// read by nobody here and written by nobody here.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Services.Database;

namespace ProGlassAutomation.Services.Projects
{
    public sealed partial class ProjectRepository
    {
        public async Task<IReadOnlyList<InquiryCandidateRow>> GetLinkedInquiriesAsync(
            int projectId, CancellationToken ct = default)
        {
            if (projectId <= 0) return Array.Empty<InquiryCandidateRow>();
            return await ReadInquiriesAsync(
                $"i.\"{ProjectSchema.InquiryProjectColumn}\" = $value", projectId, "", 500, ct)
                .ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<InquiryCandidateRow>> GetUnlinkedInquiriesAsync(
            string search, int maxRows = 200, CancellationToken ct = default) =>
            await ReadInquiriesAsync(
                $"i.\"{ProjectSchema.InquiryProjectColumn}\" IS NULL", null, search, maxRows, ct)
                .ConfigureAwait(false);

        public async Task<InquiryLinkResult> AttachInquiryAsync(
            int inquiryId, int projectId, CancellationToken ct = default)
        {
            if (inquiryId <= 0) return InquiryLinkResult.Fail("No inquiry was selected.");
            if (projectId <= 0) return InquiryLinkResult.Fail("No project was selected.");

            try
            {
                await using var db = CreateContext();
                var connection = db.Database.GetDbConnection();
                await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

                // The project must exist, and the inquiry must still be unlinked.
                // Both conditions are in the WHERE clause, so the decision and the
                // write are one statement and cannot race a second window.
                using var command = connection.CreateCommand();
                command.CommandText = $@"
                    UPDATE ""{ProjectSchema.InquiriesTable}""
                       SET ""{ProjectSchema.InquiryProjectColumn}"" = $projectId
                     WHERE ""Id"" = $inquiryId
                       AND ""{ProjectSchema.InquiryProjectColumn}"" IS NULL
                       AND EXISTS (SELECT 1 FROM ""{ProjectSchema.TableName}"" p WHERE p.""Id"" = $projectId);";
                command.Parameters.Add(new SqliteParameter("$projectId", projectId));
                command.Parameters.Add(new SqliteParameter("$inquiryId", inquiryId));

                int affected = await SqliteResilience.ExecuteWithRetryAsync(
                    token => command.ExecuteNonQueryAsync(token), ct).ConfigureAwait(false);

                if (affected == 1) return InquiryLinkResult.Ok(affected);

                return InquiryLinkResult.Fail(
                    "That inquiry is no longer available to link: it may already belong to a project, "
                    + "or it may no longer exist. Refresh the list.");
            }
            catch (Exception error) when (error is SqliteException or InvalidOperationException)
            {
                return InquiryLinkResult.Fail(DescribeWriteFailure("The inquiry could not be linked", error));
            }
        }

        public async Task<InquiryLinkResult> DetachInquiryAsync(
            int inquiryId, CancellationToken ct = default)
        {
            if (inquiryId <= 0) return InquiryLinkResult.Fail("No inquiry was selected.");

            try
            {
                await using var db = CreateContext();
                var connection = db.Database.GetDbConnection();
                await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

                using var command = connection.CreateCommand();
                command.CommandText = $@"
                    UPDATE ""{ProjectSchema.InquiriesTable}""
                       SET ""{ProjectSchema.InquiryProjectColumn}"" = NULL
                     WHERE ""Id"" = $inquiryId;";
                command.Parameters.Add(new SqliteParameter("$inquiryId", inquiryId));

                int affected = await SqliteResilience.ExecuteWithRetryAsync(
                    token => command.ExecuteNonQueryAsync(token), ct).ConfigureAwait(false);

                return affected == 1
                    ? InquiryLinkResult.Ok(affected)
                    : InquiryLinkResult.Fail("That inquiry no longer exists. Refresh the list.");
            }
            catch (Exception error) when (error is SqliteException or InvalidOperationException)
            {
                return InquiryLinkResult.Fail(DescribeWriteFailure("The inquiry could not be detached", error));
            }
        }

        /// <summary>
        /// Reads inquiries under a link condition. Only the columns the picker
        /// shows are selected, and the legacy placeholders are not among them.
        /// </summary>
        private async Task<IReadOnlyList<InquiryCandidateRow>> ReadInquiriesAsync(
            string linkCondition, int? linkValue, string search, int maxRows,
            CancellationToken ct)
        {
            await using var db = CreateContext();
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

            string text = (search ?? "").Trim();
            using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT i.""Id"", i.""InquiryNumber"", i.""CustomerName"", i.""Status"", i.""ReceivedAt"",
                       COALESCE(p.""ProjectNumber"" || ' · ' || p.""ProjectName"", '')
                  FROM ""{ProjectSchema.InquiriesTable}"" i
                  LEFT JOIN ""{ProjectSchema.TableName}"" p
                         ON p.""Id"" = i.""{ProjectSchema.InquiryProjectColumn}""
                 WHERE {linkCondition}
                   AND ($search = '' OR i.""InquiryNumber"" LIKE $like OR i.""CustomerName"" LIKE $like)
                 ORDER BY i.""ReceivedAt"" DESC
                 LIMIT $maxRows;";
            command.Parameters.Add(new SqliteParameter("$search", text));
            command.Parameters.Add(new SqliteParameter("$like", "%" + text + "%"));
            command.Parameters.Add(new SqliteParameter("$maxRows", maxRows < 1 ? 1 : maxRows));
            command.Parameters.Add(new SqliteParameter("$value", (object?)linkValue ?? DBNull.Value));

            var rows = new List<InquiryCandidateRow>();
            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(new InquiryCandidateRow
                {
                    Id = reader.GetInt32(0),
                    InquiryNumber = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    CustomerName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Status = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ReceivedAt = reader.IsDBNull(4) ? default : ParseStamp(reader.GetString(4)),
                    ProjectName = reader.IsDBNull(5) ? "" : reader.GetString(5)
                });
            }
            return rows;
        }
    }
}

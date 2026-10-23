// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryImportExportService.cs
// MODULE: Inquiry — register CSV export / import (ported).
//
// Import contract (unchanged from the legacy build):
//   - rows without customer AND project are skipped (counted);
//   - duplicate inquiry numbers inside one file are blanked so the
//     repository generates a fresh sequential number on insert (the
//     UNIQUE index made the previous importer crash mid-batch);
//   - statuses are normalized through the controlled status model.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Inquiry
{
    /// <summary>Outcome of parsing an Inquiry register CSV export.</summary>
    public sealed class InquiryImportParseResult
    {
        public List<InquiryRecord> Rows { get; } = new();
        /// <summary>Rows skipped because customer/project were missing.</summary>
        public int SkippedInvalidRows { get; set; }
        /// <summary>Numbers blanked because the same number appeared twice in the file.</summary>
        public int DuplicateNumbersRegenerated { get; set; }
    }

    public sealed class InquiryImportExportService
    {
        public async Task ExportCsvAsync(string path, IEnumerable<InquiryListProjection> rows, CancellationToken ct = default)
        {
            static string Q(string? x) => "\"" + (x ?? "").Replace("\"", "\"\"") + "\"";
            var lines = new List<string> { "Inquiry,Reference,Customer,Location,Project,Description,Salesman,Prepared By,Received,Updated,Status,Attachments" };
            lines.AddRange(rows.Select(x => string.Join(",", Q(x.InquiryNumber), Q(x.InquiryReference),
                Q(x.CustomerName), Q(x.CustomerLocation), Q(x.ProjectName), Q(x.ProjectDescription),
                Q(x.SalesmanName), Q(x.PreparedByName), Q(x.ReceivedAt.ToString("o", CultureInfo.InvariantCulture)),
                Q(x.UpdatedAt.ToString("o", CultureInfo.InvariantCulture)), Q(x.Status), x.AttachmentCount.ToString(CultureInfo.InvariantCulture))));
            await File.WriteAllLinesAsync(path, lines, new UTF8Encoding(true), ct).ConfigureAwait(false);
        }

        /// <summary>Parses a register CSV into validated records.</summary>
        public async Task<InquiryImportParseResult> ParseCsvAsync(string path, CancellationToken ct = default)
        {
            var lines = await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false);
            var result = new InquiryImportParseResult();
            var seenNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var c = ParseCsvLine(line);
                if (c.Count < 6)
                {
                    result.SkippedInvalidRows++;
                    continue;
                }
                string customer = c[2].Trim(), project = c[4].Trim();
                if (customer.Length == 0 || project.Length == 0)
                {
                    result.SkippedInvalidRows++;
                    continue;
                }
                string number = c[0].Trim();
                if (number.Length > 0 && !seenNumbers.Add(number))
                {
                    result.DuplicateNumbersRegenerated++;
                    number = "";
                }
                result.Rows.Add(new InquiryRecord
                {
                    InquiryNumber = number,
                    InquiryReference = c[1].Trim(),
                    CustomerName = customer,
                    CustomerLocation = c[3].Trim(),
                    ProjectName = project,
                    ProjectDescription = c[5].Trim(),
                    SalesmanName = c.Count > 6 ? c[6].Trim() : "",
                    PreparedByName = c.Count > 7 ? c[7].Trim() : "",
                    ReceivedAt = c.Count > 8 && DateTime.TryParse(c[8], CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var received)
                        ? DateTime.SpecifyKind(received, DateTimeKind.Local) : DateTime.Now,
                    Status = c.Count > 10 ? InquiryStatuses.Normalize(c[10]) : InquiryStatuses.Received,
                    Priority = "Normal"
                });
            }
            return result;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var cells = new List<string>(); var b = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { b.Append('"'); i++; } else quoted = !quoted; }
                else if (ch == ',' && !quoted) { cells.Add(b.ToString()); b.Clear(); }
                else b.Append(ch);
            }
            cells.Add(b.ToString()); return cells;
        }
    }
}

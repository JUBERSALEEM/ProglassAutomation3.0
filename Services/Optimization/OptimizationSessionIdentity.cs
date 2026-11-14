// Session identity envelope for PiecesJson.
// Identity lives in JSON (not new SQLite columns — EnsureCreated never migrates).
// A legacy array is UNKNOWN/LEGACY (IdentityVersion 0) and is never auto-valid.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace ProGlassAutomation.Services.Optimization
{
    /// <summary>
    /// One persisted cutting-list row, including provenance the engine needs
    /// to restore without inventing identity from composition text.
    /// Extra fields default empty on legacy rows — that is UNKNOWN, not guessed.
    /// </summary>
    public sealed class OptimizationSessionPieceRecord
    {
        public string GlassRef { get; set; } = string.Empty;
        public string PieceLabel { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string W { get; set; } = string.Empty;
        public string H { get; set; } = string.Empty;
        public string Qty { get; set; } = "1";
        public bool RotationLocked { get; set; }

        public string ProformaId { get; set; } = string.Empty;
        public string JobOrderId { get; set; } = string.Empty;
        public int SpecGroupNumber { get; set; }
        public int SourceRowSerial { get; set; }
        public string GlassType { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public int ThicknessMm { get; set; }
        public string Processing { get; set; } = string.Empty;
        public string Coating { get; set; } = string.Empty;
        public int W1 { get; set; }
        public int H1 { get; set; }
        public int W2 { get; set; }
        public int H2 { get; set; }
        public string SheetStoreSku { get; set; } = string.Empty;
        public int SheetWidthMm { get; set; }
        public int SheetHeightMm { get; set; }
        public decimal SheetPricePerSqm { get; set; }
        public string MaterialKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Wrapped PiecesJson. <see cref="IdentityVersion"/> ≤ 0 is UNKNOWN/LEGACY
    /// and must never be treated as matching the current input.
    /// </summary>
    public sealed class OptimizationSessionPiecesPayload
    {
        public int IdentityVersion { get; set; }
        public string RunId { get; set; } = string.Empty;
        public string InputFingerprint { get; set; } = string.Empty;
        public string DocumentKey { get; set; } = string.Empty;
        public List<OptimizationSessionPieceRecord> Pieces { get; set; } = new();
    }

    public static class OptimizationSessionIdentity
    {
        public const int CurrentIdentityVersion = 1;

        /// <summary>
        /// True only for a payload written by this identity contract.
        /// Version ≤ 0 (including a legacy array parsed as version 0) is never current-valid.
        /// </summary>
        public static bool IsCurrentIdentityVersion(int version) => version > 0;

        public static bool IsCurrentValid(OptimizationSessionPiecesPayload? payload) =>
            payload != null
            && IsCurrentIdentityVersion(payload.IdentityVersion)
            && !string.IsNullOrWhiteSpace(payload.RunId)
            && !string.IsNullOrWhiteSpace(payload.InputFingerprint);

        /// <summary>
        /// A saved session matches the live cutting list only when it carries a
        /// current identity version AND the fingerprints are byte-identical.
        /// Legacy / version-0 payloads never match.
        /// </summary>
        public static bool MatchesCurrentInput(
            OptimizationSessionPiecesPayload? saved, string? currentFingerprint)
        {
            if (!IsCurrentValid(saved)) return false;
            if (string.IsNullOrWhiteSpace(currentFingerprint)) return false;
            return string.Equals(saved!.InputFingerprint, currentFingerprint, StringComparison.Ordinal);
        }

        public static string FingerprintRows(IEnumerable<OptimizationSessionPieceRecord>? pieces)
        {
            if (pieces == null) return string.Empty;

            var lines = pieces
                .Select(p => string.Join("\t",
                    p.ProformaId ?? string.Empty,
                    p.JobOrderId ?? string.Empty,
                    p.GlassRef ?? string.Empty,
                    p.SpecGroupNumber.ToString(CultureInfo.InvariantCulture),
                    p.SourceRowSerial.ToString(CultureInfo.InvariantCulture),
                    p.MaterialKey ?? string.Empty,
                    p.Color ?? string.Empty,
                    p.ThicknessMm.ToString(CultureInfo.InvariantCulture),
                    p.W ?? string.Empty,
                    p.H ?? string.Empty,
                    p.Qty ?? string.Empty,
                    p.W1.ToString(CultureInfo.InvariantCulture),
                    p.H1.ToString(CultureInfo.InvariantCulture),
                    p.W2.ToString(CultureInfo.InvariantCulture),
                    p.H2.ToString(CultureInfo.InvariantCulture),
                    p.Position ?? string.Empty,
                    p.SheetStoreSku ?? string.Empty))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();

            return OptimizationMaterialIdentity.HashLines(lines);
        }

        public static OptimizationSessionPiecesPayload Parse(string? json)
        {
            var empty = new OptimizationSessionPiecesPayload
            {
                IdentityVersion = 0,
                Pieces = new List<OptimizationSessionPieceRecord>()
            };

            if (string.IsNullOrWhiteSpace(json)) return empty;

            string trimmed = json.Trim();
            try
            {
                if (trimmed[0] == '[')
                {
                    var list = JsonSerializer.Deserialize<List<OptimizationSessionPieceRecord>>(trimmed)
                               ?? new List<OptimizationSessionPieceRecord>();
                    return new OptimizationSessionPiecesPayload
                    {
                        IdentityVersion = 0,
                        Pieces = list.Where(p => p != null).ToList()
                    };
                }

                if (trimmed[0] == '{')
                {
                    var payload = JsonSerializer.Deserialize<OptimizationSessionPiecesPayload>(trimmed);
                    if (payload == null) return empty;
                    payload.Pieces ??= new List<OptimizationSessionPieceRecord>();
                    payload.RunId ??= string.Empty;
                    payload.InputFingerprint ??= string.Empty;
                    payload.DocumentKey ??= string.Empty;
                    return payload;
                }
            }
            catch (JsonException)
            {
                return empty;
            }

            return empty;
        }

        public static string Serialize(OptimizationSessionPiecesPayload payload)
        {
            if (payload == null)
            {
                payload = new OptimizationSessionPiecesPayload
                {
                    IdentityVersion = CurrentIdentityVersion,
                    Pieces = new List<OptimizationSessionPieceRecord>()
                };
            }

            if (payload.IdentityVersion <= 0)
                payload.IdentityVersion = CurrentIdentityVersion;
            payload.Pieces ??= new List<OptimizationSessionPieceRecord>();
            if (string.IsNullOrWhiteSpace(payload.InputFingerprint))
                payload.InputFingerprint = FingerprintRows(payload.Pieces);

            return JsonSerializer.Serialize(payload);
        }
    }
}

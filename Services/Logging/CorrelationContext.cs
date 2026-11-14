// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Logging/CorrelationContext.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — Correlation ID for tracing a business operation
// VERSION: Phase 2 — 2026-08-21
//
// PURPOSE:
//   Every business operation (PI save → JO → DN → TI) gets a CorrelationId
//   that flows through logs, audit trail, and UI error dialogs.
//   When user reports "invoice didn't save", support can grep logs by
//   CorrelationId and see full trace.
//
//   Uses AsyncLocal so it flows across async/await boundaries automatically.
//
// USAGE:
//   using var scope = CorrelationContext.BeginScope("PI-12345");
//   AppLogService.Info("Saving invoice", new LogContext { ... });
//
//   // In ViewModel:
//   var correlationId = CorrelationContext.NewId();
//   using (CorrelationContext.BeginScope(correlationId))
//   {
//       var result = await _repo.SaveAsync(invoice);
//       if (!result.IsSuccess)
//           ShowError(result.Error, result.ErrorId, correlationId);
//   }
//
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Threading;

namespace ProGlassAutomation.Services.Common
{
    /// <summary>
    /// Ambient correlation context — flows via AsyncLocal across async boundaries.
    /// </summary>
    public static class CorrelationContext
    {
        private static readonly AsyncLocal<string?> _current = new();
        private static readonly AsyncLocal<string?> _documentId = new();
        private static readonly AsyncLocal<string?> _documentType = new();

        /// <summary>Current correlation ID (or null if no scope).</summary>
        public static string? CurrentId => _current.Value;

        /// <summary>Current document ID being operated on (PI-xxx, JO-xxx, etc.).</summary>
        public static string? CurrentDocumentId => _documentId.Value;

        /// <summary>Current document type (ProformaInvoice, JobOrder, etc.).</summary>
        public static string? CurrentDocumentType => _documentType.Value;

        /// <summary>Generates a new correlation ID: CORR-XXXXXX</summary>
        public static string NewId() =>
            "CORR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        /// <summary>Generates a document-specific correlation: CORR-PI-XXXXXX</summary>
        public static string NewIdFor(string documentType, string? documentId = null)
        {
            var prefix = string.IsNullOrWhiteSpace(documentType) ? "CORR" : $"CORR-{documentType.ToUpperInvariant()}";
            var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            return string.IsNullOrWhiteSpace(documentId) ? $"{prefix}-{suffix}" : $"{prefix}-{documentId}-{suffix}";
        }

        /// <summary>
        /// Begins a correlation scope. Dispose to restore previous.
        /// </summary>
        public static IDisposable BeginScope(string? correlationId = null, string? documentId = null, string? documentType = null)
        {
            return new Scope(correlationId, documentId, documentType);
        }

        /// <summary>
        /// Begins a scope for a specific document operation.
        /// </summary>
        public static IDisposable BeginDocumentScope(string documentType, string documentId, string? correlationId = null)
        {
            return new Scope(correlationId ?? NewIdFor(documentType, documentId), documentId, documentType);
        }

        private sealed class Scope : IDisposable
        {
            private readonly string? _prevCorrelation;
            private readonly string? _prevDocId;
            private readonly string? _prevDocType;
            private bool _disposed;

            public Scope(string? correlationId, string? docId, string? docType)
            {
                _prevCorrelation = _current.Value;
                _prevDocId = _documentId.Value;
                _prevDocType = _documentType.Value;

                if (!string.IsNullOrWhiteSpace(correlationId))
                    _current.Value = correlationId;
                else if (_current.Value == null)
                    _current.Value = NewId();

                if (!string.IsNullOrWhiteSpace(docId))
                    _documentId.Value = docId;
                if (!string.IsNullOrWhiteSpace(docType))
                    _documentType.Value = docType;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _current.Value = _prevCorrelation;
                _documentId.Value = _prevDocId;
                _documentType.Value = _prevDocType;
            }
        }
    }
}

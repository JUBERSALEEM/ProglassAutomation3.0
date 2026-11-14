using System;

namespace ProGlassAutomation.Services.Logging
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Where an operation happened. Every field is optional — log what
    /// you know. Example: Module="Proforma Invoice", Class="PrintableInvoiceViewModel",
    /// Method="ExecuteSaveInvoiceAsync", Operation="Save".
    ///
    /// Phase 2: Added CorrelationId + Document tracking so a single business
    /// operation (PI → JO → DN → TI) can be traced across log files.
    /// </summary>
    public class LogContext
    {
        public string Module { get; set; } = "";
        public string Screen { get; set; } = "";
        public string Class { get; set; } = "";
        public string Method { get; set; } = "";
        public string Operation { get; set; } = "";

        // ── Phase 2: Correlation & Document tracing ────────────────────
        /// <summary>Correlation ID for tracing one business operation end-to-end (CORR-XXXXXX).</summary>
        public string CorrelationId { get; set; } = "";

        /// <summary>Document ID being operated on (PI-2026-001, JO-2026-001, etc.).</summary>
        public string DocumentId { get; set; } = "";

        /// <summary>Document type (ProformaInvoice, JobOrder, DeliveryNote, TaxInvoice, Statement).</summary>
        public string DocumentType { get; set; } = "";

        /// <summary>Optional extra data (JSON or key=value).</summary>
        public string Extra { get; set; } = "";

        /// <summary>Creates a LogContext with correlation from ambient context.</summary>
        public static LogContext FromCurrent(string? module = null, string? operation = null)
        {
            var ctx = new LogContext();
            if (!string.IsNullOrWhiteSpace(module)) ctx.Module = module;
            if (!string.IsNullOrWhiteSpace(operation)) ctx.Operation = operation;

            // Flow ambient correlation if present
            try
            {
                var corr = ProGlassAutomation.Services.Common.CorrelationContext.CurrentId;
                if (!string.IsNullOrWhiteSpace(corr)) ctx.CorrelationId = corr;

                var docId = ProGlassAutomation.Services.Common.CorrelationContext.CurrentDocumentId;
                if (!string.IsNullOrWhiteSpace(docId)) ctx.DocumentId = docId;

                var docType = ProGlassAutomation.Services.Common.CorrelationContext.CurrentDocumentType;
                if (!string.IsNullOrWhiteSpace(docType)) ctx.DocumentType = docType;
            }
            catch { /* logging must never throw */ }

            return ctx;
        }
    }

    /// <summary>One technical log record (app log, not user activity).</summary>
    public class LogEntry
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public LogLevel Level { get; set; } = LogLevel.Info;
        public string Message { get; set; } = "";
        public string ErrorId { get; set; } = "";
        public LogContext Context { get; set; } = new();
        public string ExceptionText { get; set; } = "";
        public string Username { get; set; } = "";

        // Phase 2: Denormalized for fast filtering
        public string CorrelationId => Context?.CorrelationId ?? "";
        public string DocumentId => Context?.DocumentId ?? "";
        public string DocumentType => Context?.DocumentType ?? "";

        public string LevelDisplay => Level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            _ => "CRITICAL"
        };

        /// <summary>"(ERR-XXXXXX)" or empty — ready for direct display.</summary>
        public string ErrorIdDisplay =>
            string.IsNullOrEmpty(ErrorId) ? "" : $"  ({ErrorId})";

        public string TimeDisplay =>
            TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");

        public string CorrelationDisplay =>
            string.IsNullOrEmpty(CorrelationId) ? "" : $"[{CorrelationId}]";

        public string ContextDisplay
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(Context.Module)) parts.Add(Context.Module);
                if (!string.IsNullOrEmpty(Context.DocumentType)) parts.Add(Context.DocumentType);
                if (!string.IsNullOrEmpty(Context.DocumentId)) parts.Add(Context.DocumentId);
                if (!string.IsNullOrEmpty(Context.Class)) parts.Add(Context.Class);
                if (!string.IsNullOrEmpty(Context.Method)) parts.Add(Context.Method + "()");
                if (!string.IsNullOrEmpty(Context.Operation)) parts.Add($"op:{Context.Operation}");
                if (!string.IsNullOrEmpty(Context.CorrelationId)) parts.Add(Context.CorrelationId);
                return string.Join(" › ", parts);
            }
        }

        /// <summary>
        /// Full row tooltip: time · level · module/function · message.
        /// </summary>
        public string RowTooltip
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>
                {
                    $"When:     {TimestampUtc.ToLocalTime():dd MMM yyyy HH:mm:ss.fff}",
                    $"Level:    {LevelDisplay}",
                    $"Where:    {(string.IsNullOrEmpty(ContextDisplay) ? "(app)" : ContextDisplay)}",
                    $"Message:  {Message}"
                };
                if (!string.IsNullOrEmpty(ErrorId))
                    parts.Add($"Error ID: {ErrorId}");
                if (!string.IsNullOrEmpty(CorrelationId))
                    parts.Add($"Correlation: {CorrelationId}");
                if (!string.IsNullOrEmpty(DocumentType))
                    parts.Add($"Doc Type: {DocumentType}");
                if (!string.IsNullOrEmpty(DocumentId))
                    parts.Add($"Doc ID: {DocumentId}");
                if (!string.IsNullOrEmpty(Username))
                    parts.Add($"User:     {Username}");
                if (!string.IsNullOrEmpty(Context.Extra))
                    parts.Add($"Extra:    {Context.Extra}");
                return string.Join("\n", parts);
            }
        }
    }
}

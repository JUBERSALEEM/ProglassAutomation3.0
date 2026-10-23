// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Database/ConcurrencyConflictException.cs
// MODULE: Core — optimistic-concurrency conflict signal.
//
// Raised by a repository UpdateAsync when the caller passed the RowVersion
// it originally loaded and the stored RowVersion has since moved on — i.e.
// a second user (or a second window) saved the same document first. Rather
// than silently overwriting their work ("last writer wins", the classic
// ERP data-loss bug), the repository throws this and the editor tells the
// user to reload.
//
// Application-level rather than EF's DbUpdateConcurrencyException because
// the editors rebuild a detached entity through the mapper on save, so EF's
// own change tracker never sees the original token. The repository instead
// reads the current RowVersion inside the same context and compares
// explicitly.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Services.Database
{
    /// <summary>
    /// Thrown when an update would overwrite a newer version of a document
    /// that another user or window saved after this one was loaded.
    /// </summary>
    public sealed class ConcurrencyConflictException : Exception
    {
        /// <summary>Document kind, e.g. "Inquiry".</summary>
        public string DocumentKind { get; }

        /// <summary>User-facing identifier, e.g. the inquiry number.</summary>
        public string DocumentId { get; }

        /// <summary>The RowVersion the caller loaded.</summary>
        public int ExpectedRowVersion { get; }

        /// <summary>The RowVersion currently stored.</summary>
        public int ActualRowVersion { get; }

        public ConcurrencyConflictException(
            string documentKind,
            string documentId,
            int expectedRowVersion,
            int actualRowVersion)
            : base(BuildMessage(documentKind, documentId))
        {
            DocumentKind = documentKind ?? "document";
            DocumentId = documentId ?? "";
            ExpectedRowVersion = expectedRowVersion;
            ActualRowVersion = actualRowVersion;
        }

        private static string BuildMessage(string kind, string id)
        {
            string label = string.IsNullOrWhiteSpace(id)
                ? (kind ?? "document")
                : $"{kind} {id}";
            return
                $"This {label} was changed by someone else after you opened it. "
                + "Your changes were not saved so their update is not lost. "
                + "Please reload the document and re-apply your changes.";
        }
    }
}

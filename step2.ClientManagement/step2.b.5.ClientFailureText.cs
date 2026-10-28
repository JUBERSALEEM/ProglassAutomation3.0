// ═══════════════════════════════════════════════════════════════════════
// FILE: step2.ClientManagement/step2.b.5.ClientFailureText.cs
// MODULE: step2 — the client repository's failure sentence, split out of
//         step2.b.4 under rule K (no file may grow past its frozen size).
//
// Why it is its own file
//   Round 33 added a diagnostic record to this method, which put step2.b.4 over
//   its frozen line count. Rule K says a new line in a grandfathered file belongs
//   in a new part, so the whole method moved rather than the file growing.
//
// What it does
//   One place turns a persistence exception into the sentence the operator reads.
//   Two rules, both deliberate:
//     • a duplicate-constraint refusal is ordinary feedback ("that code already
//       exists"), not an application fault, so it is NOT written to the log;
//     • everything else IS written, because the card can only summarise it and
//       the record is what a support conversation needs afterwards.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Diagnostics;
using ProGlassAutomation.Services.Database;
using System;

namespace ProGlassAutomation.Services.Clients
{
    public sealed partial class ClientRepository
    {
        /// <summary>
        /// Turns a persistence exception into an operator-facing sentence. The
        /// exception type is named so a support conversation has something to
        /// go on, without exposing a stack trace in the UI.
        /// </summary>
        private static string DescribeWriteFailure(string action, Exception exception)
        {
            if (exception == null) return action + ".";

            // A unique-constraint refusal is the duplicate the operator is being
            // told about — ordinary feedback, not an application fault, so it is
            // deliberately NOT written to the diagnostic log. Everything else
            // here is a real persistence failure that the screen only summarises.
            if (!SqliteErrorClassifier.IsUnique(exception))
                AppLog.Error("step2.clients.write-failure", exception, action);

            if (exception is SqliteException sqlite && SqliteErrorClassifier.IsTransient(exception))
                return action + ": the database is busy. Close other ProGlass windows and try again.";
            if (SqliteErrorClassifier.IsUnique(exception))
                return action + ": a conflicting record already exists.";
            return action + " (" + exception.GetType().Name + "): " + exception.Message;
        }
    }
}

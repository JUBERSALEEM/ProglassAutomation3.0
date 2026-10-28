// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.b.10.UserRepositoryFailureText.cs
// MODULE: step3 — the user repository's failure sentence, split out of
//         step3.b.4 under rule K (no file may grow past its frozen size).
//
// Why it is its own file
//   Round 33 added a diagnostic record to this method, which put step3.b.4 over
//   its frozen line count. Rule K says a new line in a grandfathered file belongs
//   in a new part, so the whole method moved rather than the file growing.
//
// What it does
//   The one place that turns a persistence exception into the sentence the
//   operator reads. A duplicate username is feedback, not a fault, so it is not
//   logged; every other persistence failure is, because the card only summarises
//   it. The operation id never carries a username — that is personal data.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Diagnostics;
using ProGlassAutomation.Services.Database;
using System;

namespace ProGlassAutomation.Services.Users
{
    public sealed partial class UserRepository
    {
        /// <summary>
        /// Turns a persistence exception into an operator-facing sentence. The
        /// exception type is named so a support conversation has something to go
        /// on, without exposing a stack trace in the UI.
        /// </summary>
        private static string DescribeWriteFailure(string action, Exception exception)
        {
            if (exception == null) return action + ".";

            // The duplicate-username refusal is feedback for the operator, not an
            // application fault, so it is not written to the diagnostic log. Every
            // other persistence failure is, because the card only summarises it.
            // The operation id says WHICH write failed without naming the person:
            // a username is personal data.
            if (!SqliteErrorClassifier.IsUnique(exception))
                AppLog.Error("step3.users.write-failure", exception, action);

            if (SqliteErrorClassifier.IsTransient(exception))
                return action + ": the database is busy. Close other ProGlass windows and try again.";
            if (SqliteErrorClassifier.IsUnique(exception))
                return action + ": that username is already registered to another user.";
            return action + " (" + exception.GetType().Name + "): " + exception.Message;
        }
    }
}

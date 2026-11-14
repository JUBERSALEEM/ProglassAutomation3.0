using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Logging;
using System;

namespace ProGlassAutomation.Services.Database
{
    /// <summary>
    /// Handles database creation on first app run.
    /// Called from App.xaml.cs startup before UI loads.
    /// </summary>
    public static class DatabaseInitializer
    {
        public static void Initialize()
        {
            try
            {
                using var db = new AppDbContext();

                // Creates DB file + tables if they don't exist
                db.Database.EnsureCreated();

                // Bring an EXISTING proglass.db forward if any schema
                // migrations are registered (additive only; see
                // AppSchemaMigrations). Safe no-op until one is added.
                bool upgraded = SchemaMigrationRunner.Upgrade(
                    AppDbContext.DatabaseFilePath,
                    AppSchemaMigrations.ProGlass,
                    "Sheet Store");

                if (!upgraded)
                {
                    AppLogService.Error(
                        "Main database schema was not upgraded. Writes may not match the expected schema. " +
                        SchemaMigrationRunner.FailureSummary,
                        null,
                        new LogContext
                        {
                            Module = "Database",
                            Class = nameof(DatabaseInitializer),
                            Operation = "Initialize"
                        });
                }

                AppLogService.Info(
                    upgraded
                        ? $"Main database ready at {AppDbContext.DatabaseFilePath}"
                        : $"Main database opened WITHOUT a schema upgrade at {AppDbContext.DatabaseFilePath}",
                    new LogContext
                    {
                        Module = "Database",
                        Class = nameof(DatabaseInitializer),
                        Operation = "Initialize"
                    });
            }
            catch (Exception ex)
            {
                // R-1 RECURRENCE: this previously reported only through
                // Debug.WriteLine, which is [Conditional("DEBUG")] and is
                // removed by the compiler in Release. In a shipped build a
                // failure here was completely invisible — the Sheet Store
                // would simply appear empty with no explanation, because
                // the database was never created.
                //
                // Still non-fatal by design (the app runs with degraded
                // features rather than refusing to start), but the reason
                // is now recoverable from the log.
                AppLogService.Error(
                    "Main database could not be initialized. Sheet Store "
                    + "and pricing features will be unavailable.",
                    ex,
                    new LogContext
                    {
                        Module = "Database",
                        Class = nameof(DatabaseInitializer),
                        Operation = "Initialize"
                    });
            }
        }
    }
}

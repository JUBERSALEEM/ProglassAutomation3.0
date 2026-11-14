// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Database/SchemaMigrations/AppSchemaMigrations.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — migration registries for every SQLite database
//
// One registry per database file, consumed by SchemaMigrationRunner after
// each DbContext's EnsureCreated(). Fresh databases are created at the
// CURRENT schema by EnsureCreated, so these lists may begin empty; they
// exist so that future schema changes are added here as versioned,
// additive migrations instead of relying on EnsureCreated (which can never
// alter an existing database).
//
// RULE: every migration is additive/idempotent (add columns/tables/indexes
// only). Never drop or rewrite existing rows.
//
// NOTE (audit.db): audit.db already had its own hand-written additive
// migrator (AuditDbMigrations) before this framework existed. To avoid a
// behaviour change to a security-critical file in the same change that
// introduces the framework, audit.db keeps calling its existing migrator for
// now. Its operations are identical in spirit (idempotent ADD COLUMN /
// CREATE TABLE IF NOT EXISTS) and can be folded into this registry in a
// later, separately-verified phase.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Services.Logging;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.Database.SchemaMigrations
{
    /// <summary>Central registry of schema migrations per database file.</summary>
    public static class AppSchemaMigrations
    {
        // proglass.db (Sheet Store)
        public static IReadOnlyList<SchemaMigration> ProGlass { get; }
            = new List<SchemaMigration>
            {
                // Sheet consumption traceability. EnsureCreated() only ever
                // CREATES a missing database, never alters an existing one,
                // so every install already in the field needs this or EF
                // would query columns that do not exist.
                new(1001, "Sheet history: consumption traceability columns", connection =>
                {
                    SchemaMigrationSql.AddColumnIfMissing(connection, "SheetHistories", "DocumentReference", "TEXT NULL");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "SheetHistories", "CustomerName",      "TEXT NULL");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "SheetHistories", "ProjectName",       "TEXT NULL");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "SheetHistories", "Salesman",          "TEXT NULL");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "SheetHistories", "PanePosition",      "TEXT NULL");

                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_SheetHistories_Salesman ON SheetHistories(Salesman);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_SheetHistories_CustomerName ON SheetHistories(CustomerName);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_SheetHistories_DocumentReference ON SheetHistories(DocumentReference);");
                }),

                // Non-glass consumables: spacer bar, desiccant, sealants,
                // cavity gas, hardware. See Models/SheetStore/InventoryItem
                // for why these are a separate table rather than a widened
                // GlassSheet (Stock must be DECIMAL — 45.55 LM of spacer —
                // and glass feeds live proforma-invoice pricing that must
                // not be disturbed).
                //
                // EnsureCreated() only ever creates a MISSING database, so
                // every install already in the field needs this table added
                // explicitly or EF would query a table that does not exist.
                //
                // Quantities and money are TEXT affinity, matching the D-6
                // convention used for GlassSheet prices: NUMERIC affinity
                // on SQLite yields REAL (binary float) storage, which drifts
                // under repeated add/subtract and SQL-side SUM.
                new(1003, "Inventory: non-glass consumables table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS InventoryItems (
                            Id                   INTEGER NOT NULL CONSTRAINT PK_InventoryItems PRIMARY KEY AUTOINCREMENT,
                            Name                 TEXT    NOT NULL,
                            Category             INTEGER NOT NULL DEFAULT 0,
                            PartNumber           TEXT    NOT NULL DEFAULT '',
                            Supplier             TEXT    NOT NULL DEFAULT '',
                            Specification        TEXT    NOT NULL DEFAULT '',
                            Uom                  INTEGER NOT NULL DEFAULT 0,
                            Stock                TEXT    NOT NULL DEFAULT '0',
                            Used                 TEXT    NOT NULL DEFAULT '0',
                            ReorderLevel         TEXT    NOT NULL DEFAULT '0',
                            PurchasePricePerUnit TEXT    NOT NULL DEFAULT '0',
                            SellPricePerUnit     TEXT    NOT NULL DEFAULT '0',
                            CreatedAt            TEXT    NOT NULL,
                            LastUpdated          TEXT    NOT NULL,
                            LastPurchaseDate     TEXT    NULL
                        );");

                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_InventoryItem_Category ON InventoryItems(Category);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_InventoryItem_Supplier ON InventoryItems(Supplier);");
                }),

                // Movement undo: compensating history rows. Must be 1004
                // (not 1002) so installs that already applied 1003 still
                // receive the columns.
                new(1004, "Sheet history: movement reversal (undo) columns", connection =>
                {
                    SchemaMigrationSql.AddColumnIfMissing(connection, "SheetHistories", "ReversesHistoryId", "INTEGER NULL");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "SheetHistories", "IsReversed",        "INTEGER NOT NULL DEFAULT 0");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_SheetHistories_ReversesHistoryId ON SheetHistories(ReversesHistoryId);");
                })
            };

        // joborders.db
        public static IReadOnlyList<SchemaMigration> JobOrders { get; }
            = new List<SchemaMigration>
            {
                // ERP lifecycle hardening (Phase 1): void / soft-close columns.
                // Additive + idempotent. EnsureCreated builds a fresh DB at the
                // current model (columns already present); existing installs get
                // them here so EF never queries a missing column.
                new(1001, "Job Order: void / soft-close columns", connection =>
                    AddVoidColumns(connection, "JobOrders")),

                // ERP hardening (Phase 5): optimistic-concurrency counter.
                new(1002, "Job Order: RowVersion concurrency counter", connection =>
                    AddRowVersionColumn(connection, "JobOrders")),

                // Unique JobOrderId is in OnModelCreating (fresh EnsureCreated).
                // Existing field DBs never received it via additive migration.
                // Do NOT rewrite colliding numbers (DN/TI key the string).
                new(1003, "Job Order: unique JobOrderId index", connection =>
                {
                    try
                    {
                        SchemaMigrationSql.CreateIndexIfNotExists(connection,
                            "CREATE UNIQUE INDEX IF NOT EXISTS IX_JobOrder_JobOrderId_Unique ON JobOrders(JobOrderId);");
                    }
                    catch (SqliteException ex)
                    {
                        AppLogService.Warning(
                            "[JobOrders] unique JobOrderId index not applied: " +
                            ex.Message,
                            new LogContext
                            {
                                Module = "Database",
                                Class = "SchemaMigrations",
                                Operation = "JobOrderIdUniqueIndex"
                            });
                    }
                })
            };

        // deliverynotes.db
        public static IReadOnlyList<SchemaMigration> DeliveryNotes { get; } = new List<SchemaMigration>
        {
            new(1, "Add WeightPerPiece column for glass weight tracking", connection =>
            {
                SchemaMigrationSql.AddColumnIfMissing(connection, "DeliveryNoteItems", "WeightPerPiece", "REAL NOT NULL DEFAULT 0");
            }),

            // ERP lifecycle hardening (Phase 1): void / soft-close columns.
            new(1001, "Delivery Note: void / soft-close columns", connection =>
                AddVoidColumns(connection, "DeliveryNotes")),

            // ERP hardening (Phase 5): optimistic-concurrency counter.
            new(1002, "Delivery Note: RowVersion concurrency counter", connection =>
                AddRowVersionColumn(connection, "DeliveryNotes")),

            // Unique DeliveryNoteId is in OnModelCreating (fresh EnsureCreated).
            // Existing field DBs never received it via additive migration.
            // Do NOT rewrite colliding numbers (TI keys the string).
            new(1003, "Delivery Note: unique DeliveryNoteId index", connection =>
            {
                try
                {
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_DeliveryNotes_DeliveryNoteId ON DeliveryNotes(DeliveryNoteId);");
                }
                catch (SqliteException ex)
                {
                    AppLogService.Warning(
                        "[DeliveryNotes] unique DeliveryNoteId index not applied: " +
                        ex.Message,
                        new LogContext
                        {
                            Module = "Database",
                            Class = "SchemaMigrations",
                            Operation = "DeliveryNoteIdUniqueIndex"
                        });
                }
            })
        };

        // taxinvoices.db
        public static IReadOnlyList<SchemaMigration> TaxInvoices { get; }
            = new List<SchemaMigration>
            {
                // ERP lifecycle hardening (Phase 1): void / soft-close columns.
                // A Tax Invoice is a fiscal document — it is voided, never
                // deleted once issued. Additive + idempotent.
                new(1001, "Tax Invoice: void / soft-close columns", connection =>
                    AddVoidColumns(connection, "TaxInvoices")),

                // ERP hardening (Phase 5): optimistic-concurrency counter.
                new(1002, "Tax Invoice: RowVersion concurrency counter", connection =>
                    AddRowVersionColumn(connection, "TaxInvoices")),

                // Unique InvoiceNumber is in OnModelCreating (fresh EnsureCreated).
                // Existing field DBs never received it via additive migration.
                // Do NOT rewrite colliding numbers (DN keys the string).
                new(1003, "Tax Invoice: unique InvoiceNumber index", connection =>
                {
                    try
                    {
                        SchemaMigrationSql.CreateIndexIfNotExists(connection,
                            "CREATE UNIQUE INDEX IF NOT EXISTS IX_TaxInvoices_InvoiceNumber ON TaxInvoices(InvoiceNumber);");
                    }
                    catch (SqliteException ex)
                    {
                        AppLogService.Warning(
                            "[TaxInvoices] unique InvoiceNumber index not applied: " +
                            ex.Message,
                            new LogContext
                            {
                                Module = "Database",
                                Class = "SchemaMigrations",
                                Operation = "InvoiceNumberUniqueIndex"
                            });
                    }
                })
            };

        // inquiries.db
        //
        // Interim builds shipped an earlier Inquiry domain model, so customer
        // databases exist whose Inquiries table predates the commercial
        // columns (InquiryValue / Priority / Notes / …) or even the child
        // tables. EnsureCreated never alters an existing database, and this
        // registry was empty until now — those installs crashed at first load
        // with SQLite error 1 "no such column i.InquiryValue".
        //
        // 1001 converges ANY legacy shape to the current model. It is
        // idempotent: CREATE TABLE IF NOT EXISTS, one ADD COLUMN per missing
        // column, duplicate numbers made unique, then the indexes. Reserved
        // high version for the same reason as ProformaInvoices above.
        public static IReadOnlyList<SchemaMigration> Inquiries { get; } = new List<SchemaMigration>
        {
            new(1001, "Converge legacy inquiry databases to the current model", connection =>
                ConvergeInquirySchema(connection)),
            // 1002 runs after 1001 so customers who already upgraded past
            // 1001 (and therefore still have the legacy InquiryDate column)
            // get the drop. We split it out as a separate migration
            // rather than mutating 1001, which is the documented baseline
            // for what EnsureCreated should look like after first run.
            new(1002, "Drop legacy Inquiry columns (InquiryDate, etc.) not in the model", connection =>
                DropLegacyInquiriesColumns(connection)),
            // PHASE 2 — scope of supply (BOQ) on inquiries.
            // EnsureCreated only creates missing DATABASE files; any install
            // that already ran the app has inquiries.db without this table.
            // Idempotent: CREATE TABLE IF NOT EXISTS + IF NOT EXISTS indexes.
            new(1003, "Inquiry scope of supply (BOQ line items)", connection =>
            {
                SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                    CREATE TABLE IF NOT EXISTS ""InquiryLineItems"" (
                        ""Id""              INTEGER NOT NULL CONSTRAINT ""PK_InquiryLineItems"" PRIMARY KEY AUTOINCREMENT,
                        ""InquiryId""       INTEGER NOT NULL,
                        ""SortOrder""       INTEGER NOT NULL DEFAULT 0,
                        ""GlassType""       TEXT NOT NULL DEFAULT '',
                        ""SpecName""        TEXT NOT NULL DEFAULT '',
                        ""OuterGlassColor"" TEXT NOT NULL DEFAULT '',
                        ""InnerGlassColor"" TEXT NOT NULL DEFAULT '',
                        ""WidthMm""         INTEGER NOT NULL DEFAULT 0,
                        ""HeightMm""        INTEGER NOT NULL DEFAULT 0,
                        ""Qty""             INTEGER NOT NULL DEFAULT 0,
                        ""RatePerSqm""      TEXT NOT NULL DEFAULT '0',
                        ""Notes""           TEXT NOT NULL DEFAULT '',
                        ""CreatedAt""       TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
                    );");
                SchemaMigrationSql.CreateIndexIfNotExists(connection,
                    "CREATE INDEX IF NOT EXISTS \"IX_InquiryLineItems_InquiryId\" ON InquiryLineItems(InquiryId);");
                SchemaMigrationSql.CreateIndexIfNotExists(connection,
                    "CREATE INDEX IF NOT EXISTS \"IX_InquiryLineItems_InquiryId_SortOrder\" ON InquiryLineItems(InquiryId, SortOrder);");
            })
        };

        // proformainvoices.db
        public static IReadOnlyList<SchemaMigration> ProformaInvoices { get; }
            = new List<SchemaMigration>
            {
                // Use a reserved high version: customer databases may already
                // carry PRAGMA user_version values from older installers even
                // though this registry was previously empty. Version 1 was
                // therefore incorrectly skipped, leaving InquiryId absent.
                new(1001, "Add optional stable Inquiry relationship", connection =>
                {
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProformaInvoices", "InquiryId", "INTEGER NULL");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProformaInvoices_InquiryId ON ProformaInvoices(InquiryId);");
                }),

                // ERP lifecycle hardening (Phase 1): void / soft-close columns.
                new(1002, "Proforma Invoice: void / soft-close columns", connection =>
                    AddVoidColumns(connection, "ProformaInvoices")),

                // ERP hardening (Phase 5): optimistic-concurrency counter.
                new(1003, "Proforma Invoice: RowVersion concurrency counter", connection =>
                    AddRowVersionColumn(connection, "ProformaInvoices")),

                // PI REVISIONS (2026-09-11): linked-draft revision chain.
                // The original document is never rewritten; each revision is
                // a new row sharing the original's RootProformaId.
                new(1004, "Proforma Invoice: revision chain columns", connection =>
                {
                    // ERR-C7CC11 LESSON (2026-09-11): RevisionNumber MUST be
                    // NOT NULL DEFAULT 0 — the house pattern (see
                    // AddVoidColumns/AddRowVersionColumn). The original NULL
                    // DDL left pre-existing rows with NULL, and EF threw
                    // "data is NULL at ordinal 14" materializing the
                    // non-nullable int property on every PI load.
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProformaInvoices", "RootProformaId", "TEXT NULL");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProformaInvoices", "RevisionNumber", "INTEGER NOT NULL DEFAULT 0");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProformaInvoices_RootProformaId ON ProformaInvoices(RootProformaId);");
                }),

                // ERR-C7CC11 REPAIR (2026-09-11): databases that already ran
                // the original NULL-DDL 1004 carry NULL RevisionNumber (and
                // blank RootProformaId) on every pre-revision row. Backfill:
                // originals become revision 0 and their own chain root —
                // exactly what GetRevisionChainAsync already treats them as.
                new(1005, "Proforma Invoice: backfill revision-chain columns", connection =>
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText =
                            "UPDATE ProformaInvoices SET RevisionNumber = 0 " +
                            "WHERE RevisionNumber IS NULL;";
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText =
                            "UPDATE ProformaInvoices SET RootProformaId = ProformaId " +
                            "WHERE RootProformaId IS NULL OR RootProformaId = '';";
                        cmd.ExecuteNonQuery();
                    }
                }),

                // Unique ProformaId is in OnModelCreating (fresh EnsureCreated).
                // Existing field DBs never received it via additive migration.
                // Do NOT rewrite colliding numbers (JO/TI/attachments key off
                // the string). If duplicates exist, log and leave the app-level
                // insert retry as the safety net.
                new(1006, "Proforma Invoice: unique ProformaId index", connection =>
                {
                    try
                    {
                        SchemaMigrationSql.CreateIndexIfNotExists(connection,
                            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ProformaInvoices_ProformaId ON ProformaInvoices(ProformaId);");
                    }
                    catch (SqliteException ex)
                    {
                        AppLogService.Warning(
                            "[ProformaInvoices] unique ProformaId index not applied: " +
                            ex.Message,
                            new LogContext
                            {
                                Module = "Database",
                                Class = "SchemaMigrations",
                                Operation = "ProformaIdUniqueIndex"
                            });
                    }
                })
            };

        // specifications.db
        public static IReadOnlyList<SchemaMigration> Specifications { get; }
            = new List<SchemaMigration>
            {
                // Specifications Center catalogue columns.
                //
                // EnsureCreated() only ever CREATES a missing database -- it
                // never alters an existing one. Every install that has
                // already run the app has a specifications.db without these
                // columns, so without this migration EF would query columns
                // that do not exist and the Step 5 dropdown would fail on
                // every load. Purely additive and idempotent.
                new(1001, "Specifications Center: catalogue + sheet-match columns", connection =>
                {
                    SchemaMigrationSql.AddColumnIfMissing(connection, "Specifications", "Category",      "TEXT NULL");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "Specifications", "Color",         "TEXT NULL");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "Specifications", "ThicknessMm",   "INTEGER NOT NULL DEFAULT 0");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "Specifications", "SheetWidthMm",  "INTEGER NOT NULL DEFAULT 0");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "Specifications", "SheetHeightMm", "INTEGER NOT NULL DEFAULT 0");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "Specifications", "SheetId",       "INTEGER NOT NULL DEFAULT 0");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "Specifications", "ProformaId",    "TEXT NULL");

                    // The Center filters by category and colour the way
                    // Sheet Store does.
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_Specifications_Category ON Specifications(Category);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_Specifications_Color ON Specifications(Color);");
                }),

                // Usage history. A specification is deduped on
                // GlassType + SpecName, so one row is shared by many
                // proformas -- a single ProformaId column could only ever
                // name one of them. This table records every use, plus the
                // price quoted AT THAT TIME (the spec's own PricePerSqm is
                // overwritten on each re-apply).
                new(1002, "Specifications Center: per-proforma usage history", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, """
                        CREATE TABLE IF NOT EXISTS SpecificationUsages (
                            Id              INTEGER NOT NULL CONSTRAINT PK_SpecificationUsages PRIMARY KEY AUTOINCREMENT,
                            SpecificationId INTEGER NOT NULL DEFAULT 0,
                            SpecName        TEXT    NOT NULL DEFAULT '',
                            GlassType       TEXT    NULL,
                            ProformaId      TEXT    NULL,
                            CustomerName    TEXT    NULL,
                            ProjectName     TEXT    NULL,
                            PricePerSqm     TEXT    NULL,
                            SheetWidthMm    INTEGER NOT NULL DEFAULT 0,
                            SheetHeightMm   INTEGER NOT NULL DEFAULT 0,
                            UserName        TEXT    NULL,
                            UsedAt          TEXT    NOT NULL
                        );
                        """);

                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_SpecificationUsages_SpecificationId ON SpecificationUsages(SpecificationId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_SpecificationUsages_ProformaId ON SpecificationUsages(ProformaId);");
                }),

                // Per-pane sheet matches. A DGU is two panes that can be cut
                // from different stock sizes; the single SheetWidthMm pair
                // could only ever describe the outer one.
                new(1003, "Specifications Center: per-pane sheet matches", connection =>
                {
                    SchemaMigrationSql.AddColumnIfMissing(
                        connection, "Specifications", "PanesJson", "TEXT NULL");
                }),

                // Ledger ground truth. Usage rows are derived from SAVED
                // proforma snapshots (SpecificationLedgerService), and the
                // snapshot's company name travels with each row so the
                // Center's last-activity card and history can name the
                // company without deserialising invoice JSON per row.
                new(1004, "Specifications ledger: company column on usage history", connection =>
                {
                    SchemaMigrationSql.AddColumnIfMissing(
                        connection, "SpecificationUsages", "CompanyName", "TEXT NULL");
                })
            };

        // creditnotes.db (ERP hardening — Phase 4)
        //
        // Brand-new database: EnsureCreated builds it at the current model, so
        // this registry starts empty. Future schema changes are added here as
        // versioned, additive migrations (same convention as every other DB).
        public static IReadOnlyList<SchemaMigration> CreditNotes { get; }
            = new List<SchemaMigration>();

        // masterdata.db
        public static IReadOnlyList<SchemaMigration> MasterData { get; }
            = new List<SchemaMigration>();

        // statements.db
        public static IReadOnlyList<SchemaMigration> Statements { get; }
            = new List<SchemaMigration>
            {
                // Unique StatementNumber is in OnModelCreating (fresh EnsureCreated).
                // Existing field DBs never received it via additive migration.
                new(1003, "Statement: unique StatementNumber index", connection =>
                {
                    try
                    {
                        SchemaMigrationSql.CreateIndexIfNotExists(connection,
                            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Statements_StatementNumber ON Statements(StatementNumber);");
                    }
                    catch (SqliteException ex)
                    {
                        AppLogService.Warning(
                            "[Statements] unique StatementNumber index not applied: " +
                            ex.Message,
                            new LogContext
                            {
                                Module = "Database",
                                Class = "SchemaMigrations",
                                Operation = "StatementNumberUniqueIndex"
                            });
                    }
                })
            };

        // aimemory.db
        public static IReadOnlyList<SchemaMigration> AiMemory { get; }
            = new List<SchemaMigration>();

        // production.db (Production distribution — Phase 2, 2026-09-11).
        // EnsureCreated() builds both tables from the EF model on first run;
        // additive schema changes for installs already in the field land here.
        public static IReadOnlyList<SchemaMigration> Production { get; }
            = new List<SchemaMigration>
            {
                // Screens redesign (2026-09-11): the salesman column feeds
                // the module list/detail views. Builds that already created
                // production.db before this column existed need it added,
                // or EF would query a column that does not exist.
                new(1001, "Production: salesman column", connection =>
                {
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionWorkItems", "Salesman", "TEXT NOT NULL DEFAULT ''");
                }),

                // Piece-flow (2026-09-11): every stage item tracks how many
                // pieces the floor has completed (QtyDone, 0..Qty). Only the
                // completed quantity arrives in the next stage — production
                // moves piece by piece, like a tool chain.
                new(1002, "Production: QtyDone piece-flow counter", connection =>
                {
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionWorkItems", "QtyDone", "INTEGER NOT NULL DEFAULT 0");
                }),

                // Piece-flow entries (2026-09-12): every "mark done" save
                // writes an entry (who + when) so the floor audit trail is
                // complete. EnsureCreated() builds the table for fresh
                // installs; this adds it for databases already in the field.
                new(1003, "Production: piece-flow entry log table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS ProductionEntries (
                            Id              INTEGER NOT NULL CONSTRAINT PK_ProductionEntries PRIMARY KEY AUTOINCREMENT,
                            WorkItemId      INTEGER NOT NULL,
                            JobRef          TEXT NOT NULL,
                            Stage           INTEGER NOT NULL DEFAULT 0,
                            SpecGroupNumber INTEGER NOT NULL DEFAULT 0,
                            SerialNumber    INTEGER NOT NULL DEFAULT 0,
                            Position        TEXT NOT NULL DEFAULT '',
                            SpecName        TEXT NOT NULL DEFAULT '',
                            GlassRef        TEXT NOT NULL DEFAULT '',
                            Qty             INTEGER NOT NULL DEFAULT 0,
                            DoneBy          TEXT NOT NULL DEFAULT '',
                            DoneAt          TEXT NOT NULL
                        );");

                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionEntry_WorkItemId ON ProductionEntries (WorkItemId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionEntry_Unit ON ProductionEntries (JobRef, Stage, SpecGroupNumber, SerialNumber);");
                }),

                // Piece-flow attribution (2026-09-12): the operator who
                // last completed a work item, shown in the detail page.
                new(1004, "Production: CompletedBy column", connection =>
                {
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionWorkItems", "CompletedBy", "TEXT NOT NULL DEFAULT ''");
                }),

                // Phase 14: FOC / Replacement / Cancellation columns on WorkItems
                new(1005, "Production: FOC / Replacement / Cancellation columns", connection =>
                {
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionWorkItems", "IsFoc", "INTEGER NOT NULL DEFAULT 0");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionWorkItems", "ReplacementSequence", "INTEGER NOT NULL DEFAULT 0");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionWorkItems", "ParentWorkItemId", "INTEGER NULL");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionWorkItems", "IsCancelled", "INTEGER NOT NULL DEFAULT 0");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionWorkItems", "CancelledBy", "TEXT NOT NULL DEFAULT ''");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionWorkItems", "CancelledAt", "TEXT NULL");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionWorkItems", "CancelReason", "TEXT NOT NULL DEFAULT ''");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionWorkItem_IsFoc ON ProductionWorkItems(IsFoc);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionWorkItem_IsCancelled ON ProductionWorkItems(IsCancelled);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionWorkItem_ParentWorkItemId ON ProductionWorkItems(ParentWorkItemId);");
                }),

                // Phase 14: EntryType / Breakage / Rework / FOC columns on Entries
                new(1006, "Production: EntryType / Breakage / Rework / FOC columns", connection =>
                {
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionEntries", "EntryType", "INTEGER NOT NULL DEFAULT 0");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionEntries", "Reason", "TEXT NOT NULL DEFAULT ''");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionEntries", "ParentEntryId", "INTEGER NULL");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionEntries", "ReplacementSequence", "INTEGER NOT NULL DEFAULT 0");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionEntries", "IsFoc", "INTEGER NOT NULL DEFAULT 0");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionEntries", "RelatedReservationId", "TEXT NOT NULL DEFAULT ''");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionEntries", "RelatedQcId", "TEXT NOT NULL DEFAULT ''");
                    SchemaMigrationSql.AddColumnIfMissing(connection, "ProductionEntries", "Notes", "TEXT NOT NULL DEFAULT ''");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionEntry_EntryType ON ProductionEntries(EntryType);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionEntry_ParentEntryId ON ProductionEntries(ParentEntryId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionEntry_IsFoc ON ProductionEntries(IsFoc);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionEntry_RelatedReservationId ON ProductionEntries(RelatedReservationId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionEntry_RelatedQcId ON ProductionEntries(RelatedQcId);");
                }),

                // Phase 14: Material Reservations table
                new(1007, "Production: MaterialReservations table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS MaterialReservations (
                            Id                      INTEGER NOT NULL CONSTRAINT PK_MaterialReservations PRIMARY KEY AUTOINCREMENT,
                            ReservationId           TEXT NOT NULL,
                            JobRef                  TEXT NOT NULL,
                            SourceProformaId        TEXT NOT NULL DEFAULT '',
                            SpecGroupNumber         INTEGER NOT NULL DEFAULT 0,
                            SerialNumber            INTEGER NOT NULL DEFAULT 0,
                            GlassRef                TEXT NOT NULL DEFAULT '',
                            SpecName                TEXT NOT NULL DEFAULT '',
                            OptimizationSessionId   TEXT NOT NULL DEFAULT '',
                            GlassSheetId            INTEGER NOT NULL DEFAULT 0,
                            StockSizeLabel          TEXT NOT NULL DEFAULT '',
                            WidthMm                 INTEGER NOT NULL DEFAULT 0,
                            HeightMm                INTEGER NOT NULL DEFAULT 0,
                            ThicknessMm             INTEGER NOT NULL DEFAULT 0,
                            Color                   TEXT NOT NULL DEFAULT '',
                            Quantity                INTEGER NOT NULL DEFAULT 0,
                            Status                  INTEGER NOT NULL DEFAULT 1,
                            CreatedBy               TEXT NOT NULL DEFAULT '',
                            CreatedAt               TEXT NOT NULL,
                            ReleasedAt              TEXT NULL,
                            ReleaseReason           TEXT NOT NULL DEFAULT '',
                            Notes                   TEXT NOT NULL DEFAULT ''
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_MaterialReservation_ReservationId_Unique ON MaterialReservations(ReservationId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_MaterialReservation_JobRef ON MaterialReservations(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_MaterialReservation_GlassSheetId ON MaterialReservations(GlassSheetId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_MaterialReservation_Status ON MaterialReservations(Status);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_MaterialReservation_OptSession ON MaterialReservations(OptimizationSessionId);");
                }),

                // Phase 14: Material Allocations table
                new(1008, "Production: MaterialAllocations table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS MaterialAllocations (
                            Id                      INTEGER NOT NULL CONSTRAINT PK_MaterialAllocations PRIMARY KEY AUTOINCREMENT,
                            AllocationId            TEXT NOT NULL,
                            ReservationId           TEXT NOT NULL,
                            JobRef                  TEXT NOT NULL,
                            SpecGroupNumber         INTEGER NOT NULL DEFAULT 0,
                            SerialNumber            INTEGER NOT NULL DEFAULT 0,
                            OptimizationSessionId   TEXT NOT NULL DEFAULT '',
                            SheetNo                 INTEGER NOT NULL DEFAULT 0,
                            PlacedPieceLabel        TEXT NOT NULL DEFAULT '',
                            GlassRef                TEXT NOT NULL DEFAULT '',
                            WidthMm                 INTEGER NOT NULL DEFAULT 0,
                            HeightMm                INTEGER NOT NULL DEFAULT 0,
                            Quantity                INTEGER NOT NULL DEFAULT 1,
                            UtilizationPct          TEXT NOT NULL DEFAULT '0',
                            WastagePct              TEXT NOT NULL DEFAULT '0',
                            Status                  INTEGER NOT NULL DEFAULT 2,
                            CreatedBy               TEXT NOT NULL DEFAULT '',
                            CreatedAt               TEXT NOT NULL,
                            ConsumedAt              TEXT NULL,
                            Notes                   TEXT NOT NULL DEFAULT ''
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_MaterialAllocation_AllocationId_Unique ON MaterialAllocations(AllocationId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_MaterialAllocation_ReservationId ON MaterialAllocations(ReservationId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_MaterialAllocation_JobRef ON MaterialAllocations(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_MaterialAllocation_OptSession ON MaterialAllocations(OptimizationSessionId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_MaterialAllocation_Status ON MaterialAllocations(Status);");
                }),

                // Phase 14: Production QC Entries table
                new(1009, "Production: ProductionQcEntries table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS ProductionQcEntries (
                            Id              INTEGER NOT NULL CONSTRAINT PK_ProductionQcEntries PRIMARY KEY AUTOINCREMENT,
                            QcId            TEXT NOT NULL,
                            WorkItemId      INTEGER NOT NULL,
                            JobRef          TEXT NOT NULL,
                            SourceProformaId TEXT NOT NULL DEFAULT '',
                            SpecGroupNumber INTEGER NOT NULL DEFAULT 0,
                            SerialNumber    INTEGER NOT NULL DEFAULT 0,
                            GlassRef        TEXT NOT NULL DEFAULT '',
                            SpecName        TEXT NOT NULL DEFAULT '',
                            Position        TEXT NOT NULL DEFAULT '',
                            Stage           INTEGER NOT NULL DEFAULT 0,
                            QcSequence      INTEGER NOT NULL DEFAULT 1,
                            Result          INTEGER NOT NULL DEFAULT 0,
                            Reason          TEXT NOT NULL DEFAULT '',
                            Inspector       TEXT NOT NULL DEFAULT '',
                            InspectedAt     TEXT NOT NULL,
                            Notes           TEXT NOT NULL DEFAULT '',
                            ParentQcId      TEXT NOT NULL DEFAULT '',
                            IsRework        INTEGER NOT NULL DEFAULT 0
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_ProductionQc_QcId_Unique ON ProductionQcEntries(QcId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionQc_WorkItemId ON ProductionQcEntries(WorkItemId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionQc_JobRef ON ProductionQcEntries(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionQc_Result ON ProductionQcEntries(Result);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProductionQc_Stage ON ProductionQcEntries(Stage);");
                }),

                // Phase 14: Actual Costs and Replacements tables
                new(1010, "Production: ActualCosts and Replacements tables", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS ProductionActualCosts (
                            Id                      INTEGER NOT NULL CONSTRAINT PK_ProductionActualCosts PRIMARY KEY AUTOINCREMENT,
                            CostId                  TEXT NOT NULL,
                            JobRef                  TEXT NOT NULL,
                            SourceProformaId        TEXT NOT NULL DEFAULT '',
                            SpecGroupNumber         INTEGER NOT NULL DEFAULT 0,
                            SerialNumber            INTEGER NOT NULL DEFAULT 0,
                            GlassRef                TEXT NOT NULL DEFAULT '',
                            SpecName                TEXT NOT NULL DEFAULT '',
                            CostType                INTEGER NOT NULL DEFAULT 1,
                            StockSheetId            INTEGER NULL,
                            MaterialAllocationId    TEXT NOT NULL DEFAULT '',
                            Quantity                TEXT NOT NULL DEFAULT '0',
                            UnitCost                TEXT NOT NULL DEFAULT '0',
                            TotalCost               TEXT NOT NULL DEFAULT '0',
                            SellingValue            TEXT NOT NULL DEFAULT '0',
                            IsFoc                   INTEGER NOT NULL DEFAULT 0,
                            CreatedBy               TEXT NOT NULL DEFAULT '',
                            CreatedAt               TEXT NOT NULL,
                            Notes                   TEXT NOT NULL DEFAULT ''
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_ActualCost_CostId_Unique ON ProductionActualCosts(CostId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ActualCost_JobRef ON ProductionActualCosts(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ActualCost_CostType ON ProductionActualCosts(CostType);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ActualCost_IsFoc ON ProductionActualCosts(IsFoc);");

                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS ProductionReplacements (
                            Id                      INTEGER NOT NULL CONSTRAINT PK_ProductionReplacements PRIMARY KEY AUTOINCREMENT,
                            ReplacementId           TEXT NOT NULL,
                            JobRef                  TEXT NOT NULL,
                            SourceProformaId        TEXT NOT NULL DEFAULT '',
                            SpecGroupNumber         INTEGER NOT NULL DEFAULT 0,
                            SerialNumber            INTEGER NOT NULL DEFAULT 0,
                            GlassRef                TEXT NOT NULL DEFAULT '',
                            SpecName                TEXT NOT NULL DEFAULT '',
                            Position                TEXT NOT NULL DEFAULT '',
                            Stage                   INTEGER NOT NULL DEFAULT 0,
                            OriginalWorkItemId      INTEGER NOT NULL,
                            OriginalEntryId         INTEGER NULL,
                            ReplacementWorkItemId   INTEGER NULL,
                            EntryType               INTEGER NOT NULL DEFAULT 2,
                            Reason                  TEXT NOT NULL DEFAULT '',
                            Quantity                INTEGER NOT NULL DEFAULT 1,
                            IsFoc                   INTEGER NOT NULL DEFAULT 0,
                            ReplacementSequence     INTEGER NOT NULL DEFAULT 1,
                            CreatedBy               TEXT NOT NULL DEFAULT '',
                            CreatedAt               TEXT NOT NULL,
                            Status                  INTEGER NOT NULL DEFAULT 0
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_Replacement_ReplacementId_Unique ON ProductionReplacements(ReplacementId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_Replacement_JobRef ON ProductionReplacements(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_Replacement_OriginalWorkItemId ON ProductionReplacements(OriginalWorkItemId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_Replacement_ReplacementWorkItemId ON ProductionReplacements(ReplacementWorkItemId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_Replacement_EntryType ON ProductionReplacements(EntryType);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_Replacement_IsFoc ON ProductionReplacements(IsFoc);");
                }),

                // Phase 15: Cutting Sessions, Sheets, Placed Pieces tables
                new(1011, "Production: CuttingSessions table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS CuttingSessions (
                            Id                      INTEGER NOT NULL CONSTRAINT PK_CuttingSessions PRIMARY KEY AUTOINCREMENT,
                            CuttingSessionId        TEXT NOT NULL,
                            JobRef                  TEXT NOT NULL,
                            SourceProformaId        TEXT NOT NULL DEFAULT '',
                            OptimizationSessionId   TEXT NOT NULL DEFAULT '',
                            MaterialGroupId         TEXT NOT NULL DEFAULT '',
                            TotalPieces             INTEGER NOT NULL DEFAULT 0,
                            SheetsUsed              INTEGER NOT NULL DEFAULT 0,
                            UtilizationPct          TEXT NOT NULL DEFAULT '0',
                            WastagePct              TEXT NOT NULL DEFAULT '0',
                            Status                  INTEGER NOT NULL DEFAULT 0,
                            RotationPolicy          INTEGER NOT NULL DEFAULT 1,
                            SplitMode               INTEGER NOT NULL DEFAULT 0,
                            EffortLevel             INTEGER NOT NULL DEFAULT 3,
                            MarginProfileJson       TEXT NOT NULL DEFAULT '',
                            IsVerified              INTEGER NOT NULL DEFAULT 0,
                            VerificationResult      TEXT NOT NULL DEFAULT '',
                            CreatedBy               TEXT NOT NULL DEFAULT '',
                            CreatedAt               TEXT NOT NULL,
                            CompletedAt             TEXT NULL,
                            Notes                   TEXT NOT NULL DEFAULT ''
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_CuttingSession_SessionId_Unique ON CuttingSessions(CuttingSessionId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingSession_JobRef ON CuttingSessions(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingSession_OptSession ON CuttingSessions(OptimizationSessionId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingSession_Status ON CuttingSessions(Status);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingSession_MaterialGroup ON CuttingSessions(MaterialGroupId);");
                }),

                new(1012, "Production: CuttingSheets table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS CuttingSheets (
                            Id                  INTEGER NOT NULL CONSTRAINT PK_CuttingSheets PRIMARY KEY AUTOINCREMENT,
                            CuttingSheetId      TEXT NOT NULL,
                            CuttingSessionId    TEXT NOT NULL,
                            JobRef              TEXT NOT NULL,
                            SheetNo             INTEGER NOT NULL DEFAULT 0,
                            StockSheetId        INTEGER NOT NULL DEFAULT 0,
                            StockSizeLabel      TEXT NOT NULL DEFAULT '',
                            StockWidthMm        INTEGER NOT NULL DEFAULT 0,
                            StockHeightMm       INTEGER NOT NULL DEFAULT 0,
                            ThicknessMm         INTEGER NOT NULL DEFAULT 0,
                            Color               TEXT NOT NULL DEFAULT '',
                            UsableX             INTEGER NOT NULL DEFAULT 0,
                            UsableY             INTEGER NOT NULL DEFAULT 0,
                            UsableWidth         INTEGER NOT NULL DEFAULT 0,
                            UsableHeight        INTEGER NOT NULL DEFAULT 0,
                            UsedSqm             TEXT NOT NULL DEFAULT '0',
                            StockSqm            TEXT NOT NULL DEFAULT '0',
                            UtilizationPct      TEXT NOT NULL DEFAULT '0',
                            WastagePct          TEXT NOT NULL DEFAULT '0',
                            IsVerified          INTEGER NOT NULL DEFAULT 0,
                            VerificationResult  TEXT NOT NULL DEFAULT '',
                            PlacedPieceCount    INTEGER NOT NULL DEFAULT 0,
                            CreatedBy           TEXT NOT NULL DEFAULT '',
                            CreatedAt           TEXT NOT NULL
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_CuttingSheet_SheetId_Unique ON CuttingSheets(CuttingSheetId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingSheet_SessionId ON CuttingSheets(CuttingSessionId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingSheet_JobRef ON CuttingSheets(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingSheet_StockSheetId ON CuttingSheets(StockSheetId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingSheet_SheetNo ON CuttingSheets(SheetNo);");
                }),

                new(1013, "Production: CuttingPlacedPieces table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS CuttingPlacedPieces (
                            Id                  INTEGER NOT NULL CONSTRAINT PK_CuttingPlacedPieces PRIMARY KEY AUTOINCREMENT,
                            PlacedPieceId       TEXT NOT NULL,
                            CuttingSheetId      TEXT NOT NULL,
                            CuttingSessionId    TEXT NOT NULL,
                            JobRef              TEXT NOT NULL,
                            SpecGroupNumber     INTEGER NOT NULL DEFAULT 0,
                            SerialNumber        INTEGER NOT NULL DEFAULT 0,
                            GlassRef            TEXT NOT NULL DEFAULT '',
                            SpecName            TEXT NOT NULL DEFAULT '',
                            Position            TEXT NOT NULL DEFAULT '',
                            PieceId             TEXT NOT NULL,
                            PieceLabel          TEXT NOT NULL DEFAULT '',
                            WidthMm             INTEGER NOT NULL DEFAULT 0,
                            HeightMm            INTEGER NOT NULL DEFAULT 0,
                            X                   INTEGER NOT NULL DEFAULT 0,
                            Y                   INTEGER NOT NULL DEFAULT 0,
                            IsRotated           INTEGER NOT NULL DEFAULT 0,
                            CutSequence         INTEGER NOT NULL DEFAULT 0,
                            Status              INTEGER NOT NULL DEFAULT 0,
                            SourceWorkItemId    INTEGER NOT NULL DEFAULT 0,
                            ReservationId       TEXT NOT NULL DEFAULT '',
                            AllocationId        TEXT NOT NULL DEFAULT '',
                            IsFoc               INTEGER NOT NULL DEFAULT 0,
                            CreatedBy           TEXT NOT NULL DEFAULT '',
                            CreatedAt           TEXT NOT NULL,
                            Notes               TEXT NOT NULL DEFAULT ''
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_CuttingPlacedPiece_PieceId_Unique ON CuttingPlacedPieces(PlacedPieceId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingPlacedPiece_SheetId ON CuttingPlacedPieces(CuttingSheetId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingPlacedPiece_SessionId ON CuttingPlacedPieces(CuttingSessionId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingPlacedPiece_JobRef ON CuttingPlacedPieces(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingPlacedPiece_PieceId ON CuttingPlacedPieces(PieceId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingPlacedPiece_SourceWorkItemId ON CuttingPlacedPieces(SourceWorkItemId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingPlacedPiece_Status ON CuttingPlacedPieces(Status);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingPlacedPiece_ReservationId ON CuttingPlacedPieces(ReservationId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_CuttingPlacedPiece_AllocationId ON CuttingPlacedPieces(AllocationId);");
                }),

                // Phase 16: Core Production Process Execution — shared framework
                new(1014, "Production: ProcessExecutions table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS ProcessExecutions (
                            Id                  INTEGER NOT NULL CONSTRAINT PK_ProcessExecutions PRIMARY KEY AUTOINCREMENT,
                            ExecutionId         TEXT NOT NULL,
                            JobRef              TEXT NOT NULL,
                            SourceProformaId    TEXT NOT NULL DEFAULT '',
                            JobOrderId          TEXT NOT NULL DEFAULT '',
                            SpecGroupNumber     INTEGER NOT NULL DEFAULT 0,
                            SpecName            TEXT NOT NULL DEFAULT '',
                            SerialNumber        INTEGER NOT NULL DEFAULT 0,
                            Position            TEXT NOT NULL DEFAULT '',
                            PieceId             TEXT NOT NULL DEFAULT '',
                            InputPieceId        TEXT NOT NULL DEFAULT '',
                            OutputPieceId       TEXT NOT NULL DEFAULT '',
                            ProcessType         INTEGER NOT NULL DEFAULT 1,
                            Sequence            INTEGER NOT NULL DEFAULT 0,
                            InputWorkItemId     INTEGER NOT NULL DEFAULT 0,
                            OutputWorkItemId    INTEGER NOT NULL DEFAULT 0,
                            Status              INTEGER NOT NULL DEFAULT 0,
                            StartedBy           TEXT NOT NULL DEFAULT '',
                            StartedAt           TEXT NULL,
                            CompletedBy         TEXT NOT NULL DEFAULT '',
                            CompletedAt         TEXT NULL,
                            QtyRequired         INTEGER NOT NULL DEFAULT 0,
                            QtyDone             INTEGER NOT NULL DEFAULT 0,
                            EntryType           INTEGER NOT NULL DEFAULT 0,
                            Reason              TEXT NOT NULL DEFAULT '',
                            Notes               TEXT NOT NULL DEFAULT '',
                            QcStatus            INTEGER NOT NULL DEFAULT 0,
                            QcId                TEXT NOT NULL DEFAULT '',
                            ParentExecutionId   TEXT NOT NULL DEFAULT '',
                            ReworkSequence      INTEGER NOT NULL DEFAULT 0,
                            IsFoc               INTEGER NOT NULL DEFAULT 0,
                            PreviousStage       TEXT NOT NULL DEFAULT '',
                            NextStage           TEXT NOT NULL DEFAULT '',
                            ProcessDetailJson   TEXT NOT NULL DEFAULT '',
                            CreatedBy           TEXT NOT NULL DEFAULT '',
                            CreatedAt           TEXT NOT NULL,
                            UpdatedAt           TEXT NOT NULL
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_ProcessExecution_ExecutionId_Unique ON ProcessExecutions(ExecutionId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessExecution_JobRef ON ProcessExecutions(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessExecution_ProcessType ON ProcessExecutions(ProcessType);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessExecution_Status ON ProcessExecutions(Status);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessExecution_PieceId ON ProcessExecutions(PieceId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessExecution_OutputWorkItemId ON ProcessExecutions(OutputWorkItemId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessExecution_InputWorkItemId ON ProcessExecutions(InputWorkItemId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessExecution_ParentExecutionId ON ProcessExecutions(ParentExecutionId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessExecution_Unit ON ProcessExecutions(JobRef, SpecGroupNumber, SerialNumber);");
                }),

                new(1015, "Production: ProcessPairings table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS ProcessPairings (
                            Id                  INTEGER NOT NULL CONSTRAINT PK_ProcessPairings PRIMARY KEY AUTOINCREMENT,
                            PairingId           TEXT NOT NULL,
                            JobRef              TEXT NOT NULL,
                            SourceProformaId    TEXT NOT NULL DEFAULT '',
                            SpecGroupNumber     INTEGER NOT NULL DEFAULT 0,
                            SerialNumber        INTEGER NOT NULL DEFAULT 0,
                            Position            TEXT NOT NULL DEFAULT '',
                            PairId              TEXT NOT NULL DEFAULT '',
                            OuterPieceId        TEXT NOT NULL DEFAULT '',
                            InnerPieceId        TEXT NOT NULL DEFAULT '',
                            OuterGlassRef       TEXT NOT NULL DEFAULT '',
                            InnerGlassRef       TEXT NOT NULL DEFAULT '',
                            OuterWorkItemId     INTEGER NOT NULL DEFAULT 0,
                            InnerWorkItemId     INTEGER NOT NULL DEFAULT 0,
                            OuterExecutionId    TEXT NOT NULL DEFAULT '',
                            InnerExecutionId    TEXT NOT NULL DEFAULT '',
                            Status              INTEGER NOT NULL DEFAULT 0,
                            CreatedBy           TEXT NOT NULL DEFAULT '',
                            CreatedAt           TEXT NOT NULL,
                            Notes               TEXT NOT NULL DEFAULT '',
                            IsFoc               INTEGER NOT NULL DEFAULT 0
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_ProcessPairing_PairingId_Unique ON ProcessPairings(PairingId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessPairing_JobRef ON ProcessPairings(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessPairing_PairId ON ProcessPairings(PairId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessPairing_Unit ON ProcessPairings(JobRef, SpecGroupNumber, SerialNumber);");
                }),

                new(1016, "Production: ProcessAssemblies table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS ProcessAssemblies (
                            Id                  INTEGER NOT NULL CONSTRAINT PK_ProcessAssemblies PRIMARY KEY AUTOINCREMENT,
                            AssemblyId          TEXT NOT NULL,
                            JobRef              TEXT NOT NULL,
                            SourceProformaId    TEXT NOT NULL DEFAULT '',
                            SpecGroupNumber     INTEGER NOT NULL DEFAULT 0,
                            SerialNumber        INTEGER NOT NULL DEFAULT 0,
                            Position            TEXT NOT NULL DEFAULT '',
                            AssemblyLabel       TEXT NOT NULL DEFAULT '',
                            AssemblyType        TEXT NOT NULL DEFAULT '',
                            ComponentsJson      TEXT NOT NULL DEFAULT '',
                            SpacerInfo          TEXT NOT NULL DEFAULT '',
                            InterlayerInfo      TEXT NOT NULL DEFAULT '',
                            SealantInfo         TEXT NOT NULL DEFAULT '',
                            PairingId           TEXT NOT NULL DEFAULT '',
                            Status              INTEGER NOT NULL DEFAULT 0,
                            QtyRequired         INTEGER NOT NULL DEFAULT 0,
                            QtyDone             INTEGER NOT NULL DEFAULT 0,
                            CreatedBy           TEXT NOT NULL DEFAULT '',
                            CreatedAt           TEXT NOT NULL,
                            CompletedAt         TEXT NULL,
                            Notes               TEXT NOT NULL DEFAULT '',
                            IsFoc               INTEGER NOT NULL DEFAULT 0,
                            QcId                TEXT NOT NULL DEFAULT ''
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_ProcessAssembly_AssemblyId_Unique ON ProcessAssemblies(AssemblyId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessAssembly_JobRef ON ProcessAssemblies(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessAssembly_AssemblyLabel ON ProcessAssemblies(AssemblyLabel);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessAssembly_PairingId ON ProcessAssemblies(PairingId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ProcessAssembly_Unit ON ProcessAssemblies(JobRef, SpecGroupNumber, SerialNumber);");
                }),

                // Phase 17: Production Control & Factory Readiness — Workstations master
                new(1017, "Production: Workstations master table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS Workstations (
                            Id              INTEGER NOT NULL CONSTRAINT PK_Workstations PRIMARY KEY AUTOINCREMENT,
                            WorkstationId   TEXT NOT NULL,
                            Code            TEXT NOT NULL,
                            Name            TEXT NOT NULL,
                            ProcessType     INTEGER NOT NULL DEFAULT 1,
                            ProcessName     TEXT NOT NULL DEFAULT '',
                            IsActive        INTEGER NOT NULL DEFAULT 1,
                            Description     TEXT NOT NULL DEFAULT '',
                            CreatedBy       TEXT NOT NULL DEFAULT '',
                            CreatedAt       TEXT NOT NULL,
                            UpdatedAt       TEXT NOT NULL
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_Workstation_WorkstationId_Unique ON Workstations(WorkstationId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_Workstation_Code ON Workstations(Code);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_Workstation_ProcessType ON Workstations(ProcessType);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_Workstation_IsActive ON Workstations(IsActive);");
                }),

                new(1018, "Production: ProductionControlHolds table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS ProductionControlHolds (
                            Id              INTEGER NOT NULL CONSTRAINT PK_ProductionControlHolds PRIMARY KEY AUTOINCREMENT,
                            HoldId          TEXT NOT NULL,
                            WorkItemId      INTEGER NOT NULL,
                            JobRef          TEXT NOT NULL,
                            SpecGroupNumber INTEGER NOT NULL DEFAULT 0,
                            SerialNumber    INTEGER NOT NULL DEFAULT 0,
                            Position        TEXT NOT NULL DEFAULT '',
                            Stage           INTEGER NOT NULL DEFAULT 0,
                            HoldReason      TEXT NOT NULL DEFAULT '',
                            HeldBy          TEXT NOT NULL DEFAULT '',
                            HeldAt          TEXT NOT NULL,
                            ReleasedBy      TEXT NOT NULL DEFAULT '',
                            ReleasedAt      TEXT NULL,
                            IsReleased      INTEGER NOT NULL DEFAULT 0,
                            Notes           TEXT NOT NULL DEFAULT '',
                            CreatedAt       TEXT NOT NULL,
                            UpdatedAt       TEXT NOT NULL
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_ControlHold_HoldId_Unique ON ProductionControlHolds(HoldId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ControlHold_WorkItemId ON ProductionControlHolds(WorkItemId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ControlHold_JobRef ON ProductionControlHolds(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ControlHold_IsReleased ON ProductionControlHolds(IsReleased);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ControlHold_Unit ON ProductionControlHolds(JobRef, SpecGroupNumber, SerialNumber);");
                }),

                new(1019, "Production: ProductionControlAssignments table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS ProductionControlAssignments (
                            Id              INTEGER NOT NULL CONSTRAINT PK_ProductionControlAssignments PRIMARY KEY AUTOINCREMENT,
                            AssignmentId    TEXT NOT NULL,
                            WorkItemId      INTEGER NOT NULL,
                            JobRef          TEXT NOT NULL,
                            SpecGroupNumber INTEGER NOT NULL DEFAULT 0,
                            SerialNumber    INTEGER NOT NULL DEFAULT 0,
                            Position        TEXT NOT NULL DEFAULT '',
                            Stage           INTEGER NOT NULL DEFAULT 0,
                            WorkstationId   TEXT NOT NULL DEFAULT '',
                            Operator        TEXT NOT NULL DEFAULT '',
                            AssignedBy      TEXT NOT NULL DEFAULT '',
                            AssignedAt      TEXT NOT NULL,
                            UnassignedBy    TEXT NOT NULL DEFAULT '',
                            UnassignedAt    TEXT NULL,
                            IsActive        INTEGER NOT NULL DEFAULT 1,
                            Notes           TEXT NOT NULL DEFAULT '',
                            CreatedAt       TEXT NOT NULL,
                            UpdatedAt       TEXT NOT NULL
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_ControlAssignment_AssignmentId_Unique ON ProductionControlAssignments(AssignmentId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ControlAssignment_WorkItemId ON ProductionControlAssignments(WorkItemId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ControlAssignment_JobRef ON ProductionControlAssignments(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ControlAssignment_WorkstationId ON ProductionControlAssignments(WorkstationId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ControlAssignment_IsActive ON ProductionControlAssignments(IsActive);");
                }),

                new(1020, "Production: ProductionControlOverrides table", connection =>
                {
                    SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                        CREATE TABLE IF NOT EXISTS ProductionControlOverrides (
                            Id              INTEGER NOT NULL CONSTRAINT PK_ProductionControlOverrides PRIMARY KEY AUTOINCREMENT,
                            ControlId       TEXT NOT NULL,
                            WorkItemId      INTEGER NOT NULL,
                            JobRef          TEXT NOT NULL,
                            SpecGroupNumber INTEGER NOT NULL DEFAULT 0,
                            SerialNumber    INTEGER NOT NULL DEFAULT 0,
                            Position        TEXT NOT NULL DEFAULT '',
                            Stage           INTEGER NOT NULL DEFAULT 0,
                            Priority        INTEGER NOT NULL DEFAULT 0,
                            DueDate         TEXT NULL,
                            PrioritySetBy   TEXT NOT NULL DEFAULT '',
                            PrioritySetAt   TEXT NOT NULL,
                            Notes           TEXT NOT NULL DEFAULT '',
                            CreatedAt       TEXT NOT NULL,
                            UpdatedAt       TEXT NOT NULL
                        );");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE UNIQUE INDEX IF NOT EXISTS IX_ControlOverride_ControlId_Unique ON ProductionControlOverrides(ControlId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ControlOverride_WorkItemId ON ProductionControlOverrides(WorkItemId);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ControlOverride_JobRef ON ProductionControlOverrides(JobRef);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ControlOverride_Priority ON ProductionControlOverrides(Priority);");
                    SchemaMigrationSql.CreateIndexIfNotExists(connection,
                        "CREATE INDEX IF NOT EXISTS IX_ControlOverride_DueDate ON ProductionControlOverrides(DueDate);");
                })
            };

        // ── shared: ERP lifecycle void / soft-close columns ─────────────

        /// <summary>
        /// Adds the four void / soft-close columns to a saved-document table
        /// (ProformaInvoices / JobOrders / DeliveryNotes / TaxInvoices).
        /// Idempotent — a fresh table created by EnsureCreated already carries
        /// them, so each ADD COLUMN is a no-op. IsVoid is INTEGER (SQLite bool)
        /// NOT NULL DEFAULT 0, so every existing row is "live" after upgrade.
        /// The rest are nullable audit fields, null until a void happens.
        /// </summary>
        private static void AddVoidColumns(SqliteConnection connection, string table)
        {
            SchemaMigrationSql.AddColumnIfMissing(connection, table, "IsVoid",      "INTEGER NOT NULL DEFAULT 0");
            SchemaMigrationSql.AddColumnIfMissing(connection, table, "VoidedAtUtc", "TEXT NULL");
            SchemaMigrationSql.AddColumnIfMissing(connection, table, "VoidedBy",    "TEXT NULL");
            SchemaMigrationSql.AddColumnIfMissing(connection, table, "VoidReason",  "TEXT NULL");

            SchemaMigrationSql.CreateIndexIfNotExists(connection,
                $"CREATE INDEX IF NOT EXISTS IX_{table}_IsVoid ON {table}(IsVoid);");
        }

        // ── shared: ERP optimistic-concurrency counter (Phase 5) ────────

        /// <summary>
        /// Adds the RowVersion optimistic-concurrency counter to a saved-
        /// document table. Idempotent — a fresh table created by EnsureCreated
        /// already carries it, so the ADD COLUMN is a no-op. INTEGER NOT NULL
        /// DEFAULT 0, so every existing row starts at version 0 and the first
        /// save moves it to 1.
        /// </summary>
        private static void AddRowVersionColumn(SqliteConnection connection, string table)
        {
            SchemaMigrationSql.AddColumnIfMissing(connection, table, "RowVersion", "INTEGER NOT NULL DEFAULT 0");
        }

        // ── inquiries.db helpers (migration 1001) ───────────────────────

        /// <summary>
        /// Brings any legacy inquiries.db to the current InquiryRecord /
        /// InquiryAttachment / InquiryCommunication shape without touching
        /// existing rows. Every step is idempotent, so a database created by
        /// EnsureCreated at the current schema is a complete no-op.
        /// </summary>
        private static void ConvergeInquirySchema(SqliteConnection connection)
        {
            // 1) Tables missing entirely on the earliest builds.
            SchemaMigrationSql.CreateTableIfNotExists(connection, """
                CREATE TABLE IF NOT EXISTS "Inquiries" (
                    "Id"                 INTEGER NOT NULL CONSTRAINT "PK_Inquiries" PRIMARY KEY AUTOINCREMENT,
                    "InquiryNumber"      TEXT NOT NULL DEFAULT '',
                    "InquiryReference"   TEXT NOT NULL DEFAULT 'Customer Inquiry',
                    "CustomerId"         INTEGER NULL,
                    "CustomerName"       TEXT NOT NULL DEFAULT '',
                    "CustomerLocation"   TEXT NOT NULL DEFAULT '',
                    "ProjectId"          INTEGER NULL,
                    "ProjectName"        TEXT NOT NULL DEFAULT '',
                    "ProjectDescription" TEXT NOT NULL DEFAULT '',
                    "SalesmanId"         INTEGER NULL,
                    "SalesmanName"       TEXT NOT NULL DEFAULT '',
                    "PreparedByUserId"   INTEGER NULL,
                    "PreparedByName"     TEXT NOT NULL DEFAULT '',
                    "PreparedByRole"     TEXT NOT NULL DEFAULT '',
                    "Status"             TEXT NOT NULL DEFAULT 'Received',
                    "Priority"           TEXT NOT NULL DEFAULT 'Normal',
                    "InquiryValue"       TEXT NULL DEFAULT '0',
                    "Notes"              TEXT NOT NULL DEFAULT '',
                    "ReceivedAt"         TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    "PreparedAt"         TEXT NULL,
                    "SentAt"             TEXT NULL,
                    "ConfirmedAt"        TEXT NULL,
                    "CreatedAt"          TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    "UpdatedAt"          TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    "UpdatedBy"          TEXT NOT NULL DEFAULT '',
                    "ProformaInvoiceId"  INTEGER NULL,
                    "ProformaNumber"     TEXT NOT NULL DEFAULT '',
                    "JobOrderNumber"     TEXT NOT NULL DEFAULT ''
                );
                """);
            SchemaMigrationSql.CreateTableIfNotExists(connection, """
                CREATE TABLE IF NOT EXISTS "InquiryAttachments" (
                    "Id"             INTEGER NOT NULL CONSTRAINT "PK_InquiryAttachments" PRIMARY KEY AUTOINCREMENT,
                    "InquiryId"      INTEGER NOT NULL,
                    "FileName"       TEXT NOT NULL DEFAULT '',
                    "Extension"      TEXT NOT NULL DEFAULT '',
                    "FileSize"       INTEGER NOT NULL DEFAULT 0,
                    "StoragePath"    TEXT NOT NULL DEFAULT '',
                    "AttachmentType" TEXT NOT NULL DEFAULT 'Document',
                    "UploadedBy"     TEXT NOT NULL DEFAULT '',
                    "UploadedAt"     TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    CONSTRAINT "FK_InquiryAttachments_Inquiries_InquiryId"
                        FOREIGN KEY ("InquiryId") REFERENCES "Inquiries" ("Id") ON DELETE CASCADE
                );
                """);
            SchemaMigrationSql.CreateTableIfNotExists(connection, """
                CREATE TABLE IF NOT EXISTS "InquiryCommunications" (
                    "Id"          INTEGER NOT NULL CONSTRAINT "PK_InquiryCommunications" PRIMARY KEY AUTOINCREMENT,
                    "InquiryId"   INTEGER NOT NULL,
                    "EventType"   TEXT NOT NULL DEFAULT '',
                    "Title"       TEXT NOT NULL DEFAULT '',
                    "Description" TEXT NOT NULL DEFAULT '',
                    "OccurredAt"  TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    "ActorUserId" INTEGER NULL,
                    "ActorName"   TEXT NOT NULL DEFAULT '',
                    CONSTRAINT "FK_InquiryCommunications_Inquiries_InquiryId"
                        FOREIGN KEY ("InquiryId") REFERENCES "Inquiries" ("Id") ON DELETE CASCADE
                );
                """);

            // 2) Columns missing on interim builds. ALTER TABLE ADD COLUMN
            //    fills existing rows with the DEFAULT, so EF can read them.
            (string Table, string Column, string Definition)[] columns =
            {
                ("Inquiries", "InquiryNumber",      "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "InquiryReference",   "TEXT NOT NULL DEFAULT 'Customer Inquiry'"),
                ("Inquiries", "CustomerId",         "INTEGER NULL"),
                ("Inquiries", "CustomerName",       "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "CustomerLocation",   "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "ProjectId",          "INTEGER NULL"),
                ("Inquiries", "ProjectName",        "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "ProjectDescription", "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "SalesmanId",         "INTEGER NULL"),
                ("Inquiries", "SalesmanName",       "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "PreparedByUserId",   "INTEGER NULL"),
                ("Inquiries", "PreparedByName",     "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "PreparedByRole",     "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "Status",             "TEXT NOT NULL DEFAULT 'Received'"),
                ("Inquiries", "Priority",           "TEXT NOT NULL DEFAULT 'Normal'"),
                ("Inquiries", "InquiryValue",       "TEXT NULL DEFAULT '0'"),
                ("Inquiries", "Notes",              "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "ReceivedAt",         "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
                ("Inquiries", "PreparedAt",         "TEXT NULL"),
                ("Inquiries", "SentAt",             "TEXT NULL"),
                ("Inquiries", "ConfirmedAt",        "TEXT NULL"),
                ("Inquiries", "CreatedAt",          "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
                ("Inquiries", "UpdatedAt",          "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
                ("Inquiries", "UpdatedBy",          "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "ProformaInvoiceId",  "INTEGER NULL"),
                ("Inquiries", "ProformaNumber",     "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "JobOrderNumber",     "TEXT NOT NULL DEFAULT ''"),
                ("InquiryAttachments", "InquiryId",      "INTEGER NOT NULL DEFAULT 0"),
                ("InquiryAttachments", "FileName",       "TEXT NOT NULL DEFAULT ''"),
                ("InquiryAttachments", "Extension",      "TEXT NOT NULL DEFAULT ''"),
                ("InquiryAttachments", "FileSize",       "INTEGER NOT NULL DEFAULT 0"),
                ("InquiryAttachments", "StoragePath",    "TEXT NOT NULL DEFAULT ''"),
                ("InquiryAttachments", "AttachmentType", "TEXT NOT NULL DEFAULT 'Document'"),
                ("InquiryAttachments", "UploadedBy",     "TEXT NOT NULL DEFAULT ''"),
                ("InquiryAttachments", "UploadedAt",     "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
                ("InquiryCommunications", "InquiryId",   "INTEGER NOT NULL DEFAULT 0"),
                ("InquiryCommunications", "EventType",   "TEXT NOT NULL DEFAULT ''"),
                ("InquiryCommunications", "Title",       "TEXT NOT NULL DEFAULT ''"),
                ("InquiryCommunications", "Description", "TEXT NOT NULL DEFAULT ''"),
                ("InquiryCommunications", "OccurredAt",  "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
                ("InquiryCommunications", "ActorUserId", "INTEGER NULL"),
                ("InquiryCommunications", "ActorName",   "TEXT NOT NULL DEFAULT ''"),
            };
            foreach (var (table, column, definition) in columns)
                SchemaMigrationSql.AddColumnIfMissing(connection, table, column, definition);

            // 3) Rows with duplicate InquiryNumbers (possible from the era
            //    before the unique index) would make step 4 fail. Keep the
            //    oldest row's number, make later duplicates unique.
            SchemaMigrationSql.Execute(connection, """
                UPDATE "Inquiries"
                   SET "InquiryNumber" = "InquiryNumber" || '-' || "Id"
                 WHERE "Id" NOT IN (SELECT MIN("Id") FROM "Inquiries" GROUP BY "InquiryNumber")
                   AND "InquiryNumber" IN (SELECT "InquiryNumber" FROM "Inquiries"
                                            GROUP BY "InquiryNumber" HAVING COUNT(*) > 1);
                """);

            // 4) Indexes. The unique inquiry-number index is best-effort:
            //    column convergence above must never be blocked by data.
            foreach (string sql in new[]
            {
                "CREATE INDEX IF NOT EXISTS \"IX_Inquiries_ReceivedAt\" ON \"Inquiries\" (\"ReceivedAt\");",
                "CREATE INDEX IF NOT EXISTS \"IX_Inquiries_UpdatedAt\" ON \"Inquiries\" (\"UpdatedAt\");",
                "CREATE INDEX IF NOT EXISTS \"IX_Inquiries_Status\" ON \"Inquiries\" (\"Status\");",
                "CREATE INDEX IF NOT EXISTS \"IX_Inquiries_CustomerId\" ON \"Inquiries\" (\"CustomerId\");",
                "CREATE INDEX IF NOT EXISTS \"IX_Inquiries_SalesmanId\" ON \"Inquiries\" (\"SalesmanId\");",
                "CREATE INDEX IF NOT EXISTS \"IX_Inquiries_PreparedByUserId\" ON \"Inquiries\" (\"PreparedByUserId\");",
                "CREATE INDEX IF NOT EXISTS \"IX_InquiryAttachments_InquiryId\" ON \"InquiryAttachments\" (\"InquiryId\");",
                "CREATE INDEX IF NOT EXISTS \"IX_InquiryAttachments_UploadedAt\" ON \"InquiryAttachments\" (\"UploadedAt\");",
                "CREATE INDEX IF NOT EXISTS \"IX_InquiryCommunications_InquiryId_OccurredAt\" ON \"InquiryCommunications\" (\"InquiryId\", \"OccurredAt\");",
            })
            {
                SchemaMigrationSql.CreateIndexIfNotExists(connection, sql);
            }
            try
            {
                SchemaMigrationSql.CreateIndexIfNotExists(connection,
                    "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Inquiries_InquiryNumber\" ON \"Inquiries\" (\"InquiryNumber\");");
            }
            catch (SqliteException ex)
            {
                // Non-fatal on purpose: the register still works without the
                // constraint, and the repository's number generator already
                // regenerates on unique collisions.
                ProGlassAutomation.Services.Logging.AppLogService.Debug("[AppSchemaMigrations] Unique-index creation skipped: " + ex.Message);
            }
        }

        /// <summary>
        /// Migration 1002: drop any column that exists in the live
        /// Inquiries table but isn't in the expected model, AND
        /// rename known legacy columns to their current names so
        /// user data isn't lost. Interim builds (before 1001)
        /// shipped a different InquiryRecord shape — most notably a
        /// required NOT NULL column called "InquiryDate" that was
        /// later renamed to "ReceivedAt", and a "Salesman" column
        /// that was later renamed to "SalesmanName".
        ///
        /// 1001 added the new columns and indexes but never renamed
        /// or removed the legacy ones, so every INSERT fails with
        /// SQLite error 19 (NOT NULL constraint failed) on the
        /// leftover column because EF's INSERT statement omits it
        /// (it's not in the model) and SQLite tries to apply the
        /// (missing) DEFAULT. The user's first symptom is the
        /// "Attachment failed" dialog when the editor's auto-save
        /// runs before the first attachment on a brand-new inquiry.
        ///
        /// This step runs after 1001 and is idempotent: a fresh
        /// database that was just CREATE-TABLE'd in step 1 has
        /// only the expected columns, so the rename / drop
        /// passes are both no-ops.
        /// </summary>
        private static void DropLegacyInquiriesColumns(SqliteConnection connection)
        {
            // 0) Loud pre-migration snapshot. Logs the live column
            //    list so an operator can correlate "my DB has a
            //    Salesman column" with what the migration did. This
            //    is the first place to look when a user reports a
            //    NOT NULL constraint failure that the migration was
            //    supposed to fix.
            try
            {
                using var pre = connection.CreateCommand();
                pre.CommandText = "PRAGMA table_info(\"Inquiries\");";
                using var reader = pre.ExecuteReader();
                var before = new List<string>();
                while (reader.Read()) before.Add(reader.GetString(1));
                AppLogService.Info(
                    $"[Inquiries] pre-migration columns: {string.Join(", ", before)}",
                    new LogContext { Module = "Database", Class = "SchemaMigrations", Operation = "SnapshotInquiriesSchema" });
            }
            catch { /* snapshot logging is best-effort */ }

            // 1) Rename known legacy columns so user data is preserved.
            //    Maps "old name" → "new name". Each entry is run as
            //    "ALTER TABLE Inquiries RENAME COLUMN old TO new"
            //    which is supported on SQLite 3.25+. The runner
            //    ignores failures (the column may not exist on a
            //    particular install, OR the new name may already
            //    exist from migration 1001) and logs to AppLogService
            //    so ops can see what actually happened. The drop pass
            //    below still runs after the renames, so a renamed
            //    column with a duplicate new-name is cleaned up by
            //    the orphan drop.
            var renames = new (string OldName, string NewName)[]
            {
                ("InquiryDate", "ReceivedAt"),     // pre-2025 build
                ("Salesman",    "SalesmanName"),   // pre-2025 build (no "Name" suffix)
                ("Customer",    "CustomerName"),   // pre-2025 build (no "Name" suffix)
                ("Project",     "ProjectName"),    // pre-2025 build (no "Name" suffix)
                ("PreparedBy",  "PreparedByName"), // pre-2025 build (no "Name" suffix)
            };
            foreach (var (oldName, newName) in renames)
            {
                TryRenameColumn(connection, "Inquiries", oldName, newName);
            }

            // 2) Drop any column that exists but isn't in the
            //    expected model. Idempotent on a fresh DB: a
            //    CREATE-TABLE'd Inquiries only has the expected
            //    columns, so the Except set is empty.
            string[] expectedInquiriesColumns = new[]
            {
                "Id", "InquiryNumber", "InquiryReference", "CustomerId", "CustomerName", "CustomerLocation",
                "ProjectId", "ProjectName", "ProjectDescription", "SalesmanId", "SalesmanName",
                "PreparedByUserId", "PreparedByName", "PreparedByRole", "Status", "Priority",
                "InquiryValue", "Notes", "ReceivedAt", "PreparedAt", "SentAt", "ConfirmedAt",
                "CreatedAt", "UpdatedAt", "UpdatedBy", "ProformaInvoiceId", "ProformaNumber",
                "JobOrderNumber"
            };
            var liveColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA table_info(\"Inquiries\");";
                using var reader = pragma.ExecuteReader();
                while (reader.Read()) liveColumns.Add(reader.GetString(1));
            }
            var expectedSet = new HashSet<string>(expectedInquiriesColumns, StringComparer.OrdinalIgnoreCase);
            var orphans = liveColumns.Where(c => !expectedSet.Contains(c)).ToList();
            foreach (var orphan in orphans)
            {
                TryDropColumn(connection, "Inquiries", orphan);
            }

            // 3) Loud verification step. After the pass, log the
            //    final column list so an operator reading the log
            //    can confirm the table is in the expected shape.
            //    A lingering "Salesman" or "InquiryDate" in this
            //    list means a rename failed and the operator needs
            //    to look at the file by hand.
            try
            {
                using var check = connection.CreateCommand();
                check.CommandText = "PRAGMA table_info(\"Inquiries\");";
                using var reader = check.ExecuteReader();
                var final = new List<string>();
                while (reader.Read()) final.Add(reader.GetString(1));
                AppLogService.Info(
                    $"[Inquiries] post-migration columns: {string.Join(", ", final)}",
                    new LogContext { Module = "Database", Class = "SchemaMigrations", Operation = "VerifyInquiriesSchema" });
            }
            catch { /* verification logging is best-effort */ }
        }

        /// <summary>
        /// Renames an Inquiries column from <paramref name="oldName"/>
        /// to <paramref name="newName"/> if it exists. Idempotent:
        /// silently skips when the source column isn't present.
        /// Failures are logged to AppLogService so ops can see
        /// what actually happened — the previous implementation
        /// swallowed errors to Debug.WriteLine, which is invisible
        /// in production.
        /// </summary>
        private static void TryRenameColumn(SqliteConnection connection, string table, string oldName, string newName)
        {
            try
            {
                // Confirm the source column exists. PRAGMA table_info
                // is case-insensitive on the column-name match, but
                // we keep the case the user typed in their DDL for
                // the ALTER statement.
                using (var check = connection.CreateCommand())
                {
                    check.CommandText = $"PRAGMA table_info(\"{table}\");";
                    using var reader = check.ExecuteReader();
                    bool exists = false;
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), oldName, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (!exists) return; // column not present, nothing to rename
                }

                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"ALTER TABLE \"{table}\" RENAME COLUMN \"{oldName}\" TO \"{newName}\";";
                cmd.ExecuteNonQuery();
                AppLogService.Info(
                    $"[Inquiries] renamed legacy column '{oldName}' → '{newName}' (user data preserved).",
                    new LogContext { Module = "Database", Class = "SchemaMigrations", Operation = "TryRenameColumn" });
            }
            catch (Exception ex)
            {
                // Non-fatal: another column with the same shape may
                // already exist (the new name is in the model), or
                // the rename is unsupported on this SQLite build.
                // Either way we log and continue so the drop pass
                // below still runs.
                AppLogService.Warning(
                    $"[Inquiries] could not rename column '{oldName}' → '{newName}': {ex.Message}",
                    new LogContext { Module = "Database", Class = "SchemaMigrations", Operation = "TryRenameColumn" });
            }
        }

        /// <summary>
        /// Drops a single orphan column from a table. Idempotent:
        /// silently skips when the column isn't present. Failures
        /// are logged to AppLogService (was previously swallowed
        /// to Debug.WriteLine, which is invisible in production).
        /// </summary>
        private static void TryDropColumn(SqliteConnection connection, string table, string column)
        {
            try
            {
                using var drop = connection.CreateCommand();
                drop.CommandText = $"ALTER TABLE \"{table}\" DROP COLUMN \"{column}\";";
                drop.ExecuteNonQuery();
                AppLogService.Info(
                    $"[{table}] dropped orphan column '{column}'.",
                    new LogContext { Module = "Database", Class = "SchemaMigrations", Operation = "TryDropColumn" });
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    $"[{table}] could not drop orphan column '{column}': {ex.Message}",
                    new LogContext { Module = "Database", Class = "SchemaMigrations", Operation = "TryDropColumn" });
            }
        }
    }
}

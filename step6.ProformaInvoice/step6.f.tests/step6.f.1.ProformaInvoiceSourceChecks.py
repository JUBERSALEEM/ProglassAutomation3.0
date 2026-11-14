#!/usr/bin/env python3
"""step6.f.1 — static gate for the Proforma Invoice module.

Run from anywhere:
    python3 step6.ProformaInvoice/step6.f.tests/step6.f.1.ProformaInvoiceSourceChecks.py

Why this file exists: almost everything this module must NOT do is invisible to a
compiler. It must not delete a document, must not write another module's tables,
must not reuse a number, must not let a stale total be stored, must not silently
cost a group against the wrong pricing section, and must not lose a manual charge
toggle. Each of those would corrupt data quietly rather than fail a build.

Every rule names the defect, the file and what to do instead. The behaviour half
(step6.f.2–f.6) need Windows and a .NET SDK; this half runs anywhere.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MODULE = ROOT / "step6.ProformaInvoice"
MIGRATIONS = ROOT / "Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs"
SCHEMA = MODULE / "step6.b.1.ProformaInvoiceSchema.cs"
REPO = MODULE / "step6.b.7.ProformaInvoiceRepository.cs"
READS = MODULE / "step6.b.8.ProformaInvoiceRepository.Reads.cs"
LIFECYCLE = MODULE / "step6.b.9.ProformaInvoiceRepository.Lifecycle.cs"
ACCESS = MODULE / "step6.b.6.InvoiceAccess.cs"
PARTIES = MODULE / "step6.b.10.InvoicePartyReader.cs"
CONVERTER = MODULE / "step6.b.11.InquiryBoqConverter.cs"
ATTACHMENTS = MODULE / "step6.b.12.ProformaInvoiceAttachmentStore.cs"
JSON = MODULE / "step6.b.2.ProformaInvoiceJson.cs"
DRAFT = MODULE / "step6.a.13.ProformaInvoiceDraft.cs"
STATUS = MODULE / "step6.a.1.InvoiceLifecycle.cs"
SUMMARY = MODULE / "step6.a.11.InvoiceSummary.cs"
MONEY = MODULE / "step6.a.2.MoneyRounding.cs"
PRICING = MODULE / "step6.a.6.PricingEstimator.cs"
MODELS = MODULE / "step6.a.5.PricingModels.cs"
GRID = MODULE / "step6.a.7.SpecGrid.cs"
CHARGES = MODULE / "step6.a.9.OtherCharges.cs"
AUTOMATION = MODULE / "step6.a.10.OtherChargeAutomation.cs"
PROFITLOSS = MODULE / "step6.a.12.ProfitLoss.cs"
RULES = MODULE / "step6.a.14.ProformaInvoiceRules.cs"
WORKSPACE = MODULE / "step6.c.1.ProformaInvoiceWorkspace.cs"
APPPROJ = ROOT / "ProGlassAutomation.csproj"
README = MODULE / "step6.e.docs/step6.e.1.ProformaInvoice-README.md"
OWNER = MODULE / "step6.e.docs/step6.e.2.Owner-Verification.md"
REGISTRY = ROOT / "docs/06.Feature-Registry.md"
DECISIONS = ROOT / "docs/05.Decision-Log.md"

CSHARP = sorted(MODULE.glob("*.cs"))
PDF_STYLES = MODULE / "step6.g.Export/step6.g.1.ProformaPdfStyle.cs"
PDF_LAYOUT = MODULE / "step6.g.Export/step6.g.2.ProformaPdfLayout.cs"
PDF_DOSSIER = MODULE / "step6.g.Export/step6.g.4.ProformaDossierPdfLayout.cs"
PDF_LETTERHEAD = MODULE / "step6.g.Export/step6.g.7.ProformaPdfLetterheadRenderer.cs"
PDF_EXPORTER = MODULE / "step6.g.Export/step6.g.3.ProformaPdfExporter.cs"
PDF_VIEWMODEL = MODULE / "step6.c.Presentation/step6.c.9.ProformaWorkspaceViewModel.PdfExport.cs"
PDF_VIEW = MODULE / "step6.d.Views/step6.d.7.ProformaSummaryStage.xaml"
REGISTER_VIEW = MODULE / "step6.d.Views/step6.d.1.ProformaWorkspaceView.xaml"
GRID_VIEW = MODULE / "step6.d.Views/step6.d.5.ProformaGridStage.xaml"
WORKBOOK = MODULE / "step6.g.Export/step6.g.5.ProformaWorkbookExporter.cs"
CSV_EXPORT = MODULE / "step6.g.Export/step6.g.6.ProformaCsvExporter.cs"
IMPORT_PARSER = MODULE / "step6.h.Import/step6.h.1.ProformaGridImportParser.cs"
IMPORT_SERVICE = MODULE / "step6.h.Import/step6.h.2.ProformaGridImportService.cs"
FILE_OPERATIONS = MODULE / "step6.c.Presentation/step6.c.10.ProformaWorkspaceViewModel.FileOperations.cs"
CHARGE_AUTOMATION = MODULE / "step6.a.10.OtherChargeAutomation.cs"
CHARGES_MODEL = MODULE / "step6.a.9.OtherCharges.cs"
CHARGES_VIEW = MODULE / "step6.d.Views/step6.d.6.ProformaChargesStage.xaml"
CHARGE_TESTS = MODULE / "step6.f.tests/step6.f.7.ProformaChargeAutomationChecks.cs"
PROJECT = ROOT / "ProGlassAutomation.csproj"
failures = []


def check(condition, message):
    if not condition:
        failures.append(message)


def read(path):
    return path.read_text(encoding="utf-8", errors="replace")


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def body(path):
    return strip_comments(read(path))


# 1 ── A document is never deleted ────────────────────────────────────────────
# A quotation that reached a customer is evidence. The register hides a cancelled
# document when asked; it never removes it, and its number stays used.
for path in CSHARP:
    text = body(path)
    check(not re.search(r"\bDELETE\s+FROM\b", text, re.I),
          f"{path.relative_to(ROOT)}: contains DELETE FROM — an invoice is cancelled, never deleted")
    check("db.Invoices.Remove" not in text and "Invoices.RemoveRange" not in text,
          f"{path.relative_to(ROOT)}: removes an invoice row — the number must stay used")
check("Task<" not in read(MODULE / "step6.b.4.IProformaInvoiceRepository.cs")
      or "DeleteAsync" not in read(MODULE / "step6.b.4.IProformaInvoiceRepository.cs"),
      "step6.b.4.IProformaInvoiceRepository.cs: exposes DeleteAsync — there is no delete path")
check("Command=\"Delete" not in read(DRAFT),
      "step6.a.13.ProformaInvoiceDraft.cs: exposes a delete command")

# 2 ── No writes, and no storage reads, of another module's tables ────────────
# G1-03/G1-04 are about exactly this. step6 reads master data through the owning
# module's contract and links an inquiry by CALLING the Inquiry module.
FOREIGN = ("Inquiries", "InquiryLineItems", "Clients", "Users", "CompanyProfile", "Projects")

for path in CSHARP:
    text = body(path)
    for table in FOREIGN:
        write = re.search(
            rf'(?:UPDATE|DELETE\s+FROM|INSERT\s+INTO)\s+(""|")?{table}\b', text, re.I)
        check(write is None,
              f"{path.relative_to(ROOT)}: writes/reads the {table} table directly. "
              "A module depends on the owning module's contract, not its storage")
    for context in ("ClientDbContext", "UserDbContext", "CompanyProfileDbContext", "ProjectDbContext"):
        check(f"new {context}" not in text and f"new {context}()" not in text,
              f"{path.relative_to(ROOT)}: opens {context} — a module does not read another "
              "module's storage")

parties = read(PARTIES)
for contract in ("ICompanyProfileReader", "IClientRepository", "IProjectRepository", "IUserRepository"):
    check(contract in parties,
          f"step6.b.10.InvoicePartyReader.cs: does not depend on {contract}. Stage 1 must read the "
          "master records through their owning module's contract")
check("GetIdentityAsync" in parties and "ClientIdentity" in parties and "ProjectIdentity" in parties,
      "step6.b.10.InvoicePartyReader.cs: no longer composes the company/client/project header")
check("AuthenticationContext" in parties,
      "step6.b.10.InvoicePartyReader.cs: no longer falls back to the signed-in user as preparer")
check("InquiryRepository" in CONVERTER.read_text(),
      "step6.b.11.InquiryBoqConverter.cs: no longer reads the inquiry through its repository")
check("SetProformaLinkAsync" in read(REPO),
      "step6.b.7.ProformaInvoiceRepository.cs: no longer links the source inquiry through the "
      "Inquiry module's own method")

# 3 ── Attribution is a user id + a name snapshot, never the workstation login ──
for path in CSHARP:
    text = body(path)
    check("CurrentOperator" not in text,
          f"{path.relative_to(ROOT)}: uses CurrentOperator (the Windows login). G1-06 requires the "
          "signed-in application user as the author")
    check("Environment.UserName" not in text and "Environment.UserDomainName" not in text,
          f"{path.relative_to(ROOT)}: reads the workstation account — that is not a document author")
access = read(ACCESS)
check("AuthenticationContext.Current" in access, "step6.b.6.InvoiceAccess.cs: no longer reads the session")
check("UserId" in access and "Name" in access,
      "step6.b.6.InvoiceAccess.cs: no longer carries both the stable id and the printed name")
repo = read(REPO)
check("InvoiceAccess.Current" in repo,
      "step6.b.7.ProformaInvoiceRepository.cs: the write path no longer resolves the operator, so a "
      "document could be written with no auditable author")
check("CreatedByUserId = actor.UserId" in repo and "UpdatedByUserId = actor.UserId" in repo,
      "step6.b.7.ProformaInvoiceRepository.cs: the audit columns are no longer filled from the actor")

# 4 ── Migration 1018: additive, registered, constrained ──────────────────────
schema = strip_comments(read(SCHEMA))
check("public const int MigrationVersion = 1018;" in schema,
      "step6.b.1.ProformaInvoiceSchema.cs: migration is no longer 1018")
chain = read(MIGRATIONS)
check("new(ProformaInvoiceSchema.MigrationVersion" in chain,
      "AppSchemaMigrations.cs: migration 1018 is not registered, so the tables are created by nobody")
check("ProformaInvoiceSchema.AssertShape" in chain,
      "AppSchemaMigrations.cs: 1018 registers no shape assertion")
ensure = schema[:schema.index("AssertShape")]
scan = ensure.replace("ON DELETE CASCADE", "")
for forbidden in ("DROP", "DELETE", "UPDATE ", "INSERT INTO", "RENAME", "AddColumnIfMissing"):
    check(forbidden not in scan,
          f"step6.b.1.ProformaInvoiceSchema.cs: Ensure contains {forbidden.strip()} — 1018 may only "
          "create its own two tables and their indexes")
check(ensure.count("CREATE TABLE IF NOT EXISTS") == 2,
      "step6.b.1.ProformaInvoiceSchema.cs: 1018 must create exactly two tables (invoice + attachments)")
check("CREATE UNIQUE INDEX IF NOT EXISTS" in ensure and "ProformaNumber" in ensure,
      "step6.b.1.ProformaInvoiceSchema.cs: the document number is no longer unique at the database level")
check("CK_" in ensure and "Status" in ensure,
      "step6.b.1.ProformaInvoiceSchema.cs: the status CHECK constraint is gone, so an unknown status "
      "could be stored")
check("RequireKnownStatuses" in schema,
      "step6.b.1.ProformaInvoiceSchema.cs: the migration no longer refuses an unknown stored status")
check("RequireNoDuplicateKeyValues" in schema,
      "step6.b.1.ProformaInvoiceSchema.cs: the shape assertion no longer refuses duplicate numbers")

# 5 ── Numbering: allocated once, from the highest sequence, never reused ─────
check('"PI-"' in schema and '"000"' in schema,
      "step6.b.1.ProformaInvoiceSchema.cs: the PI-YYYY-NNN numbering shape is gone")
next_number = schema[schema.index("public static string NextNumber"):]
check("highest" in next_number and "LIKE @prefix" in next_number,
      "step6.b.1.ProformaInvoiceSchema.cs: numbering no longer reads the existing numbers for the year")
check("COUNT(" not in next_number.upper(),
      "step6.b.1.ProformaInvoiceSchema.cs: numbering must not derive from a COUNT — a cancelled "
      "document would have its number reused")
check("Status" not in next_number,
      "step6.b.1.ProformaInvoiceSchema.cs: numbering filters by status, which would reuse a "
      "cancelled document's number")
check("IsAllocatedNumber" in read(RULES),
      "step6.a.14.ProformaInvoiceRules.cs: the document-number shape is no longer validated")
check("string.IsNullOrWhiteSpace(draft.ProformaNumber)" in repo,
      "step6.b.7.ProformaInvoiceRepository.cs: a number is no longer allocated only when none exists")
check("SqliteErrorClassifier.IsUnique" in repo,
      "step6.b.7.ProformaInvoiceRepository.cs: the concurrent-number race is no longer handled")

# 6 ── One status vocabulary and one transition table ────────────────────────
status = read(STATUS)
for value in ("Draft", "Sent", "Approved", "Rejected", "Expired", "Cancelled"):
    check(f'public const string {value} = "{value}";' in status,
          f"step6.a.1.InvoiceLifecycle.cs: status {value} is missing")
check("CanTransition" in status and "Table" in status,
      "step6.a.1.InvoiceLifecycle.cs: the transition table is gone")
check('From = ProformaInvoiceStatus.Cancelled' not in status,
      "step6.a.1.InvoiceLifecycle.cs: a transition leaves Cancelled — it is a terminal state")
check("IsTerminal" in status and "CanEditBody" in status,
      "step6.a.1.InvoiceLifecycle.cs: the terminal/editability rules are gone")
check("InvoiceLifecycle.CanTransition" in read(LIFECYCLE),
      "step6.b.9.ProformaInvoiceRepository.Lifecycle.cs: a status change no longer consults the table")
check("ProformaInvoiceStatus.Normalize" in read(LIFECYCLE),
      "step6.b.9.ProformaInvoiceRepository.Lifecycle.cs: the target status is no longer normalized")
check("CanEditBody" in repo,
      "step6.b.7.ProformaInvoiceRepository.cs: the save path no longer refuses to edit an issued "
      "document")
check("DuplicateAsync" in read(LIFECYCLE),
      "step6.b.9.ProformaInvoiceRepository.Lifecycle.cs: revision-by-copy is gone")

# 7 ── Money: one rounding home, VAT after discount, discount capped ─────────
for path in CSHARP:
    if path.name == MONEY.name:
        continue
    for match in re.finditer(r"Math\.Round\s*\(([^;]*?)\)", body(path)):
        check(", 2" not in match.group(1) and "2," not in match.group(1),
              f"{path.relative_to(ROOT)}: rounds money to two places itself. MoneyRounding is the one "
              "home for currency rounding")
summary = body(SUMMARY)
discount_at = summary.find("decimal discount")
vat_at = summary.find("decimal vat =")
apply_at = summary.find("beforeVat * MoneyRounding.ClampPercent")
check(0 < discount_at < vat_at,
      "step6.a.11.InvoiceSummary.cs: VAT is no longer computed after the discount")
check(apply_at > vat_at,
      "step6.a.11.InvoiceSummary.cs: VAT is no longer applied to the discounted subtotal")
check("if (discount > net) discount = net;" in summary,
      "step6.a.11.InvoiceSummary.cs: the discount is no longer capped at the net subtotal")
check("DefaultVatPercent = 5m" in summary,
      "step6.a.11.InvoiceSummary.cs: the UAE standard VAT rate is no longer 5%")
check("GrandTotal" in summary and "SetTargetGrandTotal" in summary,
      "step6.a.11.InvoiceSummary.cs: the totals waterfall or the target-total helper is gone")
check("Recalculate" in body(DRAFT) and "Summary.Recalculate()" in body(DRAFT),
      "step6.a.13.ProformaInvoiceDraft.cs: the draft no longer recomputes the summary")

# 8 ── Pricing: four builds, stated yield fallback, DGU+LAM never downgraded ──
models = read(MODELS)
dgu_lam_at = models.find("if (text.Equals(DguLam, StringComparison.OrdinalIgnoreCase))")
dgu_at = models.find("if (text.Equals(Dgu, StringComparison.OrdinalIgnoreCase))")
check(0 < dgu_lam_at < dgu_at,
      "step6.a.5.PricingModels.cs: DGU+LAM is no longer matched before DGU, so the compound build "
      "could be read as a plain DGU")
pricing = body(PRICING)
for branch in ("GlassBuildType.Sgu", "GlassBuildType.Dgu", "GlassBuildType.Lam"):
    check(branch in pricing, f"step6.a.6.PricingEstimator.cs: the {branch} formula is gone")
check("GlassBuildType.DguLam" in pricing or "default: // DGU+LAM" in read(PRICING),
      "step6.a.6.PricingEstimator.cs: the DGU+LAM formula is gone")
check("result.Warnings.Add" in pricing and "yield" in pricing.lower(),
      "step6.a.6.PricingEstimator.cs: an invalid yield is no longer reported")
fallback = pricing.find("yield = 1m;")
warning = pricing.find("result.Warnings.Add", 0)
check(fallback > 0 and warning > 0 and warning < fallback,
      "step6.a.6.PricingEstimator.cs: the yield fallback no longer adds its warning BEFORE the "
      "substitution — a silent wrong price is the recorded defect")
check("GlassBuilds.HasSpacer" in pricing,
      "step6.a.6.PricingEstimator.cs: a spacer is no longer limited to the builds that have one")

# 9 ── Charges: colour-driven wastage, manual work preserved ─────────────────
charges = read(CHARGES)
check("RebuildWastages" in charges and "ColorName" in charges,
      "step6.a.9.OtherCharges.cs: wastage rows are no longer derived from the grid's colours")
check("IsManuallyAdded" in charges and "Wastages.Where(row => row.IsManuallyAdded)" in charges,
      "step6.a.9.OtherCharges.cs: a manually added wastage row is no longer preserved")
check("HasManualActiveOverride" in charges and "if (HasManualActiveOverride) return;" in charges,
      "step6.a.9.OtherCharges.cs: automation can override an operator's charge switch")
check("SheetQuantity" in charges and "ResolveQuantity" in charges or "ResolveQuantity" in charges,
      "step6.a.9.OtherCharges.cs: charge quantities are no longer measured against the grid")
check("EnsureCatalogueRows" in charges,
      "step6.a.9.OtherCharges.cs: the catalogue rows are no longer created idempotently")
check("RebuildWastages" in body(DRAFT) and "OtherChargeAutomation.Apply" in body(DRAFT),
      "step6.a.13.ProformaInvoiceDraft.cs: the recalculation no longer drives the charge sheet")
check("UseAdvancedAutomation" in charges,
      "step6.a.9.OtherCharges.cs: the advanced/manual mode switch is gone")
check("Suggest" in read(AUTOMATION) and "HasManualActiveOverride" in read(AUTOMATION),
      "step6.a.10.OtherChargeAutomation.cs: suggestions no longer respect manual switches")

# 10 ── P&L: provenance from the group, not from the selected section ─────────
profit = body(PROFITLOSS)
check("group.CostCard" in profit,
      "step6.a.12.ProfitLoss.cs: the P&L no longer costs from the group's own cost card")
check("SelectedSection" not in profit,
      "step6.a.12.ProfitLoss.cs: reads the SELECTED pricing section — a multi-section quotation "
      "would be costed with the wrong panes")
check("SheetsRequired" in profit, "step6.a.12.ProfitLoss.cs: whole-sheet counting is gone")
check("WithWastage" in profit, "step6.a.12.ProfitLoss.cs: the wastage allowance is gone")
check("SheetQuantityFor" in profit and "Wastages" in profit,
      "step6.a.12.ProfitLoss.cs: the operator's sheet quantity no longer wins")
check("NetExVat" in profit and "MarginPercent" in profit,
      "step6.a.12.ProfitLoss.cs: the net-revenue margin is gone")
check("CostBasisPerSqm" in profit and "SpecTargetRow" in read(PROFITLOSS),
      "step6.a.12.ProfitLoss.cs: the target-price KPI is gone")

# 11 ── Stored body: unreadable is reported, never faked ─────────────────────
json = read(JSON)
check("JsonStringEnumConverter" in json,
      "step6.b.2.ProformaInvoiceJson.cs: enums are no longer stored by name")
check("FormatVersion" in json and "newer version" in json,
      "step6.b.2.ProformaInvoiceJson.cs: a newer stored format is no longer refused")
check("Fail(" in json and "Succeeded" in json,
      "step6.b.2.ProformaInvoiceJson.cs: a bad body no longer returns a failure")
check("return new ProformaInvoiceDraft" not in json,
      "step6.b.2.ProformaInvoiceJson.cs: invents an empty draft for an unreadable body")
reads = read(READS)
check("snapshot-unreadable" in reads,
      "step6.b.8.ProformaInvoiceRepository.Reads.cs: an unreadable body is no longer reported")
check("AppLog.Error" in reads,
      "step6.b.8.ProformaInvoiceRepository.Reads.cs: a read failure is no longer recorded")

# 12 ── Attachments: copy first, row second, file removed last ───────────────
attachments = read(ATTACHMENTS)
check("Guid.NewGuid" in attachments,
      "step6.b.12.ProformaInvoiceAttachmentStore.cs: stored files no longer get a generated name")
check("File.Move(temporaryPath, finalPath)" in attachments,
      "step6.b.12.ProformaInvoiceAttachmentStore.cs: the copy no longer lands atomically")
check("MaxAttachmentBytes" in attachments,
      "step6.b.12.ProformaInvoiceAttachmentStore.cs: the size cap is gone")
check("IndexOf(Path.DirectorySeparatorChar)" in reads and 'Contains("..")' in reads,
      "step6.b.8.ProformaInvoiceRepository.Reads.cs: a stored file name may escape its folder")
check("TryDelete" in attachments and "DEPRECATED" not in attachments,
      "step6.b.12.ProformaInvoiceAttachmentStore.cs: cleanup is gone")

# The file must be deleted AFTER the row commits, not before.
remove = attachments[attachments.index("public async Task<bool> RemoveAttachmentAsync"):]
commit_at = remove.find("transaction.CommitAsync")
delete_at = remove.find("TryDelete(fileToDelete)")
check(0 < commit_at < delete_at,
      "step6.b.12.ProformaInvoiceAttachmentStore.cs: the file is deleted before the row commits, so a "
      "failed save would lose evidence the row still names")

# 13 ── No UI in a backend module ────────────────────────────────────────────
for path in CSHARP:
    text = read(path)
    check("System.Windows" not in text,
          f"{path.relative_to(ROOT)}: references WPF. The domain, persistence and engine layers carry "
          "no UI type, which is what makes them checkable without a window")
check(not list(MODULE.glob("*.xaml")),
      "step6.ProformaInvoice: a XAML file was added. The view is a separate authorization; the "
      "backend build must stay backend")

# 14 ── File size (rule K) and the build separation ──────────────────────────
for path in CSHARP:
    lines = len(read(path).splitlines())
    check(lines <= 400, f"{path.relative_to(ROOT)}: {lines} lines — rule K caps a C# file at 400")
app = read(APPPROJ)
check("step6.ProformaInvoice/**/*.cs" in app,
      "ProGlassAutomation.csproj: step6 is not compiled into the application")
check("step6.ProformaInvoice/step6.f.tests/**/*.cs" in app and "Compile Remove" in app,
      "ProGlassAutomation.csproj: the verification executable is not removed from the application "
      "build, so the app would gain a second entry point")

# 15 ── The record: docs, registry and decision ──────────────────────────────
check(README.is_file(), "step6.e.1.ProformaInvoice-README.md is missing")
check(OWNER.is_file(), "step6.e.2.Owner-Verification.md is missing")
if README.is_file():
    readme = read(README)
    for topic in ("stage", "number", "VAT", "migration 1018", "ADV"):
        check(topic.lower() in readme.lower(),
              f"step6.e.1.ProformaInvoice-README.md: does not explain {topic}")
registry = read(REGISTRY)
check("`step6.ProformaInvoice/`" in registry,
      "docs/06.Feature-Registry.md: step6 has no folder recorded")
check("authori" in registry[registry.find("| step6 |"):registry.find("| step6 |") + 1200].lower(),
      "docs/06.Feature-Registry.md: the step6 row records no authorization")
decisions = read(DECISIONS)
check("step6" in decisions.lower() and "2026-11-13" in decisions,
      "docs/05.Decision-Log.md: the 2026-11-13 step6 authorization is not recorded")

# 16 ── The workspace is the only recalculation entry point ─────────────────
workspace = read(WORKSPACE)
check("RefreshDerived" in workspace and "Draft.Recalculate()" in workspace,
      "step6.c.1.ProformaInvoiceWorkspace.cs: the engine no longer recalculates after a change")
check("ProformaInvoiceRules.Validate" in workspace,
      "step6.c.1.ProformaInvoiceWorkspace.cs: the workspace no longer revalidates after a change")
check("StageStatuses" in workspace,
      "step6.c.1.ProformaInvoiceWorkspace.cs: the stage markers are gone")
for stage in ("Header", "Pricing", "SpecGrid", "OtherCharges", "Summary", "Attachments", "ProfitLoss"):
    check(stage in read(STATUS),
          f"step6.a.1.InvoiceLifecycle.cs: stage {stage} is missing")
check("new ProformaInvoiceRepository()" in read(WORKSPACE),
      "step6.c.1.ProformaInvoiceWorkspace.cs: the workspace no longer talks to the repository")

# 17 ── The five PDF styles share one QuestPDF invoice composition ────────────
style_source = read(PDF_STYLES)
style_ids = ("ClassicNavy", "ModernTeal", "EditorialInk", "IndustrialSteel", "MinimalIvory")
layout_kinds = ("FormalBand", "SplitConsole", "Editorial", "Workshop", "QuietForm")
check(style_source.count("new ProformaPdfStyle(") == 5,
      "step6.g.1.ProformaPdfStyle.cs: the PDF style catalogue must contain exactly five styles")
for style_id in style_ids:
    check(style_id in style_source,
          f"step6.g.1.ProformaPdfStyle.cs: PDF style {style_id} is missing")
for layout_kind in layout_kinds:
    check("ProformaPdfLayoutKind." + layout_kind in style_source,
          f"step6.g.1.ProformaPdfStyle.cs: legacy PDF layout kind {layout_kind} is missing")
layout_source = read(PDF_LAYOUT)
check(layout_source.count("Document.Create(") == 1,
      "step6.g.2.ProformaPdfLayout.cs: five styles must use one shared invoice composition")
dossier_source = read(PDF_DOSSIER)
check(dossier_source.count("Document.Create(") == 1
      and "includeProfitLoss = false" in dossier_source
      and "INTERNAL COPY" in dossier_source,
      "step6.g.4.ProformaDossierPdfLayout.cs: dossier must be one QuestPDF layout with P&L opt-in and an internal warning")
for section in ("ProformaPdfLetterheadRenderer.Render", "Parties(", "Specifications(", "Charges(", "FinancialSummary(", "Signatures("):
    check(section in layout_source,
          f"step6.g.2.ProformaPdfLayout.cs: PDF section {section} is missing")
project_source = read(PROJECT)
check('PackageReference Include="QuestPDF" Version="2025.12.4"' in project_source,
      "ProGlassAutomation.csproj: the approved QuestPDF dependency is missing")
check("PdfSharp" not in project_source,
      "ProGlassAutomation.csproj: a second PDF engine was added")
check("ExportPdfCommand" in read(PDF_VIEWMODEL)
      and "ExportDossierPdfCommand" in read(PDF_VIEWMODEL)
      and "IncludeProfitLossInDossier" in read(PDF_VIEWMODEL)
      and "PdfStyles" in read(PDF_VIEWMODEL)
      and "SelectedPdfStyle" in read(PDF_VIEWMODEL),
      "step6.c.9 PDF view model: the five-style export command is incomplete")
check("ExportPdfCommand" in read(PDF_VIEW)
      and "ExportDossierPdfCommand" in read(PDF_VIEW)
      and "IncludeProfitLossInDossier" in read(PDF_VIEW)
      and "SelectedPdfStyle" in read(PDF_VIEW)
      and "PdfStyles" in read(PDF_VIEW),
      "step6.d.7 summary view: the five-style export controls are not wired")
for path in (PDF_STYLES, PDF_LAYOUT, PDF_DOSSIER, PDF_LETTERHEAD, PDF_EXPORTER, PDF_VIEWMODEL):
    check(path.is_file() and len(read(path).splitlines()) <= 400,
          f"{path.relative_to(ROOT)}: missing or exceeds the 400-line file rule")

# 18 ── Workbook, CSV and cut-list import parity ────────────────────────────
check('PackageReference Include="ClosedXML" Version="0.105.0"' in project_source,
      "ProGlassAutomation.csproj: ClosedXML is missing for XLSX import/export")
check("CsvHelper" not in project_source,
      "ProGlassAutomation.csproj: CSV should use the dependency-free V3 writer")
workbook_source = read(WORKBOOK)
for token in ("CreateInvoiceWorkbook", "CreateRegisterWorkbook", "Internal P&L",
              "P&L Targets", "WriteProfitLossTargets", "WriteSpecifications", "WriteCharges"):
    check(token in workbook_source,
          f"step6.g.5.ProformaWorkbookExporter.cs: {token} is missing")
check("includeProfitLoss = false" in workbook_source,
      "step6.g.5.ProformaWorkbookExporter.cs: internal P&L must be opt-in")
csv_source = read(CSV_EXPORT)
check("13-column" in csv_source and "TOTAL SQM" in csv_source
      and "GRAND TOTAL (VAT INC)" in csv_source,
      "step6.g.6.ProformaCsvExporter.cs: the 13-column cut-list contract is incomplete")
parser_source = read(IMPORT_PARSER)
for token in ("ParseDelimitedText", "ParseTable", "ParseJsonArray", "DetectHeader", "TryDimensions"):
    check(token in parser_source,
          f"step6.h.1.ProformaGridImportParser.cs: {token} is missing")
service_source = read(IMPORT_SERVICE)
check("FileShare.ReadWrite" in service_source and ".xlsx" in service_source
      and ".csv" in service_source and ".json" in service_source,
      "step6.h.2.ProformaGridImportService.cs: XLSX/CSV/text/JSON import support is incomplete")
file_ops_source = read(FILE_OPERATIONS)
for token in ("ImportCutListCommand", "ExportWorkbookCommand", "ExportCsvCommand",
              "ExportRegisterWorkbookCommand", "ImportFileAsync", "ExportInvoiceAsync"):
    check(token in file_ops_source,
          f"step6.c.10 file operations: {token} is missing")
check("ImportCutListCommand" in read(GRID_VIEW),
      "step6.d.5 grid stage: cut-list import control is not wired")
check("ExportWorkbookCommand" in read(PDF_VIEW)
      and "ExportCsvCommand" in read(PDF_VIEW),
      "step6.d.7 summary stage: Excel/CSV export controls are not wired")
check("ExportRegisterWorkbookCommand" in read(REGISTER_VIEW),
      "step6.d.1 register: filtered register export control is not wired")
charge_source = read(CHARGE_AUTOMATION)
for token in ("SHUTTER_BONDING", "FAN_HOLES", "CUTOUT", "HOLES",
              "BEVELED", "FROSTED", "ARGON GAS"):
    check(token in charge_source,
          f"step6.a.10.OtherChargeAutomation.cs: legacy rule {token} is missing")
check("ChargeAutomation();" in read(MODULE / "step6.f.tests/step6.f.2.ProformaBehaviourChecks.cs")
      and CHARGE_TESTS.is_file(),
      "step6.f.7 charge automation regression checks are not wired")
wastage_model = read(CHARGES_MODEL)
check("IsOptionalSuggestion" in wastage_model and "SuggestionReason" in wastage_model
      and "SheetQuantity = row.SheetQuantity" in wastage_model,
      "step6.a.9: clear-glass guidance and sheet-cost quantity must survive rebuild/clone")
wastage_view = read(CHARGES_VIEW)
check("ToolTip=\"{Binding SuggestionReason}\"" in wastage_view
      and "Binding=\"{Binding SheetQuantity" in wastage_view,
      "step6.d.6: wastage guidance and sheet-cost quantity are not visible")
for path in (WORKBOOK, CSV_EXPORT, IMPORT_PARSER, IMPORT_SERVICE, FILE_OPERATIONS):
    check(path.is_file() and len(read(path).splitlines()) <= 400,
          f"{path.relative_to(ROOT)}: missing or exceeds the 400-line file rule")

for path in (
    MODULE / "step6.f.tests/step6.f.2.ProformaBehaviourChecks.cs",
    MODULE / "step6.f.tests/step6.f.4.ProformaPdfBehaviourChecks.cs",
    MODULE / "step6.f.tests/step6.f.5.ProformaBehaviourChecks.ProfitAndLoss.cs",
    MODULE / "step6.f.tests/step6.f.6.ProformaImportExportChecks.cs",
    CHARGE_TESTS,
):
    check(path.is_file() and len(read(path).splitlines()) <= 400,
          f"{path.relative_to(ROOT)}: missing or exceeds the 400-line test-file rule")

if failures:
    print(f"proforma invoice source checks: {len(failures)} failure(s).")
    for failure in failures:
        print("  FAIL: " + failure)
    sys.exit(1)

print(f"PASS: step6 deletes nothing and numbers each document once; stage 1 reads the company, "
      f"client, project and user through their owning modules' contracts; migration 1018 is additive, "
      f"registered and keeps the document number unique; money has one rounding home with VAT after "
      f"the discount; all four glass builds are priced with a stated yield fallback; wastage rows "
      f"follow the grid's colours and manual work survives automation; the P&L costs from each group's "
      f"own cost card; an unreadable body is reported, never faked; attachments are copied before "
      f"their row and removed after it; and {len(CSHARP)} C# files stay within rule K.")

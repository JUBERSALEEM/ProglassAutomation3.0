"""step4.f.2 — Company Profile source and contract checks.

Portable checks that need no .NET SDK, no Windows and no running application.
They verify the properties that are cheap to get wrong and expensive to miss:

  1. every step4 file is present and follows the allocated naming hierarchy;
  2. the project includes the module explicitly (EnableDefaultCompileItems=false
     means a missing glob silently drops the whole module from the build) and
     keeps the verification executable out of it;
  3. the shell hosts the profile on exactly one tab and one navigation entry,
     and the workspace index guard admits exactly the indexes with a page;
  4. migration 1016 is registered on the ONE existing chain, is additive, and
     creates no second schema authority over inquiries.db;
  5. the single-profile invariant is a database constraint: a fixed key plus a
     CHECK, a primary key that EF does not treat as generated, and no IsDefault
     or directory shape copied from the ADV reference;
  6. the logo reference is a FILE NAME in the app data folder — never an
     absolute path, never generated from the operator's file system, and never
     deleted before the save has committed;
  7. the write is authorised from the signed-in session, the write path is
     tracked, and a save that stores nothing is a failure;
  8. the read path a future document will use exists and returns a snapshot;
  9. nothing from step5+ is scaffolded: no numbering, VAT, currency, PDF or
     document module;
 10. the XAML obeys the theme contract: contract resources only, no literal
     colours or font sizes, dynamic card roles, explicit implicit-style
     bindings, no Canvas, no negative margins, no horizontal overflow;
 11. the page reads as a settings page: named sections, a document preview that
     is a real second composition rather than a stretched copy, one scroll
     region, per-field messages, and an honest unsaved-changes line;
 12. docs record step4 as implemented (registry, roadmap, spec, history, README).

Exit code 0 = pass. Run from anywhere: python3 step4.f.2.CompanyProfileSourceChecks.py
"""

from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
MODULE = ROOT / "step4.CompanyProfile"
FAILURES = []


def check(condition, message):
    if not condition:
        FAILURES.append(message)


def read(path):
    return path.read_text(encoding="utf-8")


def module_sources(include_tests=False):
    paths = sorted(MODULE.rglob("*.cs"))
    if not include_tests:
        paths = [p for p in paths if "step4.f.tests" not in p.parts]
    return "\n".join(read(p) for p in paths)


def source(name):
    return read(MODULE / name)

def strip_markup(text):
    """Markup with comments removed.

    A rule may quote the pattern it forbids, so the scan must look at the
    elements and attributes that actually run, not at the prose beside them.
    """
    return re.sub(r"<!--.*?-->", "", text, flags=re.S)


def strip_code(text):
    """C# with comments removed, for the same reason as strip_markup."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def module_code():
    paths = sorted(MODULE.rglob("*.cs"))
    paths = [p for p in paths if "step4.f.tests" not in p.parts]
    return "\n".join(strip_code(read(p)) for p in paths)




# ── 1. Files ────────────────────────────────────────────────────────────

def require_files():
    expected = [
        "step4.a.1.CompanyProfile.cs",
        "step4.a.2.CompanyProfileRules.cs",
        "step4.a.3.CompanyLogoStore.cs",
        "step4.b.1.CompanyProfileSchema.cs",
        "step4.b.2.ICompanyProfileRepository.cs",
        "step4.b.3.CompanyProfileDbContext.cs",
        "step4.b.4.CompanyProfileRepository.cs",
        "step4.c.1.CompanyProfileViewModel.cs",
        "step4.c.2.CompanyLogoPreview.cs",
        "step4.d.1.CompanyProfileView.xaml",
        "step4.d.2.CompanyProfileView.xaml.cs",
        "step4.e.docs/step4.e.1.Company-Profile-README.md",
        "step4.f.tests/step4.f.CompanyTests.csproj",
        "step4.f.tests/step4.f.1.CompanyProfileChecks.cs",
        "step4.f.tests/step4.f.2.CompanyProfileSourceChecks.py",
    ]
    for relative in expected:
        check((MODULE / relative).is_file(), f"missing allocated file: {relative}")

    # Every application file must be numbered beneath an allocated branch, and a
    # test file must live under step4.f.tests rather than beside the module.
    for path in sorted(MODULE.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(MODULE).as_posix()
        if relative.startswith("step4.e.docs/"):
            check(relative.startswith("step4.e."),
                  f"{relative}: docs must be numbered beneath step4.e")
            continue
        if relative.startswith("step4.f.tests/"):
            check(relative.startswith("step4.f."),
                  f"{relative}: verification files must be numbered beneath step4.f")
            continue
        check(re.fullmatch(r"step4\.[a-d]\.\d+\.[A-Za-z0-9.\-]+", relative) is not None,
              f"{relative}: file does not follow step4.<branch>.<n>.<Name>")


# ── 2. Project inclusion ────────────────────────────────────────────────

def require_project_inclusion():
    project = read(ROOT / "ProGlassAutomation.csproj")
    check("step4.CompanyProfile/**/*.cs" in project,
          "step4 sources are not in the explicit Compile include list")
    check("step4.CompanyProfile/**/*.xaml" in project,
          "step4 views are not in the explicit Page include list")
    for extension in ("cs", "xaml", "py"):
        check(f'Remove="step4.CompanyProfile/step4.f.tests/**/*.{extension}"' in project,
              f"the step4 verification files (*.{extension}) must stay out of the application build")

    tests = read(MODULE / "step4.f.tests/step4.f.CompanyTests.csproj")
    check("<OutputType>Exe</OutputType>" in tests,
          "the verification project must be a console executable")
    check("ProGlassAutomation.csproj" in tests,
          "the verification project must reference the application")


# ── 3. Shell hosting ────────────────────────────────────────────────────

def require_shell_hosting():
    main = read(ROOT / "MainWindow.xaml")
    check(main.count("<cmp:CompanyProfileView />") == 1,
          "the company profile must be hosted on exactly one shell tab")
    check(main.count('Command="{Binding ShowCompany}"') == 1,
          "the company profile must have exactly one navigation entry")
    check('xmlns:cmp="clr-namespace:ProGlassAutomation.Company"' in main,
          "the shell must declare the Company namespace")

    view_model = read(
        ROOT / "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.17.Customization.cs")
    check(re.search(r"public bool IsCompany\b", view_model) is not None,
          "IsCompany workspace flag is missing")
    check("public ICommand ShowCompany" in view_model, "ShowCompany command is missing")
    check("ShowCompany = new DelegateCommand" in view_model,
          "ShowCompany command is not initialised")
    check('10 => "Company"' in view_model, "the workspace title for index 10 is missing")
    check('10 => IconKind.Settings' in view_model,
          "the company workspace must carry its own mark")
    check("if (value is >= 0 and <= 10)" in view_model,
          "the workspace index guard must accept the appended company workspace")
    check("and <= 11)" not in view_model, "the workspace guard admits an index with no page")
    check(view_model.count("Changed(nameof(IsCompany))") == 1,
          "the company flag must be re-raised with the workspace index")


# ── 4. Migration registration ───────────────────────────────────────────

def require_migration_registration():
    registry = read(
        ROOT / "Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs")
    check("CompanyProfileSchema.MigrationVersion" in registry
          and "CompanyProfileSchema.Ensure" in registry,
          "migration 1016 is not registered on the existing chain")
    check(registry.count("CompanyProfileSchema.MigrationVersion") == 1,
          "migration 1016 must be registered exactly once")
    check(registry.count("public static IReadOnlyList<SchemaMigration> Inquiries") == 1,
          "there must be exactly one inquiries.db migration registry")
    check("using ProGlassAutomation.Services.Company;" in registry,
          "the registry must import the company namespace it now references")

    schema = source("step4.b.1.CompanyProfileSchema.cs")
    check("MigrationVersion = 1016" in schema,
          "the company profile must be migration 1016, the next free version")
    check("CREATE TABLE IF NOT EXISTS" in schema, "company DDL must be idempotent")
    check("RequireColumn" in schema and "RequireNoNullValues" in schema,
          "the migration must assert the resulting shape")
    for destructive in ("DROP TABLE", "DELETE FROM", "DROP COLUMN", "RENAME COLUMN", "UPDATE "):
        check(destructive not in schema,
              f"migration 1016 must be additive; found {destructive}")
    check("INSERT INTO" not in schema,
          "the migration must not seed a profile row: 'never saved' must stay distinguishable")

    context = source("step4.b.3.CompanyProfileDbContext.cs")
    check("InquiryDbContext.DatabaseFilePath" in context,
          "the company context must reuse the single ERP database path")
    check("EnsureCreated(" not in context and "EnsureCreatedAsync(" not in context,
          "the company context must not become a second EnsureCreated owner")

    repository = source("step4.b.4.CompanyProfileRepository.cs")
    check("AppSchemaMigrations.Inquiries" not in repository,
          "the company repository must not apply the chain itself (the initializer owns it)")
    check("new InquiryRepository().InitializeAsync" in repository,
          "the company repository must initialize through the established ERP initializer")
    check("AppDataRoot.CurrentFolder" not in repository,
          "the repository must not invent its own database location")


# ── 5. The single-profile invariant ─────────────────────────────────────

def require_single_profile_invariant():
    model = source("step4.a.1.CompanyProfile.cs")
    schema = source("step4.b.1.CompanyProfileSchema.cs")
    context = source("step4.b.3.CompanyProfileDbContext.cs")
    view = read(MODULE / "step4.d.1.CompanyProfileView.xaml")

    check("public const int SingleId = 1" in model,
          "the profile must have one fixed identity key")
    check('CHECK (""Id"" = {SingleIdSql})' in schema or "CHECK (\"\"Id\"\" = {SingleIdSql})" in schema,
          "the table must carry a CHECK on the fixed key, so a second row cannot be inserted")
    check('CONSTRAINT ""PK_CompanyProfile"" PRIMARY KEY' in schema,
          "the profile key must be the primary key")
    check('CHECK (""LogoLayout"" IN' in schema,
          "the stored layout must be constrained to the layouts the module defines")
    check("AUTOINCREMENT" not in strip_code(schema),
          "the profile key is fixed, not generated")
    check("ValueGeneratedNever" in context,
          "EF must not treat the fixed key as generated")
    check("AssertShape" in schema and "must hold at most one row" in schema,
          "the migration must also assert that at most one profile row exists")

    # The ADV reference's company directory must not be reproduced: no IsDefault,
    # no UsageCount, no second-company concept anywhere in the module.
    joined = module_code()
    for forbidden in ("IsDefault", "UsageCount", "SetDefaultCompany", "ActiveCompany"):
        check(forbidden not in joined,
              f"the ADV company-directory shape must not be copied: found {forbidden}")
    check("New" not in view, "a settings page for the one profile must offer no New/Add action")
    check("Add Company" not in joined and "New Company" not in joined,
          "nothing may offer to create a second company profile")


# ── 6. Logo storage ─────────────────────────────────────────────────────

def require_logo_storage():
    store = source("step4.a.3.CompanyLogoStore.cs")
    repository = source("step4.b.4.CompanyProfileRepository.cs")
    model = source("step4.a.1.CompanyProfile.cs")
    view_model = source("step4.c.1.CompanyProfileViewModel.cs")

    check("AppDataRoot.CurrentFolder" in store,
          "the logo must live in the application's own data folder")
    check('FolderName = "CompanyProfile"' in store,
          "the logo folder must be named for the module")
    check("Path.Combine(Folder, fileName" in store,
          "a stored name must resolve inside the logo folder only")

    # ADV stored the picker's absolute path; this module must not.
    check("dialog.FileName" not in model and "dialog.FileName" not in repository,
          "an absolute picked path must never reach the model or the database")
    check("PendingLogoSourcePath" in model,
          "the picked file must travel as a separate, importable value")
    check("IsSafeFileName" in store,
          "a stored name must be re-validated before it is resolved")

    # Old asset survives until the new row is committed.
    commit_index = repository.find("CommitAsync")
    delete_index = repository.find("CompanyLogoStore.Delete(previousLogoName)")
    check(commit_index != -1 and delete_index != -1 and commit_index < delete_index,
          "the replaced logo must be deleted only after the commit")

    # The extension is never taken from the operator's file name.
    check('"logo-" + Guid.NewGuid()' in store,
          "an imported asset must get a generated name")
    check("File.Copy" in store and "File.Move" in store,
          "an import must copy to a temporary name and move it into place")

    # A missing file must degrade, not throw.
    check("File.Exists(path) ? path : null" in store,
          "resolving a missing asset must give null instead of throwing")
    preview = source("step4.c.2.CompanyLogoPreview.cs")
    check("DecodePixelWidth" in preview,
          "the preview must decode at a bounded width, not at full size")
    check("catch" in preview, "the preview loader must contain its failures")
    check("TextBox" not in preview, "the preview loader is not a view")

    check("HasLogoPreviewProblem" in view_model,
          "the editor must be able to report a missing logo file")


# ── 7. The write path ───────────────────────────────────────────────────

def require_write_path():
    repository = source("step4.b.4.CompanyProfileRepository.cs")

    check("AuthenticationContext.Current" in repository,
          "the write must authorise from the signed-in session")
    check("UserCatalog.IsAdministrator" in repository,
          "the write must require the administrator role in the repository itself")
    check("Nothing was saved" in repository,
          "a refused write must say that nothing was saved")
    check("IsActive" in repository, "an inactive account must not write")

    # The round-27 defect: a no-tracking row accepts assignments and stores
    # nothing. The write path must load a TRACKED row and prove rows were written.
    write_block = repository[repository.find("public async Task<CompanyProfileSaveResult> SaveAsync"):]
    check("AsNoTracking" not in write_block,
          "the write path must load a tracked row (a no-tracking row stores nothing)")
    check("SaveWithRetryAsync" in write_block,
          "the write must go through the shared retry helper")
    check("written < 1" in write_block,
          "a save that stores no rows must be treated as a failure")
    check("BeginTransactionAsync" in write_block,
          "the write must run in a transaction")
    check("CommitAsync" in write_block, "the transaction must be committed explicitly")

    check("Validation" not in write_block,
          "the repository must not depend on a UI validation layer")
    check("CompanyProfileRules.Validate" in write_block,
          "the repository must re-run the rules it shares with the editor")

    interface = source("step4.b.2.ICompanyProfileRepository.cs")
    for member in ("GetAsync", "GetIdentityAsync", "ResolveLogoPath", "InitializeAsync",
                   "IsSchemaReadyAsync", "SaveAsync", "ResolveUserNameAsync"):
        check(member in interface, f"the repository interface is missing {member}")


# ── 8. The read path for future documents ───────────────────────────────

def require_read_path():
    interface = source("step4.b.2.ICompanyProfileRepository.cs")
    model = source("step4.a.1.CompanyProfile.cs")
    check("CompanyIdentitySnapshot" in interface,
          "documents need a snapshot read path, not a copy of the fields")
    check("record CompanyIdentitySnapshot" in model,
          "the snapshot must be an immutable record")
    check("public CompanyIdentitySnapshot ToSnapshot()" in model,
          "the profile must be able to produce the snapshot documents read")
    for field in ("CompanyName", "Trn", "Address", "BankName", "AccountNumber", "LogoLayout"):
        check(re.search(rf"CompanyIdentitySnapshot\([^;]*{field}", model, re.S) is not None,
              f"the snapshot must carry {field} for a document to print")


# ── 9. Scope: nothing from step5+ ───────────────────────────────────────

def require_scope_exclusions():
    joined = module_code().lower()
    # Words that would mean the module had started the transaction stages.
    for forbidden in ("proforma", "joborder", "job order", "deliverynote", "taxinvoice",
                      "invoice number", "vat rate", "currency code", "pdfsharp", "printdocument"):
        check(forbidden not in joined,
              f"step4 must not scaffold step5+ work: found '{forbidden}'")
    check("numbering" not in joined, "step4 must not scaffold document numbering")
    check("supabase" not in joined, "step4 must not introduce a second backend")
    check("EnsureCreated" not in module_code(),
          "step4 must not create a schema outside the chain")


# ── 10/11. The view ─────────────────────────────────────────────────────

def require_view_contract():
    view_path = MODULE / "step4.d.1.CompanyProfileView.xaml"
    text = strip_markup(read(view_path))
    root = ET.parse(view_path).getroot()  # must be well-formed
    check(root.tag.endswith("UserControl"), "the profile view must be a UserControl")

    contract = read(ROOT / "Theme/step1.a.ThemeEngine/step1.a.1.ThemeContract.cs")
    declared = set(re.findall(r'\["([A-Za-z0-9]+)"\]\s*=\s*typeof', contract))
    # Implicit control styles are declared by TARGET TYPE, not by string key.
    controls = set(re.findall(r"\[typeof\(([A-Za-z0-9]+)\)\]\s*=\s*typeof", contract))
    local = set(re.findall(r'x:Key="([A-Za-z0-9]+)"', text))

    for key in re.findall(r"\{DynamicResource ([A-Za-z0-9]+)\}", text):
        check(key in declared, f"unknown theme resource '{key}'")
    for key in re.findall(r"\{StaticResource ([A-Za-z0-9]+)\}", text):
        check(key in local, f"unknown local resource '{key}'")
    for key in re.findall(r"\{DynamicResource \{x:Type ([A-Za-z0-9]+)\}\}", text):
        check(key in controls, f"unknown theme control style '{key}'")

    # No private theme dictionaries and no literal visual values.
    for pattern, label in (
        (r'="(#[0-9A-Fa-f]{3,8})"', "literal colour"),
        (r'="(White|Black|Gray|Grey|Red|Green|Blue|Orange|Silver)"', "literal brush name"),
        (r'FontSize="(\d+(?:\.\d+)?)"', "literal font size"),
        (r'FontFamily="([^"{]+)"', "literal font family"),
    ):
        for literal in re.findall(pattern, text):
            check(False, f"{view_path.name}: {label} {literal}")

    # Theme-owned card roles are looked up dynamically, never captured statically.
    for role in ("Card", "Plate", "PrimaryButton", "SubtleButton"):
        check(f'StaticResource {role}' not in text,
              f"{view_path.name}: '{role}' must be looked up dynamically, not captured statically")
        check(f"DynamicResource {role}" in text,
              f"{view_path.name}: '{role}' is not used at all")

    # Controls with theme-owned templates bind the active implicit style.
    for control in ("TextBox", "ComboBox"):
        check(f"<{control} " in text, f"{view_path.name}: expected the {control} control")
        occurrences = len(re.findall(rf"<{control}\s", text))
        bound = text.count(f'Style="{{DynamicResource {{x:Type {control}}}}}"')
        check(bound == occurrences,
              f"{view_path.name}: every {control} must bind the active implicit theme style "
              f"({bound} of {occurrences})")

    # Style must never be set twice on one element (a XamlParseException).
    for element in root.iter():
        if not isinstance(element.tag, str) or "Style" not in element.attrib:
            continue
        for child in element:
            check(not (isinstance(child.tag, str) and child.tag.endswith(".Style")),
                  f"{view_path.name}: <{element.tag}> sets Style twice")

    # `BasedOn` on a Style is a plain CLR property: a DynamicResource there throws.
    for match in re.finditer(r"BasedOn=\"\{DynamicResource", text):
        check(False, f"{view_path.name}: BasedOn cannot take a DynamicResource")

    # Layout rules: no Canvas, no absolute placement, no negative margins, no
    # horizontal scrolling, and no horizontal StackPanel inside a star column.
    for forbidden in ("<Canvas", "Canvas.Left", "Canvas.Top", "Negative", "ScrollViewer.HorizontalScrollBarVisibility=\"Visible\""):
        check(forbidden not in text, f"{view_path.name}: layout rule broken by '{forbidden}'")
    for margin in re.findall(r'Margin="(-[^"]*)"', text):
        check(False, f"{view_path.name}: negative margin '{margin}'")
    check('HorizontalScrollBarVisibility="Disabled"' in text,
          f"{view_path.name}: the page must not scroll sideways")
    check(text.count("<ScrollViewer") == 1,
          f"{view_path.name}: the page must have exactly one scroll region")
    check('Padding="{DynamicResource PaddingPage}"' in text,
          f"{view_path.name}: the page must use the shared page inset")
    check("<Grid.ColumnDefinitions>" in text and 'Width="*"' in text,
          f"{view_path.name}: the form must use star columns so it reflows")

    # A settings page reads as sections, and each field has its own message.
    for section in ("Business identity", "Representative and contact", "Banking",
                    "Logo and document header"):
        check(section in text, f"{view_path.name}: missing the '{section}' section")
    for error in ("CompanyNameError", "TrnError", "AddressError", "EmailError",
                  "RepresentativeMobileError", "BankNameError", "AccountNumberError",
                  "LogoLayoutError", "LogoSizeError"):
        check(error in text, f"{view_path.name}: missing the field message binding for {error}")

    # The two logo compositions are real, and neither stretches the image.
    check("IsLeftPortraitLayout" in text and "IsTopPanelLayout" in text,
          f"{view_path.name}: both logo compositions must be previewed")
    check("IsNoLogoLayout" in text, f"{view_path.name}: the text-only composition must be previewed")
    for match in re.finditer(r"<Image\b[^>]*>", text):
        check('Stretch="Uniform"' in match.group(0),
              f"{view_path.name}: every logo image must use Stretch=\"Uniform\"")
    check(text.count('Stretch="Uniform"') == text.count("<Image"),
          f"{view_path.name}: a non-uniform stretch would distort the logo")

    # Every path the page binds must exist on the view model. A binding to a
    # member that does not exist is silent at compile time and dead at run time:
    # the message is stored and never shown, which is exactly the defect the
    # logo size message had (the editor raised "LogoSizeMmError", the page binds
    # "LogoSizeError").
    require_bindings_exist(text)

    # Honest state: unsaved changes, a refusal reason, a save confirmation.
    for binding in ("HasUnsavedNotice", "UnsavedText", "HasReadOnlyReason", "HasError", "HasStatus"):
        check(binding in text, f"{view_path.name}: missing the state binding {binding}")

    # Radio buttons, not a canvas or a bespoke toggle, choose the composition:
    # every installed theme templates RadioButton.
    check(text.count("GroupName=\"CompanyLogoLayout\"") == 3,
          f"{view_path.name}: the three compositions must be one radio group")

    code_behind = read(MODULE / "step4.d.2.CompanyProfileView.xaml.cs")
    check("OpenFileDialog" in code_behind,
          "the view owns the file picker (a view model cannot open a dialog)")
    check("SetPickedLogo" in code_behind, "the picked file must reach the view model")
    check("HasUnsavedChanges" in code_behind,
          "returning to the page must not reload over an unsaved edit")
    check('x:Class="ProGlassAutomation.Company.CompanyProfileView"' in text,
          "the view's class name must match the code-behind")


# ── 12. Docs ────────────────────────────────────────────────────────────

def require_docs_recorded():
    registry = read(ROOT / "docs/06.Feature-Registry.md")
    check("step4" in registry and "Company Profile" in registry,
          "docs/06 must register step4 Company Profile")
    check("### step4 identifiers in use" in registry,
          "docs/06 must record the step4 child identifiers")

    roadmap = read(ROOT / "docs/11.Business-Module-Roadmap.md")
    check("step4" in roadmap and "Implemented 2026-10-28" in roadmap,
          "docs/11 must show the company profile as implemented")

    project_roadmap = read(ROOT / "docs/04.Project-Roadmap.md")
    check("step4" in project_roadmap and "implemented 2026-10-28" in project_roadmap,
          "docs/04 must record the company profile stage")

    log = read(ROOT / "docs/05.Decision-Log.md")
    check("step4" in log and "Company Profile" in log,
          "docs/05 must record the company profile decisions")
    check("CHECK" in log or "constraint" in log,
          "docs/05 must record why the single-profile invariant is a constraint")

    spec = read(ROOT / "docs/12.Module-Specifications.md")
    check("step4" in spec and "Company Profile" in spec,
          "docs/12 must specify the company profile module")
    check("1016" in spec, "docs/12 must name migration 1016")
    check("CompanyProfile" in spec, "docs/12 must document the CompanyProfile table")

    history = read(ROOT / "PROJECT_HISTORY.md")
    check("step4" in history and "Company Profile" in history,
          "PROJECT_HISTORY.md must record the Company Profile stage")

    readme = read(MODULE / "step4.e.docs/step4.e.1.Company-Profile-README.md")
    check("step4" in readme, "the module README must identify the stage")
    check("1016" in readme, "the module README must name its migration")




def require_bindings_exist(markup):
    view_model = source("step4.c.1.CompanyProfileViewModel.cs")
    declared = set(re.findall(
        r"\b(?:public|private|internal|protected)\s+(?:static\s+)?[A-Za-z_<>,\[\]\.\?\s]+?\s+"
        r"([A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|=>|;|\()", view_model))
    declared |= set(re.findall(r"nameof\(([A-Za-z_][A-Za-z0-9_]*)\)", view_model))

    for path in re.findall(r"\{Binding ([^},]+)", markup):
        name = path.strip().split(".")[0].strip()
        if not name:
            continue
        check(name in declared,
              f"step4.d.1.CompanyProfileView.xaml binds '{name}', "
              f"which the view model does not expose")


def main():
    require_files()
    require_project_inclusion()
    require_shell_hosting()
    require_migration_registration()
    require_single_profile_invariant()
    require_logo_storage()
    require_write_path()
    require_read_path()
    require_scope_exclusions()
    require_view_contract()
    require_docs_recorded()

    if FAILURES:
        print(f"step4 source checks: {len(FAILURES)} failure(s).")
        for failure in FAILURES:
            print("  FAIL:", failure)
        return 1
    print("step4 source checks: PASS.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

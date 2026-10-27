"""step2.f.2 — Client Management source and contract checks.

Portable checks that need no .NET SDK, no Windows and no running application.
They verify the properties that are cheap to get wrong and expensive to miss:

  1. every step2 file is present and follows the allocated naming hierarchy;
  2. the project includes the module explicitly (EnableDefaultCompileItems=false
     means a missing glob silently drops the whole module from the build);
  3. the shell hosts the workspace on exactly one tab and one navigation entry;
  4. migration 1011 is registered on the ONE existing chain, and no second
     chain, no Inquiry schema change and no Inquiry→Client foreign key exists;
  5. the client directory has no representative column/table (deferred to the
     User Management stage) and no delete path;
  6. the XAML obeys the theme contract: contract resources only, no literal
     colours or sizes, dynamic card roles, explicit implicit-style bindings for
     theme-templated controls, no hardcoded padding on card content;
  7. all nine installed themes actually provide the roles the views consume.

Exit code 0 = pass. Run from anywhere: python3 step2.f.2.ClientSourceChecks.py
"""

from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
MODULE = ROOT / "step2.ClientManagement"
FAILURES = []


def check(condition, message):
    if not condition:
        FAILURES.append(message)


def read(path):
    return path.read_text(encoding="utf-8")


def require_files():
    expected = [
        "step2.a.1.Client.cs",
        "step2.a.2.ClientRules.cs",
        "step2.a.3.ClientDuplicatePolicy.cs",
        "step2.b.1.ClientSchema.cs",
        "step2.b.2.IClientRepository.cs",
        "step2.b.3.ClientDbContext.cs",
        "step2.b.4.ClientRepository.cs",
        "step2.c.1.ClientEditorViewModel.cs",
        "step2.c.2.ClientWorkspaceViewModel.cs",
        "step2.d.1.ClientWorkspaceView.xaml",
        "step2.d.1.ClientWorkspaceView.xaml.cs",
        "step2.d.2.ClientEditorView.xaml",
        "step2.d.2.ClientEditorView.xaml.cs",
        "step2.e.docs/step2.e.1.ClientManagement-README.md",
        "step2.f.tests/step2.f.ClientTests.csproj",
        "step2.f.tests/step2.f.1.ClientRuleChecks.cs",
        "step2.f.tests/step2.f.2.ClientSourceChecks.py",
    ]
    for relative in expected:
        check((MODULE / relative).is_file(), f"missing allocated file: {relative}")

    # Every application-owned file must be numbered beneath an allocated branch.
    for path in sorted(MODULE.rglob("*")):
        if not path.is_file():
            continue
        name = path.name
        check(re.match(r"^step2\.[a-f]\.", name) is not None,
              f"file outside the step2 hierarchy: {path.relative_to(ROOT)}")


def require_project_inclusion():
    project = read(ROOT / "ProGlassAutomation.csproj")
    check("step2.ClientManagement/**/*.cs" in project,
          "step2 sources are not in the explicit Compile include list")
    check("step2.ClientManagement/**/*.xaml" in project,
          "step2 views are not in the explicit Page include list")


def require_shell_hosting():
    main = read(ROOT / "MainWindow.xaml")
    check(main.count("<cli:ClientWorkspaceView />") == 1,
          "the client workspace must be hosted on exactly one shell tab")
    check(main.count('Command="{Binding ShowClients}"') == 1,
          "the client workspace must have exactly one navigation entry")
    check('xmlns:cli="clr-namespace:ProGlassAutomation.Clients"' in main,
          "the shell must declare the Clients namespace")

    view_model = read(
        ROOT / "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.17.Customization.cs")
    check("public bool IsClients" in view_model, "IsClients workspace flag is missing")
    check("public ICommand ShowClients" in view_model, "ShowClients command is missing")
    check("ShowClients = new DelegateCommand" in view_model,
          "ShowClients command is not initialised")
    check("if (value is >= 0 and <= 8)" in view_model,
          "the workspace index guard must accept the appended client workspace")


def require_migration_registration():
    registry = read(
        ROOT / "Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs")
    check("ClientSchema.MigrationVersion" in registry and "ClientSchema.Ensure" in registry,
          "migration 1011 is not registered on the existing chain")
    check(registry.count("public static IReadOnlyList<SchemaMigration> Inquiries") == 1,
          "there must be exactly one inquiries.db migration registry")
    repository = read(MODULE / "step2.b.4.ClientRepository.cs")
    check("AppSchemaMigrations.Inquiries" not in repository,
          "the client repository must not apply the chain itself (the initializer owns it)")
    check("new InquiryRepository().InitializeAsync" in repository,
          "the client repository must initialize through the established ERP initializer")

    schema = read(MODULE / "step2.b.1.ClientSchema.cs")
    check("MigrationVersion = 1011" in schema,
          "the client directory must be migration 1011, the next free version")
    check('CREATE TABLE IF NOT EXISTS' in schema, "client DDL must be idempotent")
    check("RequireColumn" in schema and "RequireIndex" in schema,
          "the migration must assert the resulting shape")

    # The client table must not be a second schema authority over the file.
    client_context = read(MODULE / "step2.b.3.ClientDbContext.cs")
    check("InquiryDbContext.DatabaseFilePath" in client_context,
          "the client context must reuse the single ERP database path")
    check("EnsureCreated(" not in client_context and "EnsureCreatedAsync(" not in client_context,
          "the client context must not become a second EnsureCreated owner")


def require_no_inquiry_schema_change():
    model = read(ROOT / "Inquiry/Models/InquiryRecord.cs")
    for field in ("CustomerId", "ProjectId", "SalesmanId", "PreparedByUserId"):
        check(field in model, f"Inquiry snapshot field {field} disappeared")

    module_text = "\n".join(read(p) for p in MODULE.rglob("*.cs"))
    for forbidden in ("ALTER TABLE \"Inquiries\"", "FOREIGN KEY", "REFERENCES \"Inquiries\"",
                      "REFERENCES \"Clients\""):
        check(forbidden not in module_text,
              f"step2 must not modify the Inquiry schema or add a foreign key ({forbidden})")
    for table in ("Salesmen", "SalesmenTable", "SalesRepresentativeName"):
        check(table not in module_text,
              f"representative identity must stay deferred to the User Management stage ({table})")


def require_no_delete_path():
    module_text = "\n".join(read(p) for p in MODULE.rglob("*.cs"))
    check(".Remove(" not in module_text, "step2 must not remove client rows")
    check("ExecuteDelete" not in module_text, "step2 must not delete client rows")
    check("DeleteCommand" not in module_text, "step2 must not expose a delete command")
    check("SetActiveAsync" in module_text, "deactivation must be the lifecycle transition")


def require_theme_contract():
    views = sorted(MODULE.glob("*.xaml"))
    check(len(views) >= 2, "expected the workspace and editor views")

    contract = read(ROOT / "Theme/step1.a.ThemeEngine/step1.a.1.ThemeContract.cs")
    declared = set(re.findall(r'\["([A-Za-z0-9]+)"\]\s*=\s*typeof', contract))

    for view in views:
        ET.parse(view)  # must be well-formed
        text = read(view)

        # Contract resources only: no literal colours, no font sizes in points.
        for literal in re.findall(r'="(#[0-9A-Fa-f]{3,8})"', text):
            check(False, f"{view.name}: literal colour {literal}")
        for literal in re.findall(r'="(White|Black|Gray|Red|Green|Blue)"', text):
            check(False, f"{view.name}: literal brush name {literal}")
        for literal in re.findall(r'FontSize="(\d+(?:\.\d+)?)"', text):
            check(False, f"{view.name}: literal font size {literal}")

        # Theme-owned card roles are looked up dynamically, never BasedOn.
        check('BasedOn="{StaticResource Card}"' not in text,
              f"{view.name}: card styles must not be captured statically")
        check("CompactCard" not in text, f"{view.name}: retired card role referenced")

        # Controls with theme-owned templates must track theme replacement.
        for control in ("TextBox", "CheckBox", "DataGrid", "ComboBox"):
            if f"<{control} " in text:
                check(f'Style="{{DynamicResource {{x:Type {control}}}}}"' in text,
                      f"{view.name}: {control} must bind the active implicit theme style")

        # Every DynamicResource key must exist in the shared contract, except
        # module-local keys declared in this file.
        local = set(re.findall(r'x:Key="([A-Za-z0-9]+)"', text))
        for key in re.findall(r"\{DynamicResource ([A-Za-z0-9]+)\}", text):
            check(key in declared or key in local,
                  f"{view.name}: unknown theme resource '{key}'")

        # Card content never hardcodes padding (the theme owns it).
        for match in re.finditer(r"<ContentControl[^>]*Style=\"\{DynamicResource "
                                 r"(Card|HeroCard|MetricCard)\"[^>]*>", text):
            check('Padding="' not in match.group(0),
                  f"{view.name}: card content must not set Padding")
        for key in local:
            if key in ("ClientRegisterVisible",):
                check(False, f"{view.name}: stale resource '{key}'")

    # All nine installed themes must still provide the roles the views consume.
    catalog = read(ROOT / "Theme/step1.a.ThemeEngine/step1.a.6.ThemeCatalog.cs")
    installed = re.findall(r'new ThemeDefinition\((\w+Name)', catalog)
    check(len(installed) == 9, f"expected nine installed themes, found {len(installed)}")
    themes = sorted(p for p in (ROOT / "Theme").glob("Theme*") if p.is_dir())
    check(len(themes) == 9, f"expected nine theme packages, found {len(themes)}")
    # Role ownership verified against the theme packages themselves:
    # Foundation owns Card, Workspace owns the status badge/text roles.
    roles = {
        "step1.e.2.Foundation.xaml": ("Card",),
        "step1.e.9.Workspace.xaml": ("HeroCard", "MetricCard", "StatusBadge", "StatusText"),
    }
    for theme in themes:
        for component, expected_roles in roles.items():
            path = theme / "step1.e.Components" / component
            check(path.is_file(), f"{theme.name} is missing {component}")
            if not path.is_file():
                continue
            body = read(path)
            for role in expected_roles:
                check(f'x:Key="{role}"' in body,
                      f"{theme.name}/{component} no longer provides '{role}'")
        data_grid = list(theme.rglob("*.xaml"))
        check(any('TargetType="DataGrid"' in read(p) for p in data_grid),
              f"{theme.name} no longer styles DataGrid")


def require_binding_integrity():
    """Every binding path must resolve to a real member of its view model.

    A misspelled binding fails silently at runtime (WPF logs a warning nobody
    reads), so it is checked at source level instead.
    """
    def members(path):
        text = read(path)
        return set(re.findall(r"public\s+(?:[\w<>?,\[\]\. ]+?)\s+(\w+)\s*(?:\{|=>)", text))

    workspace_members = members(MODULE / "step2.c.2.ClientWorkspaceViewModel.cs")
    editor_members = members(MODULE / "step2.c.1.ClientEditorViewModel.cs")
    row_members = members(MODULE / "step2.b.2.IClientRepository.cs")

    def binding_names(view):
        text = read(view)
        names = set()
        for match in re.finditer(r"\{Binding\s+([^}]+)\}", text):
            expression = match.group(1).strip()
            if expression.startswith("RelativeSource") or expression.startswith("DataContext."):
                continue
            first = re.split(r"[,\s]", expression)[0]
            if first and first[0].isalpha():
                names.add(first)
        return names

    targets = {
        "step2.d.1.ClientWorkspaceView.xaml": workspace_members | row_members,
        "step2.d.2.ClientEditorView.xaml": editor_members,
    }
    for view_name, known in targets.items():
        names = binding_names(MODULE / view_name)
        check(len(names) > 20, f"{view_name}: expected the module's bindings, found {len(names)}")
        for name in sorted(names - known):
            check(False, f"{view_name}: binding to unknown member '{name}'")


def require_docs_recorded():
    registry = read(ROOT / "docs/06.Feature-Registry.md")
    check("| step2 | Client Management |" in registry, "the registry does not record step2")
    roadmap = read(ROOT / "docs/04.Project-Roadmap.md")
    check("step2" in roadmap and "Client Management" in roadmap,
          "the roadmap does not record the step2 gate")
    rules = read(ROOT / "docs/00.Project-Rules.md")
    check("`step2` is allocated to Client Management" in rules,
          "the naming rules still describe step2 as unallocated")
    log = read(ROOT / "docs/05.Decision-Log.md")
    check("step2 allocated to Client Management" in log,
          "the decision log does not record the allocation")


def main():
    require_files()
    require_project_inclusion()
    require_shell_hosting()
    require_migration_registration()
    require_no_inquiry_schema_change()
    require_no_delete_path()
    require_theme_contract()
    require_binding_integrity()
    require_docs_recorded()

    if FAILURES:
        print(f"FAIL: {len(FAILURES)} client-management source check(s) failed")
        for failure in FAILURES:
            print("  - " + failure)
        return 1
    print("PASS: step2 client management — naming, single migration chain, no Inquiry schema "
          "change, no representative identity, no delete path, nine-theme contract, docs recorded.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

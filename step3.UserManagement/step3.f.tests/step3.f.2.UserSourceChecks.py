"""step3.f.2 — User Management source and contract checks.

Portable checks that need no .NET SDK, no Windows and no running application.
They verify the properties that are cheap to get wrong and expensive to miss:

  1. every step3 file is present and follows the allocated naming hierarchy;
  2. the project includes the module explicitly (EnableDefaultCompileItems=false
     means a missing glob silently drops the whole module from the build);
  3. the shell hosts the workspace on exactly one tab and one navigation entry,
     and the workspace index guard accepts it;
  4. migration 1013 is registered on the ONE existing chain, is additive, and
     creates no second schema authority over inquiries.db;
  5. there is no password, hash, salt or login table anywhere — identity comes
     from the workstation account;
  6. a department is never consulted as a permission;
  7. the departments and roles have exactly one definition, in step3.a.2;
  8. there is no delete path: deactivation is the lifecycle transition;
  9. attribution persists a stable id and a name snapshot, never a typed name;
 10. the XAML obeys the theme contract: contract resources only, no literal
     colours or sizes, dynamic card roles, explicit implicit-style bindings;
 11. docs record step3 as implemented (registry, roadmap, spec, history);
 12. the layout rules hold: no non-wrapping action row that can overlap, one
     page padding, one scroll region per screen, action buttons on the shared
     height/gap tokens, and a shell whose sidebar and content stay in their own
     columns with no floating control over them.

Exit code 0 = pass. Run from anywhere: python3 step3.f.2.UserSourceChecks.py
"""

from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
MODULE = ROOT / "step3.UserManagement"
FAILURES = []


def check(condition, message):
    if not condition:
        FAILURES.append(message)


def read(path):
    return path.read_text(encoding="utf-8")


def module_sources(include_tests=False):
    paths = sorted(MODULE.rglob("*.cs"))
    if not include_tests:
        paths = [p for p in paths if "step3.f.tests" not in p.parts]
    return "\n".join(read(p) for p in paths)


def require_files():
    expected = [
        "step3.a.1.User.cs",
        "step3.a.2.UserCatalog.cs",
        "step3.a.3.UserRules.cs",
        "step3.a.4.UserAuthorization.cs",
        "step3.b.1.UserSchema.cs",
        "step3.b.2.IUserRepository.cs",
        "step3.b.3.UserDbContext.cs",
        "step3.b.4.UserRepository.cs",
        "step3.b.5.UserSession.cs",
        "step3.c.1.UserEditorViewModel.cs",
        "step3.c.2.UserWorkspaceViewModel.cs",
        "step3.d.1.UserWorkspaceView.xaml",
        "step3.d.1.UserWorkspaceView.xaml.cs",
        "step3.d.2.UserEditorView.xaml",
        "step3.d.2.UserEditorView.xaml.cs",
        "step3.e.docs/step3.e.1.UserManagement-README.md",
        "step3.f.tests/step3.f.UserTests.csproj",
        "step3.f.tests/step3.f.1.UserBehaviourChecks.cs",
        "step3.f.tests/step3.f.2.UserSourceChecks.py",
    ]
    for relative in expected:
        check((MODULE / relative).is_file(), f"missing allocated file: {relative}")

    # Every application-owned file must be numbered beneath an allocated branch.
    for path in sorted(MODULE.rglob("*")):
        if not path.is_file():
            continue
        check(re.match(r"^step3\.[a-f]\.", path.name) is not None,
              f"file outside the step3 hierarchy: {path.relative_to(ROOT)}")


def require_project_inclusion():
    project = read(ROOT / "ProGlassAutomation.csproj")
    check("step3.UserManagement/**/*.cs" in project,
          "step3 sources are not in the explicit Compile include list")
    check("step3.UserManagement/**/*.xaml" in project,
          "step3 views are not in the explicit Page include list")
    check('Compile Remove="step3.UserManagement/step3.f.tests/**/*.cs"' in project,
          "the step3 verification executable must stay out of the application build")


def require_shell_hosting():
    main = read(ROOT / "MainWindow.xaml")
    check(main.count("<usr:UserWorkspaceView />") == 1,
          "the user workspace must be hosted on exactly one shell tab")
    check(main.count('Command="{Binding ShowUsers}"') == 1,
          "the user workspace must have exactly one navigation entry")
    check('xmlns:usr="clr-namespace:ProGlassAutomation.Users"' in main,
          "the shell must declare the Users namespace")

    view_model = read(
        ROOT / "step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.17.Customization.cs")
    check("public bool IsUsers" in view_model, "IsUsers workspace flag is missing")
    check("public ICommand ShowUsers" in view_model, "ShowUsers command is missing")
    check("ShowUsers = new DelegateCommand" in view_model,
          "ShowUsers command is not initialised")
    check('9 => "Users"' in view_model, "the workspace title for index 9 is missing")
    check("if (value is >= 0 and <= 9)" in view_model,
          "the workspace index guard must accept the appended user workspace")
    # The bound is raised in one place only: a bound of 10 would admit an index
    # with no page behind it.
    check("and <= 10)" not in view_model, "the workspace guard admits an index with no page")


def require_migration_registration():
    registry = read(
        ROOT / "Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs")
    check("UserSchema.MigrationVersion" in registry and "UserSchema.Ensure" in registry,
          "migration 1013 is not registered on the existing chain")
    check(registry.count("UserSchema.MigrationVersion") == 1,
          "migration 1013 must be registered exactly once")
    check(registry.count("public static IReadOnlyList<SchemaMigration> Inquiries") == 1,
          "there must be exactly one inquiries.db migration registry")

    repository = read(MODULE / "step3.b.4.UserRepository.cs")
    check("AppSchemaMigrations.Inquiries" not in repository,
          "the user repository must not apply the chain itself (the initializer owns it)")
    check("new InquiryRepository().InitializeAsync" in repository,
          "the user repository must initialize through the established ERP initializer")

    schema = read(MODULE / "step3.b.1.UserSchema.cs")
    check("MigrationVersion = 1013" in schema,
          "the user directory must be migration 1013, the next free version")
    check("CREATE TABLE IF NOT EXISTS" in schema, "user DDL must be idempotent")
    check("RequireColumn" in schema and "RequireIndex" in schema,
          "the migration must assert the resulting shape")
    check("RequireNoDuplicateKeyValues" in schema,
          "the unique username index must be preceded by a readable duplicate check")
    for destructive in ("DROP TABLE", "DELETE FROM", "DROP COLUMN", "RENAME COLUMN"):
        check(destructive not in schema,
              f"migration 1013 must be additive; found {destructive}")

    context = read(MODULE / "step3.b.3.UserDbContext.cs")
    check("InquiryDbContext.DatabaseFilePath" in context,
          "the user context must reuse the single ERP database path")
    check("EnsureCreated(" not in context and "EnsureCreatedAsync(" not in context,
          "the user context must not become a second EnsureCreated owner")


def require_no_password_storage():
    """No credentialed login exists, so nothing may store a credential."""
    sources = module_sources(include_tests=True)
    for forbidden in (r"\bPassword\b\s*\{", r"\bPasswordHash\b", r"\bPasswordSalt\b",
                      r"\bSalt\b\s*\{", r'"Password"', r'"PasswordHash"',
                      r"\bVerifyPassword\b", r"\bHashedPassword\b"):
        check(re.search(forbidden, sources) is None,
              f"step3 must not store credentials; found /{forbidden}/")
    for forbidden in (r"\bLoginViewModel\b", r"\bLoginView\b", r"\bSignIn\b",
                      r"\bSignInAsync\b", r"\bPasswordBox\b", r"\bAuthenticate\b"):
        check(re.search(forbidden, sources) is None,
              f"step3 must not introduce a login flow; found /{forbidden}/")
    check("CurrentOperator.Username" in sources,
          "the current user must be derived from the existing CurrentOperator identity")


def require_department_is_not_a_permission():
    authorization = read(MODULE / "step3.a.4.UserAuthorization.cs")
    # Strip comments: the file explains departments in prose, and that is fine.
    body = re.sub(r"//[^\n]*", "", authorization)
    check("Department" not in body,
          "authorization must never consult the department: it is a team, not a permission")
    check("Role" in body, "authorization must decide on the role")
    for rule in ("CanManageUsers", "CanAttribute", "ValidateChange"):
        check(rule in authorization, f"authorization rule {rule} is missing")


def require_single_catalogue_definition():
    catalog = read(MODULE / "step3.a.2.UserCatalog.cs")
    for department in ("Administration", "Accounts", "Sales", "Estimation",
                       "Production", "Cutting Department", "Double Glazing Department"):
        check(f'"{department}"' in catalog, f"department '{department}' is not in the catalogue")
    for role in ("Administrator", "Accountant", "Salesman", "Estimator",
                 "Foreman", "Cutting Department", "Double Glazing Department"):
        check(f'"{role}"' in catalog, f"role '{role}' is not in the catalogue")
    check('new CatalogEntry("ESTIMATOR", "Estimator", isDefault: true)' in catalog,
          "Estimator must be the catalogue's default role")

    # The wording must live in exactly one place; every other file refers to
    # the catalogue by code, so a rename cannot half-apply.
    others = "\n".join(
        read(p) for p in sorted(MODULE.rglob("*.cs"))
        if "step3.f.tests" not in p.parts and p.name != "step3.a.2.UserCatalog.cs")
    for wording in ("Double Glazing Department", "Cutting Department"):
        check(wording not in others,
              f"'{wording}' is duplicated outside the catalogue")

    # A department may be added without a code change: the editor and the
    # register read the list from the database, not from a hardcoded array.
    workspace = read(MODULE / "step3.c.2.UserWorkspaceViewModel.cs")
    check("GetDepartmentsAsync" in workspace, "the workspace must load departments from the catalogue")
    check("GetRolesAsync" in workspace, "the workspace must load roles from the catalogue")


def require_no_delete_path():
    sources = module_sources()
    check(".Remove(" not in sources, "step3 must not remove user rows")
    check("ExecuteDelete" not in sources, "step3 must not delete user rows")
    check("DeleteCommand" not in sources, "step3 must not expose a delete command")
    check("SetActiveAsync" in sources, "deactivation must be the lifecycle transition")


def require_attribution_is_by_id():
    model = read(MODULE / "step3.a.1.User.cs")
    check("public int? CreatedByUserId" in model,
          "the directory must record who granted access as a stable id")
    check("public int? UpdatedByUserId" in model,
          "the directory must record who last changed access as a stable id")
    for forbidden in ("CreatedByName", "UpdatedByName", "PreparedByName"):
        check(forbidden not in model,
              f"a name column would go stale; store the id only ({forbidden})")

    session = read(MODULE / "step3.b.5.UserSession.cs")
    check("public int UserId" in session, "the Prepared By stamp must carry the stable user id")
    check("public string FullName" in session,
          "the Prepared By stamp must carry the name snapshot taken at capture time")
    check("CapturedAtUtc" in session,
          "the Prepared By stamp must record when attribution was captured")
    check("CanAttribute" in session,
          "a stamp may only be captured for an identified, active operator")


def require_theme_contract():
    views = sorted(MODULE.glob("*.xaml"))
    check(len(views) >= 2, "expected the workspace and editor views")

    contract = read(ROOT / "Theme/step1.a.ThemeEngine/step1.a.1.ThemeContract.cs")
    declared = set(re.findall(r'\["([A-Za-z0-9]+)"\]\s*=\s*typeof', contract))

    for view in views:
        ET.parse(view)  # must be well-formed
        text = read(view)
        root = ET.parse(view).getroot()

        for element in root.iter():
            if not isinstance(element.tag, str) or "Style" not in element.attrib:
                continue
            for child in element:
                check(not (isinstance(child.tag, str) and child.tag.endswith(".Style")),
                      f"{view.name}: <{element.tag}> sets Style twice")

        for literal in re.findall(r'="(#[0-9A-Fa-f]{3,8})"', text):
            check(False, f"{view.name}: literal colour {literal}")
        for literal in re.findall(r'="(White|Black|Gray|Red|Green|Blue)"', text):
            check(False, f"{view.name}: literal brush name {literal}")
        for literal in re.findall(r'FontSize="(\d+(?:\.\d+)?)"', text):
            check(False, f"{view.name}: literal font size {literal}")

        check('BasedOn="{StaticResource Card}"' not in text,
              f"{view.name}: card styles must not be captured statically")

        for control in ("TextBox", "CheckBox", "DataGrid", "ComboBox"):
            if f"<{control} " in text:
                check(f'Style="{{DynamicResource {{x:Type {control}}}}}"' in text,
                      f"{view.name}: {control} must bind the active implicit theme style")

        local = set(re.findall(r'x:Key="([A-Za-z0-9]+)"', text))
        for key in re.findall(r"\{DynamicResource ([A-Za-z0-9]+)\}", text):
            check(key in declared or key in local,
                  f"{view.name}: unknown theme resource '{key}'")

        for match in re.finditer(r"<ContentControl[^>]*Style=\"\{DynamicResource "
                                 r"(Card|HeroCard|MetricCard)\"[^>]*>", text):
            check('Padding="' not in match.group(0),
                  f"{view.name}: card content must not set Padding")

        # A ContentPresenter is not a ContentControl: the *Content* alignment
        # properties do not exist on it and are a XAML compile error.
        for match in re.finditer(r"<ContentPresenter[^>]*>", text):
            check('HorizontalContentAlignment="' not in match.group(0),
                  f"{view.name}: ContentPresenter has no HorizontalContentAlignment property")
            check('VerticalContentAlignment="' not in match.group(0),
                  f"{view.name}: ContentPresenter has no VerticalContentAlignment property")


def require_register_layout():
    view = read(MODULE / "step3.d.1.UserWorkspaceView.xaml")
    for column in ("Name", "Username", "Department", "Role", "Status", "Updated", "Actions"):
        check(f'Text="{column}"' in view, f"the register is missing the '{column}' column")
    check('ItemsSource="{Binding Rows}"' in view, "the register must bind the row collection")
    check('Binding SearchText' in view, "the register must offer a search box")
    check('ItemsSource="{Binding DepartmentFilters}"' in view,
          "the register must offer a department filter")
    check('ItemsSource="{Binding RoleFilters}"' in view,
          "the register must offer a role filter")
    check('ItemsSource="{Binding StatusTabs}"' in view,
          "the register must show the status chips")
    check('Command="{Binding NewUserCommand}"' in view, "the register must offer Add User")
    check("EditUserCommand" in view, "rows must offer Edit User")
    check('Command="{Binding RegisterFirstAdministratorCommand}"' in view,
          "a fresh directory must offer the first-administrator step")
    check('Binding PermissionMessage' in view,
          "the read-only notice must explain why actions are unavailable")
    # No literal password surface, and no free-text department entry.
    check("Password" not in view, "the user screen must not show a password field")
    for match in re.finditer(r"<ComboBox[\s][^>]*>", view, flags=re.S):
        block = match.group(0)
        check('SelectedValuePath="Code"' in block,
              "catalogue dropdowns must select by stable code")
        check("IsEditable=\"True\"" not in block,
              "a department or role must be chosen from the list, not typed")

    editor = read(MODULE / "step3.d.2.UserEditorView.xaml")
    for label in ("Full name *", "Username *", "Department *", "Role *", "Status"):
        check(f'Text="{label}"' in editor, f"the editor is missing the '{label}' field")
    check("Password" not in editor, "the editor must not show a password field")
    check('Style="{DynamicResource Plate}"' in editor,
          "the action buttons must sit in a themed footer plate")


def require_layout_contract():
    """Layout rules that were reported as defects and must never come back.

    These are the machine-checkable half of rules A–J in docs/00. A future edit
    that reintroduces a single non-wrapping action row, a per-row page margin or
    a stretched editor footer fails here instead of in front of the owner.
    """
    view = read(MODULE / "step3.d.1.UserWorkspaceView.xaml")
    editor = read(MODULE / "step3.d.2.UserEditorView.xaml")

    # A1 — the command bar is two wrapping rows. One row of chips + dropdowns +
    # search + buttons is what overlapped itself once the sidebar took its width.
    check('<WrapPanel Grid.Row="0"' in view,
          "the filter row must be a WrapPanel so it wraps instead of overlapping")
    check('<WrapPanel Grid.Row="1"' in view,
          "the action row must be a WrapPanel so it wraps instead of overlapping")
    check('StackPanel Grid.Column="1" Orientation="Horizontal"' not in view,
          "the old non-shrinking right-hand action stack must not come back")

    # B2 — one page padding for the view, no stacked per-row page margins: three
    # collapsed banner rows plus two page margins is what produced the dead band
    # between the bar and the card.
    check('Margin="{DynamicResource PaddingPage}"' not in view,
          "the register must use the shared page padding once, not a page margin per row")
    check('Margin="{DynamicResource UiPagePadding}"' in view,
          "the register must apply the shared page padding token")

    # C3 — the editor has exactly one scroll region and no stretched footer row.
    check(editor.count("<ScrollViewer") == 1,
          "the editor must scroll in exactly one region")
    check('Grid.Row="1" Style="{DynamicResource Plate}"' not in editor,
          "the editor footer must be part of the scrolling column, not a stretched bottom row")

    # D4 — every action button meets the interactive baseline, and adjacent
    # buttons are spaced by the action gap token rather than touching.
    page_buttons = 0
    for path in (MODULE / "step3.d.1.UserWorkspaceView.xaml",
                 MODULE / "step3.d.2.UserEditorView.xaml"):
        text = read(path)
        for match in re.finditer(r"<Button\b[^>]*>", text, flags=re.S):
            tag = match.group(0)
            style = re.search(r'Style="\{DynamicResource (\w+)\}', tag)
            key = style.group(1) if style else ""
            if key in ("PrimaryButton", "SubtleButton", "DestructiveButton"):
                page_buttons += 1
                check('MinHeight="{DynamicResource UiActionMinHeight}"' in tag,
                      f"{path.name}: action button without the 36 px minimum height")
                check(any(token in tag for token in (
                          'Margin="{DynamicResource UiActionGap}"',
                          'Margin="{DynamicResource UiGapAction}"',
                          'Margin="{DynamicResource UiGapCluster}"',
                          'Margin="{DynamicResource UiGapSection}"')),
                      f"{path.name}: action button without a shared gap token")
    check(page_buttons >= 8, "the step3 views must still expose their action buttons")

    # Adjacent buttons inside one cluster use the 8 px action gap: SAVE USER,
    # the status action and Cancel sit in the same footer cluster.
    footer = editor[editor.index('Style="{DynamicResource Plate}"'):]
    check(footer.count('Margin="{DynamicResource UiGapAction}"') >= 3,
          "the editor footer buttons must be separated by the shared action gap")

    # B5 — the shared scale is a contract, not a module-local dictionary.
    contract = read(ROOT / "Theme/step1.a.ThemeEngine/step1.a.1.ThemeContract.cs")
    for token in ("UiGapField", "UiGapGroup", "UiGapSection", "UiGapAction",
                  "UiGapCluster", "UiPagePadding", "UiCardPadding",
                  "UiActionMinHeight", "UiActionPadding"):
        check(f'["{token}"]' in contract, f"the theme contract must declare {token}")
    for package in ("ThemeBento", "ThemeClaymorphism", "ThemeFluent", "ThemeGlassmorphism",
                    "ThemeHolographic", "ThemeLiquidGlass", "ThemeNeumorphism",
                    "ThemeSpatial", "ThemeSwiss"):
        for mode in ("Light", "Dark"):
            dictionary = read(ROOT / f"Theme/{package}/{package}.{mode}.xaml")
            check('x:Key="UiActionMinHeight"' in dictionary,
                  f"{package}/{mode} must define the shared layout scale")
    check("<sys:Double x:Key=\"UiActionMinHeight\">36</sys:Double>" in
              read(ROOT / "Theme/ThemeFluent/ThemeFluent.Light.xaml"),
          "the interactive baseline must be 36 px")

    # F6 — the shell keeps the sidebar and the content in separate columns, and
    # no control floats over them.
    shell = read(ROOT / "MainWindow.xaml")
    check('<ColumnDefinition Width="Auto" />' in shell and '<ColumnDefinition Width="*" />' in shell,
          "the shell must keep the sidebar and content in separate grid columns")
    check('Grid.ColumnSpan="2" HorizontalAlignment="Left"' not in shell,
          "the floating menu button must not overlay the shell again")
    check('Style="{StaticResource ShellMenuToggleButton}"' in shell,
          "the menu toggle must live inside the banner and take layout space")


def require_docs_recorded():
    registry = read(ROOT / "docs/06.Feature-Registry.md")
    row = next((line for line in registry.splitlines() if line.startswith("| step3 |")), "")
    check(row != "", "the feature registry has no step3 row")
    check("Implemented" in row, f"the registry must record step3 as implemented: {row}")

    roadmap = read(ROOT / "docs/11.Business-Module-Roadmap.md")
    check("step3" in roadmap, "the roadmap must still name step3")

    spec = read(ROOT / "docs/12.Module-Specifications.md")
    check("step3" in spec, "the specifications must still describe step3")

    history = read(ROOT / "PROJECT_HISTORY.md")
    check("step3" in history and "User Management" in history,
          "PROJECT_HISTORY.md must record the User Management stage")

    readme = read(MODULE / "step3.e.docs/step3.e.1.UserManagement-README.md")
    check("step3" in readme, "the module README must identify the stage")


def main():
    require_files()
    require_project_inclusion()
    require_shell_hosting()
    require_migration_registration()
    require_no_password_storage()
    require_department_is_not_a_permission()
    require_single_catalogue_definition()
    require_no_delete_path()
    require_attribution_is_by_id()
    require_theme_contract()
    require_register_layout()
    require_layout_contract()
    require_docs_recorded()

    if FAILURES:
        print(f"step3 source checks: {len(FAILURES)} failure(s).")
        for failure in FAILURES:
            print("  FAIL:", failure)
        return 1
    print("step3 source checks: PASS.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

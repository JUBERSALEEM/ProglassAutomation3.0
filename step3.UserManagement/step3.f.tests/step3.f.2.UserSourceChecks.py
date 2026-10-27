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
  5. a credential exists and has the right shape: PBKDF2 hash, per-row salt,
     constant-time compare, a sign-in gate in front of the app, and a session
     that only a verified sign-in can set;
  6. a department is never consulted as a permission;
  7. the departments and roles have exactly one definition, in step3.a.2;
  8. there is no delete path: deactivation is the lifecycle transition;
  9. attribution persists a stable id and a name snapshot, never a typed name;
 10. the XAML obeys the theme contract: contract resources only, no literal
     colours or sizes, dynamic card roles, explicit implicit-style bindings;
 11. docs record step3 as implemented (registry, roadmap, spec, history);
 12. the first-run flow exists as one service, is reachable from the shell before
     the app is usable, reads the Windows account instead of accepting one, and
     verifies that the new row is really recognised;
 13. the layout rules hold: a command bar that wraps instead of overlapping, a
     visible caption on every filter dropdown, one page padding, one scroll
     region per screen, action buttons on the shared height/gap tokens, and a
     shell whose sidebar and content stay in their own columns with no floating
     control over them.

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
        "step3.b.6.FirstRunService.cs",
        "step3.c.1.UserEditorViewModel.cs",
        "step3.c.2.UserWorkspaceViewModel.cs",
        "step3.c.3.FirstRunViewModel.cs",
        "step3.d.1.UserWorkspaceView.xaml",
        "step3.d.1.UserWorkspaceView.xaml.cs",
        "step3.d.2.UserEditorView.xaml",
        "step3.d.2.UserEditorView.xaml.cs",
        "step3.d.3.FirstRunSetupView.xaml",
        "step3.d.3.FirstRunSetupView.xaml.cs",
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


def require_credential_policy():
    """Owner instruction (round 24): every user has a password.

    This replaces the earlier "no credential may exist anywhere" rule, which the
    owner has since reversed. The property worth guarding is no longer the
    absence of a credential but its SHAPE: a real PBKDF2 hash with a per-row
    salt, a constant-time comparison, a sign-in gate in front of the app, and an
    identity that only a verified sign-in can set.
    """
    password = read(MODULE / "step3.a.5.UserPassword.cs")
    check("Rfc2898DeriveBytes.Pbkdf2" in password,
          "the password hash must come from the platform PBKDF2 implementation, not hand-rolled")
    check("HashAlgorithmName.SHA256" in password,
          "the password hash must name its algorithm explicitly")
    check("FixedTimeEquals" in password,
          "password verification must compare in constant time")
    check("MinimumLength = 8" in password,
          "the password policy must state its minimum length")
    check(re.search(r"Iterations\s*=\s*[\d_]{5,}", password) is not None,
          "the password hash must state its iteration count")
    for forbidden in ("Encoding.UTF8.GetString", "Convert.ToBase64String(Encoding"):
        check(forbidden not in password,
              f"nothing may turn a hash back into text ({forbidden})")

    # Storage: a hash and a salt, never the password itself.
    migration = read(MODULE / "step3.b.1.UserSchema.cs")
    check("1015" in migration, "migration 1015 must add the credential columns")
    check("PasswordHash" in migration and "PasswordSalt" in migration,
          "the credential columns must be a hash and a salt")
    check(re.search(r"\bPassword\s+TEXT", migration) is None,
          "there must be no plaintext password column")
    check("MustChangePassword" in migration,
          "an administrator-set password must be able to require a change")

    # Identity: set by a verified sign-in only, and read from there.
    context = read(MODULE / "step3.b.7.AuthenticationContext.cs")
    check("SignedOut" in context, "the session must be able to end")
    check("IsAdministrator" in context,
          "the session must distinguish an administrator from everybody else")
    repository = read(MODULE / "step3.b.4.UserRepository.cs")
    check("AuthenticationContext" in repository,
          "the repository must read the signed-in identity from AuthenticationContext")
    check("CurrentOperator.Username" in module_sources(),
          "the first-run path still needs the workstation account as its starting identity")

    # The gate itself, and the shell hosting it.
    gate_vm = read(MODULE / "step3.c.4.SignInViewModel.cs")
    gate_view = read(MODULE / "step3.d.4.SignInView.xaml")
    shell = read(ROOT / "MainWindow.xaml")
    check("SignInView" in shell, "the shell must host the sign-in gate")
    check(shell.index("SignInGate") < shell.index("FirstRunGate"),
          "sign-in must sit UNDER the first-run gate: a later sibling paints on top")
    check("PasswordBox" in gate_view, "sign-in must ask for a password")
    check("SignInAsync" in gate_vm, "the sign-in view model must verify the password")
    check("MustChangePassword" in gate_vm,
          "a temporary password must be replaced before the session is handed over")

    # Read scope: a normal user reads their own row, an administrator reads all.
    check("ScopeToUserId" in repository,
          "the user query must be able to restrict itself to one person's row")
    workspace = read(MODULE / "step3.c.2.UserWorkspaceViewModel.cs")
    check(workspace.count("ScopeToUserId = ReadScope") == 2,
          "both register queries (active and inactive) must ask for the same read "
          f"scope; found {workspace.count('ScopeToUserId = ReadScope')}")
    check("IsAdministrator" in workspace,
          "the workspace must know whether the operator is an administrator")


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
    check('public const string SalesmanRole = "SALESMAN";' in catalog,
          "the salesman role code must be published once, for Client Management to read")

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


def require_command_namespace():
    """`DelegateCommand` is declared in ProGlassAutomation.ThemeStudio.

    A file that constructs one without that using does not compile, and the error
    only appears on a real build (CS0246). This check keeps the step3 module from
    shipping that mistake again: every .cs file that mentions DelegateCommand must
    import the namespace that declares it.
    """
    for path in sorted(MODULE.rglob("*.cs")):
        if "step3.f.tests" in path.parts:
            continue
        text = read(path)
        if "new DelegateCommand" not in text and "DelegateCommand<" not in text:
            continue
        check("using ProGlassAutomation.ThemeStudio;" in text,
              f"{path.name}: uses DelegateCommand without importing the namespace that "
              "declares it (CS0246)")


def require_first_run_flow():
    """The first-run path must exist, be reachable before the app is used, and
    have exactly one implementation."""
    service = read(MODULE / "step3.b.6.FirstRunService.cs")
    gate_vm = read(MODULE / "step3.c.3.FirstRunViewModel.cs")
    gate_view = read(MODULE / "step3.d.3.FirstRunSetupView.xaml")
    page = read(MODULE / "step3.c.2.UserWorkspaceViewModel.cs")
    shell = read(ROOT / "MainWindow.xaml")
    shell_code = read(ROOT / "MainWindow.xaml.cs")

    # One implementation: the write goes through the repository's guarded
    # bootstrap, and the Users page uses the same service instead of repeating it.
    check("RegisterFirstAdministratorAsync" in service,
          "the first-run service must use the repository's guarded bootstrap write")
    check("IFirstRunService" in page and "new FirstRunService(" in page,
          "the Users page must call the same first-run service, not its own copy")

    # Not typed: the account registered is the one the process runs under.
    check("CurrentOperator.Username" in service,
          "the first-run service must read the Windows account, never accept one")
    check("Binding Username" not in gate_view and "UsernameBox" not in gate_view,
          "the first-run gate must not offer a username field to type")
    check("OperatorUsername" in gate_view,
          "the first-run gate must show the account it is about to register")

    # Verified, not assumed.
    check("FindCurrentUserAsync" in service,
          "the first-run service must verify that the new row matches the workstation account")

    # Reachable before the operator can use the database.
    check("usr:FirstRunSetupView" in shell,
          "the shell must host the first-run gate")
    check("FirstRunGate.Visibility" in shell_code,
          "the shell must show and hide the gate")
    check("InitializeAsync" in shell_code and "FocusFirstField" in shell_code,
          "the shell must ask the gate whether setup is required and focus its first field")

    # Safe retry, useful failure, no credential.
    check("Set up later" in gate_view, "the gate must allow deferring setup")
    check("IsBusy" in gate_vm, "the gate must not allow a double submit")
    # Round 24: the first administrator chooses their own password here. Without
    # it the row would exist and nobody could ever sign in as it.
    check("PasswordBox" in gate_view, "the first-run gate must let the administrator choose a password")
    check("PasswordConfirmBox" in gate_view, "the first-run gate must ask for the password twice")
    check("UserPassword.Validate" in gate_vm,
          "the first-run gate must apply the one password policy")
    check("OperatingSystemAccounts" not in gate_view and "AccessToken" not in gate_view,
          "no token or harvested credential may appear in the first-run flow")

    # Rules J: the layout rules apply to this screen too.
    for token in ("UiGapAction", "UiGapGroup", "UiActionMinHeight"):
        check(token in gate_view, f"the first-run gate must use the shared spacing token {token}")
    check("AutomationProperties.Name" in gate_view,
          "the first-run gate must expose accessible names")

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
    # The register itself never shows a password; the first-administrator card
    # that shares this view must, because the row it creates needs one.
    check('ItemsSource="{Binding Rows}"' in view, "the register must bind the row collection")
    grid_start = view.index("<DataGrid")
    grid_end = view.index("</DataGrid>", grid_start)
    check("Password" not in view[grid_start:grid_end],
          "the register must not show a password column")
    check("PasswordBox" in view,
          "a fresh directory must ask the first administrator for a password")
    for match in re.finditer(r"<ComboBox[\s][^>]*>", view, flags=re.S):
        block = match.group(0)
        # The two register filters bind the row OBJECT (see A2a); every other
        # catalogue dropdown selects by the stable code, never by display text.
        if "SelectedDepartmentFilterItem" in block or "SelectedRoleFilterItem" in block \
                or "BootstrapDepartmentItem" in block:
            check('SelectedValuePath' not in block,
                  "a filter that binds an item must not also resolve a value path")
        else:
            check('SelectedValuePath="Code"' in block,
                  "catalogue dropdowns must select by stable code")
        check("IsEditable=\"True\"" not in block,
              "a department or role must be chosen from the list, not typed")

    editor = read(MODULE / "step3.d.2.UserEditorView.xaml")
    for label in ("Full name *", "Username *", "Department *", "Role *", "Status"):
        check(f'Text="{label}"' in editor, f"the editor is missing the '{label}' field")
    # Round 24: a user only exists if they can sign in, so the editor must offer
    # the credential — a mandatory one for a new user, an optional reset for an
    # existing one, and the current password when you are editing yourself.
    check("PasswordBox" in editor, "the editor must offer a password field")
    for label in ("New password", "Confirm the new password", "Current password *"):
        check(f'Text="{label}"' in editor, f"the editor is missing the '{label}' field")
    check("ShowCurrentPassword" in editor,
          "the current-password box must only appear when you edit yourself")
    check("HasPasswordError" in editor,
          "a refused password must be able to show its reason")
    check('<Border Height="{DynamicResource StrokeWidth}"' in editor,
          "the action buttons must sit below the footer rule, not in a nested card")


def require_layout_contract():
    """Layout rules that were reported as defects and must never come back.

    These are the machine-checkable half of rules A–J in docs/00. A future edit
    that reintroduces a single non-wrapping action row, a per-row page margin or
    a stretched editor footer fails here instead of in front of the owner.
    """
    view = read(MODULE / "step3.d.1.UserWorkspaceView.xaml")
    editor = read(MODULE / "step3.d.2.UserEditorView.xaml")
    vm = read(MODULE / "step3.c.2.UserWorkspaceViewModel.cs")

    # A1 — the command bar is one row made of two WrapPanels: filters in the
    # flexible column, actions in the content-sized column. A StackPanel cannot
    # shrink and was what overlapped itself once the sidebar took its width; a
    # WrapPanel wraps instead, at any width.
    check('<WrapPanel Grid.Row="0" Grid.Column="0"' in view,
          "the filter cluster must be a WrapPanel so it wraps instead of overlapping")
    check('<WrapPanel Grid.Row="0" Grid.Column="1"' in view,
          "the action cluster must be a WrapPanel so it wraps instead of overlapping")
    check('StackPanel Grid.Column="1" Orientation="Horizontal"' not in view,
          "the old non-shrinking right-hand action stack must not come back")
    check('<ColumnDefinition Width="*" MinWidth="0" />' in view,
          "the filter cluster must sit in a shrinkable column beside the actions")

    # A2 — a filter's face carries its SELECTION and nothing else. Three earlier
    # arrangements were rejected by the owner: a caption standing beside the box, the
    # field name printed on the button ("Department: All"), and then the field name
    # back again as a prefix on every row. What the box shows is the value — "All
    # departments" / "All roles" when nothing is filtered, so a closed box is never
    # blank and never a riddle, then the catalogue's own wording, verbatim.
    check('DisplayName = "All departments"' in vm and 'DisplayName = "All roles"' in vm,
          "each filter needs an unfiltered row that says what it covers")
    check('"Department: "' not in vm and '"Role: "' not in vm,
          "the field name must not be printed on the filter buttons")
    check("DepartmentFilters.Add(department);" in vm and "RoleFilters.Add(role);" in vm,
          "filter rows must reuse the catalogue row verbatim, code and wording")
    filter_body = vm[vm.index("private void ApplyCatalogueFilters("):]
    next_member = filter_body.find("\n        private ", 10)
    if next_member != -1:
        filter_body = filter_body[:next_member]
    literals = set(re.findall(r'DisplayName = ("[^"]*")', filter_body))
    check(literals == {'"All departments"', '"All roles"'},
          "the only labels the filters may invent are the two 'All …' rows; every "
          f"other entry is the catalogue's own wording (found {sorted(literals)})")

    # A2a — the selection is bound as an ITEM. A string SelectedValue set before
    # the item list exists leaves the ComboBox unselected, which is the blank box
    # the owner reported twice; binding the row object removes the lookup.
    check('SelectedItem="{Binding SelectedDepartmentFilterItem, Mode=TwoWay}"' in view
          and 'SelectedItem="{Binding SelectedRoleFilterItem, Mode=TwoWay}"' in view,
          "the filters must bind their selection as an item, not as a code string")
    check("_selectedDepartmentFilterItem = DepartmentFilters[0];" in vm
          and "_selectedRoleFilterItem = RoleFilters[0];" in vm,
          "the boxes must be pointed at their default row once the catalogues load")

    # The same defect in this module's other catalogue pickers: the editor's
    # department and role boxes, the first-run card's department box, and the shell
    # gate's department box (its view model lives here too) must all bind a row.
    # A SelectedValue in any of them is the blank face coming back.
    for path in (MODULE / "step3.d.1.UserWorkspaceView.xaml",
                 MODULE / "step3.d.2.UserEditorView.xaml",
                 MODULE / "step3.d.3.FirstRunSetupView.xaml"):
        text = read(path)
        check('SelectedValuePath' not in text and "SelectedValue=" not in text,
              f"{path.name} must bind a row object; a code string can fail to resolve "
              "and leaves the box blank")
    filter_start = view.index('<WrapPanel Grid.Row="0" Grid.Column="0"')
    filter_end = view.index("</WrapPanel>", filter_start)
    filter_bar = view[filter_start:filter_end]

    # A2a-2 — the field word is a LABEL beside the box, the way the Inquiry
    # toolbar labels its status chips, and the face carries the selection only.
    # Printing the word inside every row was tried and rejected: it reads as a
    # placeholder and doubles the word once a row already says "Cutting
    # Department".
    for word, items in (("Department", "DepartmentFilters"), ("Role", "RoleFilters")):
        check(f'<TextBlock Text="{word}"' in filter_bar,
              f"the {word.lower()} filter must carry a visible '{word}' label")
        box = re.search(r"<ComboBox\b[^>]*" + items + r"[\s\S]*?</ComboBox>", filter_bar)
        check(box is not None, f"the {word.lower()} filter dropdown is missing")
        if box:
            check('{Binding DisplayName}' in box.group(0) and f'Text="{word}"' not in box.group(0),
                  f"the {word.lower()} filter face must carry the selection only, not the field word")
    # A2b — a single action is a button, not an overflow menu. Refresh used to be
    # the only item inside a "⋯" menu on the command bar, so the action took two
    # clicks and read as a mystery. Row menus with two or more real choices are
    # still allowed; the command bar may not hide an action at all.
    bar_start = view.index('<ContentControl Grid.Row="0"')
    bar_end = view.index("</ContentControl>", bar_start)
    command_bar = view[bar_start:bar_end]
    check("<ContextMenu" not in command_bar,
          "the command bar must not hide its actions in a menu")
    check('Command="{Binding RefreshCommand}"' in command_bar,
          "refresh must be a visible button on the command bar")

    # A2c — every page-level card pins one inset. `Card` ships PaddingCard and
    # `Plate` ships PaddingPlate, and those differ inside the same theme (8–24 px
    # were measured across the nine packages), which is why the table did not line
    # up with the bar above it. Pinning UiCardPadding makes all nine themes equal.
    for path in (MODULE / "step3.d.1.UserWorkspaceView.xaml",
                 MODULE / "step3.d.2.UserEditorView.xaml"):
        text = read(path)
        for match in re.finditer(r'<ContentControl\b[^>]*>', text, flags=re.S):
            tag = match.group(0)
            is_card = 'Style="{DynamicResource Card}"' in tag
            is_page_plate = 'Style="{DynamicResource Plate}"' in tag and 'Grid.Row="0"' in tag
            is_banner = 'Style="{DynamicResource HeroCard}"' in tag
            if not (is_card or is_page_plate or is_banner):
                continue
            check('Padding="{DynamicResource UiCardPadding}"' in tag,
                  f"{path.name}: a page-level card must pin UiCardPadding, or its "
                  "inset changes with the theme and stops matching its neighbours")
    for match in re.finditer(r"<ComboBox\b[^>]*>", view, flags=re.S):
        tag = match.group(0)
        if 'Filters}"' not in tag:
            continue
        check("ToolTip=" in tag, "each filter dropdown needs a tooltip")
        check("AutomationProperties.Name=" in tag, "each filter dropdown needs an accessible name")
        check('DisplayMemberPath' not in tag,
              "a filter dropdown must render its selection with an ItemTemplate")

    # A3 — a closed dropdown must never be blank. A SelectedValue set while the
    # ItemsSource is still empty stays unselected and is never re-applied by
    # itself, so the view model must re-raise the selection after the catalogue
    # arrives (that was the empty box in the owner's screenshot).
    check("OnPropertyChanged(nameof(SelectedDepartmentFilter));" in vm
          and "OnPropertyChanged(nameof(SelectedRoleFilter));" in vm,
          "the filter selections must be re-applied after the catalogue loads")

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
    check(page_buttons >= 6, "the step3 views must still expose their action buttons")
    for command in ("EditSelectedCommand", "ToggleActiveCommand", "NewUserCommand",
                    "SaveCommand", "DeactivateCommand", "CancelCommand",
                    "RegisterFirstAdministratorCommand"):
        check(f'Command="{{Binding {command}}}"' in view or
              f'Command="{{Binding {command}}}"' in editor,
              f"the module must still offer {command}")

    # Adjacent buttons inside one cluster use the 8 px action gap: SAVE USER,
    # the status action and Cancel sit in the same footer cluster.
    footer = editor[editor.index("<WrapPanel HorizontalAlignment=\"Right\">"):]
    check(footer.count('Margin="{DynamicResource UiGapAction}"') >= 3,
          "the editor footer buttons must be separated by the shared action gap")

    # B6 — no surface inside a surface. The footer used to be a Plate inside the
    # Card, and because Plate ships PaddingPlate while the card pins UiCardPadding
    # the buttons sat a few pixels in from the fields above them — in every theme.
    # A hairline at the section gap is the separator now.
    check('Style="{DynamicResource Plate}"' not in editor,
          "the editor must not nest a card surface inside its card")
    check('<Border Height="{DynamicResource StrokeWidth}"' in editor
          and 'Background="{DynamicResource BorderStrong}"' in editor,
          "the editor footer must be separated by a hairline rule, not a nested card")

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
    require_credential_policy()
    require_department_is_not_a_permission()
    require_single_catalogue_definition()
    require_no_delete_path()
    require_attribution_is_by_id()
    require_theme_contract()
    require_register_layout()
    require_first_run_flow()
    require_command_namespace()
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

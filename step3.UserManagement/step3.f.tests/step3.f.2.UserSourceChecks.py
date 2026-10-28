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
 13. the shell can always be signed into: a failure never hides the gate, and the
     banner shows the session and offers Sign in / Sign out on every page;
 14. the layout rules hold: a command bar that wraps instead of overlapping, a
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


def catch_blocks(source):
    """The body of every catch block, found by brace matching.

    Regexes over whole files are the wrong tool here: the distance from a `catch`
    to the statement that matters is set by whatever comment happens to sit in
    between, and an over-long window quietly matches the next method's code. That
    is how a check turns green while the defect is still there.
    """
    bodies = []
    for match in re.finditer(r"\bcatch\b", source):
        start = source.find("{", match.end())
        if start < 0:
            continue
        depth = 0
        for index in range(start, len(source)):
            if source[index] == "{":
                depth += 1
            elif source[index] == "}":
                depth -= 1
                if depth == 0:
                    bodies.append(source[start:index + 1])
                    break
    return bodies


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
        "step3.b.7.AuthenticationContext.cs",
        "step3.b.8.DeviceSessionStore.cs",
        "step3.b.9.RememberedSignIn.cs",
        "step3.c.1.UserEditorViewModel.cs",
        "step3.c.2.UserWorkspaceViewModel.cs",
        "step3.c.3.FirstRunViewModel.cs",
        "step3.c.4.SignInViewModel.cs",
        "step3.c.5.ShellSessionViewModel.cs",
        "step3.d.1.UserWorkspaceView.xaml",
        "step3.d.1.UserWorkspaceView.xaml.cs",
        "step3.d.2.UserEditorView.xaml",
        "step3.d.2.UserEditorView.xaml.cs",
        "step3.d.3.FirstRunSetupView.xaml",
        "step3.d.3.FirstRunSetupView.xaml.cs",
        "step3.d.4.SignInView.xaml",
        "step3.d.4.SignInView.xaml.cs",
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
    # The bound is raised in exactly one place, and only when a page is actually
    # appended behind the new index: step4 Company Profile owns index 10, so ten
    # is legitimate and eleven would admit an index with no page behind it.
    check("if (value is >= 0 and <= 10)" in view_model,
          "the workspace index guard must accept the appended user workspace")
    check("and <= 11)" not in view_model, "the workspace guard admits an index with no page")


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

    # A password is typed into a masked box, never a plain TextBox and never a
    # binding that would put it in the visual tree twice.
    for name in ("step3.d.1.UserWorkspaceView.xaml", "step3.d.2.UserEditorView.xaml",
                 "step3.d.3.FirstRunSetupView.xaml", "step3.d.4.SignInView.xaml"):
        text = read(MODULE / name)
        secret = re.search(
            r'Text="\{Binding\s+(?:Path=)?(?:Password|PasswordConfirm|NewPassword'
            r'|NewPasswordConfirm|CurrentPassword)\b', text)
        check(secret is None,
              f"{name}: the password itself must never be bound as text "
              "(the error and prompt strings are fine)")
        for tag in re.findall(r"<TextBox\b[^>]*>", text):
            check("Password" not in tag,
                  f"{name}: a TextBox must never carry a password")

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

    # The scope has to reach the biggest register too, or "only the data related
    # to them" stops at the front door.
    inquiry_contracts = read(ROOT / "Inquiry/Services/Inquiry/InquiryContracts.cs")
    inquiry_repository = read(ROOT / "Inquiry/Services/Inquiry/InquiryRepository.cs")
    inquiry_list = read(ROOT / "Inquiry/ViewModels/InquiryListViewModel.cs")
    check("ScopeToUserId" in inquiry_contracts,
          "the inquiry query must be able to restrict itself to one person")
    check(inquiry_repository.count("ReadScopeFor(") >= 3,
          "the inquiry repository must apply the read scope as a ceiling, in both "
          "the register and the single-record fetch")
    check("ScopeToUserId = AuthenticationContext.IsAdministrator" in inquiry_list,
          "the inquiry register must ask for the signed-in person's scope")


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
    """Types declared elsewhere need their namespace, or the build breaks.

    `DelegateCommand` lives in ProGlassAutomation.ThemeStudio and `ViewModelBase`
    in ProGlassAutomation.ViewModels.Inquiry. A file that uses one without the
    using does not compile (CS0246), and this environment has no compiler, so the
    error would only appear on the owner's machine — which is exactly what
    happened to step3.c.4.SignInViewModel. Every .cs file in the throwaway-free
    part of the tree is checked, not just step3, so the same mistake cannot come
    back in step2 or the shell.
    """
    declared = (
        # (marker in the file, using line that must be present, type name)
        ("DelegateCommand", "using ProGlassAutomation.ThemeStudio;", "DelegateCommand"),
        ("ViewModelBase", "using ProGlassAutomation.ViewModels.Inquiry;", "ViewModelBase"),
    )

    paths = [p for p in sorted(ROOT.rglob("*.cs"))
             if not any(part in ("bin", "obj", ".git") or part.endswith(".tests") for part in p.parts)]

    for path in paths:
        text = read(path)
        namespace = re.search(r"^namespace\s+([\w.]+)", text, re.M)
        if namespace is None:
            continue
        own_namespace = namespace.group(1)
        for marker, using_line, type_name in declared:
            if not re.search(r"\b" + type_name + r"\b", text):
                continue
            if using_line in text:
                continue
            # Declared in this very file, or reachable through the file's own
            # namespace (the Inquiry view models share it), or fully qualified.
            if f"class {type_name}" in text or f"interface {type_name}" in text:
                continue
            declared_in = using_line[len("using "):-1]
            if own_namespace == declared_in or own_namespace.startswith(declared_in + ".") \
                    or declared_in.startswith(own_namespace + "."):
                continue
            if using_line[len("using "):-1] + "." + type_name in text:
                continue
            check(False,
                  f"{path.relative_to(ROOT).as_posix()}: uses {type_name} without "
                  f"'{using_line}' (CS0246 on a real build)")


def require_operator_lookup_uses_its_own_parameter():
    """The operator lookup must normalize the name it was given (CS0103).

    Splitting `FindUserRowAsync` into an own-context and an in-context overload
    left `UserRules.UsernameKey(operatorUsername)` inside the overload whose
    parameter is called `username`. That does not compile, and with no .NET SDK in
    this environment only the owner's build could see it. This is where the rename
    landed, so this is the line that is pinned.
    """
    repository = read(MODULE / "step3.b.4.UserRepository.cs")
    signature = "UserDbContext db, string username, CancellationToken ct)"
    start = repository.find(signature)
    check(start >= 0, "the in-context operator lookup helper is missing")
    if start < 0:
        return
    body = repository[start:start + 400]
    check("UsernameKey(username)" in body,
          "the operator lookup must normalize its own parameter; a stale "
          "`operatorUsername` inside it does not compile (CS0103)")
    check("UsernameKey(operatorUsername)" not in body,
          "the operator lookup still references a name that is not in scope (CS0103)")


def require_stamp_needs_a_verified_sign_in():
    """A Prepared By stamp must come from a password-verified sign-in.

    Round 35.0: the round-trip check built a bare `new UserSession(repository)` and
    asserted a stamp. `CaptureStampAsync` reads the process identity, which only
    `SignIn` fills after a password is checked — so the assertion could never pass,
    and it contradicted the file's own claim that no session exists before a
    password is verified.

    The rule is per CHECK, not per file: a sign-in elsewhere in the file does not
    make this check's stamp real. Only the non-null stamp assertions are examined,
    because asserting a NULL stamp (a refusal) is legitimate without a session.
    """
    compiled = read(MODULE / "step3.f.tests" / "step3.f.1.UserBehaviourChecks.cs")
    offenders = []
    for capture in re.finditer(r"(\w+)\s*=\s*await\s+[^;]{0,120}?CaptureStampAsync\(", compiled):
        variable = capture.group(1)
        following = compiled[capture.end():capture.end() + 300]
        if not re.search(r"True\(\s*" + re.escape(variable) + r"\s*!=\s*null", following):
            continue  # asserting a refusal: no identity is the point
        method_start = 0
        for header in re.finditer(r"(?m)^    private static [^\n]*?\b\w+\(", compiled[:capture.start()]):
            method_start = header.start()
        preamble = compiled[method_start:capture.start()]
        if not re.search(r"SignInAsync\(|\.SignIn\(", preamble):
            line = compiled[:capture.start()].count("\n") + 1
            offenders.append(f"{line}: '{variable}' is asserted non-null without a sign-in in its check")
    check(not offenders,
          "a stamp must be captured after a password-verified sign-in, not from a bare "
          f"new UserSession (identity is proved, never assumed): {offenders}")
    check(".SignOut()" in compiled,
          "a check that signs in must sign out, or the static identity leaks into later checks")


def require_bootstrap_carries_its_password():
    """The first-administrator draft must carry the password it was given.

    Round 34.8: `RegisterFirstAdministratorAsync` checked the password policy and
    then built a `UserDraft` without `Password`, so `UserRules.Validate` refused it
    every time with "Password is required for a new user". The FIRST administrator
    could never be registered — a fresh installation could not be set up at all.
    Six checks failed across two projects for that one missing initializer.
    """
    repository = read(MODULE / "step3.b.4.UserRepository.cs")
    start = repository.find("public async Task<UserSaveResult> RegisterFirstAdministratorAsync")
    check(start >= 0, "RegisterFirstAdministratorAsync must exist: it is the only way into a new directory")
    if start < 0:
        return
    body = repository[start:repository.find("public async Task<UserSaveResult> SetFirstPasswordAsync", start)]
    check("Password = password" in body,
          "the bootstrap draft must carry the validated password, or every registration fails")
    check("PasswordConfirm = password" in body,
          "the bootstrap draft must confirm the same password, or the match rule refuses it")
    check(body.find("UserPassword.Validate(password)") < body.find("UserRules.Validate(draft)"),
          "the password policy must be checked before the draft is validated")


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

def require_first_password_upgrade():
    """An upgraded directory must still have a way in.

    Owner question (round 25): "first boot how can have a password when until now
    there was no password in username?" On a fresh install the setup card asks for
    one before the row exists, so the two appear together. The case that was
    missing is the OTHER first boot — an installation that already has users, all
    of them created before migration 1015, so none has a password. There, sign-in
    refuses every account and "ask an administrator" is circular again.

    These checks pin the single door out of that state, and its narrowness.
    """
    repo = read(MODULE / "step3.b.4.UserRepository.cs")
    contract = read(MODULE / "step3.b.2.IUserRepository.cs")
    service = read(MODULE / "step3.b.6.FirstRunService.cs")
    gate_vm = read(MODULE / "step3.c.3.FirstRunViewModel.cs")
    gate_view = read(MODULE / "step3.d.3.FirstRunSetupView.xaml")
    page = read(MODULE / "step3.c.2.UserWorkspaceViewModel.cs")
    page_view = read(MODULE / "step3.d.1.UserWorkspaceView.xaml")

    # 1. A row with no credential cannot sign in. There must be no path where an
    #    empty hash is treated as "no password needed".
    start = repo.find("if (stored.IsEmpty)")
    check(start >= 0,
          "sign-in must refuse a row that has no password instead of falling "
          "through to a successful sign-in")
    if start >= 0:
        refusal = repo[start:start + 1200]
        check("SignInCode.NoPassword" in refusal,
              "the refusal must be reported as 'no password', not as a wrong password")
        # 2. The refusal must name the situation: one person missing a password is
        #    an administrator's job; nobody having one is not.
        check("credentialed == 0" in refusal,
              "the no-password refusal must tell apart 'this person has none' from "
              "'nobody in this installation has one', or an upgraded directory is "
              "told to ask an administrator who cannot sign in either")

    # 3. Exactly one door, in the service, not in a view model.
    check("SetFirstPasswordAsync" in contract,
          "the repository contract must expose the one-time first-password step")
    method = repo.find("public async Task<UserSaveResult> SetFirstPasswordAsync(")
    check(method >= 0, "the first-password write is missing from the repository")
    if method >= 0:
        body = repo[method:method + 5000]

        # One shot: closed the moment any row has a credential, so it can never be
        # used to take over an installation that already has passwords.
        check('CountAsync(x => x.PasswordHash != ""' in body and "if (credentialed > 0)" in body,
              "the first-password step must refuse once ANY account has a password")

        # Aimed by the workstation account, resolved inside the write transaction,
        # so a caller cannot name somebody else's row.
        check("FindUserRowTrackedAsync(db, operatorUsername" in body,
              "the first-password step must resolve the row from the workstation "
              "account inside its own transaction, never from a caller-supplied id, "
              "and it must be a TRACKED row or the update is dropped")
        check("FindUserRowAsync(db, operatorUsername" not in body,
              "the first-password write must not use the read-only lookup for the "
              "row it saves: the assignment succeeds in memory and SaveChanges "
              "writes nothing")
        check("int userId" not in body,
              "the first-password step must not take a user id: that would let a "
              "caller aim it at another person's row")

        check("!target.IsActive" in body,
              "an inactive row must not be given the first password")
        check("UserCatalog.IsAdministrator(target.Role)" in body,
              "only an Administrator may be given the first password; otherwise any "
              "normal user's row could be claimed at the machine")

        # Their own password, so no forced change at the next sign-in.
        check("mustChange: false" in body,
              "the first password is the person's own choice, so it must not be "
              "marked temporary")

    # 4. The state is computed once, and both surfaces ask the same question.
    check("CountCredentialedUsersAsync" in contract and "CountCredentialedUsersAsync" in service,
          "the 'nobody has a password' state must come from the credential count")
    check("IsCredentialSetupRequired" in service and "CanClaimFirstPassword" in service,
          "the first-run service must report the upgrade state and whether this "
          "workstation may claim it")

    # 5. The startup gate opens for it — otherwise the operator meets the sign-in
    #    wall with no way past it.
    check("snapshot.IsSetupRequired || snapshot.IsCredentialSetupRequired" in gate_vm,
          "the startup gate must be visible for the upgrade state as well as the "
          "empty directory")
    check("SetFirstPasswordAsync" in gate_vm,
          "the startup gate must be able to set the first password")
    check("ShowIdentityFields" in gate_vm and
          'Visibility="{Binding ShowIdentityFields' in gate_view,
          "the first-password step must hide the name and department fields: it "
          "gives an existing row a way in, it does not edit who somebody is")

    # 6. The Users page offers the same step, because the gate can be dismissed.
    shown = re.search(r"public bool ShowBootstrapCard\s*=>([^;]+);", page)
    check(shown is not None, "the Users page must decide when its first-run card shows")
    if shown is not None:
        check("IsDirectoryEmpty" in shown.group(1) and "IsCredentialSetup" in shown.group(1),
              "the Users page card must show for both first-run states: an empty "
              "directory AND a directory where nobody has a password")
    check('Binding ShowBootstrapCard' in page_view,
          "the Users page card's visibility must be bound to that decision")
    check("SetFirstPasswordAsync" in page,
          "the Users page card must be able to set the first password")
    check("ShowBootstrapIdentityFields" in page and
          "Converter={StaticResource UserVisibility}" in page_view,
          "the Users page card must hide the identity fields in the upgrade state")
    check("string.Equals(Password, PasswordConfirm, StringComparison.Ordinal)" in gate_vm,
          "the first-password step must compare the two typed passwords before writing")
    check("SetFirstPasswordAsync(BootstrapPassword)" in page,
          "the Users page card must pass the typed password to the first-password step")

    # 7. After that write on the page there is still no session, so the page must
    #    ask the shell for the sign-in gate rather than leave the operator on a
    #    page that cannot act.
    context = read(MODULE / "step3.b.7.AuthenticationContext.cs")
    shell_code = read(ROOT / "MainWindow.xaml.cs")
    check("public static void RequestSignIn()" in context,
          "there must be one way to ask for the sign-in gate while nobody is signed in")
    check("AuthenticationContext.RequestSignIn()" in page,
          "setting the first password from the page must ask for the sign-in gate")
    check("AuthenticationContext.SignInRequested += OnSignInRequested" in shell_code,
          "the shell must show the sign-in gate when it is asked for")


def require_writes_are_tracked():
    """A write path must never be handed a no-tracking row.

    Owner report (round 27, from the running build): the first-password card
    answered "The password was written, but the directory now reports 0
    account(s) with a password." The write had returned success and stored
    nothing — the row came from a read-only (`AsNoTracking`) lookup, so
    `SaveChangesAsync` wrote zero rows. The same mistake was already in the
    sign-in path, which means the failed-attempt counter and the 60-second
    lockout were silently never persisted: five wrong passwords locked nothing.

    These checks pin the rule rather than the two call sites.
    """
    repo = read(MODULE / "step3.b.4.UserRepository.cs")

    # The two lookups, and the difference between them, must be documented: the
    # next person to add a write path will copy whichever line is nearest.
    check("private static async Task<User?> FindUserRowTrackedAsync(" in repo,
          "there must be a tracked lookup for rows a write will modify")
    check("private static async Task<User?> FindUserRowAsync(" in repo,
          "the read-only lookup must stay for reads")

    read_only = repo.find("private static async Task<User?> FindUserRowAsync(\n            UserDbContext db")
    check(read_only >= 0, "the read-only lookup's signature changed")
    if read_only >= 0:
        doc = repo[max(0, read_only - 1200):read_only]
        check("never handed to a write path" in doc or "Reads only" in doc,
              "the no-tracking lookup must say, where it is defined, that its rows "
              "must not be used for writes")
        check("FindUserRowTrackedAsync" in doc,
              "the no-tracking lookup must point at the tracked one for writes")

    # Sign-in writes: the failed-attempt counter and the lock.
    start = repo.find("public async Task<SignInResult> SignInAsync(")
    check(start >= 0, "sign-in is missing")
    if start >= 0:
        body = repo[start:start + 7000]
        check("FindUserRowTrackedAsync(db, username" in body,
              "sign-in must load a TRACKED row: it writes FailedSignInCount and "
              "LockedUntilUtc, and a detached row takes the change and drops it")
        check("FindUserRowAsync(db, username" not in body,
              "sign-in must not use the read-only lookup, or the lockout never persists")

    # A guard that refuses an untracked row, so a future call site fails loudly
    # instead of silently writing nothing.
    check("private static void RequireTracked(" in repo,
          "there must be one guard that refuses to save an untracked row")
    check("EntityState.Detached" in repo,
          "the guard must test for a detached row")
    # Counted, not sampled: every password write keeps the affected-row count, checks
    # it, and passes the tracked guard. One of the three missing is the defect.
    writes = repo.count("int written = await SqliteResilience.SaveWithRetryAsync(")
    checked_counts = repo.count("if (written < 1)")
    tracked_guards = repo.count("RequireTracked(db, target,")

    check(writes >= 3,
          "every password write must keep the affected-row count it got back")
    check(checked_counts == writes,
          f"{writes - checked_counts} password write(s) return success without "
          "checking that SaveChanges stored anything")
    check(tracked_guards == writes,
          f"{writes - tracked_guards} password write(s) save a row without first "
          "proving it is tracked")

    # The first-password step in particular: it is the door an upgraded directory
    # depends on, so it gets the same two proofs.
    first = repo.find("public async Task<UserSaveResult> SetFirstPasswordAsync(")
    if first >= 0:
        body = repo[first:first + 5000]
        check("RequireTracked(db, target," in body,
              "the first-password write must prove its row is tracked before saving")
        check("if (written < 1)" in body,
              "the first-password write must report a save that stored nothing")


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
                # Round 30b: a button carries a shared gap token. Inside a row it
                # is the 16 px control gap (UiGapControl / UiGapControlEnd); a
                # standalone action under a card uses the vertical scale instead.
                # step1.d.18 owns the row rule; this one owns "not margin-less".
                check(any(token in tag for token in (
                          'Margin="{DynamicResource UiGapControl}"',
                          'Margin="{DynamicResource UiGapControlEnd}"',
                          'Margin="{DynamicResource UiGapGroup}"',
                          'Margin="{DynamicResource UiGapSection}"')),
                      f"{path.name}: action button without a shared gap token")
    check(page_buttons >= 6, "the step3 views must still expose their action buttons")
    for command in ("EditSelectedCommand", "ToggleActiveCommand", "NewUserCommand",
                    "SaveCommand", "DeactivateCommand", "CancelCommand",
                    "RegisterFirstAdministratorCommand"):
        check(f'Command="{{Binding {command}}}"' in view or
              f'Command="{{Binding {command}}}"' in editor,
              f"the module must still offer {command}")

    # Adjacent buttons inside one cluster use the control gap: SAVE USER, the
    # status action and Cancel sit in the same right-anchored footer cluster, so
    # each carries the END token and the last one stays flush with the card edge.
    footer = editor[editor.index("<WrapPanel HorizontalAlignment=\"Right\">"):]
    check(footer.count('Margin="{DynamicResource UiGapControlEnd}"') >= 3,
          "the editor footer buttons must be separated by the 16 px control gap")

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


def require_sign_in_is_reachable():
    """Round 30 (owner report): the shell must always offer a way into a session.

    The defect was not a wrong password. `BeginSignInAsync` collapsed the sign-in
    gate inside its own `catch`, and the first-run check collapsed both gates the
    same way. With no session every write is refused, so the application sat
    read-only with nothing on screen that could ask for a password again — only a
    restart recovered it. Both halves are guarded here:

      * a failure never hides the gate, and always has a surface to be reported on;
      * the shell itself shows the session and offers the step (Sign in / Sign
        out) on every page, so the gate is not the only door.
    """
    shell_view = read(ROOT / "MainWindow.xaml")
    shell = read(ROOT / "MainWindow.xaml.cs")
    banner = read(MODULE / "step3.c.5.ShellSessionViewModel.cs")
    gate_vm = read(MODULE / "step3.c.4.SignInViewModel.cs")

    # The gate is shown before anything that can fail, and nothing collapses it.
    check("SignInGate.Visibility = Visibility.Visible" in shell,
          "the sign-in gate must be shown by the shell")
    check("await signIn.InitializeAsync" in shell,
          "the shell must prepare the sign-in card")
    check(shell.index("SignInGate.Visibility = Visibility.Visible")
          < shell.index("await signIn.InitializeAsync"),
          "the gate must be visible BEFORE the step that can fail, or a failure "
          "leaves the shell with no way in")
    blocks = catch_blocks(shell)
    check(not any("SignInGate.Visibility" in body and "Collapsed" in body for body in blocks),
          "a failed sign-in preparation must never hide the gate: that is the "
          "defect this rule exists for")

    # Every failure has a surface.
    check("signIn.ReportProblem(" in shell,
          "a failure while preparing the card must land on the card's own error surface")
    check("signIn.ReportProblem(problem" in shell,
          "the reason the gate was opened must reach the card, not only the debug log")
    check("sessionBanner.ReportProblem(" in shell,
          "a failure that cannot even build the card must be reported in the banner")
    check("public void ReportProblem" in gate_vm,
          "the sign-in card must be able to display a failure it did not cause")
    check(any("BeginSignInAsync" in body for body in blocks),
          "a failure answering the first-run question must still open the way in")

    # The shell shows the session itself, on every page.
    check('x:Name="SessionBanner"' in shell_view,
          "the shell banner must host the session control")
    check('Command="{Binding ActionCommand}"' in shell_view,
          "the session control must be pressable")
    check("SessionBanner.DataContext = sessionBanner" in shell,
          "the session control's view model must be assigned by the shell")
    # The exact label expression, not the two words: "Sign in" also appears in a
    # tooltip and an accessible name, so a substring check stays green even when
    # the button itself stops offering the step.
    check('public string ActionLabel => IsSignedIn ? "Sign out" : "Sign in";' in banner,
          "the session control's button must offer Sign in when nobody is signed in "
          "and Sign out when somebody is")

    # It reads the session; it never writes one, and it never becomes a second
    # authority over who may act.
    check("AuthenticationContext.IsSignedIn" in banner,
          "the session control must read the process-wide session")
    for forbidden in ("AuthenticationContext.SignIn(", "AuthenticationContext.SignOut("):
        check(forbidden not in banner,
              f"the session control must not write the session itself ({forbidden})")
    check("SignInCommand" not in banner and "SignInAsync(" not in banner,
          "the banner must ask the shell to open the gate, not verify a password itself")

    # Collapsing the gate is only ever right on success, never to hide a failure.
    check(sum(1 for body in blocks if "SignInGate.Visibility" in body) == 0,
          "no catch block may touch the gate's visibility at all")


def require_session_is_remembered():
    """Round 32 (owner report): "remember the last login until Sign out".

    The session used to live in one static field, so every launch of the same
    application on the same workstation asked for the same password again. It is
    now remembered on this device — and because "remember me" is a security
    decision, the remembering is checked here, not just exercised:

      * the record holds no password, no hash and no salt;
      * it is bound to the Windows account and to the password stamp;
      * every reason it could be stale (row gone, inactive, no password, a reset,
        another Windows account) clears it instead of resuming;
      * one explicit Sign out clears it, and the shell is the only place that
        writes or clears it.
    """
    store = read(MODULE / "step3.b.8.DeviceSessionStore.cs")
    restorer = read(MODULE / "step3.b.9.RememberedSignIn.cs")
    context = read(MODULE / "step3.b.7.AuthenticationContext.cs")
    shell = read(ROOT / "MainWindow.xaml.cs")

    # What is stored: an id, a name, the Windows account and a password stamp.
    check("public int UserId { get; set; }" in store,
          "the remembered sign-in must be keyed by the directory id")
    check("WindowsAccount" in store,
          "the record must name the Windows account it was made under")
    check("PasswordUpdatedAtUtc" in store,
          "the record must carry the password stamp, so a reset invalidates it")
    for secret in ("PasswordHash", "PasswordSalt", "PasswordIterations", "Password ="):
        check(secret not in store,
              f"the remembered sign-in must never store {secret.strip()}")

    # The file is a device fact, not a database: LocalApplicationData, bounded.
    check("LocalApplicationData" in store,
          "the record belongs in this device's local application data")
    check("MaxBytes" in store and "MaxBytes)" in store,
          "the record must be size-bounded before it is trusted")

    # Every way a remembered session can stop being true must clear it.
    for guard, why in (
        ('StringComparison.OrdinalIgnoreCase', "the Windows account must match"),
        ("user == null", "a row that no longer exists must not resume"),
        ("!user.IsActive", "an inactive user must not resume"),
        ("!user.HasPassword", "an account with no password must not resume"),
        ("user.MustChangePassword", "an account that must change its password must not resume"),
        ("if (!SameStamp(remembered.PasswordUpdatedAtUtc, user.PasswordUpdatedAt))",
         "a password set since the record must invalidate it"),
    ):
        check(guard in restorer, f"the remembered sign-in must refuse to resume: {why}")
    check(restorer.count("DeviceSessionStore.Forget();") >= 3,
          "every failed check must clear the record, not leave it to be retried forever")

    # The session announces itself, so exactly one place can remember it.
    check("public static event EventHandler? SignedIn;" in context,
          "AuthenticationContext must announce a session, not only its end")
    check("SignedIn?.Invoke(null, EventArgs.Empty);" in context,
          "the announcement must be raised when a session is established")

    # The shell: resume before asking, remember on every sign-in, forget on exit.
    check("RememberedSignIn.TryRestoreAsync(" in shell,
          "the shell must try this device's remembered sign-in at launch")
    check("await BeginSignInAsync(remembered.Username" in shell,
          "a refused remembered sign-in must still open the gate, with its reason")
    try:
        ordered = shell.index("RememberedSignIn.TryRestoreAsync(") \
            < shell.index("await BeginSignInAsync(remembered.Username")
    except ValueError:
        ordered = False
    check(ordered,
          "the remembered sign-in must be tried BEFORE the gate is opened")
    check("DeviceSessionStore.Remember(" in shell,
          "a sign-in must be written down for the next launch")
    check("DeviceSessionStore.Forget();" in shell,
          "signing out must clear the remembered sign-in")
    sign_out = shell[shell.index("private void OnSignedOut"):]
    sign_out = sign_out[:sign_out.index("\n    }")]
    check("DeviceSessionStore.Forget();" in sign_out,
          "Sign out is the explicit \"do not remember me\": the record goes there, first")
    check("remembered.Restored" in shell,
          "the shell must act on the outcome instead of asking anyway")
    restored_block = shell[shell.index("if (remembered.Restored)") + 1:]
    restored_block = restored_block[:restored_block.index("\n        }")]
    check("return;" in restored_block,
          "a resumed session must NOT fall through to the gate: the password it "
          "just avoided asking for would be asked anyway")


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
    require_operator_lookup_uses_its_own_parameter()
    require_stamp_needs_a_verified_sign_in()
    require_bootstrap_carries_its_password()
    require_first_run_flow()
    require_first_password_upgrade()
    require_writes_are_tracked()
    require_command_namespace()
    require_layout_contract()
    require_sign_in_is_reachable()
    require_session_is_remembered()
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

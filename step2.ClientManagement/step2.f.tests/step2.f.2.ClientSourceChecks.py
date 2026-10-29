"""step2.f.2 — Client Management source and contract checks.

Portable checks that need no .NET SDK, no Windows and no running application.
They verify the properties that are cheap to get wrong and expensive to miss:

  1. every step2 file is present and follows the allocated naming hierarchy;
  2. the project includes the module explicitly (EnableDefaultCompileItems=false
     means a missing glob silently drops the whole module from the build);
  3. the shell hosts the workspace on exactly one tab and one navigation entry;
  4. migration 1011 is registered on the ONE existing chain, and no second
     chain, no Inquiry schema change and no Inquiry→Client foreign key exists;
  5. the client's representative is a stable user REFERENCE (migration 1014,
     nullable, verified against the user directory), never a typed name, and
     the directory still has no delete path;
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
    # The upper bound is a number, not a fixed literal: step3 appended index 9,
    # and the check must keep proving that index 8 is reachable without pinning
    # the file to a bound that the next stage legitimately raises.
    bound = re.search(r"if \(value is >= 0 and <= (\d+)\)", view_model)
    check(bound is not None, "the workspace index guard is missing")
    check(bound is not None and int(bound.group(1)) >= 8,
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
    # The representative is a reference into the user directory (1014). A second
    # people table or a typed representative name is what this forbids — the word
    # "salesman" in prose is fine.
    for forbidden_artifact in ("SalesmenTable", "SalesRepresentativeName",
                               'CREATE TABLE "Salesmen"', "SalesRepresentativeId"):
        check(forbidden_artifact not in module_text,
              "representative identity must be a reference into Users, not a second "
              f"people table or a typed name ({forbidden_artifact})")


def require_no_delete_path():
    module_text = "\n".join(read(p) for p in MODULE.rglob("*.cs"))
    check(".Remove(" not in module_text, "step2 must not remove client rows")
    check("ExecuteDelete" not in module_text, "step2 must not delete client rows")
    check("DeleteCommand" not in module_text, "step2 must not expose a delete command")
    check("SetActiveAsync" in module_text, "deactivation must be the lifecycle transition")


def theme_style_keys():
    """Every style key the installed themes declare."""
    keys = set()
    for dictionary in (ROOT / "Theme").glob("Theme*/step1.e.Components/*.xaml"):
        keys |= set(re.findall(r'x:Key="([A-Za-z0-9]+)"', dictionary.read_text(encoding="utf-8")))
    return keys


def require_theme_contract():
    views = sorted(MODULE.glob("*.xaml"))
    check(len(views) >= 2, "expected the workspace and editor views")

    contract = read(ROOT / "Theme/step1.a.ThemeEngine/step1.a.1.ThemeContract.cs")
    declared = set(re.findall(r'\["([A-Za-z0-9]+)"\]\s*=\s*typeof', contract))

    for view in views:
        ET.parse(view)  # must be well-formed
        text = read(view)

        # A Style attribute plus a <Element.Style> child is a XAML compile error
        # ("property has already been set"), which no runtime test would reach.
        root = ET.parse(view).getroot()
        for element in root.iter():
            if not isinstance(element.tag, str) or "Style" not in element.attrib:
                continue
            for child in element:
                check(not (isinstance(child.tag, str) and child.tag.endswith(".Style")),
                      f"{view.name}: <{element.tag}> sets Style twice")

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
        # A register names the themed ListGrid key instead of the implicit style:
        # both are declared by the active package, so both follow a theme switch,
        # and the keyed one is what carries the shared list pattern.
        for control in ("TextBox", "CheckBox", "ComboBox"):
            if f"<{control} " in text:
                check(f'Style="{{DynamicResource {{x:Type {control}}}}}"' in text,
                      f"{view.name}: {control} must bind the active implicit theme style")
        if "<DataGrid " in text:
            check('Style="{DynamicResource {x:Type DataGrid}}"' in text
                  or 'Style="{StaticResource ListGrid}"' in text,
                  f"{view.name}: DataGrid must bind the active theme's register style")

        # Every DynamicResource key must exist in the shared contract, except
        # module-local keys declared in this file.
        local = set(re.findall(r'x:Key="([A-Za-z0-9]+)"', text))
        theme_styles = theme_style_keys()
        # A DynamicResource names either a token (declared by the contract) or a
        # style (declared by the theme dictionaries). Round 50 moved the register
        # chrome into shared styles, so both kinds are legal and both are checked.
        for key in re.findall(r"\{DynamicResource ([A-Za-z0-9]+)\}", text):
            check(key in declared or key in local or key in theme_styles,
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


def require_attention_name():
    """ClientName is the company, so the person to address must be stored too."""
    # Word-boundary regexes, not substrings: "AttentionNameX" or
    # "ContactPersonMaxLength" must not satisfy these checks.
    property_pattern = r"public\s+string\s+AttentionName\s*\{"

    model = read(MODULE / "step2.a.1.Client.cs")
    check(re.search(property_pattern, model) is not None,
          "the client model must carry AttentionName next to the company name")
    check(re.search(r"\bContactPerson\b", model) is None,
          "the superseded ContactPerson member must not remain in the model")

    rules = read(MODULE / "step2.a.2.ClientRules.cs")
    check("ValidateAttentionName" in rules, "attention name validation is missing")
    check(re.search(r"\bContactPerson\b", rules) is None,
          "rules still reference the superseded column")

    editor = read(MODULE / "step2.c.1.ClientEditorViewModel.cs")
    check(re.search(property_pattern, editor) is not None and "HasAttentionNameError" in editor,
          "the editor view model must expose the attention name and its error state")
    check(re.search(r"\bContactPerson\b", editor) is None,
          "editor view model still references ContactPerson")

    editor_view = read(MODULE / "step2.d.2.ClientEditorView.xaml")
    check('Text="Attention name"' in editor_view,
          "the editor must label the field 'Attention name'")
    check('Binding AttentionName' in editor_view,
          "the editor must bind the attention name")
    check('Text="Client name (company) *"' in editor_view,
          "the required field must say it is the company")

    workspace_view = read(MODULE / "step2.d.1.ClientWorkspaceView.xaml")
    check(re.search(r'<DataGridTextColumn\s[^>]*Binding="\{Binding AttentionName\}"', workspace_view)
          is not None,
          "the register must show the attention name")
    check(re.search(r"\bContactPerson\b", workspace_view) is None,
          "the register still binds the superseded column")

    schema = read(MODULE / "step2.b.1.ClientSchema.cs")
    check('PersonColumn = "AttentionName"' in schema, "schema person column must be AttentionName")
    check("EnsureAttentionName" in schema, "the attention-name convergence (1012) is missing")
    check("dataPreservationVerified: true" in schema,
          "the legacy column must only be dropped after verified preservation")

    registry = read(
        ROOT / "Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs")
    check("ClientSchema.AttentionNameMigrationVersion" in registry,
          "migration 1012 is not registered")
    check(registry.count("new(ClientSchema.") == 3,
          "all three client migrations (1011, 1012 and 1014) must be on the one chain")
    # 1011 stays the immutable baseline: it must keep creating what it shipped.
    check("LegacyPersonColumn" in schema and "MigrationVersion = 1011" in schema,
          "1011 must remain the untouched baseline that 1012 converges from")


def require_action_footer():
    """The action buttons must sit in a themed footer, not crowd a card edge."""
    view = read(MODULE / "step2.d.2.ClientEditorView.xaml")
    check('Style="{DynamicResource Plate}"' in view,
          "the action buttons must sit in a themed footer plate")
    footer_at = view.find('Style="{DynamicResource Plate}"')
    save_at = view.find('SAVE CLIENT')
    check(0 < footer_at < save_at, "the save button must be inside the footer plate")
    # The footer must not hardcode padding: the theme owns it.
    for match in re.finditer(r"<ContentControl[^>]*Style=\"\{DynamicResource Plate\}\"[^>]*>", view):
        check("Padding=" not in match.group(0),
              "the footer plate must not hardcode Padding")
    # Both two-field groups must share one label/input/error row set, so labels
    # cannot drift apart vertically.
    check(view.count("<Grid.RowDefinitions>") >= 2,
          "two-field groups must share explicit label/input/error rows")


def require_button_icons():
    """Action buttons carry marks from the ONE shared icon system."""
    icon_service = read(ROOT / "Theme/step1.g.Customization/step1.g.8.IconService.cs")
    enum_body = icon_service.split("public enum IconKind", 1)[1].split("}", 1)[0]
    enum_body = re.sub(r"//[^\n]*", " ", enum_body)          # strip comments
    kinds = {name.strip() for name in enum_body.replace("\n", " ").split(",") if name.strip()}
    for required in ("Add", "Edit", "Refresh", "Search", "Save", "Cancel"):
        check(required in kinds, f"IconKind is missing the shared action mark '{required}'")
    for kind in ("Add", "Edit", "Refresh", "Search", "Save", "Cancel"):
        check(f"(IconKind.{kind}," in icon_service,
              f"IconKind.{kind} has no geometry in IconService.Mark")

    for view_name, expected_kinds in (
        ("step2.d.1.ClientWorkspaceView.xaml", {"Add", "Edit", "Search", "Cancel"}),
        ("step2.d.2.ClientEditorView.xaml", {"Save", "Search", "Cancel"}),
    ):
        text = read(MODULE / view_name)
        check('xmlns:studio="clr-namespace:ProGlassAutomation.ThemeStudio"' in text,
              f"{view_name}: the shared ShellMark namespace is not declared")
        used = set(re.findall(r'<studio:ShellMark Kind="([A-Za-z]+)"', text))
        for kind in expected_kinds:
            check(kind in used, f"{view_name}: no '{kind}' mark on the action buttons")
        for kind in used:
            check(kind in kinds, f"{view_name}: mark '{kind}' does not exist in IconKind")
        # Marks must never hardcode a colour or a size: the theme owns both.
        for match in re.finditer(r"<studio:ShellMark[^>]*>", text):
            check("Foreground=" not in match.group(0),
                  f"{view_name}: a mark must inherit its colour, not hardcode it")
            check('Width="{DynamicResource' in match.group(0),
                  f"{view_name}: a mark must size itself from a theme metric")


# SVG/WPF path command -> required argument count.
_PATH_ARITY = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2,
               "A": 7, "Z": 0}


def _validate_path_grammar(data):
    """Returns an error string, or None when the path is well formed.

    Geometry.Parse throws at render time on a malformed path, and a throwing
    mark would take the window down, so the grammar is checked at source level.
    """
    if not re.fullmatch(r"[MLHVCSQTAZmlhvcsqtaz0-9.,\-\s]+", data):
        return "contains characters that are not path data"
    tokens = re.findall(r"[A-Za-z]|-?\d*\.?\d+", data)
    index = 0
    saw_move = False
    while index < len(tokens):
        token = tokens[index]
        if not token.isalpha():
            return f"number '{token}' without a preceding command"
        command = token.upper()
        if command not in _PATH_ARITY:
            return f"unknown command '{token}'"
        if command == "M":
            saw_move = True
        arity = _PATH_ARITY[command]
        index += 1
        consumed = 0
        while index < len(tokens) and not tokens[index].isalpha():
            consumed += 1
            index += 1
        if arity == 0:
            if consumed:
                return f"'{command}' takes no arguments"
        else:
            if consumed == 0 or consumed % arity:
                return f"'{command}' needs a multiple of {arity} arguments, got {consumed}"
    if not saw_move:
        return "does not start with a move command"
    return None


def require_icon_geometry_valid():
    """Every mark path must be parseable, including the ones added for step2."""
    icon_service = read(ROOT / "Theme/step1.g.Customization/step1.g.8.IconService.cs")
    entries = re.findall(r"\(IconKind\.(\w+),[^)]*\)\s*=>\s*\"([^\"]+)\"", icon_service)
    check(len(entries) >= 15, f"expected the icon geometry table, found {len(entries)} entries")
    for kind, path in entries:
        problem = _validate_path_grammar(path)
        check(problem is None, f"IconKind.{kind} path is malformed: {problem}")


def require_field_heights():
    """A field beside a taller neighbour must keep its own height."""
    view = read(MODULE / "step2.d.2.ClientEditorView.xaml")
    boxes = re.findall(r"<TextBox\b[^>]*/>", view, flags=re.S)
    trn = [box for box in boxes if "{Binding Trn" in box]
    check(len(trn) == 1, "expected exactly one TRN text box")
    if trn:
        check('VerticalAlignment="Center"' in trn[0],
              "the TRN box must centre vertically, otherwise it stretches to the "
              "height of the Status cell beside it")



def shared_pattern():
    """The list pattern every module register uses, read from every theme.

    Round 50 replaced five per-module copies of this with one definition in
    each visual package, so the rules below are asserted against the definition
    rather than against the client page: a page that could not satisfy them
    after the move would have been a page with its own copy back.
    """
    texts = {}
    for package in sorted((ROOT / "Theme").glob("Theme*")):
        dictionary = package / "step1.e.Components/step1.e.10.Analytics.xaml"
        if dictionary.is_file():
            texts[package.name] = dictionary.read_text(encoding="utf-8")
    return texts


def style_block(theme_text, key):
    match = re.search(rf'<Style x:Key="{key}"[^>]*>(.*?)</Style>', theme_text, re.S)
    return match.group(0) if match else None


def require_register_alignment():
    """The register's alignment and chrome come from the shared list pattern.

    Owner, round 50: "make a global design pattern so upcoming module pages
    strictly follow these shared style ... column section top panel heading
    should be bold in grid line and center aligned. list related text should
    not have grid line but professional spacing and center aligned."

    Every guarantee the client page used to make for itself is asserted here
    against the pattern, in every installed theme, so a new list page inherits
    them and cannot quietly reintroduce a table of its own.
    """
    themes = shared_pattern()
    check(len(themes) == 9, f"expected 9 theme packages, found {len(themes)}")
    for name, text in themes.items():
        cell = style_block(text, "ListCell")
        check(cell is not None, f"{name}: the shared list cell style is missing")
        if cell:
            check('BasedOn="{StaticResource {x:Type DataGridCell}}"' in cell,
                  f"{name}: the cell must derive from the theme's cell style, or the register "
                  "loses the theme's selection and focus chrome")
            check('Property="HorizontalContentAlignment" Value="Center"' in cell,
                  f"{name}: the cell must centre its content, for the columns whose value is a "
                  "template rather than a text block")
            check('<ContentPresenter HorizontalAlignment="Stretch"' in cell,
                  f"{name}: the cell's presenter must take the whole column. Bound to "
                  "HorizontalContentAlignment it shrinks to the text, so the stretched TextBlock "
                  "inside it cannot be wider than its glyphs and a centred value renders left")
            check('Property="VerticalContentAlignment" Value="Center"' in cell,
                  f"{name}: a cell's VerticalContentAlignment defaults to Top, so every value "
                  "would sit at the top of its row")
            check("HorizontalContentAlignment=\"" not in cell.split("<ControlTemplate")[1],
                  f"{name}: HorizontalContentAlignment is a ContentControl property; a "
                  "ContentPresenter has no such member (MC3072)")
            check('Property="BorderBrush" Value="{DynamicResource TransparentBrush}"' in cell,
                  f"{name}: the cell must not paint a border of its own, or every data row is boxed")

        header = style_block(text, "ListColumnHeader")
        check(header is not None, f"{name}: the shared column-header style is missing")
        if header:
            check('BasedOn="{StaticResource {x:Type DataGridColumnHeader}}"' in header,
                  f"{name}: the header must derive from the theme's header style")
            # Bold, and the literal that makes it bold: FontWeightHeading is
            # SemiBold in every package, which is the weight the owner called
            # "not the heading type bold".
            check('Property="FontWeight" Value="Bold"' in header,
                  f"{name}: the header must be bold type, not the heading token (SemiBold)")
            check('Property="HorizontalContentAlignment" Value="Center"' in header,
                  f"{name}: the header label must be centred")
            check('Property="Template"' not in header,
                  f"{name}: the header carries a hand-written template. The framework's template "
                  "is the one that honours HorizontalContentAlignment; a re-created presenter has "
                  "rendered this heading flush left twice")
            check('Property="BorderBrush" Value="{DynamicResource BorderStrong}"' in header
                  and 'Property="BorderThickness" Value="0,0,1,1"' in header,
                  f"{name}: the header band must draw one hairline rule on its trailing and "
                  "bottom edges")

        value = style_block(text, "ListText")
        check(value is not None, f"{name}: the shared cell-text style is missing")
        if value:
            check('Property="TextAlignment" Value="Center"' in value,
                  f"{name}: register values must be centred under their centred headers")
            check('Property="TextTrimming" Value="CharacterEllipsis"' in value,
                  f"{name}: a long value must trim instead of pushing its column wider")
            check('Property="ToolTip" Value="{Binding Text, RelativeSource={RelativeSource Self}}"'
                  in value,
                  f"{name}: a trimmed value must keep its full text one hover away (owner, round "
                  "50). The value binds its own text to ToolTip: no measuring, no dispatcher work")

    view = read(MODULE / "step2.d.1.ClientWorkspaceView.xaml")
    check('Style="{StaticResource ListGrid}"' in view,
          "the register does not use the shared register style, so it has no ruled header band, "
          "no centred body and no equal-width columns")
    for style in ("ListText", "ListMonoText", "ListStrongText"):
        # StaticResource: ElementStyle is a plain CLR property on DataGridColumn, so
        # a DynamicResource there is silently ignored — the column keeps the default
        # left-aligned, untrimmed TextBlock, which is exactly what the owner saw.
        check(f'ElementStyle="{{StaticResource {style}}}"' in view,
              f"the register has no column using {style} through a StaticResource, so part of the "
              "table is not following the pattern")


def require_command_bar():
    """The register page is a register: one bar of chrome, then rows.

    Owner feedback: "a list page has a list from the start ... on header there
    are too many options, move them into a dropdown ... big cards fix in small
    cards". Four cards, three headings and five buttons used to sit above a
    six-column register and left about eleven rows on screen.

    This pins the replacement: no hero or metric cards, one bar, one primary
    action, one overflow menu, filter state as chips, and selection actions in a
    footer strip. Anything creeping back in - a second primary button, a card per
    counter, a toolbar row - fails here.
    """
    view = read(MODULE / "step2.d.1.ClientWorkspaceView.xaml")

    check('Style="{DynamicResource HeroCard}"' not in view,
          "the hero card is gone: the page heading is a PageHeading plus a Caption "
          "inside the command bar")
    check('Style="{DynamicResource MetricCard}"' not in view,
          "the counter cards are gone: the counts are a caption and the chip counts")

    bar = re.search(r'<ContentControl Grid.Row="0" Style="\{DynamicResource Plate\}".*?</ContentControl>',
                    view, re.S)
    check(bar is not None, "the page needs one command bar - a Plate on row 0")
    if bar:
        body = bar.group(0)
        check('Text="{Binding RegisterSummary}"' in body,
              "the command bar carries the live count sentence")
        check('Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"' in body,
              "search lives in the bar: a list page without a visible search field "
              "reads as broken")
        check('Command="{Binding NewClientCommand}"' in body,
              "the primary action stays visible; it must never move into a menu")
        check('x:Name="MoreActionsButton"' in body and "<ContextMenu" in body,
              "secondary actions belong in one themed overflow menu")
        check("<MenuItem Header=\"Refresh\"" in body,
              "Refresh should sit in the overflow menu, bound to RefreshCommand")
        check('Command="{Binding ClearSearchCommand}"' in body,
              "clearing a search is a field affordance next to the field")

    check(view.count('Style="{DynamicResource PrimaryButton}"') == 1,
          "exactly one primary action on the page, or the hierarchy is noise")

    chips = re.search(r'<ItemsControl ItemsSource="\{Binding StatusTabs\}".*?</ItemsControl>', view, re.S)
    check(chips is not None, "the filter state is a chip strip bound to StatusTabs")
    if chips:
        body = chips.group(0)
        check('Command="{Binding DataContext.SelectStatusTabCommand,' in body,
              "a chip selects its own tab through the workspace command")
        check('Style="{StaticResource ClientFilterChip}"' in body,
              "chips use the shared theme-token style")
    check('IsChecked="{Binding IncludeInactive}"' not in view,
          "the Include inactive checkbox is replaced by the All / Active / Inactive "
          "chips; a checkbox hides the filter's state")
    check('Text="Show"' not in view,
          "the filter card and its headings are gone")

    # The strip under the register is gone. Owner direction: "move these to top
    # so this space can be extra" - the selection actions moved into the bar and
    # the register gets those pixels back as rows. The strip also printed
    # SelectedSummary, which repeated the row already on screen ("duplicated
    # section row data name").
    check('Grid.Row="1" Style="{DynamicResource Plate}"' not in view,
          "no action strip under the register: its buttons belong in the command "
          "bar and its space belongs to the register")
    check('Text="{Binding SelectedSummary}"' not in view,
          "the page must not repeat the selected row's data outside the register")
    for command in ("EditSelectedCommand", "ToggleActiveCommand"):
        check(f'Command="{{Binding {command}}}"' in view,
              f"{command} must stay reachable from the command bar")

    # The bar no longer repeats the page title: the shell banner already carries
    # it, and the duplicate was the crowding the owner rejected.
    check('Style="{DynamicResource PageHeading}"' not in view,
          "the page title lives in the shell banner; repeating it in the command "
          "bar is what made that corner look crowded")

    # Every column has a header, including the row-menu column, which used to
    # open a headerless gap at the right edge of the band.
    check('Header="Actions"' in view,
          "the row-menu column needs a header; an empty one reads as a broken band")
    check("DataGridTemplateColumn.Header" not in view,
          "a column carries a hand-written header template; Header=\"…\" is the pattern, and the "
          "band renders it bold and centred itself")
    check(view.count('Header="') + view.count('Text="')
          >= 9, "the register lost a column header")

    # Per-row actions: one quiet trigger, two themed menu items.
    check('Style="{StaticResource ClientRowMenuButton}"' in view,
          "each row needs one row-menu trigger, not three inline icons")
    check('Command="{Binding PlacementTarget.Tag.EditClientCommand,' in view,
          "the row menu edits the row it was opened on")
    check('Command="{Binding PlacementTarget.Tag.ToggleActiveCommand,' in view,
          "the row menu carries the lifecycle action")
    check('Header="{Binding ToggleLabel}"' in view,
          "the lifecycle label comes from the row projection, so the menu reads "
          "'Deactivate client' or 'Re-activate client' for that row")

    code_behind = read(MODULE / "step2.d.1.ClientWorkspaceView.xaml.cs")
    check("private void OnMoreActionsClick(" in code_behind and
          "private void OnRowMenuClick(" in code_behind,
          "a Button.ContextMenu only opens on right-click; the visible menus need "
          "these two handlers, or the overflow and row menus are dead controls")
    for handler in ("OnMoreActionsClick", "OnRowMenuClick"):
        check(f'Click="{handler}"' in view, f"{handler} is not wired to anything")


def require_paired_field_widths():
    """TRN and Status take one width each, and the rows above match."""
    view = read(MODULE / "step2.d.2.ClientEditorView.xaml")
    check('Width="1.4*"' not in view,
          "a paired field row still uses a 1 : 1.4 split; the pairs are equal halves")
    equal_pairs = re.findall(r'<ColumnDefinition Width="1\*" />\s*<ColumnDefinition Width="1\*" />', view)
    check(len(equal_pairs) == 2,
          f"both paired field rows must be equal halves, found {len(equal_pairs)}")

def require_header_rules():
    """Column rules belong in the header row, not between the rows of the list.

    Owner feedback, three times: "grid lines only in the column header", "row
    data normal blank". Round 50 made it the page-level rule: the header band is
    ruled, the body is not. Two mechanisms used to draw body rules - the view's
    GridLinesVisibility and the package's per-cell border - so both are asserted
    where they now live, in the shared pattern.
    """
    themes = shared_pattern()
    for name, text in themes.items():
        grid = style_block(text, "ListGrid")
        check(grid is not None, f"{name}: the shared register style is missing")
        if grid:
            check('Property="GridLinesVisibility" Value="None"' in grid,
                  f"{name}: the register must state GridLinesVisibility None; leaving it unset lets "
                  "each package decide, and some of them draw horizontal rules")
            check('Property="ColumnWidth" Value="*"' in grid,
                  f"{name}: the register must give every column an equal share, which is what makes "
                  "a column fit its page (owner, round 50)")
            check('Property="MinColumnWidth" Value="{DynamicResource ControlMinWidth}"' in grid,
                  f"{name}: a floor is needed, or a column can be dragged down to nothing")
            check('Property="CanUserResizeColumns" Value="False"' not in grid,
                  f"{name}: columns must stay resizable by hand")
            check('Property="CanUserResizeRows" Value="False"' in grid,
                  f"{name}: row height is the theme's business, not the user's")
            check('Property="Margin" Value="{DynamicResource MarginItem}"' in grid,
                  f"{name}: the register needs the theme's own outside margin")
            check('VerticalGridLinesBrush=' not in grid and 'HorizontalGridLinesBrush=' not in grid,
                  f"{name}: a rule brush override is not wanted: the header carries the rules")

        header = style_block(text, "ListColumnHeader")
        if header:
            # The rule that divides the columns is the header's own border, one
            # hairline on the trailing and bottom edges, in a visible theme colour
            # — what the master app's header style does.
            check('Property="BorderBrush" Value="{DynamicResource BorderStrong}"' in header,
                  f"{name}: the header rule must use a visible theme token")
            check('Property="BorderThickness" Value="0,0,1,1"' in header,
                  f"{name}: the rule belongs on the column boundary and under the band, one "
                  "hairline thick, not on a template element")

    view = read(MODULE / "step2.d.1.ClientWorkspaceView.xaml")
    datagrid = view[view.index("<DataGrid "):]
    datagrid = datagrid[:datagrid.index(">")]
    check('GridLinesVisibility=' not in datagrid,
          "the register sets its own line visibility again; the shared style owns that rule now, "
          "and two competing settings is how the body rules came back before")


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
            # RelativeSource / PlacementTarget expressions resolve outside the
            # view model (a row menu's command comes from the grid's Tag, for
            # instance), so only plain paths can be checked here.
            if "RelativeSource" in expression or expression.startswith("DataContext"):
                continue
            first = re.split(r"[,\s]", expression)[0]
            if first and first[0].isalpha():
                names.add(first)
        return names

    targets = {
        "step2.d.1.ClientWorkspaceView.xaml": workspace_members | row_members,
        # The representative picker renders ClientRepresentative items, whose
        # members are declared in the repository contract file, not the view model.
        "step2.d.2.ClientEditorView.xaml": editor_members | row_members,
    }
    for view_name, known in targets.items():
        names = binding_names(MODULE / view_name)
        check(len(names) > 20, f"{view_name}: expected the module's bindings, found {len(names)}")
        for name in sorted(names - known):
            check(False, f"{view_name}: binding to unknown member '{name}'")


def require_representative_reference():
    """The representative is a reference, added only after Users existed."""
    schema = read(MODULE / "step2.b.1.ClientSchema.cs")
    check("RepresentativeMigrationVersion = 1014" in schema,
          "the representative reference must be migration 1014")
    check('RepresentativeColumn = "RepresentativeUserId"' in schema,
          "the representative column must be the user id, not a name")
    check('AddColumnIfMissing(connection, TableName, RepresentativeColumn, "INTEGER NULL")' in schema,
          "the representative column must be a NULLABLE integer reference")
    check("AssertRepresentativeReference" in schema and "NOT EXISTS" in schema,
          "the migration must refuse client rows that point at a missing user")
    check("TableExists" in schema,
          "the orphan check must tolerate a database that predates the Users table")

    registry = read(ROOT / "Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs")
    check("ClientSchema.RepresentativeMigrationVersion" in registry,
          "migration 1014 is not registered on the existing chain")
    users_at = registry.find("UserSchema.MigrationVersion")
    representative_at = registry.find("ClientSchema.RepresentativeMigrationVersion")
    check(0 <= users_at < representative_at,
          "1014 must be registered AFTER 1013, or a client could reference a table that does not exist yet")

    model = read(MODULE / "step2.a.1.Client.cs")
    check("public int? RepresentativeUserId { get; set; }" in model,
          "the client entity must carry a nullable user id")
    check("RepresentativeName { get; set; }" not in model,
          "the client entity must not store a representative NAME")

    repository = read(MODULE / "step2.b.4.ClientRepository.cs")
    def method_body(name):
        start = repository.index(f"public async Task<ClientSaveResult> {name}(")
        following = repository.find("public async Task<ClientSaveResult>", start + 10)
        return repository[start:following if following != -1 else len(repository)]

    for method in ("CreateAsync", "UpdateAsync"):
        check("ValidateRepresentativeAsync(" in method_body(method),
              f"{method} must verify that the chosen representative exists before writing")
    check("UserCatalog.SalesmanRole" in repository,
          "the picker must read the role code from the one catalogue")
    picker = repository[repository.index("GetRepresentativesAsync("):]
    picker = picker[:picker.index("public async Task<ClientSaveResult>") if "public async Task<ClientSaveResult>" in picker else len(picker)]
    check("Where(user => user.IsActive)" in picker,
          "the picker must offer the active users, not only one role: a business "
          "with no salesman yet still has to be able to assign somebody")
    others = "\n".join(
        read(path) for path in sorted(MODULE.glob("*.cs")))
    check('"SALESMAN"' not in others,
          "the salesman role code must not be retyped outside the catalogue")

    editor = read(MODULE / "step2.d.2.ClientEditorView.xaml")
    check('ItemsSource="{Binding Representatives}"' in editor,
          "the editor must offer the representative picker")
    check('SelectedItem="{Binding SelectedRepresentative, Mode=TwoWay}"' in editor,
          "the picker must bind the row itself, not an id that can fail to resolve")
    check("RepresentativeHint" in editor,
          "an empty picker must explain itself instead of showing a dead dropdown")

    register = read(MODULE / "step2.d.1.ClientWorkspaceView.xaml")
    check('Header="Representative"' in register or 'Text="Representative"' in register,
          "the register must show the representative column")
    check('Binding="{Binding RepresentativeText}"' in register,
          "the register column must render the resolved name, not an id")

    spec = read(ROOT / "docs/12.Module-Specifications.md")
    check("RepresentativeUserId" in spec and "1014" in spec,
          "docs/12 must record the reference and its migration")
    check("not added in this stage" not in spec,
          "docs/12 still claims the representative reference is deferred")


def require_trn_separators_tolerated():
    """A formatted TRN must be accepted and stored as digits, not refused.

    Round 34.7: `ValidateTrn` rejected any non-digit while `NormalizeTrn` stored
    digits only and `TrnKey` compared digits — the module's own normalization
    check feeds it "100-123-4567 00003", and the repository round-trip saves
    "100-123-456-700-003". So the stripping path was unreachable from any typed
    or pasted value, and the operator was refused a TRN written the way a
    certificate prints it, while the Company Profile field accepted it.
    """
    rules = read(MODULE / "step2.a.2.ClientRules.cs")
    validator_start = rules.find("public static string? ValidateTrn")
    check(validator_start >= 0, "ValidateTrn must exist")
    if validator_start < 0:
        return
    validator = rules[validator_start:rules.find("public static", validator_start + 10)]
    check('.Replace(" ", "").Replace("-", "")' in validator,
          "the TRN validator must tolerate the printed separators (space and dash) "
          "instead of refusing the field")
    check("digits.Length > TrnMaxLength" in validator,
          "the TRN length bound must apply to the stored digits, not to the typed "
          "string, or a valid dashed TRN is refused for being formatted")
    check("NormalizeTrn" in rules and "=> DigitsOnly(value)" in rules,
          "NormalizeTrn must still store the TRN as digits only")


def require_migration_assertion_derives_the_tip():
    """A migration check must assert the chain's tip, not the day it was written.

    Round 34.7: step2.f.1 asserted `PRAGMA user_version == "1012"`. When 1013-1016
    joined the same chain the value became 1016 and the check failed on correct
    behaviour. The registry is the authority, so the expectation is derived from it.
    """
    compiled = read(MODULE / "step2.f.tests/step2.f.1.ClientRuleChecks.cs")
    check("AppSchemaMigrations.Inquiries.Max(" in compiled,
          "the compiled migration check must derive the expected version from the "
          "registered chain instead of hard-coding a number")
    hard_coded = re.findall(r'user_version[^\n]*\n[^\n]*Equal\("1[0-9]{3}"', compiled)
    check(not hard_coded,
          "a hard-coded PRAGMA user_version literal pins today's chain tip and fails "
          "as soon as a migration is added")


def require_docs_recorded():
    registry = read(ROOT / "docs/06.Feature-Registry.md")
    check("| step2 | Client Management |" in registry, "the registry does not record step2")
    # 2026-10-27: the owner established the remaining order and allocated step3-step11.
    # The registry has to carry every allocation, or the order is unrecorded again.
    for number, name in (("step3", "User Management"), ("step4", "Company Profile"),
                         ("step5", "Project Records & Inquiry Integration"),
                         ("step6", "Proforma Invoice"), ("step7", "Job Order"),
                         ("step8", "Delivery Note"), ("step9", "Tax Invoice"),
                         ("step10", "Statement"), ("step11", "Reports")):
        check(f"| {number} | {name}" in registry,
              f"the registry does not allocate {number} ({name})")
    roadmap = read(ROOT / "docs/04.Project-Roadmap.md")
    check("step2" in roadmap and "Client Management" in roadmap,
          "the roadmap does not record the step2 gate")
    check("Foundation Review" in roadmap and "`G1`" in roadmap,
          "the roadmap does not record the Foundation Review gate")
    rules = read(ROOT / "docs/00.Project-Rules.md")
    check("`step2` is Client Management" in rules,
          "the naming rules do not describe step2 as Client Management")
    check("step3 onward is unallocated" not in rules,
          "the naming rules still claim everything after step2 is unallocated")
    check("Dotted sub-identifiers are not feature identifiers" in rules,
          "the naming rules do not forbid dotted sub-identifiers such as step2.1")
    order = read(ROOT / "docs/11.Business-Module-Roadmap.md")
    for module in ("User Management", "Company Profile", "Project Records & Inquiry Integration",
                   "Proforma Invoice", "Job Order", "Delivery Note", "Tax Invoice",
                   "Statement", "Reports"):
        check(module in order, f"the business-module roadmap does not record {module}")
    check("G1" in order, "the business-module roadmap does not record the Foundation Review gate")
    log = read(ROOT / "docs/05.Decision-Log.md")
    check("the remaining implementation order is established now" in log,
          "the decision log does not record the 2026-10-27 order decision")


def main():
    require_files()
    require_project_inclusion()
    require_shell_hosting()
    require_migration_registration()
    require_no_inquiry_schema_change()
    require_no_delete_path()
    require_theme_contract()
    require_attention_name()
    require_action_footer()
    require_button_icons()
    require_icon_geometry_valid()
    require_field_heights()
    require_register_alignment()
    require_command_bar()
    require_header_rules()
    require_paired_field_widths()
    require_binding_integrity()
    require_representative_reference()
    require_trn_separators_tolerated()
    require_migration_assertion_derives_the_tip()
    require_docs_recorded()

    if FAILURES:
        print(f"FAIL: {len(FAILURES)} client-management source check(s) failed")
        for failure in FAILURES:
            print("  - " + failure)
        return 1
    print("PASS: step2 client management — naming, single migration chain, no Inquiry schema "
          "change, representative as a verified user reference, no delete path, nine-theme "
          "contract, centred register, header rules and manual resize, one command bar, equal "
          "paired fields, docs recorded.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

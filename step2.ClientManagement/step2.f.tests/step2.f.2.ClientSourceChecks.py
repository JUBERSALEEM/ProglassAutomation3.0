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
    check(registry.count("new(ClientSchema.") == 2,
          "both client migrations (1011 and 1012) must be on the one chain")
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
        ("step2.d.1.ClientWorkspaceView.xaml", {"Add", "Edit", "Refresh", "Cancel"}),
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



def require_register_alignment():
    """Every register column is centred, header and value alike.

    Alignment cannot be done from inside the content: a DataGridTextColumn's
    cell gives its TextBlock a full-width slot, but a template column's cell
    gives its content a content-sized one, so the Status badge ignored its own
    HorizontalAlignment and hugged the left edge. Centring therefore belongs on
    the cell and on the header, which is what these checks pin down.
    """
    view = read(MODULE / "step2.d.1.ClientWorkspaceView.xaml")

    cell = re.search(r'<Style x:Key="ClientCell" TargetType="DataGridCell"(.*?)</Style>', view, re.S)
    check(cell is not None, "the register has no cell style to centre its values")
    if cell:
        check('Property="HorizontalContentAlignment" Value="Center"' in cell.group(0),
              "the cell style must centre its content, otherwise the status badge "
              "stays hard against the left edge")
        check('BasedOn="{StaticResource {x:Type DataGridCell}}"' in cell.group(0),
              "the cell style must derive from the theme's cell style, or the "
              "register loses the theme's cell chrome")

    header = re.search(r'<Style x:Key="ClientColumnHeader" TargetType="DataGridColumnHeader"(.*?)</Style>',
                       view, re.S)
    check(header is not None, "the register has no column-header style")
    if header:
        check('Property="HorizontalContentAlignment" Value="Center"' in header.group(0),
              "the column-header style must centre its label")
        check('BasedOn="{StaticResource {x:Type DataGridColumnHeader}}"' in header.group(0),
              "the header style must derive from the theme's header style, or the "
              "register loses the theme's header template and resize grippers")

    check('CellStyle="{StaticResource ClientCell}"' in view,
          "the register DataGrid does not use the centring cell style")
    check('ColumnHeaderStyle="{StaticResource ClientColumnHeader}"' in view,
          "the register DataGrid does not use the centring header style")

    check(len(re.findall(r'\bHeader="[A-Za-z]', view)) == 7,
          "all seven register columns must be present")

    for style_name in ("ClientNameCell", "ClientMonoCell", "ClientPlainCell"):
        style = re.search(r'<Style x:Key="' + style_name + r'"[^>]*>(.*?)</Style>', view, re.S)
        check(style is not None, f"{style_name} is missing from the register")
        if style:
            body = style.group(1)
            check('Property="TextAlignment" Value="Center"' in body,
                  f"{style_name} must centre the text inside its own box")
    check('Style="{DynamicResource StatusBadge}"' in view,
          "the status badge must keep its themed style")

    columns = re.findall(r"<DataGridTextColumn\s[^>]*>", view, re.S)
    check(len(columns) == 6, f"expected six text columns, found {len(columns)}")
    for column in columns:
        check("ElementStyle=" in column, "a register text column has no cell style")


def require_action_layout():
    """The actions card must read as three equal, full-width buttons.

    Owner feedback twice: the secondaries looked collapsed and unequal. Two
    causes are guarded here - a secondary that does not stretch into its half,
    and doubled margins (a margin on the button inside a grid that already has
    one) which silently makes one button narrower than the other.
    """
    view = read(MODULE / "step2.d.1.ClientWorkspaceView.xaml")
    primary = re.search(r'<Button Style="\{DynamicResource PrimaryButton\}"(.*?)>', view, re.S)
    check(primary is not None, "the primary action button is missing")
    if primary:
        check('HorizontalAlignment="Stretch"' in primary.group(1),
              "NEW CLIENT must fill the card width")
    grid = re.search(r"<Grid Margin=\"\{DynamicResource MarginInline\}\">\s*"
                     r"<Grid.ColumnDefinitions>\s*"
                     r'<ColumnDefinition Width="1\*" />\s*'
                     r'<ColumnDefinition Width="1\*" />', view)
    check(grid is not None, "Edit and Refresh must share one row of equal halves")
    for command in ("EditSelectedCommand", "RefreshCommand"):
        block = re.search(r"<Button[^>]*Command=\"\{Binding " + command + r"\}\"[^>]*>", view, re.S)
        check(block is not None, f"{command} has no button")
        if block:
            check('HorizontalAlignment="Stretch"' in block.group(0),
                  f"{command} must stretch into its half instead of hugging its label")
            check("Margin=" not in block.group(0),
                  f"{command} carries a margin inside an already-inset grid, so the "
                  "two secondaries cannot be the same width")


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

    Owner feedback: "grid line in top column not in the list". The theme owns
    the row rules (it sets GridLinesVisibility and both brushes itself), so this
    view must not override either — it only makes the header's own border
    visible, which is what gives each column an edge to grab when resizing.
    """
    view = read(MODULE / "step2.d.1.ClientWorkspaceView.xaml")
    header = re.search(r'<Style x:Key="ClientColumnHeader" TargetType="DataGridColumnHeader"(.*?)</Style>',
                       view, re.S)
    check(header is not None, "the register has no column-header style")
    if header:
        check('Property="BorderBrush" Value="{DynamicResource BorderStrong}"' in header.group(0),
              "the header rule must be repainted in a visible theme token")
    check('GridLinesVisibility="' not in view,
          "the view must not override the theme's row rules")
    check('VerticalGridLinesBrush="' not in view,
          "vertical rules between rows are not wanted; the header carries them")
    check('CanUserResizeColumns="True"' in view,
          "columns must stay resizable by hand")
    check('MinColumnWidth="{DynamicResource ControlMinWidth}"' in view,
          "a floor is needed, or a column can be dragged down to nothing")


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
    require_attention_name()
    require_action_footer()
    require_button_icons()
    require_icon_geometry_valid()
    require_field_heights()
    require_register_alignment()
    require_action_layout()
    require_header_rules()
    require_paired_field_widths()
    require_binding_integrity()
    require_docs_recorded()

    if FAILURES:
        print(f"FAIL: {len(FAILURES)} client-management source check(s) failed")
        for failure in FAILURES:
            print("  - " + failure)
        return 1
    print("PASS: step2 client management — naming, single migration chain, no Inquiry schema "
          "change, no representative identity, no delete path, nine-theme contract, centred "
          "register, header rules and manual resize, equal action buttons, equal "
          "paired fields, docs recorded.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

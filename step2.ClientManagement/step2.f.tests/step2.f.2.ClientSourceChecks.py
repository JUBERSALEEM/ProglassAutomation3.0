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



def require_register_alignment():
    """Every register column is centred, header and value alike.

    Alignment cannot be done from inside the content and it cannot be delegated
    either: the packages hand a DataGridCell a content-sized slot, so a value
    with HorizontalAlignment="Center" or TextAlignment="Center" still rendered
    hard against the left edge (measured on the Neumorphism client screen). The
    same is true of the header. So the view owns both templates and stretches the
    presenter; the value styles then centre inside a full-width slot. These
    checks pin that arrangement down, because nothing else can.
    """
    view = read(MODULE / "step2.d.1.ClientWorkspaceView.xaml")

    cell = re.search(r'<Style x:Key="ClientCell" TargetType="DataGridCell"(.*?)</Style>', view, re.S)
    check(cell is not None, "the register has no cell style to centre its values")
    if cell:
        body = cell.group(0)
        check('BasedOn="{StaticResource {x:Type DataGridCell}}"' in body,
              "the cell style must derive from the theme's cell style, or the "
              "register loses the theme's selection and focus chrome")
        check('Property="HorizontalContentAlignment" Value="Center"' in body,
              "the cell style must centre its content as well, for the columns "
              "whose value is a template rather than a text block")
        check('HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"' in body,
              "the presenter must take its alignment from the cell through a "
              "TemplateBinding; a hard-coded value ignores the cell style, and "
              "ContentPresenter cannot carry ContentControl alignment itself")
        check('Property="VerticalContentAlignment" Value="Center"' in body,
              "a cell's VerticalContentAlignment defaults to Top; without it the "
              "presenter binds to Top and every value sits at the top of its row")
        check("HorizontalContentAlignment=\"" not in body.split("<ControlTemplate")[1],
              "HorizontalContentAlignment is a ContentControl property; a "
              "ContentPresenter has no such member (MC3072) and the alignment "
              "belongs on the cell style above")
        check('Property="BorderBrush" Value="{DynamicResource TransparentBrush}"' in body,
              "the cell must not paint a border of its own; the packages drew one "
              "per cell, which boxed every data row")

    header = re.search(r'<Style x:Key="ClientColumnHeader" TargetType="DataGridColumnHeader"(.*?)</Style>',
                       view, re.S)
    check(header is not None, "the register has no column-header style")
    if header:
        body = header.group(0)
        check('BasedOn="{StaticResource {x:Type DataGridColumnHeader}}"' in body,
              "the header style must derive from the theme's header style")
        check('HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"' in body,
              "the header presenter must take its alignment from the header style, "
              "or the label and the column rule collapse to the width of the text")
        check('Property="HorizontalContentAlignment" Value="Stretch"' in body,
              "the header style must stretch its content, and that property "
              "belongs on the column header (a ContentControl), not on the "
              "presenter inside its template")
        check('Property="Padding" Value="0"' in body,
              "the header's own padding must go: the label carries the theme inset")

    check('CellStyle="{StaticResource ClientCell}"' in view,
          "the register DataGrid does not use the centring cell style")
    check('ColumnHeaderStyle="{StaticResource ClientColumnHeader}"' in view,
          "the register DataGrid does not use the centring header style")

    label = re.search(r'<Style x:Key="ClientHeaderLabel" TargetType="TextBlock">(.*?)</Style>', view, re.S)
    check(label is not None, "the register has no header label style")
    if label:
        check('Property="TextAlignment" Value="Center"' in label.group(1),
              "the header label must centre its text")
        check('Property="HorizontalAlignment" Value="Stretch"' in label.group(1),
              "the header label must fill the stretched presenter to have anything "
              "to centre inside")

    columns = re.findall(r"<DataGridTextColumn\s[^>]*>", view, re.S)
    check(len(columns) == 6, f"expected six text columns, found {len(columns)}")
    for column in columns:
        check("ElementStyle=" in column, "a register text column has no cell style")

    # Seven columns, so six rules: the last column closes the band with the grid's
    # own edge instead of a divider hanging in mid-air.
    check(view.count('Style="{StaticResource ClientHeaderLabel}"') == 8,
          "every register column must use the centred header label, including the "
          "row-menu column")
    check(view.count('Style="{StaticResource ClientHeaderRule}"') == 7,
          "one rule per column boundary: seven for eight columns; the last column "
          "closes the band on the grid's own edge")

    for style_name in ("ClientNameCell", "ClientMonoCell", "ClientPlainCell"):
        style = re.search(r'<Style x:Key="' + style_name + r'"[^>]*>(.*?)</Style>', view, re.S)
        check(style is not None, f"{style_name} is missing from the register")
        if style:
            check('Property="TextAlignment" Value="Center"' in style.group(1),
                  f"{style_name} must centre the text inside its own box")
    check('Style="{DynamicResource StatusBadge}"' in view,
          "the status badge must keep its themed style")
    register_columns = view[view.index("<DataGrid.Columns>"):]
    register_columns = register_columns[:register_columns.index("</DataGrid.Columns>")]
    # Only the column declarations matter here: a MenuItem's Header inside a cell
    # template is a menu item label, not a column header.
    check(not re.search(r"<DataGrid(?:Text|Template)Column\b[^>]*\sHeader=", register_columns),
          "a plain string header cannot be centred or carry a rule; every column "
          "must use the header label template")


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
        check('Text="Clients"' in body and 'Style="{DynamicResource PageHeading}"' in body,
              "the command bar owns the page heading")
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

    footer = re.search(r'<ContentControl Grid.Row="1" Style="\{DynamicResource Plate\}".*?</ContentControl>',
                       view, re.S)
    check(footer is not None, "selection actions belong to a footer strip, not a card")
    if footer:
        body = footer.group(0)
        check('Text="{Binding SelectedSummary}"' in body,
              "the footer states what is selected")
        check('Command="{Binding EditSelectedCommand}"' in body and
              'Command="{Binding ToggleActiveCommand}"' in body,
              "the footer keeps both selection actions")

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
    data normal blank". Two separate mechanisms drew the body rules - the view's
    own GridLinesVisibility and the package's per-cell border - and clearing only
    the first one left the rows boxed. The view now states both, and draws the
    header rule itself so a package's header weight cannot decide how heavy the
    table looks.
    """
    view = read(MODULE / "step2.d.1.ClientWorkspaceView.xaml")
    # Read the attribute from the element itself: the doc comments above mention
    # GridLinesVisibility by name, and a whole-file substring test passed happily
    # on that comment after the attribute had been deleted.
    datagrid = view[view.index("<DataGrid "):]
    datagrid = datagrid[:datagrid.index(">")]
    check('GridLinesVisibility="None"' in datagrid,
          "the register must state GridLinesVisibility=\"None\"; leaving it unset "
          "lets each package decide, and some of them draw horizontal rules")
    check(datagrid.count('GridLinesVisibility="') == 1,
          "the register grid must not carry two competing line settings")
    check('VerticalGridLinesBrush="' not in view and 'HorizontalGridLinesBrush="' not in view,
          "a rule brush override is not wanted: the header carries the rules")

    rule = re.search(r'<Style x:Key="ClientHeaderRule" TargetType="Border">(.*?)</Style>', view, re.S)
    check(rule is not None, "the header rule style is missing")
    if rule:
        body = rule.group(1)
        check('Property="Background" Value="{DynamicResource BorderStrong}"' in body,
              "the header rule must use a visible theme token")
        check('Property="Width" Value="{DynamicResource StrokeWidth}"' in body,
              "the rule width must follow the theme, not a literal")
        check('Property="HorizontalAlignment" Value="Right"' in body,
              "the rule belongs on the column boundary, so it sits at the right edge")
    check('CanUserResizeColumns="True"' in view,
          "columns must stay resizable by hand")
    check('CanUserResizeRows="False"' in view,
          "row height is the theme's business, not the user's")
    check('MinColumnWidth="{DynamicResource ControlMinWidth}"' in view,
          "a floor is needed, or a column can be dragged down to nothing")
    header = re.search(r'<Style x:Key="ClientColumnHeader" TargetType="DataGridColumnHeader"(.*?)</Style>',
                       view, re.S)
    check(header is not None, "the register has no column-header style")
    if header:
        check('Property="BorderThickness" Value="0"' in header.group(0),
              "the package's header frame must be switched off, or its control "
              "weight (2px, 4px) boxes the header band")
        check("PART_RightHeaderGripper" in header.group(0) and "PART_LeftHeaderGripper" in header.group(0),
              "a header template without the resize grippers silently disables "
              "column resizing")


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
    require_command_bar()
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
          "register, header rules and manual resize, one command bar, equal "
          "paired fields, docs recorded.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
